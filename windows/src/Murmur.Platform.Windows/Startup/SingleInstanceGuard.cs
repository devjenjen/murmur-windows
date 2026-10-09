namespace Murmur.Platform.Windows.Startup;

/// <summary>
/// Keeps one Murmur process alive per user session.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>
    /// Named mutex used to identify the running Murmur instance.
    /// </summary>
    public const string MutexName = "Local\\Murmur.App.2A68B7DE-0A17-4A48-BE67-5A3B7B93AB2E";

    private Mutex? _mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// Tries to create the process-lifetime instance guard.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire()
        => TryAcquire(MutexName);

    internal static SingleInstanceGuard? TryAcquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        var mutex = new Mutex(initiallyOwned: false, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex);
    }

    /// <summary>
    /// Releases the instance guard. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _mutex, null)?.Dispose();
    }
}
