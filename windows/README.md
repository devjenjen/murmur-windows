# Murmur for Windows

Murmur is a Windows tray application for push-to-talk dictation. Hold the configured key, speak, and release it to inject cleaned-up text into the application that had focus.

Public downloads contain two x64 assets:

- `Murmur-Setup-vX.Y.Z.exe` installs per-user under `%LOCALAPPDATA%\Programs\Murmur`.
- `Murmur-<version>-win-x64.zip` is portable. Extract it and run `Murmur.App.exe`; it does not create an installation.

The Setup EXE is unsigned. SmartScreen may warn before it runs.

## First run and updates

On first run Murmur shows the setup screen. Select a microphone, push-to-talk key, and speech model, then download the selected model. The download is required before dictation can start.

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

Exactly one model is loaded at a time. See [model notes](../docs/PARAKEET-WINDOWS.md) for the current archives and local paths.

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

The blocking release gate excludes tests marked `RealModel`. The separate
`.github/workflows/real-model.yml` workflow runs those model-download and
inference checks manually or nightly.

The default push-to-talk key is Right Ctrl. The keyboard hook observes keys without swallowing them, and the HUD never takes focus. `SendInput` cannot inject text into an elevated foreground application because Windows UIPI blocks it.

CI cannot verify real foreground text injection. Test that behavior on a Windows machine.

## License

Murmur source code is MIT licensed. Model and bundled third-party notices are in [the repository NOTICE](../NOTICE.md).
