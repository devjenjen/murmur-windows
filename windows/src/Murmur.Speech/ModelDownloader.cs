using System.Diagnostics;
using System.Security.Cryptography;

namespace Murmur.Speech;

internal interface IArchiveExtractor
{
    Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken);
}

internal sealed class TarArchiveExtractor : IArchiveExtractor
{
    public async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        var tarPath = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!Path.IsPathFullyQualified(tarPath) || !File.Exists(tarPath))
        {
            throw new FileNotFoundException("Trusted system tar.exe was not found.", tarPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = tarPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-xjf");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(destination);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited between the cancellation and the kill check.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"tar.exe extraction failed with exit code {process.ExitCode}: {stderr}");
        }
    }
}

/// <summary>
/// Downloads and extracts pinned speech models.
/// </summary>
public sealed class ModelDownloader
{
    private readonly HttpClient _httpClient;
    private readonly IArchiveExtractor _archiveExtractor;

    /// <summary>Initializes a new instance of ModelDownloader.</summary>
    public ModelDownloader(HttpClient? httpClient = null)
        : this(httpClient, new TarArchiveExtractor())
    {
    }

    internal ModelDownloader(HttpClient? httpClient, IArchiveExtractor archiveExtractor)
    {
        _httpClient = httpClient ?? new HttpClient();
        _archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
    }

    /// <summary>Downloads and extracts the specified model archive asynchronously with progress reporting.</summary>
    public async Task DownloadModelAsync(ModelManifest model, string? targetDir = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var finalDir = Path.GetFullPath(targetDir ?? ModelLocator.GetDefaultModelDirectory(model));
        if (ModelLocator.IsModelDownloaded(model, finalDir))
        {
            progress?.Report(1.0);
            return;
        }

        var parentDir = Path.GetFullPath(Path.GetDirectoryName(finalDir) ?? "");
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        EnsureEnoughDiskSpace(parentDir, finalDir, model.ArchiveSize, model.ExtractedSize);
        var partFile = CreateChildPath(parentDir, $"{Guid.NewGuid():N}.part");
        var stagingDir = CreateChildPath(parentDir, $"{Guid.NewGuid():N}.staging");

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
                            var ratio = Math.Clamp((double)totalRead / totalBytes, 0.0, 1.0) * 0.75;
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

            progress?.Report(0.78);
            var actualHash = await ComputeSha256Async(partFile, ct).ConfigureAwait(false);
            if (!string.Equals(actualHash, model.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-256 hash mismatch: expected {model.ArchiveSha256}, got {actualHash}.");
            }

            progress?.Report(0.82);
            Directory.CreateDirectory(stagingDir);
            await _archiveExtractor.ExtractAsync(partFile, stagingDir, ct).ConfigureAwait(false);
            progress?.Report(0.9);

            var extractedDir = stagingDir;
            var expectedFirstFile = model.RequiredFiles.Count > 0 ? model.RequiredFiles[0] : "encoder.int8.onnx";
            if (!File.Exists(Path.Combine(extractedDir, expectedFirstFile)))
            {
                var subdirs = Directory.GetDirectories(stagingDir);
                if (subdirs.Length == 1 && File.Exists(Path.Combine(subdirs[0], expectedFirstFile)))
                {
                    extractedDir = subdirs[0];
                }
            }

            ValidateModelDirectory(extractedDir, model);
            await ModelIntegrityManifest.WriteAsync(model, extractedDir, ct).ConfigureAwait(false);
            progress?.Report(0.97);
            ReplaceModelDirectory(finalDir, extractedDir, parentDir);
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

    internal static void ValidateModelDirectory(string path, ModelManifest? model = null)
    {
        if (!Directory.Exists(path))
        {
            throw new InvalidDataException($"Directory does not exist: {path}");
        }

        var requiredFiles = model?.RequiredFiles ?? ModelManifest.DefaultRequiredFiles;
        foreach (var file in requiredFiles)
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

    private static void ReplaceModelDirectory(string finalDir, string extractedDir, string parentDir)
    {
        var backupDir = CreateChildPath(parentDir, $"{Path.GetFileName(finalDir)}.{Guid.NewGuid():N}.previous");
        var movedPrevious = false;
        try
        {
            if (Directory.Exists(finalDir))
            {
                Directory.Move(finalDir, backupDir);
                movedPrevious = true;
            }

            Directory.Move(extractedDir, finalDir);
            if (movedPrevious && Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, recursive: true);
            }
        }
        catch
        {
            if (movedPrevious && !Directory.Exists(finalDir) && Directory.Exists(backupDir))
            {
                Directory.Move(backupDir, finalDir);
            }
            throw;
        }
    }

    private static string CreateChildPath(string parentDir, string childName)
    {
        var root = Path.GetFullPath(parentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(parentDir, childName));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe staging path: {childName}");
        }
        return fullPath;
    }

    private static void EnsureEnoughDiskSpace(string parentDir, string finalDir, long archiveSize, long extractedSize)
    {
        var root = Path.GetPathRoot(parentDir);
        if (string.IsNullOrEmpty(root))
        {
            throw new IOException("Could not determine model storage volume.");
        }

        try
        {
            const long safetyReserve = 128L * 1024 * 1024;
            var extractionReserve = extractedSize > 0 ? extractedSize : safetyReserve;
            var previousModelSize = Directory.Exists(finalDir) ? GetDirectorySize(finalDir) : 0L;
            var required = checked(archiveSize + extractionReserve + previousModelSize + safetyReserve);
            var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < required)
            {
                throw new IOException($"Not enough free disk space for model download and extraction. Required at least {required / (1024 * 1024)} MB including the measured extraction size and previous model copy.");
            }
        }
        catch (IOException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("Could not inspect free disk space for model download.", ex);
        }
    }

    private static long GetDirectorySize(string directory)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            total = checked(total + new FileInfo(file).Length);
        }

        return total;
    }
}
