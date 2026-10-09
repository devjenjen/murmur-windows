using System.Collections.Concurrent;
using System.Text;

namespace Murmur.App.Diagnostics;

/// <summary>Small process-safe UTF-8 log writer with bounded active and backup files.</summary>
internal sealed class RotatingFileLog
{
    private const string TruncatedMarker = " [truncated]";
    private static readonly ConcurrentDictionary<string, Lock> SharedLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _backupCount;
    private readonly Lock _lock;

    /// <summary>Creates a bounded log writer.</summary>
    public RotatingFileLog(string path, long maxBytes, int backupCount)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Log path is required.", nameof(path));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maxBytes,
            StrictUtf8.GetByteCount(TruncatedMarker + Environment.NewLine),
            nameof(maxBytes));
        ArgumentOutOfRangeException.ThrowIfNegative(backupCount, nameof(backupCount));

        _path = Path.GetFullPath(path);
        _maxBytes = maxBytes;
        _backupCount = backupCount;
        _lock = SharedLocks.GetOrAdd(_path, static _ => new Lock());
    }

    /// <summary>Appends one message, rotating before an append would exceed the byte limit.</summary>
    public void WriteLine(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = EncodeBounded(message);

        lock (_lock)
        {
            var currentLength = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            if (currentLength > 0 && currentLength + payload.Length > _maxBytes)
            {
                RotateUnsafe();
            }

            EnsureDirectory();
            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(payload, 0, payload.Length);
        }
    }

    private byte[] EncodeBounded(string message)
    {
        var full = StrictUtf8.GetBytes(message + Environment.NewLine);
        if (full.LongLength <= _maxBytes)
        {
            return full;
        }

        var marker = StrictUtf8.GetBytes(TruncatedMarker + Environment.NewLine);
        var prefixBudget = checked((int)_maxBytes - marker.Length);
        var messageBytes = StrictUtf8.GetBytes(message);
        var prefixLength = Math.Min(prefixBudget, messageBytes.Length);
        string prefix;
        while (true)
        {
            try
            {
                prefix = StrictUtf8.GetString(messageBytes, 0, prefixLength);
                break;
            }
            catch (DecoderFallbackException)
            {
                prefixLength--;
            }
        }

        return StrictUtf8.GetBytes(prefix + TruncatedMarker + Environment.NewLine);
    }

    private void RotateUnsafe()
    {
        if (_backupCount == 0)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            return;
        }

        for (var index = _backupCount; index >= 1; index--)
        {
            var source = index == 1 ? _path : BackupPath(index - 1);
            var destination = BackupPath(index);
            if (!File.Exists(source))
            {
                continue;
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }
    }

    private string BackupPath(int index) => $"{_path}.{index}";

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
