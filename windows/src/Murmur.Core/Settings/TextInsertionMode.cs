namespace Murmur.Core.Settings;

/// <summary>
/// Selects where completed dictation text is inserted.
/// </summary>
public enum TextInsertionMode
{
    /// <summary>Insert at the target application's current caret.</summary>
    AtCaret,

    /// <summary>Move to the target application's document end before inserting.</summary>
    DocumentEnd
}
