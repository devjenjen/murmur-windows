# Murmur

[Deutsch](README.de.md) | [English](README.md) | [Changelog](CHANGELOG.md)

Murmur is a Windows dictation app. Hold your push-to-talk key, speak, then release it. Murmur formats the transcript and types it into the focused application.

Murmur is a separate, AI-assisted Windows adaptation inspired by [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube).

## Download and first run

Murmur 0.2.1 is being prepared as a beta release. Published downloads appear on
the [GitHub Releases page](https://github.com/devjenjen/murmur-windows/releases):

- `Murmur-Setup-vX.Y.Z.exe` installs Murmur for the current user.
- `Murmur-<version>-win-x64.zip` is portable. Extract it and run `Murmur.App.exe`; it does not install Murmur.
- `build-info.json` records the source commit and build details. `SHA256SUMS.txt` contains checksums for the downloads.

The installer is unsigned, so Windows SmartScreen may show a warning. Murmur opens its setup screen on first run. Choose a microphone, hotkey, and model, then download the selected speech model. Dictation works after that download completes.

### Requirements for release downloads

- Windows 11 x64 and a working microphone; allow desktop apps to access the microphone in Windows privacy settings.
- The Setup EXE and portable ZIP include the .NET runtime, UI libraries, and native speech engine. You do not need to install the .NET SDK, Python, or CUDA. Recognition runs on the CPU; a dedicated GPU is not required.
- Internet access to GitHub is required for the first model download inside Murmur. Model weights are not bundled with the installer. After the model is downloaded, speech recognition runs locally and works without an internet connection; the startup update check still contacts GitHub.
- Keep all extracted files together when using the portable ZIP. The application EXE depends on the libraries beside it.

You only need the .NET SDK if you build from source. Murmur extracts models with Windows 11's built-in `tar.exe`.

Murmur checks for a newer stable release at startup. If it finds one, the tray menu offers an action that opens that release page. It does not update itself.

The model archives are about 465 MiB (Parakeet) and 453 MiB (Nemotron). Their
extracted files use about 640 MiB and 653 MiB in the local release measurement.
Allow extra free disk space for downloading and extraction. RAM use and speed
depend on the model, audio session, and PC; there is no guaranteed RAM minimum
or speed for every device.

## Data locations

Speech and transcripts stay on the PC; they are not uploaded to a speech service. History is disabled by default on fresh installs. Stored settings, history, dictionary, and logs are local and unencrypted; review them before sharing. Logs are limited to a 2 MiB active file and two backups.

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\Murmur\settings.json` | settings |
| `%LOCALAPPDATA%\Murmur\history.jsonl` | dictation history |
| `%LOCALAPPDATA%\Murmur\dictionary.txt` | personal dictionary |
| `%LOCALAPPDATA%\Murmur\murmur.log` | application log |
| `%LOCALAPPDATA%\Murmur\models\` | downloaded speech models |
| `%LOCALAPPDATA%\Programs\Murmur` | Setup EXE installation |

## What it does

- Captures microphone audio while the push-to-talk key is held.
- Runs either Parakeet v3 offline or Nemotron 3.5 live streaming through sherpa-onnx. One model is resident at a time.
- Formats text with fixed rules and applies your dictionary corrections before typing it.
- Shows a recording overlay that keeps focus in the target application.

### Optional audio feedback

To play a sound after text is inserted, place your own WAV at
`Sounds\feedback.wav` beside `Murmur.App.exe` and enable audio feedback in
Settings. Murmur includes no sound files and stays silent if that file is
missing.

Murmur uses .NET 10 and Avalonia. It captures audio with WASAPI via NAudio and runs speech models through sherpa-onnx.

## Limits

Transcription accuracy has not been measured. The default model was not chosen from an accuracy benchmark.

Automated checks cover only part of the app's behavior. Compatibility with every microphone, PC, or target application has not been verified. Windows blocks text insertion into applications with higher privileges than Murmur.

## License

Murmur source code is MIT licensed. Model and bundled third-party notices are in [NOTICE.md](NOTICE.md).

## Build

```powershell
cd windows
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build --filter 'Category!=RealModel&Category!=QualityCorpus'
```

This command runs the unit and UI tests used by normal CI. The release workflow
also downloads models and runs inference tests marked `RealModel`.
`.github/workflows/real-model.yml` runs model tests manually or nightly.
Tests marked `QualityCorpus` require a separately supplied corpus.

More detail: [Windows setup and build notes](windows/README.md).
