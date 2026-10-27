using Shouldly;
using Xunit;
using System.Reflection;
using Murmur.Core;
using Murmur.Core.Abstractions;
using Murmur.Core.Settings;
using Murmur.Core.State;

namespace Murmur.Speech.Tests;

public class NativeSessionLifetimeTests
{
    private sealed class Audio : IAudioCapture
    {
        public event Action<ReadOnlyMemory<float>>? AudioChunkAvailable;
        public event Action<float>? LevelChanged { add { } remove { } }
        public event Action<Exception>? CaptureFailed { add { } remove { } }
        public bool IsRunning { get; private set; }
        public AudioCaptureFormat? Format => null;
        public AudioCaptureStats? LastSessionStats => null;
        public void Start() => IsRunning = true;
        public void Emit() => AudioChunkAvailable?.Invoke(new float[160]);
        public Task StopAsync(CancellationToken cancellationToken = default) { IsRunning = false; return Task.CompletedTask; }
        public void Dispose() { }
    }
    private sealed class Keys : IHotkeySource
    {
        public event Action? Pressed;
        public event Action? Released;
        public void Press() => Pressed?.Invoke();
        public void Release() => Released?.Invoke();
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Injector : ITextInjector
    {
        public int Insertions { get; private set; }
        public TextInjectionTarget CaptureTarget() => new(1, 2);
        public void Insert(string text, TextInjectionTarget target, TextInsertionMode mode, bool restorePreviousWindow = false)
            => Insertions++;
    }

    [Fact]
    public async Task ControllerTimeoutRetainsNemotronFinalDecodeAndRecoveryUntilActualCleanup()
    {
        using var unblock = new ManualResetEventSlim();
        var finalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partialSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var final = false;
        var ready = true;
        var streamsDisposed = 0;
        var recognizersDisposed = 0;
        var transcriber = new NemotronStreamingTranscriber(() => new NemotronStreamingTranscriber.NativeSession(
            _ => { }, () => ready, () =>
            {
                if (final) { finalEntered.TrySetResult(); unblock.Wait(); }
                ready = false;
            }, () => "partial recovery", () => { final = true; ready = true; },
            () => Interlocked.Increment(ref streamsDisposed)), () => Interlocked.Increment(ref recognizersDisposed));
        var audio = new Audio();
        var keys = new Keys();
        var injector = new Injector();
        var controller = new DictationController(audio, keys, transcriber, injector, "fake");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        async Task Wait(Task work, TimeSpan budget, CancellationToken cancellation)
        {
            if (budget == TimeSpan.FromSeconds(30))
            {
                var winner = await Task.WhenAny(work, deadline.Task).WaitAsync(cancellation);
                if (winner == deadline.Task) throw new TimeoutException();
            }
            else if (budget == TimeSpan.FromSeconds(3) && deadline.Task.IsCompleted) throw new TimeoutException();
            await work.WaitAsync(cancellation);
        }
        typeof(DictationController).GetProperty("WaitForWorkAsync", flags)!.SetValue(controller,
            (Func<Task, TimeSpan, CancellationToken, Task>)Wait);
        controller.TranscriptChanged += text => { if (text == "partial recovery") partialSeen.TrySetResult(); };
        controller.StateChanged += state => { if (state.Status == DictationStatus.Error) timedOut.TrySetResult(); };
        try
        {
            controller.Start(); keys.Press(); audio.Emit();
            await partialSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            keys.Release();
            await finalEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            deadline.SetResult();
            await timedOut.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.RequiresRestart.ShouldBeTrue();
            controller.CurrentTranscript.ShouldBe("partial recovery");
            await Should.ThrowAsync<TimeoutException>(() => controller.DisposeAsync().AsTask());
            streamsDisposed.ShouldBe(0);
            recognizersDisposed.ShouldBe(0);
            unblock.Set();
            await ((Task)typeof(DictationController).GetProperty("DisposalCompletion", flags)!
                .GetValue(controller)!).WaitAsync(TimeSpan.FromSeconds(5));
            streamsDisposed.ShouldBe(1);
            recognizersDisposed.ShouldBe(1);
            injector.Insertions.ShouldBe(0);
        }
        finally
        {
            unblock.Set();
            try { await controller.DisposeAsync(); } catch (TimeoutException) { }
            await ((Task)typeof(DictationController).GetProperty("DisposalCompletion", flags)!
                .GetValue(controller)!).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderOrFinalDecodeRetainsWholeNativeChainUntilActuallyReturned(bool finalDecode)
    {
        using var unblock = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streamDisposals = 0;
        var recognizerDisposals = 0;
        var final = false;
        var ready = true;
        var transcriber = new NemotronStreamingTranscriber(() => new NemotronStreamingTranscriber.NativeSession(
            _ => { }, () => ready && (!finalDecode || final), () =>
            {
                entered.TrySetResult();
                unblock.Wait();
                ready = false;
            }, () => "late result", () => final = true, () => Interlocked.Increment(ref streamDisposals)),
            () => Interlocked.Increment(ref recognizerDisposals));
        Task? disposal = null;
        try
        {
            transcriber.BeginSession();
            if (!finalDecode) transcriber.AcceptAudio(new float[160]);
            using var cancellation = new CancellationTokenSource();
            var completion = transcriber.CompleteSessionAsync(cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => completion);
            Should.Throw<InvalidOperationException>(transcriber.BeginSession);
            var cancelled = transcriber.CancelSessionAsync();
            disposal = Task.Run(() => { disposalStarted.SetResult(); transcriber.Dispose(); });
            await disposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancelled.IsCompleted.ShouldBeFalse();
            streamDisposals.ShouldBe(0);
            recognizerDisposals.ShouldBe(0);
            unblock.Set();
            await Task.WhenAll(cancelled, disposal).WaitAsync(TimeSpan.FromSeconds(5));
            streamDisposals.ShouldBe(1);
            recognizerDisposals.ShouldBe(1);
            transcriber.Dispose();
            recognizerDisposals.ShouldBe(1);
        }
        finally
        {
            unblock.Set();
            if (disposal != null) await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            transcriber.Dispose();
        }
    }
}
