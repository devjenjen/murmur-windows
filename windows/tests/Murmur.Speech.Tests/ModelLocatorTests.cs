namespace Murmur.Speech.Tests;

using System;
using System.IO;
using Murmur.Speech;
using Shouldly;
using Xunit;

public class ModelLocatorTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-model-tests-{Guid.NewGuid():N}");

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
    public void Nemotron_160ms_Manifest_Is_Pinned()
    {
        var model = ModelManifest.NemotronStreaming160Ms;
        model.Id.ShouldBe("nemotron-3.5-streaming-160ms");
        model.ArchiveSize.ShouldBe(475_273_363);
        model.ArchiveSha256.ShouldBe("a81909a1780d84cff16d73c15e13e67d9d81d8839faf14870d507d8499f7a61a");
        model.ExtractedSize.ShouldBe(684_575_710);
        model.SupportsLivePreview.ShouldBeTrue();
    }

    [Fact]
    public void Locator_Uses_Requested_Model_Directory()
    {
        ModelLocator.GetDefaultModelDirectory(ModelManifest.ParakeetV3)
            .ShouldEndWith(Path.Combine("models", "parakeet-v3"));
        ModelLocator.GetDefaultModelDirectory(ModelManifest.NemotronStreaming160Ms)
            .ShouldEndWith(Path.Combine("models", "nemotron-3.5-streaming-160ms"));
    }

    [Fact]
    public void Completeness_All_Non_Empty_Files_Passes()
    {
        using var temp = new TempDirectory();
        foreach (var file in ModelManifest.DefaultRequiredFiles)
        {
            File.WriteAllText(Path.Combine(temp.Path, file), "data");
        }

        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, temp.Path).ShouldBeTrue();
    }

    [Fact]
    public void Completeness_Empty_Encoder_Fails()
    {
        using var temp = new TempDirectory();
        foreach (var file in ModelManifest.DefaultRequiredFiles)
        {
            var content = file == "encoder.int8.onnx" ? "" : "data";
            File.WriteAllText(Path.Combine(temp.Path, file), content);
        }

        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, temp.Path).ShouldBeFalse();
    }

    [Fact]
    public void Completeness_Missing_File_Fails()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "decoder.int8.onnx"), "data");
        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, temp.Path).ShouldBeFalse();
    }

    [Fact]
    public async Task Valid_integrity_manifest_is_accepted()
    {
        using var temp = new TempDirectory();
        WriteRequiredFiles(temp.Path);
        await ModelIntegrityManifest.WriteAsync(ModelManifest.ParakeetV3, temp.Path);

        ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, temp.Path).ShouldBe(ModelIntegrityStatus.Valid);
        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, temp.Path).ShouldBeTrue();
    }

    [Fact]
    public async Task Modified_file_is_corrupt()
    {
        using var temp = new TempDirectory();
        WriteRequiredFiles(temp.Path);
        await ModelIntegrityManifest.WriteAsync(ModelManifest.ParakeetV3, temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "encoder.int8.onnx"), "changed");

        ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, temp.Path).ShouldBe(ModelIntegrityStatus.Corrupt);
        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, temp.Path).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleted_file_is_missing()
    {
        using var temp = new TempDirectory();
        WriteRequiredFiles(temp.Path);
        await ModelIntegrityManifest.WriteAsync(ModelManifest.ParakeetV3, temp.Path);
        File.Delete(Path.Combine(temp.Path, "tokens.txt"));

        ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, temp.Path).ShouldBe(ModelIntegrityStatus.Missing);
        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, temp.Path).ShouldBeFalse();
    }

    [Fact]
    public void Traversal_entry_is_corrupt()
    {
        using var temp = new TempDirectory();
        WriteRequiredFiles(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, ModelIntegrityManifest.FileName),
            "{\"version\":1,\"modelId\":\"parakeet-v3\",\"files\":[{\"path\":\"../encoder.int8.onnx\",\"length\":1,\"sha256\":\"00\"}]}" );

        ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, temp.Path).ShouldBe(ModelIntegrityStatus.Corrupt);
    }

    [Fact]
    public void Wrong_model_id_is_corrupt()
    {
        using var temp = new TempDirectory();
        WriteRequiredFiles(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, ModelIntegrityManifest.FileName),
            "{\"version\":1,\"modelId\":\"other\",\"files\":[]}");

        ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, temp.Path).ShouldBe(ModelIntegrityStatus.Corrupt);
    }

    [Fact]
    public void Malformed_manifest_is_corrupt()
    {
        using var temp = new TempDirectory();
        WriteRequiredFiles(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, ModelIntegrityManifest.FileName), "not json");

        ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, temp.Path).ShouldBe(ModelIntegrityStatus.Corrupt);
    }

    [Fact]
    public void Legacy_non_empty_install_remains_loadable_but_unverified()
    {
        using var temp = new TempDirectory();
        WriteRequiredFiles(temp.Path);

        ModelLocator.GetIntegrityStatus(ModelManifest.ParakeetV3, temp.Path).ShouldBe(ModelIntegrityStatus.LegacyUnverified);
        ModelLocator.IsModelDownloaded(ModelManifest.ParakeetV3, temp.Path).ShouldBeTrue();
    }

    private static void WriteRequiredFiles(string directory)
    {
        foreach (var file in ModelManifest.DefaultRequiredFiles)
        {
            File.WriteAllText(Path.Combine(directory, file), "data");
        }
    }
}
