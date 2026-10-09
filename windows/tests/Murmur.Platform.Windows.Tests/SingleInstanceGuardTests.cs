namespace Murmur.Platform.Windows.Tests;

using Murmur.Platform.Windows.Startup;
using Shouldly;
using Xunit;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Second_guard_is_denied_until_first_is_disposed()
    {
        var name = $"Local\\Murmur.Test.{Guid.NewGuid():N}";
        var first = SingleInstanceGuard.TryAcquire(name);

        first.ShouldNotBeNull();
        using (var denied = SingleInstanceGuard.TryAcquire(name))
        {
            denied.ShouldBeNull();
        }

        first!.Dispose();
        using var next = SingleInstanceGuard.TryAcquire(name);
        next.ShouldNotBeNull();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var name = $"Local\\Murmur.Test.{Guid.NewGuid():N}";
        using var guard = SingleInstanceGuard.TryAcquire(name);

        guard.ShouldNotBeNull();
        guard!.Dispose();
        guard.Dispose();

        using var next = SingleInstanceGuard.TryAcquire(name);
        next.ShouldNotBeNull();
    }

    [Fact]
    public async Task Concurrent_acquire_allows_exactly_one_guard()
    {
        var name = $"Local\\Murmur.Test.{Guid.NewGuid():N}";
        var results = await Task.WhenAll(
            Task.Run(() => SingleInstanceGuard.TryAcquire(name)),
            Task.Run(() => SingleInstanceGuard.TryAcquire(name)));

        results.Count(guard => guard is not null).ShouldBe(1);
        foreach (var result in results)
        {
            result?.Dispose();
        }
    }
}
