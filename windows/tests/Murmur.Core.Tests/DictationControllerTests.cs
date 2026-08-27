using Murmur.Core.Abstractions;
using Murmur.Core.Formatting;
using Murmur.Core.Settings;
using Murmur.Core.State;
using Murmur.Dictionary;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public class DictationControllerTests
{
    private static async Task WaitForStatusAsync(DictationController controller, DictationStatus status)
    {
        if (controller.State.Status == status)
        {
            return;
        }

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(DictationState state)
        {
            if (state.Status == status)
            {
                reached.TrySetResult();
            }
        }

        controller.StateChanged += OnStateChanged;
        try
        {
            if (controller.State.Status == status)
            {
                return;
            }

            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            controller.StateChanged -= OnStateChanged;
        }
    }

    private sealed class FakeAudioCapture : IAudioCapture
    {
        public event Action<ReadOnlyMemory<float>>? AudioChunkAvailable;
        public event Action<float>? LevelChanged;
        public bool IsRunning { get; private set; }
        public AudioCaptureFormat? Format { get; } = new(16000, 1, 32, "IeeeFloat");
        public AudioCaptureStats? LastSessionStats { get; } = new(0.1, 0.2f, 0, 1600);
        public float[]? EmitOnStart { get; set; }
        public bool ThrowOnStart { get; set; }
        public Exception? StopException { get; set; }

        public TaskCompletionSource StopGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DelayStop { get; set; }

        public void Start()
        {
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("capture start failed");
            }

            IsRunning = true;
            if (EmitOnStart != null && EmitOnStart.Length > 0)
            {
                AudioChunkAvailable?.Invoke(EmitOnStart);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            if (StopException != null)
            {
                return Task.FromException(StopException);
            }
            return DelayStop ? StopGate.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
        }

        public void EmitChunk(float[] chunk) => AudioChunkAvailable?.Invoke(chunk);
        public void EmitLevel(float level) => LevelChanged?.Invoke(level);
        public void Dispose() { }
    }

    private sealed class FakeHotkeySource : IHotkeySource
    {
        public event Action? Pressed;
        public event Action? Released;

        public void Press() => Pressed?.Invoke();
        public void Release() => Released?.Invoke();

        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        private readonly string _transcript;
        public FakeTranscriber(string transcript) => _transcript = transcript;

        public Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default)
            => Task.FromResult(_transcript);

        public void Dispose() { }
    }

    private sealed class FakeStreamingTranscriber : IStreamingTranscriber
    {
        private readonly string _finalText;
        public event Action<string>? PartialTranscriptChanged;
        public List<float> AcceptedSamples { get; } = [];
        public bool SessionActive { get; private set; }
        public int CancelCalls { get; private set; }

        public FakeStreamingTranscriber(string finalText)
        {
            _finalText = finalText;
        }

        public void BeginSession()
        {
            SessionActive = true;
        }

        public void AcceptAudio(ReadOnlyMemory<float> audioSamples)
        {
            AcceptedSamples.AddRange(audioSamples.Span);
        }

        public Task<string> CompleteSessionAsync(CancellationToken cancellationToken = default)
        {
            SessionActive = false;
            return Task.FromResult(_finalText);
        }

        public void CancelSession()
        {
            CancelCalls++;
            SessionActive = false;
        }

        public Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_finalText);
        }

        public void EmitPartial(string partial)
        {
            PartialTranscriptChanged?.Invoke(partial);
        }

        public void Dispose() { }
    }

    private sealed class SpyTranscriber : ITranscriber
    {
        public int TranscribeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int LastSampleCount { get; private set; }
        public TaskCompletionSource Transcribed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default)
        {
            TranscribeCalls++;
            LastSampleCount = audioSamples.Length;
            Transcribed.TrySetResult();
            return Task.FromResult("test");
        }

        public void Dispose()
        {
            DisposeCalls++;
        }
    }

    private sealed class FakeTextInjector : ITextInjector
    {
        private readonly Action<string>? _onInsert;

        public FakeTextInjector(Action<string>? onInsert = null) => _onInsert = onInsert;

        public List<string> Injected { get; } = [];

        public void Insert(string text)
        {
            Injected.Add(text);
            _onInsert?.Invoke(text);
        }
    }

    [Fact]
    public async Task Complete_Dictation_Cycle_Formats_And_Injects_Text()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injector = new FakeTextInjector();
        var entry = DictionaryEntry.Correction("claude code", "Claude Code");
        var dictionary = new DictionaryCorrector([entry]);

        using var controller = new DictationController(
            audio,
            hotkey,
            new FakeTranscriber("um hello world claude code new line test"),
            injector,
            "test-engine",
            new RuleBasedFormatter(),
            dictionary
        );

        controller.Start();
        controller.State.Status.ShouldBe(DictationStatus.Idle);

        // Press hotkey
        hotkey.Press();
        controller.State.Status.ShouldBe(DictationStatus.Listening);
        audio.IsRunning.ShouldBeTrue();

        // Feed audio chunks
        audio.EmitChunk([0.1f, 0.2f, -0.1f]);
        audio.EmitLevel(0.8f);

        // Release hotkey
        hotkey.Release();

        // Wait for async finishing to complete
        await Task.Delay(100);

        controller.State.Status.ShouldBe(DictationStatus.Idle);
        injector.Injected.Count.ShouldBe(1);
        injector.Injected[0].ShouldBe("Hello world Claude Code\ntest.");
    }

    [Fact]
    public async Task Dictionary_CanBeReplaced_WithoutRestartingController()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injector = new FakeTextInjector();

        using var controller = new DictationController(
            audio,
            hotkey,
            new FakeTranscriber("cloud code"),
            injector,
            "test-engine");

        controller.SetDictionary([DictionaryEntry.Correction("cloud code", "Claude Code")]);
        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await Task.Delay(100);

        injector.Injected.ShouldContain("Claude Code.");
    }

    [Fact]
    public async Task Reuses_Transcriber_Across_Multiple_Dictations()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injector = new FakeTextInjector();
        var transcriber = new SpyTranscriber();

        var controller = new DictationController(
            audio,
            hotkey,
            transcriber,
            injector,
            "test-engine"
        );

        controller.Start();

        // Dictation 1
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        var firstIdle = WaitForStatusAsync(controller, DictationStatus.Idle);
        hotkey.Release();
        await firstIdle;

        // Dictation 2
        hotkey.Press();
        audio.EmitChunk([0.2f]);
        var secondIdle = WaitForStatusAsync(controller, DictationStatus.Idle);
        hotkey.Release();
        await secondIdle;

        transcriber.TranscribeCalls.ShouldBe(2);
        transcriber.DisposeCalls.ShouldBe(0);

        controller.Dispose();
        transcriber.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Completion_Event_Precedes_Injection_And_Carries_Final_Text()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var order = new List<string>();
        var injected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var injector = new FakeTextInjector(_ =>
        {
            order.Add("inject");
            injected.SetResult();
        });
        CompletedDictation? completed = null;

        using var controller = new DictationController(
            audio,
            hotkey,
            new FakeTranscriber("hello world"),
            injector,
            "Parakeet v3");
        controller.DictationCompleted += value =>
        {
            completed = value;
            order.Add("history");
        };

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(1));

        order.ShouldBe(["history", "inject"]);
        completed.ShouldNotBeNull();
        completed!.Engine.ShouldBe("Parakeet v3");
        completed.Text.ShouldBe("Hello world.");
        completed.AudioDurationMs.ShouldBeGreaterThan(0);
        completed.InferenceDurationMs.ShouldBeGreaterThanOrEqualTo(0);
        completed.Timestamp.ShouldBeGreaterThan(DateTimeOffset.Now.AddMinutes(-1));
    }

    [Fact]
    public async Task Completion_Subscriber_Failure_Does_Not_Block_Injection()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var controller = new DictationController(
            audio,
            hotkey,
            new FakeTranscriber("recoverable text"),
            new FakeTextInjector(_ => injected.SetResult()),
            "Parakeet v3");
        controller.DictationCompleted += _ => throw new IOException("history unavailable");

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(1));

        controller.State.Status.ShouldBe(DictationStatus.Idle);
    }

    [Fact]
    public async Task Release_Waits_For_Capture_Stop_Before_Transcribing()
    {
        var audio = new FakeAudioCapture { DelayStop = true };
        var hotkey = new FakeHotkeySource();
        var transcriber = new SpyTranscriber();
        using var controller = new DictationController(
            audio, hotkey, transcriber, new FakeTextInjector(), "test-engine");

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await Task.Delay(80);

        transcriber.TranscribeCalls.ShouldBe(0);
        audio.EmitChunk([0.2f]);
        audio.StopGate.SetResult();
        await Task.Delay(80);

        transcriber.LastSampleCount.ShouldBe(2);
    }

    [Fact]
    public async Task Chunk_Raised_During_Start_Is_Not_Dropped()
    {
        var audio = new FakeAudioCapture { EmitOnStart = [0.1f, 0.2f] };
        var hotkey = new FakeHotkeySource();
        var transcriber = new SpyTranscriber();
        using var controller = new DictationController(
            audio, hotkey, transcriber, new FakeTextInjector(), "test-engine");

        controller.Start();
        hotkey.Press();
        hotkey.Release();
        await transcriber.Transcribed.Task.WaitAsync(TimeSpan.FromSeconds(1));

        transcriber.LastSampleCount.ShouldBe(2);
    }

    [Fact]
    public async Task Streaming_Partial_Is_Shown_Before_Release_But_Not_Completed()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("final words");
        var partials = new List<string>();
        var completions = new List<CompletedDictation>();
        using var controller = new DictationController(
            audio, hotkey, transcriber, new FakeTextInjector(), "Nemotron 3.5");
        controller.TranscriptChanged += partials.Add;
        controller.DictationCompleted += completions.Add;

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f, 0.2f]);
        transcriber.EmitPartial("live words");

        controller.CurrentTranscript.ShouldBe("live words");
        partials.ShouldContain("live words");
        completions.ShouldBeEmpty();

        hotkey.Release();
        await Task.Delay(100);
        completions.Single().Text.ShouldBe("Final words.");
    }

    [Fact]
    public void Late_Partial_After_Idle_Is_Ignored()
    {
        var transcriber = new FakeStreamingTranscriber("final");
        using var controller = new DictationController(
            new FakeAudioCapture(), new FakeHotkeySource(), transcriber,
            new FakeTextInjector(), "Nemotron 3.5");

        transcriber.EmitPartial("stale");
        controller.CurrentTranscript.ShouldBeEmpty();
    }

    [Fact]
    public async Task Capture_Stop_Error_Enters_Error_State()
    {
        var audio = new FakeAudioCapture
        {
            StopException = new IOException("device disconnected")
        };
        var hotkey = new FakeHotkeySource();
        var error = new TaskCompletionSource<DictationState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = new DictationController(
            audio, hotkey, new FakeTranscriber("unused"),
            new FakeTextInjector(), "test-engine");
        controller.StateChanged += state =>
        {
            if (state.Status == DictationStatus.Error)
            {
                error.TrySetResult(state);
            }
        };

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();

        var state = await error.Task.WaitAsync(TimeSpan.FromSeconds(1));
        state.Message.ShouldNotBeNull();
        state.Message.ShouldContain("device disconnected");
    }

    [Fact]
    public void Capture_Start_Error_Cancels_Streaming_Session()
    {
        var audio = new FakeAudioCapture { ThrowOnStart = true };
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("unused");
        using var controller = new DictationController(
            audio, hotkey, transcriber, new FakeTextInjector(), "Nemotron 3.5");

        controller.Start();
        hotkey.Press();

        controller.State.Status.ShouldBe(DictationStatus.Error);
        transcriber.SessionActive.ShouldBeFalse();
        transcriber.CancelCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Capture_Stop_Error_Cancels_Streaming_Session()
    {
        var audio = new FakeAudioCapture
        {
            StopException = new IOException("device disconnected")
        };
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("unused");
        var error = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = new DictationController(
            audio, hotkey, transcriber, new FakeTextInjector(), "Nemotron 3.5");
        controller.StateChanged += state =>
        {
            if (state.Status == DictationStatus.Error)
            {
                error.TrySetResult();
            }
        };

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await error.Task.WaitAsync(TimeSpan.FromSeconds(1));

        transcriber.SessionActive.ShouldBeFalse();
        transcriber.CancelCalls.ShouldBe(1);
    }

    [Fact]
    public async Task TextInjected_Fires_After_TextInjector_Returns()
    {
        var order = new List<string>();
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injector = new FakeTextInjector(_ => order.Add("inject"));

        using var controller = new DictationController(
            audio, hotkey, new FakeTranscriber("hello world"), injector, "test-engine");

        controller.DictationCompleted += _ => order.Add("history");
        var textInjected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.TextInjected += _ =>
        {
            order.Add("feedback");
            textInjected.TrySetResult();
        };

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await textInjected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        order.ShouldBe(["history", "inject", "feedback"]);
    }

    [Fact]
    public async Task TextInjected_Subscriber_Failure_Does_Not_Fail_Dictation()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injector = new FakeTextInjector();

        using var controller = new DictationController(
            audio, hotkey, new FakeTranscriber("hello world"), injector, "test-engine");
        controller.TextInjected += _ => throw new InvalidOperationException("feedback failed");

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await Task.Delay(100);

        injector.Injected.Count.ShouldBe(1);
        controller.State.Status.ShouldBe(DictationStatus.Idle);
    }

    [Fact]
    public void Streaming_Partials_Are_Forwarded_In_Order_And_Whitespace_Is_Ignored()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("final");
        var partials = new List<string>();
        using var controller = new DictationController(
            audio, hotkey, transcriber, new FakeTextInjector(), "Nemotron 3.5");

        controller.TranscriptChanged += partials.Add;
        controller.Start();
        hotkey.Press();
        transcriber.EmitPartial("Das");
        transcriber.EmitPartial("   ");
        transcriber.EmitPartial("Das ist");
        transcriber.EmitPartial("\t");

        partials.ShouldBe([string.Empty, "Das", "Das ist"]);
        controller.CurrentTranscript.ShouldBe("Das ist");
    }

    [Fact]
    public async Task Streaming_Partial_Does_Not_Inject_Before_Final_Result()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("final words");
        var injector = new FakeTextInjector();
        using var controller = new DictationController(
            audio, hotkey, transcriber, injector, "Nemotron 3.5");

        controller.Start();
        hotkey.Press();
        transcriber.EmitPartial("live words");

        injector.Injected.ShouldBeEmpty();
        controller.State.Status.ShouldBe(DictationStatus.Listening);

        hotkey.Release();
        await Task.Delay(100);

        injector.Injected.Count.ShouldBe(1);
        injector.Injected[0].ShouldBe("Final words.");
    }

    [Fact]
    public async Task New_Streaming_Session_Clears_Previous_Transcript()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeStreamingTranscriber("final");
        var partials = new List<string>();
        using var controller = new DictationController(
            audio, hotkey, transcriber, new FakeTextInjector(), "Nemotron 3.5");

        controller.TranscriptChanged += partials.Add;
        controller.Start();

        hotkey.Press();
        transcriber.EmitPartial("old words");
        hotkey.Release();
        await Task.Delay(100);

        hotkey.Press();

        controller.CurrentTranscript.ShouldBeEmpty();
        partials[^1].ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateProfile_Uses_Math_Formatter_For_Next_Dictation()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var injector = new FakeTextInjector(_ => injected.SetResult());

        using var controller = new DictationController(
            audio,
            hotkey,
            new FakeTranscriber("x plus y gleich z"),
            injector,
            "test-engine");

        var mathProfile = new DictationProfile
        {
            Id = "math",
            Name = "Mathe",
            FormatterKind = ProfileFormatterKind.Math
        };
        var mathFormatter = new GermanMathFormatter(MathFormatMode.Unicode);

        controller.UpdateProfile(mathProfile, mathFormatter);
        controller.Start();

        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        injector.Injected.Count.ShouldBe(1);
        injector.Injected[0].ShouldBe("x + y = z.");
    }

    [Fact]
    public void CurrentProfile_And_ProfileChanged_Track_The_Swapped_Profile()
    {
        using var controller = new DictationController(
            new FakeAudioCapture(),
            new FakeHotkeySource(),
            new FakeTranscriber("unused"),
            new FakeTextInjector(),
            "test-engine");

        controller.CurrentProfile.Id.ShouldBe("standard");

        DictationProfile? notifiedProfile = null;
        controller.ProfileChanged += p => notifiedProfile = p;

        var mathProfile = new DictationProfile
        {
            Id = "math",
            Name = "Mathe",
            FormatterKind = ProfileFormatterKind.Math
        };
        controller.UpdateProfile(mathProfile, new GermanMathFormatter());

        controller.CurrentProfile.ShouldBe(mathProfile);
        notifiedProfile.ShouldBe(mathProfile);
    }

    [Fact]
    public async Task Profile_Update_During_Active_Dictation_Applies_On_Next_Dictation()
    {
        var audio = new FakeAudioCapture { DelayStop = true };
        var hotkey = new FakeHotkeySource();
        var injected1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var injected2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var injector = new FakeTextInjector(_ =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                injected1.SetResult();
            }
            else
            {
                injected2.SetResult();
            }
        });
        var transcriber = new FakeTranscriber("x plus y gleich z");

        using var controller = new DictationController(
            audio,
            hotkey,
            transcriber,
            injector,
            "test-engine");

        controller.Start();

        // 1. Start first dictation under Standard profile
        hotkey.Press();
        audio.EmitChunk([0.1f]);

        // Swap to Math profile while first session is still active
        var mathProfile = new DictationProfile
        {
            Id = "math",
            Name = "Mathe",
            FormatterKind = ProfileFormatterKind.Math
        };
        controller.UpdateProfile(mathProfile, new GermanMathFormatter(MathFormatMode.Unicode));

        // Release first dictation and ungate stop
        hotkey.Release();
        audio.StopGate.SetResult();
        await injected1.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // First result used the snapshot captured on press (Standard -> "X plus y gleich z.")
        injector.Injected.Count.ShouldBe(1);
        injector.Injected[0].ShouldBe("X plus y gleich z.");

        // 2. Start second dictation under Math profile
        audio.DelayStop = false;
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await injected2.Task.WaitAsync(TimeSpan.FromSeconds(2));

        injector.Injected.Count.ShouldBe(2);
        injector.Injected[1].ShouldBe("x + y = z.");
    }

    [Fact]
    public async Task SetDictionary_Still_Affects_Later_Dictations_After_Profile_Update()
    {
        var audio = new FakeAudioCapture();
        var hotkey = new FakeHotkeySource();
        var injected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var injector = new FakeTextInjector(_ => injected.SetResult());

        using var controller = new DictationController(
            audio,
            hotkey,
            new FakeTranscriber("cloud code"),
            injector,
            "test-engine");

        var mathProfile = new DictationProfile
        {
            Id = "math",
            Name = "Mathe",
            FormatterKind = ProfileFormatterKind.Math
        };
        controller.UpdateProfile(mathProfile, new GermanMathFormatter(MathFormatMode.Unicode));
        controller.SetDictionary([DictionaryEntry.Correction("cloud code", "Claude Code")]);

        controller.Start();
        hotkey.Press();
        audio.EmitChunk([0.1f]);
        hotkey.Release();
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(2));

        injector.Injected.Count.ShouldBe(1);
        injector.Injected[0].ShouldBe("Claude Code.");
    }
}
