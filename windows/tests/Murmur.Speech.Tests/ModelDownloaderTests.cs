namespace Murmur.Speech.Tests;

using System.Text;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
        foreach (var file in ModelManifest.DefaultRequiredFiles)
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
        foreach (var file in ModelManifest.DefaultRequiredFiles)
        {
            await File.WriteAllTextAsync(Path.Combine(temp.Path, file), "data");
        }

        await ModelIntegrityManifest.WriteAsync(ModelManifest.ParakeetV3, temp.Path);

        var manifest = ModelIntegrityManifest.Read(Path.Combine(temp.Path, ModelIntegrityManifest.FileName));
        manifest.Version.ShouldBe(ModelIntegrityManifest.CurrentVersion);
        manifest.ModelId.ShouldBe(ModelManifest.ParakeetV3.Id);
        manifest.Files.Select(file => file.Path).ShouldBe(ModelManifest.ParakeetV3.RequiredFiles);
        manifest.Files.ShouldAllBe(file => file.Length == 4 && file.Sha256.Length == 64);
    }

    [Fact]
    public async Task Cancellation_during_extraction_stops_child_before_cleanup()
    {
        using var temp = new TempDirectory();
        var archive = Encoding.UTF8.GetBytes("archive");
        var model = TestModel(archive);
        var extractor = new BlockingExtractor();
        var downloader = new ModelDownloader(
            new HttpClient(new FixedResponseHandler(archive)),
            extractor);
        using var cancellation = new CancellationTokenSource();

        var download = downloader.DownloadModelAsync(model, Path.Combine(temp.Path, model.Id), ct: cancellation.Token);
        await extractor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await download);
        extractor.WasCancelled.ShouldBeTrue();
        Directory.GetFiles(temp.Path, "*.part").ShouldBeEmpty();
        Directory.GetDirectories(temp.Path, "*.staging").ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_replacement_preserves_previous_valid_model()
    {
        using var temp = new TempDirectory();
        var archive = Encoding.UTF8.GetBytes("archive");
        var model = TestModel(archive);
        var finalDir = Path.Combine(temp.Path, model.Id);
        Directory.CreateDirectory(finalDir);
        foreach (var file in ModelManifest.DefaultRequiredFiles)
        {
            await File.WriteAllTextAsync(Path.Combine(finalDir, file), "previous model content");
        }
        await ModelIntegrityManifest.WriteAsync(
            model with { Id = "previous-model" },
            finalDir);
        await File.WriteAllTextAsync(Path.Combine(finalDir, "previous.marker"), "keep me");

        var handler = new FixedResponseHandler(Encoding.UTF8.GetBytes("wrong archive"));
        var downloader = new ModelDownloader(new HttpClient(handler), new NoOpExtractor());

        await Should.ThrowAsync<InvalidDataException>(async () =>
            await downloader.DownloadModelAsync(model, finalDir));

        File.Exists(Path.Combine(finalDir, "previous.marker")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(finalDir, "tokens.txt")).ShouldBe("previous model content");
        Directory.GetDirectories(temp.Path, "*.invalid-*").ShouldBeEmpty();
    }

    private static ModelManifest TestModel(byte[] archive) => new(
        "test-model",
        "Test model",
        "https://example.invalid/model.tar.bz2",
        archive.Length,
        Convert.ToHexStringLower(SHA256.HashData(archive)),
        false);

    private sealed class FixedResponseHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public FixedResponseHandler(byte[] payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
            response.Content.Headers.ContentLength = _payload.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class BlockingExtractor : IArchiveExtractor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }

        public async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
    }

    private sealed class NoOpExtractor : IArchiveExtractor
    {
        public Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
