using Murmur.App.Diagnostics;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public class ShutdownDeadlineTests
{
    [Fact]
    public async Task ExitBudgetEndsProcessViaCallbackWithoutDisposingUnfinishedWork()
    {
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = 0;
        var normal = 0;
        await ShutdownDeadline.RunAsync(work.Task, () => normal++, () => exited++, (pending, budget) =>
        {
            pending.ShouldBe(work.Task);
            budget.ShouldBe(TimeSpan.FromSeconds(3));
            return Task.FromException(new TimeoutException());
        });
        exited.ShouldBe(1);
        normal.ShouldBe(0);
        work.Task.IsCompleted.ShouldBeFalse();
        work.SetException(new IOException("late cleanup failure"));
    }

    [Fact]
    public async Task HealthyExitUsesNormalCallback()
    {
        var normal = 0;
        await ShutdownDeadline.RunAsync(Task.CompletedTask, () => normal++, () => throw new InvalidOperationException());
        normal.ShouldBe(1);
    }
}
