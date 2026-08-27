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
    /// <summary>The four required model files for Sherpa-ONNX transducer models.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } =
        ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];

    /// <summary>Pinned Parakeet v3 offline model.</summary>
    public static ModelManifest ParakeetV3 { get; } = new(
        "parakeet-v3",
        "Parakeet v3 (offline)",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2",
        487_170_055,
        "5793d0fd397c5778d2cf2126994d58e9d56b1be7c04d13c7a15bb1b4eafb16bf",
        false);

    /// <summary>Pinned Nemotron 3.5 live streaming model.</summary>
    public static ModelManifest NemotronStreaming160Ms { get; } = new(
        "nemotron-3.5-streaming-160ms",
        "Nemotron 3.5 (live, 160 ms)",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-160ms-int8-2026-06-11.tar.bz2",
        475_273_363,
        "a81909a1780d84cff16d73c15e13e67d9d81d8839faf14870d507d8499f7a61a",
        true);

    /// <summary>All available pinned speech models.</summary>
    public static IReadOnlyList<ModelManifest> All { get; } =
        [ParakeetV3, NemotronStreaming160Ms];

    /// <summary>Gets a model manifest by ID, or defaults to Parakeet v3.</summary>
    public static ModelManifest Get(string? id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? ParakeetV3;
}
