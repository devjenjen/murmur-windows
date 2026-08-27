using System.Globalization;
using Murmur.Platform.Windows.Native;

namespace Murmur.Platform.Windows.Hotkey;

internal sealed record HotkeyOption(
    string Id,
    string DisplayName,
    uint VirtualKey,
    bool ExtendedControl = false)
{
    internal bool Matches(uint virtualKey, uint flags)
    {
        if (ExtendedControl)
        {
            return virtualKey == Win32.VK_RCONTROL ||
                (virtualKey == Win32.VK_CONTROL && (flags & Win32.LLKHF_EXTENDED) != 0);
        }

        if (VirtualKey == Win32.VK_LCONTROL)
        {
            return virtualKey == Win32.VK_LCONTROL ||
                (virtualKey == Win32.VK_CONTROL && (flags & Win32.LLKHF_EXTENDED) == 0);
        }

        return virtualKey == VirtualKey;
    }
}

internal static class HotkeyOptions
{
    internal static IReadOnlyList<HotkeyOption> All { get; } =
    [
        new("right-control", "Right Ctrl", Win32.VK_RCONTROL, true),
        new("left-control", "Left Ctrl", Win32.VK_LCONTROL),
        new("f8", "F8", Win32.VK_F8),
        new("f9", "F9", Win32.VK_F9),
        new("f10", "F10", Win32.VK_F10),
        new("f11", "F11", Win32.VK_F11),
        new("f12", "F12", Win32.VK_F12),
        new("pause", "Pause", Win32.VK_PAUSE),
        new("scroll-lock", "Scroll Lock", Win32.VK_SCROLL)
    ];

    internal static HotkeyOption Get(string? id)
    {
        var legacy = All.FirstOrDefault(
            x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (legacy != null)
        {
            return legacy;
        }

        if (id is { Length: > 3 } &&
            id.StartsWith("vk:", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(id.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var virtualKey) &&
            virtualKey is > 0 and <= 0xFF)
        {
            return new HotkeyOption($"vk:{virtualKey}", $"Key {virtualKey}", virtualKey);
        }

        return All[0];
    }
}
