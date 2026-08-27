namespace Murmur.Core.Abstractions;

/// <summary>
/// Inserts text into the currently focused window/caret.
/// </summary>
public interface ITextInjector
{
    /// <summary>Inserts text into the focused application.</summary>
    void Insert(string text);
}
