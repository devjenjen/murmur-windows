using Murmur.Core.Abstractions;
using Murmur.Core.State;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public partial class DictationControllerTests
{
    private sealed class EdgeHotkey : IHotkeySource
    {
        private bool _pressed;
        public event Action? Pressed;
        public event Action? Released;
        public void Press() { if (!_pressed) { _pressed = true; Pressed?.Invoke(); } }
        public void Release() { if (_pressed) { _pressed = false; Released?.Invoke(); } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    [Theory]
    [InlineData(4800000)]
    [InlineData(4800001)]
    [InlineData(5000000)]
    public async Task DurationLimitAutoFinishesAtExactBoundaryWithoutWaitingInCallback(int length)
    {
        var audio = new FakeAudioCapture { DelayStop = true };
        var keys = new EdgeHotkey();
        var transcriber = new SpyTranscriber();
        var injector = new FakeTextInjector();
        await using var controller = new DictationController(audio, keys, transcriber, injector, "fake");
        try
        {
            controller.Start(); keys.Press();
            audio.EmitChunk(new float[length]);
            controller.State.Status.ShouldBe(DictationStatus.Finishing);
            controller.State.Message.ShouldBe("RecordingDurationLimit");
            controller.SessionCompletion.IsCompleted.ShouldBeFalse();
            transcriber.TranscribeCalls.ShouldBe(0);
            audio.EmitChunk(new float[20]);
            audio.StopGate.TrySetResult();
            await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            transcriber.LastSampleCount.ShouldBe(4800000);
            injector.Injected.Count.ShouldBe(1);
            controller.State.Message.ShouldBe("RecordingDurationLimit");
            // Match the production hook's existing key-down deduplication after automatic stop.
            keys.Press();
            controller.State.Status.ShouldBe(DictationStatus.Error);
            keys.Release(); keys.Press();
            controller.State.Status.ShouldBe(DictationStatus.Listening);
            audio.EmitChunk(new float[1]); keys.Release();
            await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            transcriber.LastSampleCount.ShouldBe(1);
        }
        finally { audio.StopGate.TrySetResult(); }
    }

    [Fact]
    public async Task StreamingFailureStopsCaptureOffCallbackAndPreservesIncompletePartial()
    {
        var audio = new FakeAudioCapture { DelayStop = true };
        var keys = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("must not insert");
        var injector = new FakeTextInjector();
        await using var controller = new DictationController(audio, keys, transcriber, injector, "fake");
        try
        {
            controller.Start(); keys.Press(); transcriber.EmitPartial("unvollstaendig");
            transcriber.Fail(new InvalidOperationException("StreamingBacklogExceeded"));
            controller.State.Status.ShouldBe(DictationStatus.Error);
            controller.State.Message.ShouldBe("StreamingBacklogExceeded");
            controller.CurrentTranscript.ShouldBe("unvollstaendig");
            controller.SessionCompletion.IsCompleted.ShouldBeFalse();
            injector.Injected.ShouldBeEmpty();
            audio.StopGate.TrySetResult();
            await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            controller.RequiresRestart.ShouldBeFalse();
            keys.Press(); controller.State.Status.ShouldBe(DictationStatus.Listening);
        }
        finally { audio.StopGate.TrySetResult(); }
    }

    [Fact]
    public async Task AcceptAudioExceptionCannotEscapeCaptureCallback()
    {
        var audio = new FakeAudioCapture();
        var keys = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("must not insert") { AcceptFailure = new IOException("queue failed") };
        var injector = new FakeTextInjector();
        await using var controller = new DictationController(audio, keys, transcriber, injector, "fake");
        controller.Start(); keys.Press();
        Should.NotThrow(() => audio.EmitChunk(new float[1]));
        await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.State.Status.ShouldBe(DictationStatus.Error);
        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task BelowDurationLimitKeepsListeningUntilRelease()
    {
        var audio = new FakeAudioCapture();
        var keys = new EdgeHotkey();
        var transcriber = new SpyTranscriber();
        await using var controller = new DictationController(audio, keys, transcriber, new FakeTextInjector(), "fake");
        controller.Start(); keys.Press();
        audio.EmitChunk(new float[4799999]);
        controller.State.Status.ShouldBe(DictationStatus.Listening);
        keys.Release();
        await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        transcriber.LastSampleCount.ShouldBe(4799999);
        controller.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task StreamingDurationAutoFinishDoesNotAcceptPostLimitSamples()
    {
        var audio = new FakeAudioCapture();
        var keys = new EdgeHotkey();
        var transcriber = new FakeStreamingTranscriber("complete to the limit");
        var injector = new FakeTextInjector();
        await using var controller = new DictationController(audio, keys, transcriber, injector, "fake");
        controller.Start(); keys.Press();
        var chunk = new float[16000];
        for (var i = 0; i < 300; i++) audio.EmitChunk(chunk);
        audio.EmitChunk(chunk);
        await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        transcriber.AcceptedSamples.Count.ShouldBe(4800000);
        injector.Injected.Count.ShouldBe(1);
        controller.State.Message.ShouldBe("RecordingDurationLimit");
    }

    [Fact]
    public async Task OldStreamingFailureCallbackCannotAbortNewSession()
    {
        var audio = new FakeAudioCapture();
        var keys = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("next");
        await using var controller = new DictationController(audio, keys, transcriber, new FakeTextInjector(), "fake");
        controller.Start(); keys.Press();
        var oldCallback = transcriber.FailureCallback!;
        transcriber.Fail(new InvalidOperationException("StreamingRecognitionFailed"));
        await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        keys.Press();
        oldCallback(new IOException("late old failure"));
        controller.State.Status.ShouldBe(DictationStatus.Listening);
        audio.EmitChunk(new float[1]); keys.Release();
        await controller.SessionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.State.ShouldBe(DictationState.Idle);
    }
}
