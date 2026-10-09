namespace Murmur.Core.Abstractions;

/// <summary>Classifies a user-visible text injection failure without exposing platform details.</summary>
public enum TextInjectionFailureReason
{
    /// <summary>No valid foreground window was available.</summary>
    TargetUnavailable,

    /// <summary>The foreground window or owning process changed.</summary>
    TargetChanged,

    /// <summary>A modifier key remained physically held.</summary>
    ModifierHeld
}

/// <summary>Reports a text injection failure that the UI can localize.</summary>
public sealed class TextInjectionException : InvalidOperationException
{
    /// <summary>Gets the failure category.</summary>
    public TextInjectionFailureReason Reason { get; }

    /// <summary>Gets the localization resource key for the user-facing message.</summary>
    public string UserMessageKey => Reason switch
    {
        TextInjectionFailureReason.TargetUnavailable => "TextTargetUnavailable",
        TextInjectionFailureReason.TargetChanged => "TextTargetChanged",
        TextInjectionFailureReason.ModifierHeld => "TextModifierHeld",
        _ => "TextInjectionFailed"
    };

    /// <summary>Initializes a classified text injection failure.</summary>
    public TextInjectionException(TextInjectionFailureReason reason)
        : base(reason.ToString())
    {
        Reason = reason;
    }
}
