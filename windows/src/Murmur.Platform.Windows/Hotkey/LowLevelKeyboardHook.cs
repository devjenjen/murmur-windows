using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Murmur.Core.Abstractions;
using Murmur.Platform.Windows.Native;

namespace Murmur.Platform.Windows.Hotkey;

/// <summary>
/// Global low-level keyboard hook watching for push-to-talk hold and release without swallowing input.
/// </summary>
public sealed class LowLevelKeyboardHook : IHotkeySource
{
    /// <inheritdoc />
    public event Action? Pressed;

    /// <inheritdoc />
    public event Action? Released;

    private readonly HotkeyOption _option;
    private Win32.HookProc? _hookProc;
    private nint _hookHandle;
    private bool _isPressed;
    private Thread? _messageLoopThread;

    /// <summary>
    /// Initializes a new instance of the <see cref="LowLevelKeyboardHook"/> class for the specified hotkey.
    /// </summary>
    /// <param name="hotkeyId">Configured hotkey ID, or null for default.</param>
    public LowLevelKeyboardHook(string? hotkeyId = null)
    {
        _option = HotkeyOptions.Get(hotkeyId);
    }

    /// <inheritdoc />
    public void Start()
    {
        if (_hookHandle != 0) return;

        var readyEvent = new ManualResetEventSlim(false);

        _messageLoopThread = new Thread(() =>
        {
            _hookProc = HookCallback;
            _hookHandle = Win32.SetWindowsHookEx(
                Win32.WH_KEYBOARD_LL,
                _hookProc,
                nint.Zero,
                0);

            // Set AFTER SetWindowsHookEx so the calling thread can read _hookHandle.
            readyEvent.Set();

            if (_hookHandle == 0) return; // calling thread will throw

            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        })
        {
            IsBackground = true,
            Name = "Murmur.HotkeyHookThread"
        };

        _messageLoopThread.Start();
        readyEvent.Wait(2000);

        if (_hookHandle == 0)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Failed to install low-level keyboard hook.");
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_hookHandle != 0)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
            _hookProc = null;
        }
        _isPressed = false;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);

            if (_option.Matches(kbd.vkCode, kbd.flags))
            {
                var isDown = wParam is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
                var isUp = wParam is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;

                if (isDown && !_isPressed)
                {
                    _isPressed = true;
                    Pressed?.Invoke();
                }
                else if (isUp && _isPressed)
                {
                    _isPressed = false;
                    Released?.Invoke();
                }
            }
        }

        return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage([In] ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }
}
