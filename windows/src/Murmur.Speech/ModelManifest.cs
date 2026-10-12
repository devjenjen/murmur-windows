namespace Murmur.Speech;

/// <summary>
/// Manifest and catalog for pinned speech models.
/// </summary>
public sealed record ModelManifest(
    string Id,
    string DisplayName,
    string ArchiveUrl,
    long ArchiveSize,
    string ArchiveSha256,
    bool SupportsLivePreview)
{
    /// <summary>Measured bytes written by the pinned archive during extraction.</summary>
    public long ExtractedSize { get; init; }

    /// <summary>The default four required model files for Sherpa-ONNX transducer models.</summary>
    public static IReadOnlyList<string> DefaultRequiredFiles { get; } =
        ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];

    /// <summary>The required model files for this speech model.</summary>
    public IReadOnlyList<string> RequiredFiles { get; init; } = DefaultRequiredFiles;

    /// <summary>Pinned Parakeet v3 offline model.</summary>
    public static ModelManifest ParakeetV3 { get; } = new(
        "parakeet-v3",
        "Parakeet v3 (offline)",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2",
        487_170_055,
        "5793d0fd397c5778d2cf2126994d58e9d56b1be7c04d13c7a15bb1b4eafb16bf",
        false)
    {
        ExtractedSize = 671_239_705
    };

    /// <summary>Pinned Nemotron 3.5 live streaming model.</summary>
    public static ModelManifest NemotronStreaming160Ms { get; } = new(
        "nemotron-3.5-streaming-160ms",
        "Nemotron 3.5 (live, 160 ms)",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-160ms-int8-2026-06-11.tar.bz2",
        475_273_363,
        "a81909a1780d84cff16d73c15e13e67d9d81d8839faf14870d507d8499f7a61a",
        true)
    {
        ExtractedSize = 684_575_710
    };

    /// <summary>Pinned Whisper Large-v3-Turbo offline model.</summary>
    public static ModelManifest WhisperV3Turbo { get; } = new(
        "whisper-v3-turbo",
        "Whisper Large-v3-Turbo (offline)",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-whisper-turbo.tar.bz2",
        1_036_000_000,
        "b54e7bc5be673b5df549eb4ebaa8d53b49ec96cba7b4d1ba83072b2203fa1a80",
        false)
    {
        ExtractedSize = 1_080_000_000,
        RequiredFiles = ["turbo-encoder.int8.onnx", "turbo-decoder.int8.onnx", "tokens.txt"]
    };

    /// <summary>All available pinned speech models.</summary>
    public static IReadOnlyList<ModelManifest> All { get; } =
        [ParakeetV3, NemotronStreaming160Ms, WhisperV3Turbo];

    /// <summary>Gets a model manifest by ID, or defaults to Parakeet v3.</summary>
    public static ModelManifest Get(string? id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? ParakeetV3;
}
