# Murmur

[Deutsch](README.de.md) | [English](README.md) | [Changelog](CHANGELOG.md)

Murmur is a Windows push-to-talk dictation utility. Hold the configured key, speak, then release it to type cleaned-up text into the focused application.

This is an AI-assisted Windows adaptation inspired by [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube). It does not claim to be the upstream implementation.

## Download and first run

Download a release asset from the GitHub Releases page:

- `Murmur-Setup-vX.Y.Z.exe` installs Murmur for the current user.
- `Murmur-<version>-win-x64.zip` is portable. Extract it and run `Murmur.App.exe`; it does not install Murmur.

The installer is unsigned, so Windows SmartScreen may show a warning. Murmur opens its setup screen on first run. Choose a microphone, hotkey, and model, then download the selected speech model. Dictation works after that download completes.

Murmur checks for a newer stable release at startup. If it finds one, the tray menu offers an action that opens that release page. It does not update itself.

## Data locations

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\Murmur\settings.json` | settings |
| `%LOCALAPPDATA%\Murmur\history.jsonl` | dictation history |
| `%LOCALAPPDATA%\Murmur\murmur.log` | application log |
| `%LOCALAPPDATA%\Murmur\models\` | downloaded speech models |
| `%LOCALAPPDATA%\Programs\Murmur` | Setup EXE installation |

## What it does

- Captures microphone audio while the push-to-talk key is held.
- Runs either Parakeet v3 offline or Nemotron 3.5 live streaming through sherpa-onnx. One model is resident at a time.
- Applies deterministic formatting and personal dictionary corrections before injection.
- Shows a non-activating recording HUD so focus stays in the target application.

### Optional audio feedback

No ElevenLabs feedback WAV ships in the repository, the Setup EXE, or the
Portable ZIP. To use feedback, place your own licensed `Sounds\feedback.wav`
beside `Murmur.App.exe`. If the file is missing, feedback is silent. You are
responsible for the sound's license; Murmur's MIT License covers Murmur source
code only, not user-owned sounds. See [NOTICE.md](NOTICE.md) for relevant
ElevenLabs terms links.

Murmur uses .NET 10 and Avalonia. It captures audio with WASAPI via NAudio and runs speech models through sherpa-onnx.

The settings screen uses the Lens redesign across setup, history, and dictionary views.

## Limits

Transcription quality is unmeasured. This project makes no accuracy claim and has no benchmark supporting a default-model choice.

CI cannot prove text injection into a real foreground application. That needs a real Windows machine and a person testing it.

## License

Murmur source code is MIT licensed. Model and bundled third-party notices are in [NOTICE.md](NOTICE.md).

## Build

```powershell
cd windows
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build
```

The blocking release gate excludes tests marked `RealModel`. The separate
`.github/workflows/real-model.yml` workflow runs those model-download and
inference checks manually or nightly.

More detail: [Windows development notes](windows/README.md) and [speech-model notes](docs/PARAKEET-WINDOWS.md).
