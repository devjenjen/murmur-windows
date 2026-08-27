using System.ComponentModel;
using System.Runtime.InteropServices;
using Murmur.Core.Abstractions;
using Murmur.Platform.Windows.Native;

namespace Murmur.Platform.Windows.Injection;

/// <summary>
/// Injects Unicode text into the active foreground window using Win32 SendInput.
/// </summary>
public sealed class SendInputInjector : ITextInjector
{
    /// <inheritdoc />
    public void Insert(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var array = BuildInputs(text);
        var inserted = Win32.SendInput((uint)array.Length, array, Marshal.SizeOf<Win32.INPUT>());
        if (inserted != array.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"SendInput inserted {inserted} of {array.Length} keyboard events.");
        }
    }

    internal static Win32.INPUT[] BuildInputs(string text)
    {
        var inputs = new List<Win32.INPUT>(text.Length * 2 + 4)
        {
            KeyboardInput((ushort)Win32.VK_CONTROL, 0),
            KeyboardInput((ushort)Win32.VK_END, 0),
            KeyboardInput((ushort)Win32.VK_END, Win32.KEYEVENTF_KEYUP),
            KeyboardInput((ushort)Win32.VK_CONTROL, Win32.KEYEVENTF_KEYUP)
        };

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
