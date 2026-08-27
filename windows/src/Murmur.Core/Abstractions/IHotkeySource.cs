namespace Murmur.Core.Abstractions;

/// <summary>
/// Watches for a push-to-talk modifier key hold and release.
/// </summary>
public interface IHotkeySource : IDisposable
{
    /// <summary>Fired when the push-to-talk key is pressed down.</summary>
    event Action? Pressed;

    /// <summary>Fired when the push-to-talk key is released.</summary>
    event Action? Released;

    /// <summary>Arms the hotkey listener.</summary>
    void Start();

    /// <summary>Disarms the hotkey listener.</summary>
    void Stop();
}
