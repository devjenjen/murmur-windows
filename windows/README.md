# Murmur for Windows

Murmur is a Windows tray application for push-to-talk dictation. Hold the configured key, speak, and release it to inject cleaned-up text into the application that had focus.

Public downloads contain two x64 assets:

- `Murmur-Setup-vX.Y.Z.exe` installs per-user under `%LOCALAPPDATA%\Programs\Murmur`.
- `Murmur-<version>-win-x64.zip` is portable. Extract it and run `Murmur.App.exe`; it does not create an installation.
- Beta candidates also publish `build-info.json` and `SHA256SUMS.txt`. Verify
  those files before testing a draft asset.

The Setup EXE is unsigned. SmartScreen may warn before it runs.

The supported beta target is Windows 11 x64. Current pinned archive sizes are
about 465 MiB (Parakeet) and 453 MiB (Nemotron); extracted model files measure
about 640 MiB and 653 MiB in the local release measurement. The
downloader preflights archive plus extraction reserve but cannot guarantee free
space after another process writes to the disk. RAM depends on selected model
and host; no universal hardware minimum is advertised.

## First run and updates

On first run Murmur shows the setup screen. Select a microphone, push-to-talk key, and speech model, then download the selected model. The download is required before dictation can start.

Release downloads include the .NET runtime, UI libraries, and native speech engine. The .NET SDK is only needed to build the source. Python, CUDA, and a dedicated GPU are not required; recognition runs on the CPU.

Allow microphone access for desktop apps in Windows privacy settings. The first model download needs internet access to GitHub; model weights are not included in Setup EXE or ZIP. Once downloaded, the model can recognize speech locally without internet. The startup update check still contacts GitHub. Extraction uses Windows 11's built-in `tar.exe`, so no separate archive utility is needed on a standard installation. Portable users must keep the EXE and all extracted libraries together.

Murmur checks GitHub for a newer stable release during startup. When an update is available, its tray-menu action opens the release page. It does not download or install updates itself.

The settings screen uses the Lens redesign across setup, history, and dictionary views.

## Local files

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\Murmur\settings.json` | settings |
| `%LOCALAPPDATA%\Murmur\history.jsonl` | dictation history |
| `%LOCALAPPDATA%\Murmur\murmur.log` | application log |
| `%LOCALAPPDATA%\Murmur\models\parakeet-v3` | Parakeet v3 model |
| `%LOCALAPPDATA%\Murmur\models\nemotron-3.5-streaming-160ms` | Nemotron 3.5 model |

## Speech models

Murmur downloads and verifies pinned model archives through `sherpa-onnx`:

| Model | Mode |
|---|---|
| Parakeet v3 (offline) | Final transcription after key release |
| Nemotron 3.5 (live, 160 ms) | Live HUD preview while speaking |

Exactly one model is loaded at a time. Downloads use pinned archives with size and SHA-256 verification. Missing or corrupt model files can be downloaded again from Settings; no manual model-folder setup is needed.

## Optional audio feedback

No ElevenLabs feedback WAV ships in the repository, the Setup EXE, or the
Portable ZIP. Users may optionally place their own licensed
`Sounds\feedback.wav` beside `Murmur.App.exe`. If the file is missing, Murmur
is silent. Users remain responsible for the sound's license; Murmur's MIT
License covers Murmur source code only, not user-owned sounds. See
[NOTICE](../NOTICE.md) for relevant ElevenLabs terms links.

Transcription quality is unmeasured. Do not infer an accuracy ranking or benchmark result from the selected default.

## Development

```powershell
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build
```

Normal CI excludes tests marked `RealModel` and `QualityCorpus`. The release workflow also runs model-download and inference checks marked `RealModel`; the separate `.github/workflows/real-model.yml` workflow runs model checks manually or nightly. Accuracy evaluations marked `QualityCorpus` need an explicitly supplied corpus.

The default push-to-talk key is Right Ctrl. The keyboard hook observes keys without swallowing them, and the HUD never takes focus. `SendInput` cannot inject text into an elevated foreground application because Windows UIPI blocks it.

Automated checks do not establish compatibility with every microphone, PC, or foreground application.

## License

Murmur source code is MIT licensed. Model and bundled third-party notices are in [the repository NOTICE](../NOTICE.md).
