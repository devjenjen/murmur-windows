namespace Murmur.Platform.Windows.Audio;

/// <summary>
/// Represents an active audio input device.
/// </summary>
/// <param name="Id">The MMDevice ID.</param>
/// <param name="Name">The friendly display name of the device.</param>
/// <param name="IsDefault">Whether this is the current Windows default console capture device.</param>
public sealed record AudioInputDevice(string Id, string Name, bool IsDefault);
