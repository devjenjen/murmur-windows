using System.ComponentModel;
using System.Runtime.InteropServices;
using Murmur.Core.Abstractions;
using Murmur.Core.Settings;
using Murmur.Platform.Windows.Native;

namespace Murmur.Platform.Windows.Injection;

/// <summary>
/// Injects Unicode text into the active foreground window using Win32 SendInput.
/// </summary>
public sealed class SendInputInjector : ITextInjector
{
    private readonly Func<TextInjectionTarget> _readCurrentTarget;
    private readonly Func<bool> _hasHeldModifier;
    private readonly Func<nint, bool> _restoreWindowFocus;
    private readonly Action<Win32.INPUT[]> _sendInputs;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendInputInjector"/> class.
    /// </summary>
    public SendInputInjector()
        : this(
            ReadCurrentTarget,
            () => HasHeldModifier(
                Win32.GetAsyncKeyState((int)Win32.VK_CONTROL),
                Win32.GetAsyncKeyState((int)Win32.VK_MENU),
                Win32.GetAsyncKeyState((int)Win32.VK_SHIFT)),
            RestoreWindowFocus,
            SendInputs)
    {
    }

    internal SendInputInjector(
        Func<TextInjectionTarget> readCurrentTarget,
        Func<bool> hasHeldModifier,
        Func<nint, bool> restoreWindowFocus,
        Action<Win32.INPUT[]> sendInputs)
    {
        _readCurrentTarget = readCurrentTarget ?? throw new ArgumentNullException(nameof(readCurrentTarget));
        _hasHeldModifier = hasHeldModifier ?? throw new ArgumentNullException(nameof(hasHeldModifier));
        _restoreWindowFocus = restoreWindowFocus ?? throw new ArgumentNullException(nameof(restoreWindowFocus));
        _sendInputs = sendInputs ?? throw new ArgumentNullException(nameof(sendInputs));
    }

    /// <inheritdoc />
    public TextInjectionTarget CaptureTarget()
    {
        var target = _readCurrentTarget();
        if (target.WindowHandle == 0 || target.ProcessId == 0)
        {
            throw new TextInjectionException(TextInjectionFailureReason.TargetUnavailable);
        }

        return target;
    }

    /// <inheritdoc />
    public void Insert(string text, TextInjectionTarget target, TextInsertionMode mode, bool restorePreviousWindow = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_hasHeldModifier())
        {
            throw new TextInjectionException(TextInjectionFailureReason.ModifierHeld);
        }

        var currentTarget = _readCurrentTarget();
        nint intermediateHandle = 0;
        if (!IsSameTarget(target, currentTarget))
        {
            intermediateHandle = currentTarget.WindowHandle;
            if (!_restoreWindowFocus(target.WindowHandle))
            {
                throw new TextInjectionException(TextInjectionFailureReason.TargetUnavailable);
            }

            currentTarget = _readCurrentTarget();
            if (!IsSameTarget(target, currentTarget))
            {
                throw new TextInjectionException(TextInjectionFailureReason.TargetChanged);
            }
        }

        var array = BuildInputs(text, mode);
        if (_hasHeldModifier())
        {
            throw new TextInjectionException(TextInjectionFailureReason.ModifierHeld);
        }

        currentTarget = _readCurrentTarget();
        if (!IsSameTarget(target, currentTarget))
        {
            if (intermediateHandle == 0)
            {
                intermediateHandle = currentTarget.WindowHandle;
            }

            if (!_restoreWindowFocus(target.WindowHandle))
            {
                throw new TextInjectionException(TextInjectionFailureReason.TargetUnavailable);
            }

            currentTarget = _readCurrentTarget();
            if (!IsSameTarget(target, currentTarget))
            {
                throw new TextInjectionException(TextInjectionFailureReason.TargetChanged);
            }
        }

        _sendInputs(array);

        if (restorePreviousWindow && intermediateHandle != 0 && intermediateHandle != target.WindowHandle)
        {
            _restoreWindowFocus(intermediateHandle);
        }
    }

    private static void SendInputs(Win32.INPUT[] array)
    {
        var inserted = Win32.SendInput((uint)array.Length, array, Marshal.SizeOf<Win32.INPUT>());
        if (inserted != array.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"SendInput inserted {inserted} of {array.Length} keyboard events.");
        }
    }

    internal static bool RestoreWindowFocus(nint windowHandle)
    {
        if (windowHandle == 0 || !Win32.IsWindow(windowHandle))
        {
            return false;
        }

        if (Win32.IsIconic(windowHandle))
        {
            Win32.ShowWindow(windowHandle, Win32.SW_RESTORE);
        }

        var currentForeground = Win32.GetForegroundWindow();
        if (currentForeground == windowHandle)
        {
            return true;
        }

        var currentThreadId = Win32.GetCurrentThreadId();
        var foregroundThreadId = Win32.GetWindowThreadProcessId(currentForeground, out _);
        var targetThreadId = Win32.GetWindowThreadProcessId(windowHandle, out _);

        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            Win32.AttachThreadInput(currentThreadId, foregroundThreadId, true);
        }

        if (targetThreadId != 0 && targetThreadId != currentThreadId)
        {
            Win32.AttachThreadInput(currentThreadId, targetThreadId, true);
        }

        Win32.SetForegroundWindow(windowHandle);
        Win32.BringWindowToTop(windowHandle);

        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            Win32.AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }

        if (targetThreadId != 0 && targetThreadId != currentThreadId)
        {
            Win32.AttachThreadInput(currentThreadId, targetThreadId, false);
        }

        for (var i = 0; i < 10; i++)
        {
            if (Win32.GetForegroundWindow() == windowHandle)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return Win32.GetForegroundWindow() == windowHandle;
    }

    internal static Win32.INPUT[] BuildInputs(string text, TextInsertionMode mode)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (mode is not (TextInsertionMode.AtCaret or TextInsertionMode.DocumentEnd))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown text insertion mode.");
        }

        var inputs = new List<Win32.INPUT>(text.Length * 2 + (mode == TextInsertionMode.DocumentEnd ? 4 : 0));
        if (mode == TextInsertionMode.DocumentEnd)
        {
            inputs.Add(KeyboardInput((ushort)Win32.VK_CONTROL, 0));
            inputs.Add(KeyboardInput((ushort)Win32.VK_END, 0));
            inputs.Add(KeyboardInput((ushort)Win32.VK_END, Win32.KEYEVENTF_KEYUP));
            inputs.Add(KeyboardInput((ushort)Win32.VK_CONTROL, Win32.KEYEVENTF_KEYUP));
        }

        foreach (var c in text)
        {
            // Key Down
            inputs.Add(new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                u = new Win32.InputUnion
                {
                    ki = new Win32.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = Win32.KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = 0
                    }
                }
            });

            // Key Up
            inputs.Add(new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                u = new Win32.InputUnion
                {
                    ki = new Win32.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = 0
                    }
                }
            });
        }

        return inputs.ToArray();
    }

    internal static bool IsSameTarget(TextInjectionTarget expected, TextInjectionTarget current) =>
        expected.WindowHandle != 0 &&
        expected.ProcessId != 0 &&
        current.WindowHandle != 0 &&
        current.ProcessId != 0 &&
        expected.WindowHandle == current.WindowHandle &&
        expected.ProcessId == current.ProcessId;

    internal static bool HasHeldModifier(short controlState, short altState, short shiftState) =>
        (controlState & unchecked((short)0x8000)) != 0 ||
        (altState & unchecked((short)0x8000)) != 0 ||
        (shiftState & unchecked((short)0x8000)) != 0;

    private static TextInjectionTarget ReadCurrentTarget()
    {
        var windowHandle = Win32.GetForegroundWindow();
        if (windowHandle == 0 || !Win32.IsWindow(windowHandle))
        {
            return default;
        }

        if (Win32.GetWindowThreadProcessId(windowHandle, out var processId) == 0 || processId == 0)
        {
            return default;
        }

        return new TextInjectionTarget(windowHandle, processId);
    }

    private static Win32.INPUT KeyboardInput(ushort virtualKey, uint flags)
    {
        return new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.InputUnion
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags
                }
            }
        };
    }
}
