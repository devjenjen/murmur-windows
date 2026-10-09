# Murmur

[Deutsch](README.de.md) | [English](README.md) | [Changelog](CHANGELOG.md)

Murmur is a Windows push-to-talk dictation utility. Hold the configured key, speak, then release it to type cleaned-up text into the focused application.

This is an AI-assisted Windows adaptation inspired by [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube). It does not claim to be the upstream implementation.

## Download and first run

Murmur 0.2.0 is a beta draft candidate, not a stable release. Use the
[GitHub Releases page](https://github.com/devjenjen/murmur-windows/releases)
and choose an explicitly published asset:

- `Murmur-Setup-vX.Y.Z.exe` installs Murmur for the current user.
- `Murmur-<version>-win-x64.zip` is portable. Extract it and run `Murmur.App.exe`; it does not install Murmur.
- `build-info.json` and `SHA256SUMS.txt` describe and verify the candidate.

The installer is unsigned, so Windows SmartScreen may show a warning. Murmur opens its setup screen on first run. Choose a microphone, hotkey, and model, then download the selected speech model. Dictation works after that download completes.

### Requirements for release downloads

- Windows 11 x64 and a working microphone; allow desktop apps to access the microphone in Windows privacy settings.
- The Setup EXE and portable ZIP include the .NET runtime, UI libraries, and native speech engine. You do not need to install the .NET SDK, Python, or CUDA. Recognition runs on the CPU; a dedicated GPU is not required.
- Internet access to GitHub is required for the first model download inside Murmur. Model weights are not bundled with the installer. After the model is downloaded, speech recognition runs locally and works without an internet connection; the startup update check still contacts GitHub.
- Keep all extracted files together when using the portable ZIP. The application EXE depends on the libraries beside it.

The .NET SDK in the Build section is for compiling the source, not for using a release download. Model extraction uses Windows 11's built-in `tar.exe`; no separate archive utility is required on a standard Windows installation.

Murmur checks for a newer stable release at startup. If it finds one, the tray menu offers an action that opens that release page. It does not update itself.

The supported beta target is Windows 11 x64. The two model archives are about
465 MiB (Parakeet) and 453 MiB (Nemotron); model files occupy about 640 MiB and
653 MiB respectively in the current local measurement. Download and extraction
need additional free disk space. RAM depends on the selected model, backend,
audio session, and host; no guaranteed RAM minimum or speed across all PCs is advertised.

## Data locations

Speech and transcripts stay on the PC; they are not uploaded to a speech service. History is disabled by default on fresh installs. Stored settings, history, dictionary, and logs are local and unencrypted; review them before sharing. Logs are limited to a 2 MiB active file and two backups.

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

Murmur uses .NET 10 and Avalonia. It captures audio with WASAPI via NAudio and runs speech models through sherpa-onnx. Models are downloaded from GitHub on demand; the app is not an offline installer.

## Limits

Transcription quality is unmeasured. This project makes no accuracy claim and has no benchmark supporting a default-model choice.

Automated checks cannot cover every microphone, PC, or foreground application. Compatibility across other devices has not been exhaustively verified.

## License

Murmur source code is MIT licensed. Model and bundled third-party notices are in [NOTICE.md](NOTICE.md).

## Build

```powershell
cd windows
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build
```

Normal CI excludes tests marked `RealModel` and `QualityCorpus`. The release workflow also runs model-download and inference checks marked `RealModel`; the separate `.github/workflows/real-model.yml` workflow runs model checks manually or nightly. Accuracy evaluations marked `QualityCorpus` need an explicitly supplied corpus.

More detail: [Windows setup and build notes](windows/README.md).
