using System.Diagnostics;
using System.Security.Cryptography;

namespace Murmur.Speech;

/// <summary>
/// Downloads and extracts pinned speech models.
/// </summary>
public sealed class ModelDownloader
{
    private readonly HttpClient _httpClient;

    /// <summary>Initializes a new instance of ModelDownloader.</summary>
    public ModelDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>Downloads and extracts the specified model archive asynchronously with progress reporting.</summary>
    public async Task DownloadModelAsync(ModelManifest model, string? targetDir = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var finalDir = targetDir ?? ModelLocator.GetDefaultModelDirectory(model);
        if (ModelLocator.IsModelDownloaded(model, finalDir))
        {
            progress?.Report(1.0);
            return;
        }

        var parentDir = Path.GetDirectoryName(finalDir) ?? "";
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        if (Directory.Exists(finalDir))
        {
            var invalidDir = Path.Combine(parentDir, $"{Path.GetFileName(finalDir)}.invalid-{Guid.NewGuid():N}");
            try
            {
                Directory.Move(finalDir, invalidDir);
            }
            catch
            {
                // Best effort rename
            }
        }

        var partFile = Path.Combine(parentDir, $"{Guid.NewGuid():N}.part");
        var stagingDir = Path.Combine(parentDir, $"{Guid.NewGuid():N}.staging");

        try
        {
            using (var response = await _httpClient.GetAsync(new Uri(model.ArchiveUrl), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? model.ArchiveSize;
                await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using (var fileStream = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    var totalRead = 0L;
                    var lastReportedRatio = -1.0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        totalRead += bytesRead;
                        if (totalBytes > 0 && progress != null)
                        {
                            var ratio = (double)totalRead / totalBytes;
                            if (ratio - lastReportedRatio >= 0.005 || ratio >= 1.0)
                            {
                                lastReportedRatio = ratio;
                                progress.Report(ratio);
                            }
                        }
                    }
                }
            }

            var fileInfo = new FileInfo(partFile);
            if (fileInfo.Length != model.ArchiveSize)
            {
                throw new InvalidDataException($"Downloaded archive size {fileInfo.Length} did not match expected {model.ArchiveSize}.");
            }

            var actualHash = await ComputeSha256Async(partFile, ct).ConfigureAwait(false);
            if (!string.Equals(actualHash, model.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-256 hash mismatch: expected {model.ArchiveSha256}, got {actualHash}.");
            }

            Directory.CreateDirectory(stagingDir);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "tar.exe",
                    Arguments = $"-xjf \"{partFile}\" -C \"{stagingDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            _ = await stdoutTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"tar.exe extraction failed with exit code {process.ExitCode}: {stderr}");
            }

            var extractedDir = stagingDir;
            if (!File.Exists(Path.Combine(extractedDir, "encoder.int8.onnx")))
            {
                var subdirs = Directory.GetDirectories(stagingDir);
                if (subdirs.Length == 1 && File.Exists(Path.Combine(subdirs[0], "encoder.int8.onnx")))
                {
                    extractedDir = subdirs[0];
                }
            }

            ValidateModelDirectory(extractedDir);
            await ModelIntegrityManifest.WriteAsync(model, extractedDir, ct).ConfigureAwait(false);

            if (Directory.Exists(finalDir))
            {
                Directory.Delete(finalDir, recursive: true);
            }

            Directory.Move(extractedDir, finalDir);
            progress?.Report(1.0);
        }
        finally
        {
            try
            {
                if (File.Exists(partFile))
                {
                    File.Delete(partFile);
                }
                if (Directory.Exists(stagingDir))
                {
                    Directory.Delete(stagingDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>Downloads and extracts the default Parakeet v3 model archive asynchronously with progress reporting.</summary>
    public Task DownloadModelAsync(string? targetDir = null, IProgress<double>? progress = null, CancellationToken ct = default) =>
        DownloadModelAsync(ModelManifest.ParakeetV3, targetDir, progress, ct);

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hashBytes);
    }

    internal static void ValidateModelDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new InvalidDataException($"Directory does not exist: {path}");
        }

        foreach (var file in ModelManifest.RequiredFiles)
        {
            var filePath = Path.Combine(path, file);
            if (!File.Exists(filePath))
            {
                throw new InvalidDataException($"Missing required model file: {file}");
            }

            var info = new FileInfo(filePath);
            if (info.Length == 0)
            {
                throw new InvalidDataException($"Required model file is empty: {file}");
            }
        }
    }
}
