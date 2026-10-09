using Murmur.App.Diagnostics;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public sealed class RotatingFileLogTests
{
    [Fact]
    public void Unicode_lines_rotate_by_utf8_bytes_and_keep_latest_active()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "murmur.log");
        var log = new RotatingFileLog(path, maxBytes: 128, backupCount: 2);

        for (var i = 0; i < 12; i++)
        {
            log.WriteLine($"Zeile {i}: ÄÖÜ 😀 {new string('x', 28)}");
        }

        var files = Directory.GetFiles(temp.Path, "murmur.log*");
        files.Length.ShouldBeLessThanOrEqualTo(3);
        foreach (var file in files)
        {
            new FileInfo(file).Length.ShouldBeLessThanOrEqualTo(128);
        }

        File.ReadAllText(path).ShouldContain("Zeile 11");
    }

    [Fact]
    public void Oversized_entry_is_truncated_and_bounded()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "murmur.log");
        var log = new RotatingFileLog(path, maxBytes: 128, backupCount: 2);

        log.WriteLine(new string('a', 1000));

        new FileInfo(path).Length.ShouldBeLessThanOrEqualTo(128);
        File.ReadAllText(path).ShouldContain("[truncated]");
    }

    [Fact]
    public void Oversized_unicode_entry_is_truncated_without_splitting_utf8()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "murmur.log");
        var log = new RotatingFileLog(path, maxBytes: 128, backupCount: 2);

        log.WriteLine(string.Concat(Enumerable.Repeat("😀", 200)));

        new FileInfo(path).Length.ShouldBeLessThanOrEqualTo(128);
        File.ReadAllText(path).ShouldContain("[truncated]");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"murmur-log-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

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
                // Best-effort test cleanup.
            }
        }
    }
}
