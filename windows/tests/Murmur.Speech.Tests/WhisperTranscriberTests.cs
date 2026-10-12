namespace Murmur.Speech.Tests;

using System;
using System.IO;
using Murmur.Speech;
using Shouldly;
using Xunit;

public class WhisperTranscriberTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-whisper-tests-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void WhisperV3Turbo_Manifest_Is_Registered()
    {
        var manifest = ModelManifest.Get("whisper-v3-turbo");
        manifest.ShouldNotBeNull();
        manifest.ShouldBeSameAs(ModelManifest.WhisperV3Turbo);
        manifest.Id.ShouldBe("whisper-v3-turbo");
        manifest.DisplayName.ShouldBe("Whisper Large-v3-Turbo (offline)");
        manifest.ArchiveUrl.ShouldBe("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-whisper-turbo.tar.bz2");
        manifest.ArchiveSize.ShouldBe(1_036_000_000);
        manifest.ArchiveSha256.ShouldBe("b54e7bc5be673b5df549eb4ebaa8d53b49ec96cba7b4d1ba83072b2203fa1a80");
        manifest.SupportsLivePreview.ShouldBeFalse();
        manifest.ExtractedSize.ShouldBe(1_080_000_000);
        manifest.RequiredFiles.ShouldBe(["turbo-encoder.int8.onnx", "turbo-decoder.int8.onnx", "tokens.txt"]);
        manifest.RequiredFiles.ShouldContain("turbo-encoder.int8.onnx");
        manifest.RequiredFiles.ShouldContain("turbo-decoder.int8.onnx");
        manifest.RequiredFiles.ShouldContain("tokens.txt");
        ModelManifest.All.ShouldContain(ModelManifest.WhisperV3Turbo);
    }

    [Fact]
    public void Constructor_Throws_DirectoryNotFoundException_When_Directory_Missing()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid():N}");
        Should.Throw<DirectoryNotFoundException>(() => new SherpaOnnxWhisperTranscriber(missingDir));
    }

    [Fact]
    public void Constructor_Throws_DirectoryNotFoundException_When_Files_Missing()
    {
        using var temp = new TempDirectory();
        // Only write one file
        File.WriteAllBytes(Path.Combine(temp.Path, "tokens.txt"), [1, 2, 3]);

        Should.Throw<DirectoryNotFoundException>(() => new SherpaOnnxWhisperTranscriber(temp.Path));
    }
}
