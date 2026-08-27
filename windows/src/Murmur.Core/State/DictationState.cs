namespace Murmur.Core.State;

/// <summary>Status of the dictation lifecycle.</summary>
public enum DictationStatus
{
    /// <summary>Idle, waiting for hotkey.</summary>
    Idle,
    /// <summary>Starting audio capture.</summary>
    Starting,
    /// <summary>Capturing audio and listening to user.</summary>
    Listening,
    /// <summary>Transcribing and injecting text.</summary>
    Finishing,
    /// <summary>Error encountered.</summary>
    Error
}

/// <summary>State representation of dictation lifecycle.</summary>
public sealed record DictationState(DictationStatus Status, string? Message = null)
{
    /// <summary>Idle state singleton.</summary>
    public static DictationState Idle { get; } = new(DictationStatus.Idle);
    /// <summary>Starting state singleton.</summary>
    public static DictationState Starting { get; } = new(DictationStatus.Starting);
    /// <summary>Listening state singleton.</summary>
    public static DictationState Listening { get; } = new(DictationStatus.Listening);
    /// <summary>Finishing state singleton.</summary>
    public static DictationState Finishing { get; } = new(DictationStatus.Finishing);
    /// <summary>Creates an error state with the given error message.</summary>
    public static DictationState ErrorState(string message) => new(DictationStatus.Error, message);

    /// <summary>True if dictation is currently in progress.</summary>
    public bool IsActive => Status is DictationStatus.Starting or DictationStatus.Listening or DictationStatus.Finishing;
}
