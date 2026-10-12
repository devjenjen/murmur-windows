# Whisper Large-V3-Turbo, Self-Correction & Window Preservation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement offline Whisper Large-V3-Turbo speech transcription, SLM-based conversational self-correction (Qwen 2.5 0.5B via LLamaSharp), and Win32 target window focus preservation in Murmur.

**Architecture:**
- Extend `SendInputInjector` in `Murmur.Platform.Windows` to reactivate the original target window via Win32 thread-input attachment and window foreground restoration instead of aborting when the active window changes.
- Add `WhisperV3Turbo` offline speech model support to `Murmur.Speech` using `sherpa-onnx`'s built-in `OfflineWhisperModelConfig`.
- Introduce `ISpeechRepairer` abstraction in `Murmur.Core.Abstractions` and `LlamaSharpSpeechRepairer` in `Murmur.Speech`, integrating it into `DictationController` between transcription and formatting.
- Expose settings (`EnableSelfCorrection`, `RestorePreviousWindowAfterInjection`, model choice) in `Murmur.App` settings and localization.

**Tech Stack:** C# 13, .NET 10, `org.k2fsa.sherpa.onnx` 1.13.5, `LLamaSharp` 0.22.0, `LLamaSharp.Backend.Cpu` 0.22.0, Avalonia 11.3.20, Win32 User32.

## Global Constraints

- All speech recognition and text repair must run 100% locally and offline on CPU without requiring GPU, Python, or external daemons.
- Central package management in `Directory.Packages.props` must be used for all NuGet additions.
- Existing unit tests (`dotnet test windows/Murmur.sln --filter "Category!=RealModel"`) must continue passing at every step.
- Follow TDD: write a failing test, verify it fails, write minimal code, verify it passes, commit.

---

### Task 1: Target Window Focus Preservation & Restoration

**Files:**
- Modify: `windows/src/Murmur.Platform.Windows/Native/Win32.cs`
- Modify: `windows/src/Murmur.Platform.Windows/Injection/SendInputInjector.cs`
- Modify: `windows/src/Murmur.Core/Settings/AppSettings.cs`
- Test: `windows/tests/Murmur.Platform.Windows.Tests/SendInputInjectorTests.cs`

**Interfaces:**
- Consumes: `TextInjectionTarget(nint WindowHandle, uint ProcessId)`
- Produces: `SendInputInjector.Insert(string text, TextInjectionTarget target, TextInsertionMode mode, bool restorePreviousWindow = false)`

- [ ] **Step 1: Write unit tests for window focus restoration and preservation logic**

In `windows/tests/Murmur.Platform.Windows.Tests/SendInputInjectorTests.cs`, add tests verifying target window recovery handling:
```csharp
[Fact]
public void IsSameTarget_Returns_True_When_Handles_And_Processes_Match()
{
    var a = new TextInjectionTarget(1234, 5678);
    var b = new TextInjectionTarget(1234, 5678);
    SendInputInjector.IsSameTarget(a, b).ShouldBeTrue();
}

[Fact]
public void IsSameTarget_Returns_False_When_Handles_Differ()
{
    var a = new TextInjectionTarget(1234, 5678);
    var b = new TextInjectionTarget(9999, 5678);
    SendInputInjector.IsSameTarget(a, b).ShouldBeFalse();
}
```

- [ ] **Step 2: Run tests to verify test suite passes**

Run: `dotnet test windows/tests/Murmur.Platform.Windows.Tests --filter "FullyQualifiedName~SendInputInjectorTests"`
Expected: PASS

- [ ] **Step 3: Update `Win32.cs` with required window management APIs**

In `windows/src/Murmur.Platform.Windows/Native/Win32.cs`, declare:
```csharp
public const int SW_RESTORE = 9;

[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static extern bool BringWindowToTop(nint hWnd);

[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static extern bool ShowWindow(nint hWnd, int nCmdShow);

[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static extern bool IsIconic(nint hWnd);

[DllImport("kernel32.dll")]
public static extern uint GetCurrentThreadId();
```

- [ ] **Step 4: Update `SendInputInjector.cs` to restore target focus**

In `windows/src/Murmur.Platform.Windows/Injection/SendInputInjector.cs`, add a focus restoration helper:
```csharp
internal static bool RestoreWindowFocus(nint windowHandle)
{
    if (windowHandle == 0 || !Win32.IsWindow(windowHandle))
        return false;

    if (Win32.IsIconic(windowHandle))
    {
        Win32.ShowWindow(windowHandle, Win32.SW_RESTORE);
    }

    var currentForeground = Win32.GetForegroundWindow();
    if (currentForeground == windowHandle)
        return true;

    var currentThreadId = Win32.GetCurrentThreadId();
    var foregroundThreadId = Win32.GetWindowThreadProcessId(currentForeground, out _);
    var targetThreadId = Win32.GetWindowThreadProcessId(windowHandle, out _);

    if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        Win32.AttachThreadInput(currentThreadId, foregroundThreadId, true);
    if (targetThreadId != 0 && targetThreadId != currentThreadId)
        Win32.AttachThreadInput(currentThreadId, targetThreadId, true);

    Win32.SetForegroundWindow(windowHandle);
    Win32.BringWindowToTop(windowHandle);

    if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        Win32.AttachThreadInput(currentThreadId, foregroundThreadId, false);
    if (targetThreadId != 0 && targetThreadId != currentThreadId)
        Win32.AttachThreadInput(currentThreadId, targetThreadId, false);

    for (var i = 0; i < 10; i++)
    {
        if (Win32.GetForegroundWindow() == windowHandle)
            return true;
        Thread.Sleep(10);
    }

    return Win32.GetForegroundWindow() == windowHandle;
}
```

In `Insert(...)`, if `!IsSameTarget(target, currentTarget)`:
```csharp
var intermediateHandle = currentTarget.WindowHandle;
if (!IsSameTarget(target, currentTarget))
{
    if (!RestoreWindowFocus(target.WindowHandle))
    {
        throw new TextInjectionException(TextInjectionFailureReason.TargetUnavailable);
    }
}
```
And if `restorePreviousWindow` is enabled and `intermediateHandle != 0`, restore `intermediateHandle` after `SendInput`.

- [ ] **Step 5: Run tests and verify build**

Run: `dotnet test windows/Murmur.sln --filter "Category!=RealModel"`
Expected: All tests PASS (0 errors).

- [ ] **Step 6: Commit**

```bash
git add windows/src/Murmur.Platform.Windows windows/tests/Murmur.Platform.Windows.Tests
git commit -m "feat(injection): support target window focus restoration on window switch"
```

---

### Task 2: Whisper Large-V3-Turbo Model Support in `Murmur.Speech`

**Files:**
- Modify: `windows/src/Murmur.Speech/ModelManifest.cs`
- Modify: `windows/src/Murmur.Speech/ModelLocator.cs`
- Create: `windows/src/Murmur.Speech/SherpaOnnxWhisperTranscriber.cs`
- Test: `windows/tests/Murmur.Speech.Tests/WhisperTranscriberTests.cs`

**Interfaces:**
- Consumes: `ModelManifest`, `SherpaOnnx.OfflineRecognizerConfig`
- Produces: `SherpaOnnxWhisperTranscriber : ITranscriber`

- [ ] **Step 1: Write test for Whisper manifest and config initialization**

Create `windows/tests/Murmur.Speech.Tests/WhisperTranscriberTests.cs`:
```csharp
namespace Murmur.Speech.Tests;

using Shouldly;
using Xunit;

public class WhisperTranscriberTests
{
    [Fact]
    public void WhisperV3Turbo_Manifest_Is_Registered()
    {
        var manifest = ModelManifest.Get("whisper-v3-turbo");
        manifest.ShouldNotBeNull();
        manifest.Id.ShouldBe("whisper-v3-turbo");
        manifest.SupportsLivePreview.ShouldBeFalse();
        manifest.RequiredFiles.ShouldContain("turbo-encoder.int8.onnx");
        manifest.RequiredFiles.ShouldContain("turbo-decoder.int8.onnx");
        manifest.RequiredFiles.ShouldContain("tokens.txt");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test windows/tests/Murmur.Speech.Tests --filter "FullyQualifiedName~WhisperTranscriberTests"`
Expected: FAIL (manifest not found or defaults to ParakeetV3).

- [ ] **Step 3: Update `ModelManifest.cs` with Whisper Large-V3-Turbo definition**

In `windows/src/Murmur.Speech/ModelManifest.cs`, define:
```csharp
public static ModelManifest WhisperV3Turbo { get; } = new(
    "whisper-v3-turbo",
    "Whisper Large-v3-Turbo (offline)",
    "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-whisper-turbo.tar.bz2",
    1_036_000_000,
    "b54e7bc5be673b5df549eb4ebaa8d53b49ec96cba7b4d1ba83072b2203fa1a80",
    false)
{
    ExtractedSize = 1_080_000_000
};
```
Update `All` and `RequiredFiles` logic to support both Transducer models and Whisper models.

- [ ] **Step 4: Implement `SherpaOnnxWhisperTranscriber.cs`**

Create `windows/src/Murmur.Speech/SherpaOnnxWhisperTranscriber.cs`:
```csharp
using Murmur.Core.Abstractions;
using SherpaOnnx;

namespace Murmur.Speech;

public sealed class SherpaOnnxWhisperTranscriber : ITranscriber
{
    private readonly OfflineRecognizer _recognizer;
    private readonly Lock _resourceLock = new();
    private bool _disposed;

    public SherpaOnnxWhisperTranscriber(string? modelDir = null)
    {
        var dir = modelDir ?? ModelLocator.GetDefaultModelDirectory(ModelManifest.WhisperV3Turbo);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Whisper model files not found in: {dir}");

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 128;

        config.ModelConfig.Whisper.Encoder = Path.Combine(dir, "turbo-encoder.int8.onnx");
        config.ModelConfig.Whisper.Decoder = Path.Combine(dir, "turbo-decoder.int8.onnx");
        config.ModelConfig.Whisper.Language = "";
        config.ModelConfig.Whisper.Task = "transcribe";
        config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        config.ModelConfig.NumThreads = 4;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        _recognizer = new OfflineRecognizer(config);
    }

    public Task<string> TranscribeAsync(ReadOnlyMemory<float> audioSamples, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_resourceLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(16000, audioSamples.ToArray());
                _recognizer.Decode(stream);
                cancellationToken.ThrowIfCancellationRequested();
                return stream.Result.Text ?? string.Empty;
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        lock (_resourceLock)
        {
            if (_disposed) return;
            _disposed = true;
            _recognizer.Dispose();
        }
    }
}
```

- [ ] **Step 5: Run tests and verify passing**

Run: `dotnet test windows/tests/Murmur.Speech.Tests --filter "FullyQualifiedName~WhisperTranscriberTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add windows/src/Murmur.Speech windows/tests/Murmur.Speech.Tests
git commit -m "feat(speech): add Whisper Large-V3-Turbo transcriber and manifest"
```

---

### Task 3: Conversational Self-Correction Interface & SLM Cleaner

**Files:**
- Modify: `windows/Directory.Packages.props`
- Modify: `windows/src/Murmur.Core/Murmur.Core.csproj`
- Create: `windows/src/Murmur.Core/Abstractions/ISpeechRepairer.cs`
- Create: `windows/src/Murmur.Speech/LlamaSharpSpeechRepairer.cs`
- Create: `windows/src/Murmur.Speech/RuleBasedSpeechRepairer.cs`
- Test: `windows/tests/Murmur.Speech.Tests/SpeechRepairerTests.cs`

**Interfaces:**
- Consumes: `rawTranscript` (string)
- Produces: `ISpeechRepairer.RepairAsync(string rawTranscript, CancellationToken cancellationToken)` -> cleaned string

- [ ] **Step 1: Write test for `RuleBasedSpeechRepairer` and `ISpeechRepairer` contract**

Create `windows/tests/Murmur.Speech.Tests/SpeechRepairerTests.cs`:
```csharp
namespace Murmur.Speech.Tests;

using Murmur.Core.Abstractions;
using Shouldly;
using Xunit;

public class SpeechRepairerTests
{
    [Theory]
    [InlineData("Wir treffen uns ja Dienstag. Ach nee, Mittwoch.", "Wir treffen uns Mittwoch.")]
    [InlineData("Ich rufe dich morgen an, warte mal, übermorgen.", "Ich rufe dich übermorgen an.")]
    [InlineData("Das Meeting ist um 10 Uhr, ach nein, 11 Uhr.", "Das Meeting ist um 11 Uhr.")]
    public async Task RuleBasedSpeechRepairer_Removes_Common_German_Self_Corrections(string input, string expected)
    {
        var repairer = new RuleBasedSpeechRepairer();
        var result = await repairer.RepairAsync(input);
        result.ShouldBe(expected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test windows/tests/Murmur.Speech.Tests --filter "FullyQualifiedName~SpeechRepairerTests"`
Expected: FAIL (`RuleBasedSpeechRepairer` does not exist).

- [ ] **Step 3: Define `ISpeechRepairer.cs`**

Create `windows/src/Murmur.Core/Abstractions/ISpeechRepairer.cs`:
```csharp
namespace Murmur.Core.Abstractions;

/// <summary>
/// Post-processes raw transcripts to remove conversational disfluencies and self-corrections.
/// </summary>
public interface ISpeechRepairer
{
    Task<string> RepairAsync(string rawTranscript, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement `RuleBasedSpeechRepairer.cs` (Deterministic baseline)**

Create `windows/src/Murmur.Speech/RuleBasedSpeechRepairer.cs`:
Implements regex/pattern detection for German & English correction markers (`ach nee`, `ach nein`, `warte mal`, `no wait`, etc.), stripping preceding repaired tokens.

- [ ] **Step 5: Add `LLamaSharp` packages and implement `LlamaSharpSpeechRepairer.cs`**

In `windows/Directory.Packages.props`, add:
```xml
<PackageVersion Include="LLamaSharp" Version="0.22.0" />
<PackageVersion Include="LLamaSharp.Backend.Cpu" Version="0.22.0" />
```
In `windows/src/Murmur.Speech/LlamaSharpSpeechRepairer.cs`, implement `ISpeechRepairer` with prompt execution on `Qwen2.5-0.5B-Instruct-Q4_K_M.gguf`.

- [ ] **Step 6: Run tests to verify passing**

Run: `dotnet test windows/tests/Murmur.Speech.Tests --filter "FullyQualifiedName~SpeechRepairerTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add windows/Directory.Packages.props windows/src/Murmur.Core windows/src/Murmur.Speech windows/tests/Murmur.Speech.Tests
git commit -m "feat(speech): implement ISpeechRepairer and speech repair engines"
```

---

### Task 4: Pipeline Integration in `DictationController`

**Files:**
- Modify: `windows/src/Murmur.Core/DictationController.cs`
- Modify: `windows/src/Murmur.Core/Settings/AppSettings.cs`
- Test: `windows/tests/Murmur.Core.Tests/DictationControllerTests.cs`

**Interfaces:**
- Consumes: `ISpeechRepairer?` passed to `DictationController` or `UpdateRepairer`
- Produces: Integrated dictation pipeline applying transcription -> speech repair -> formatting -> dictionary

- [ ] **Step 1: Write unit tests for speech repair integration in `DictationControllerTests.cs`**

Add tests verifying:
1. When `ISpeechRepairer` is provided, raw transcript is passed through repairer before formatting.
2. If `ISpeechRepairer` throws, fallback to raw transcript occurs gracefully without failing insertion.

- [ ] **Step 2: Run tests to verify test suite**

Run: `dotnet test windows/tests/Murmur.Core.Tests --filter "FullyQualifiedName~DictationControllerTests"`
Expected: Tests compile and run.

- [ ] **Step 3: Modify `DictationController.cs`**

In `windows/src/Murmur.Core/DictationController.cs`:
1. Add optional `ISpeechRepairer? speechRepairer = null` parameter to constructor.
2. In `FinishSessionAsync`:
   ```csharp
   var repaired = raw;
   if (_speechRepairer != null)
   {
       try { repaired = await _speechRepairer.RepairAsync(raw, cancellationToken).ConfigureAwait(false); }
       catch { repaired = raw; }
   }
   var cleaned = session.Runtime.Formatter.Format(repaired);
   ```

- [ ] **Step 4: Run all unit tests**

Run: `dotnet test windows/Murmur.sln --filter "Category!=RealModel"`
Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add windows/src/Murmur.Core windows/tests/Murmur.Core.Tests
git commit -m "feat(core): integrate speech repairer into DictationController pipeline"
```

---

### Task 5: Settings Window UI, Model Selection & Localization

**Files:**
- Modify: `windows/src/Murmur.App/Localization/UiTextResources.resx`
- Modify: `windows/src/Murmur.App/Localization/UiTextResources.de.resx`
- Modify: `windows/src/Murmur.App/Views/SettingsWindow.axaml`
- Modify: `windows/src/Murmur.App/Views/SettingsWindow.axaml.cs`
- Modify: `windows/src/Murmur.App/App.axaml.cs`
- Test: `windows/tests/Murmur.App.Tests/SettingsWindowTests.cs`

**Interfaces:**
- Consumes: `AppSettings.RestorePreviousWindowAfterInjection`, `AppSettings.EnableSelfCorrection`
- Produces: Settings controls and localized strings for window restoration and self-correction

- [ ] **Step 1: Add localized strings in `UiTextResources.resx` and `UiTextResources.de.resx`**

Add:
- `RestorePreviousWindowAfterInjection`: "Return to previous window after injection" / "Nach Einfügen zum vorherigen Fenster zurückkehren"
- `EnableSelfCorrection`: "Automatically remove self-corrections (e.g. 'Oh no, Wednesday')" / "Versprecher und Selbstkorrekturen automatisch tilgen"

- [ ] **Step 2: Add checkboxes to `SettingsWindow.axaml`**

Bind to `AppSettings`:
- CheckBox for `EnableSelfCorrection`
- CheckBox for `RestorePreviousWindowAfterInjection`

- [ ] **Step 3: Update `App.axaml.cs` to wire up Whisper and Repairer based on settings**

In `App.axaml.cs`:
When instantiating `ITranscriber`, check if `settings.SelectedModelId == "whisper-v3-turbo"` and instantiate `SherpaOnnxWhisperTranscriber`.

- [ ] **Step 4: Run full solution tests**

Run: `dotnet test windows/Murmur.sln --filter "Category!=RealModel"`
Expected: All tests PASS (0 errors, 0 warnings).

- [ ] **Step 5: Commit**

```bash
git add windows/src/Murmur.App windows/tests/Murmur.App.Tests
git commit -m "feat(ui): add settings and localization for self-correction and window restoration"
```
