using Murmur.Core.Settings;

namespace Murmur.Core.Abstractions;

/// <summary>
/// Inserts text into the currently focused window/caret.
/// </summary>
public interface ITextInjector
{
    /// <summary>Captures the current foreground target before audio capture starts.</summary>
    TextInjectionTarget CaptureTarget();

    /// <summary>Inserts text into the captured target.</summary>
    void Insert(string text, TextInjectionTarget target, TextInsertionMode mode);
}
