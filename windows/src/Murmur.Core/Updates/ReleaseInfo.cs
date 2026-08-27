namespace Murmur.Core.Updates;

/// <summary>Stable release metadata used by the update checker.</summary>
public sealed record ReleaseInfo
{
    /// <summary>Initializes stable release metadata.</summary>
    public ReleaseInfo(Version version, string tag, Uri releaseUri)
    {
        Version = version;
        Tag = tag;
        ReleaseUri = releaseUri;
    }

    /// <summary>Semantic version represented by the release tag.</summary>
    public Version Version { get; }

    /// <summary>Validated stable release tag, including its v prefix.</summary>
    public string Tag { get; }

    /// <summary>Release page on the fixed Murmur GitHub repository.</summary>
    public Uri ReleaseUri { get; }
}