namespace Murmur.Core.Abstractions;

/// <summary>
/// Foreground window identity captured when a dictation starts.
/// </summary>
public readonly record struct TextInjectionTarget(nint WindowHandle, uint ProcessId);
