namespace Murmur.Speech.Tests;

using System.Text;
using Shouldly;
using Xunit;

public class ModelDownloaderTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-dl-tests-{Guid.NewGuid():N}");

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
    public async Task ComputeSha256Async_Computes_Correct_Hash()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "test.txt");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("abc"));

        var hash = await ModelDownloader.ComputeSha256Async(path, CancellationToken.None);
        hash.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void ValidateModelDirectory_Throws_When_Empty_File()
    {
        using var temp = new TempDirectory();
        foreach (var file in ModelManifest.RequiredFiles)
        {
            var content = file == "joiner.int8.onnx" ? "" : "data";
            File.WriteAllText(Path.Combine(temp.Path, file), content);
        }

        Should.Throw<InvalidDataException>(() => ModelDownloader.ValidateModelDirectory(temp.Path));
    }

    [Fact]
    public void ValidateModelDirectory_Throws_When_Missing_File()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "tokens.txt"), "data");

        Should.Throw<InvalidDataException>(() => ModelDownloader.ValidateModelDirectory(temp.Path));
    }

    [Fact]
    public async Task Integrity_manifest_is_written_for_every_required_file()
    {
        using var temp = new TempDirectory();
        foreach (var file in ModelManifest.RequiredFiles)
        {
            await File.WriteAllTextAsync(Path.Combine(temp.Path, file), "data");
        }

        await ModelIntegrityManifest.WriteAsync(ModelManifest.ParakeetV3, temp.Path);

        var manifest = ModelIntegrityManifest.Read(Path.Combine(temp.Path, ModelIntegrityManifest.FileName));
        manifest.Version.ShouldBe(ModelIntegrityManifest.CurrentVersion);
        manifest.ModelId.ShouldBe(ModelManifest.ParakeetV3.Id);
        manifest.Files.Select(file => file.Path).ShouldBe(ModelManifest.RequiredFiles);
        manifest.Files.ShouldAllBe(file => file.Length == 4 && file.Sha256.Length == 64);
    }
}
