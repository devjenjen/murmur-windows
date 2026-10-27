namespace Murmur.App.Diagnostics;

internal static class ShutdownDeadline
{
    internal static void Observe(Task work) => _ = work.ContinueWith(
        static completed => { _ = completed.Exception; }, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    internal static async Task RunAsync(Task cleanup, Action completed, Action forceExit,
        Func<Task, TimeSpan, Task>? wait = null)
    {
        Observe(cleanup);
        try
        {
            wait ??= static (work, budget) => work.WaitAsync(budget);
            await wait(cleanup, TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Only process termination can reclaim a genuinely stuck native call safely.
            forceExit();
            return;
        }
        completed();
    }
}
