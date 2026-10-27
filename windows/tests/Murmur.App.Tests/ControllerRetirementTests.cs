using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Murmur.Core;
using Murmur.Core.Abstractions;
using Shouldly;

namespace Murmur.App.Tests;

public class ControllerRetirementTests
{
    private sealed class Audio : IAudioCapture
    {
        public event Action<ReadOnlyMemory<float>>? AudioChunkAvailable;
        public event Action<float>? LevelChanged { add { } remove { } }
        public event Action<Exception>? CaptureFailed { add { } remove { } }
        public bool IsRunning { get; private set; }
        public AudioCaptureFormat? Format => null;
        public AudioCaptureStats? LastSessionStats => null;
        public int Disposals { get; private set; }
        public void Start() { IsRunning = true; AudioChunkAvailable?.Invoke(new float[160]); }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsRunning = false; return Task.CompletedTask; }
        public void Dispose() => Disposals++;
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
    private sealed class Native : ITranscriber
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals { get; private set; }
        public Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default)
        { Entered.SetResult(); return Returned.Task; }
        public void Dispose() { Returned.Task.IsCompleted.ShouldBeTrue(); Disposals++; }
    }
    private sealed class Injector : ITextInjector
    {
        public TextInjectionTarget CaptureTarget() => new(1, 2);
        public void Insert(string text, TextInjectionTarget target, Murmur.Core.Settings.TextInsertionMode mode,
            bool restorePreviousWindow = false) => throw new InvalidOperationException("Late result must never be inserted");
    }

    [AvaloniaFact]
    public async Task ModelSwitchCannotCreateReplacementAfterBoundedControllerDisposal()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var app = Application.Current.ShouldBeOfType<App>();
        var ownerField = typeof(App).GetField("_controller", flags)!;
        var recoveryField = typeof(App).GetField("_recoveryTranscript", flags)!;
        var previousRecovery = recoveryField.GetValue(app);
        ownerField.GetValue(app).ShouldBeNull();
        var audio = new Audio();
        var keys = new Keys();
        var native = new Native();
        var controller = new DictationController(audio, keys, native, new Injector(), "fake");
        var expire = false;
        Func<Task, TimeSpan, CancellationToken, Task> wait = (work, budget, cancellation) =>
            expire && budget == TimeSpan.FromSeconds(3) ? Task.FromException(new TimeoutException()) : work.WaitAsync(cancellation);
        typeof(DictationController).GetProperty("WaitForWorkAsync", flags)!.SetValue(controller, wait);
        ownerField.SetValue(app, controller);
        try
        {
            controller.Start(); keys.Press(); keys.Release();
            await native.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            typeof(DictationController).GetProperty(nameof(DictationController.CurrentTranscript))!
                .SetValue(controller, "incomplete recovery");
            expire = true;
            var retirement = (Task)typeof(App).GetMethod("DisposeControllerAsync", flags)!.Invoke(app, null)!;
            await Should.ThrowAsync<InvalidOperationException>(() => retirement);
            typeof(App).GetField("_recognitionBlocked", flags)!.GetValue(app).ShouldBe(true);
            recoveryField.GetValue(app).ShouldBe("incomplete recovery");
            native.Disposals.ShouldBe(0);
            audio.Disposals.ShouldBe(0);
            await (Task)typeof(App).GetMethod("StartControllerAsync", flags)!.Invoke(app, null)!;
            ownerField.GetValue(app).ShouldBeNull();
            native.Returned.SetResult("late");
            await ((Task)typeof(DictationController).GetProperty("DisposalCompletion", flags)!
                .GetValue(controller)!).WaitAsync(TimeSpan.FromSeconds(5));
            native.Disposals.ShouldBe(1);
            audio.Disposals.ShouldBe(1);
        }
        finally
        {
            native.Returned.TrySetResult("late");
            expire = false;
            try { await controller.DisposeAsync(); } catch (TimeoutException) { }
            await ((Task)typeof(DictationController).GetProperty("DisposalCompletion", flags)!
                .GetValue(controller)!).WaitAsync(TimeSpan.FromSeconds(5));
            ownerField.SetValue(app, null);
            recoveryField.SetValue(app, previousRecovery);
            typeof(App).GetField("_recognitionBlocked", flags)!.SetValue(app, false);
        }
    }
}
