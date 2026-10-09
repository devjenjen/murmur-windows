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
    /// <inheritdoc />
    public TextInjectionTarget CaptureTarget()
    {
        var target = ReadCurrentTarget();
        if (target.WindowHandle == 0 || target.ProcessId == 0)
        {
            throw new TextInjectionException(TextInjectionFailureReason.TargetUnavailable);
        }

        return target;
    }

    /// <inheritdoc />
    public void Insert(string text, TextInjectionTarget target, TextInsertionMode mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (HasHeldModifier(
                Win32.GetAsyncKeyState((int)Win32.VK_CONTROL),
                Win32.GetAsyncKeyState((int)Win32.VK_MENU),
                Win32.GetAsyncKeyState((int)Win32.VK_SHIFT)))
        {
            throw new TextInjectionException(TextInjectionFailureReason.ModifierHeld);
        }

        var currentTarget = CaptureTarget();
        if (!IsSameTarget(target, currentTarget))
        {
            throw new TextInjectionException(TextInjectionFailureReason.TargetChanged);
        }

        var array = BuildInputs(text, mode);
        if (HasHeldModifier(
                Win32.GetAsyncKeyState((int)Win32.VK_CONTROL),
                Win32.GetAsyncKeyState((int)Win32.VK_MENU),
                Win32.GetAsyncKeyState((int)Win32.VK_SHIFT)))
        {
            throw new TextInjectionException(TextInjectionFailureReason.ModifierHeld);
        }

        currentTarget = CaptureTarget();
        if (!IsSameTarget(target, currentTarget))
        {
            throw new TextInjectionException(TextInjectionFailureReason.TargetChanged);
        }

        var inserted = Win32.SendInput((uint)array.Length, array, Marshal.SizeOf<Win32.INPUT>());
        if (inserted != array.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"SendInput inserted {inserted} of {array.Length} keyboard events.");
        }
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
