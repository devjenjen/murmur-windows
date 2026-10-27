using System.Collections.Concurrent;
using Murmur.Core.Abstractions;
using Murmur.Core.State;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public partial class DictationControllerTests
{
    private sealed class ManualDeadlines : IDisposable
    {
        internal sealed record Request(Task Work, TimeSpan Budget, TaskCompletionSource Expired);
        private readonly ConcurrentQueue<Request> _requests = new();
        private readonly SemaphoreSlim _available = new(0);
        public async Task WaitAsync(Task work, TimeSpan budget, CancellationToken cancellationToken)
        {
            if (work.IsCompleted) { await work; return; }
            var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requests.Enqueue(new Request(work, budget, expired));
            _available.Release();
            var completed = await Task.WhenAny(work, expired.Task).WaitAsync(cancellationToken);
            if (completed == expired.Task) throw new TimeoutException();
            await work;
        }
        public void Dispose() => _available.Dispose();
        public async Task<Request> NextAsync()
        {
            (await _available.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            _requests.TryDequeue(out var request).ShouldBeTrue();
            return request!;
        }
    }

    private sealed class BlockedTranscriber : ITranscriber
    {
        public TaskCompletionSource<string> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default)
        {
            Calls++;
            Entered.TrySetResult();
            return Returned.Task;
        }
        public void Dispose()
        {
            Returned.Task.IsCompleted.ShouldBeTrue("native owner must finish before disposal");
            DisposeCalls++;
        }
    }

    [Theory]
    [InlineData(false, 160, 60)]
    [InlineData(true, 160, 60)]
    [InlineData(false, 960000, 120)]
    public async Task DecodeDeadlineOrStopBudgetQuarantinesUntilRealCompletion(bool stopFirst, int samples, int seconds)
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var transcriber = new BlockedTranscriber();
        var repairer = new FakeSpeechRepairer();
        var injector = new FakeTextInjector();
        using var deadlines = new ManualDeadlines();
        var controller = new DictationController(audio, hotkey, transcriber, injector, "fake", speechRepairer: repairer)
        { WaitForWorkAsync = deadlines.WaitAsync };
        try
        {
            controller.Start();
            hotkey.Press();
            audio.EmitChunk(new float[samples]);
            hotkey.Release();
            var inference = await deadlines.NextAsync();
            if (inference.Budget == TimeSpan.FromSeconds(3))
            {
                await inference.Work.WaitAsync(TimeSpan.FromSeconds(5));
                inference = await deadlines.NextAsync();
            }
            inference.Budget.ShouldBe(TimeSpan.FromSeconds(seconds));
            await transcriber.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (stopFirst)
            {
                var stop = controller.StopAsync();
                var stopDeadline = await deadlines.NextAsync();
                stopDeadline.Budget.ShouldBe(TimeSpan.FromSeconds(3));
                stopDeadline.Expired.SetResult();
                await Should.ThrowAsync<TimeoutException>(() => stop);
            }
            else
            {
                inference.Expired.SetResult();
                await WaitForStatusAsync(controller, DictationStatus.Error);
            }
            controller.RequiresRestart.ShouldBeTrue();
            controller.State.Message.ShouldBe("RecognitionRestartRequired");
            hotkey.Press();
            transcriber.Calls.ShouldBe(1);
            var disposal = controller.DisposeAsync().AsTask();
            // Dispose has its own public deadline; the actual stop/cleanup task remains owned.
            var pending = await deadlines.NextAsync();
            pending.Expired.SetResult();
            if (!stopFirst)
            {
                pending = await deadlines.NextAsync();
                pending.Expired.SetResult();
            }
            await Should.ThrowAsync<TimeoutException>(() => disposal);
            transcriber.DisposeCalls.ShouldBe(0);
            audio.DisposeCalls.ShouldBe(0);
            repairer.IsDisposed.ShouldBeFalse();
            transcriber.Returned.SetResult("late text");
            await controller.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            transcriber.DisposeCalls.ShouldBe(1);
            audio.DisposeCalls.ShouldBe(1);
            repairer.IsDisposed.ShouldBeTrue();
            injector.Injected.ShouldBeEmpty();
            controller.State.Status.ShouldBe(DictationStatus.Error);
        }
        finally
        {
            transcriber.Returned.TrySetResult("late text");
            controller.WaitForWorkAsync = static (work, budget, cancellation) => work.WaitAsync(budget, cancellation);
            try { await controller.DisposeAsync(); } catch (TimeoutException) { }
            await controller.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CaptureStopIsAlsoOwnedUntilActualCompletion()
    {
        var audio = new FakeAudioCapture { DelayStop = true };
        var hotkey = new FakeHotkeySource();
        var transcriber = new SpyTranscriber();
        using var deadlines = new ManualDeadlines();
        var controller = new DictationController(audio, hotkey, transcriber, new FakeTextInjector(), "fake")
        { WaitForWorkAsync = deadlines.WaitAsync };
        try
        {
            controller.Start(); hotkey.Press(); audio.EmitChunk(new float[160]); hotkey.Release();
            var captureStop = await deadlines.NextAsync();
            captureStop.Budget.ShouldBe(TimeSpan.FromSeconds(3));
            captureStop.Expired.SetResult();
            await WaitForStatusAsync(controller, DictationStatus.Error);
            var disposal = controller.DisposeAsync().AsTask();
            (await deadlines.NextAsync()).Expired.SetResult();
            (await deadlines.NextAsync()).Expired.SetResult();
            await Should.ThrowAsync<TimeoutException>(() => disposal);
            audio.DisposeCalls.ShouldBe(0);
            transcriber.DisposeCalls.ShouldBe(0);
            audio.StopGate.SetResult();
            await controller.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            audio.DisposeCalls.ShouldBe(1);
            transcriber.DisposeCalls.ShouldBe(1);
            transcriber.TranscribeCalls.ShouldBe(0);
        }
        finally
        {
            audio.StopGate.TrySetResult();
            controller.WaitForWorkAsync = static (work, budget, cancellation) => work.WaitAsync(budget, cancellation);
            try { await controller.DisposeAsync(); } catch (TimeoutException) { }
            await controller.DisposalCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
