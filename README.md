# Murmur

[Deutsch](README.de.md) | [English](README.md) | [Changelog](CHANGELOG.md)

Murmur is a Windows dictation app. Hold your push-to-talk key, speak, then release it. Murmur formats the transcript and types it into the focused application.

Murmur is a separate, AI-assisted Windows adaptation inspired by [per-simmons/murmur-youtube](https://github.com/per-simmons/murmur-youtube).

## Download and first run

Download [Murmur 0.2.2 beta](https://github.com/devjenjen/murmur-windows/releases/tag/v0.2.2) for Windows 11 x64:

- [Setup EXE](https://github.com/devjenjen/murmur-windows/releases/download/v0.2.2/Murmur-Setup-v0.2.2.exe) installs Murmur for the current user.
- [Portable ZIP](https://github.com/devjenjen/murmur-windows/releases/download/v0.2.2/Murmur-0.2.2-win-x64.zip): extract it and run `Murmur.App.exe`; it does not install Murmur.
- `build-info.json` records the source commit and build details. `SHA256SUMS.txt` contains checksums for the downloads.

The installer is unsigned, so Windows SmartScreen may show a warning. Murmur opens its setup screen on first run. Choose a microphone, hotkey, and model, then download the selected speech model. After setup, hold **Right Ctrl** to speak and release it to insert the text. You can change this key in Settings. Murmur stays in the system tray; open Settings or quit from its tray menu.

### Requirements for release downloads

- Windows 11 x64 and a working microphone; allow desktop apps to access the microphone in Windows privacy settings.
- The Setup EXE and portable ZIP include the .NET runtime, UI libraries, and native speech engine. You do not need to install the .NET SDK, Python, or CUDA. Recognition runs on the CPU; a dedicated GPU is not required.
- Internet access to GitHub is required for the first model download inside Murmur. Model weights are not bundled with the installer. After the model is downloaded, speech recognition runs locally and works without an internet connection; the startup update check still contacts GitHub.
- Keep all extracted files together when using the portable ZIP. The application EXE depends on the libraries beside it.

You only need the .NET SDK if you build from source. Murmur extracts models with Windows 11's built-in `tar.exe`.

Murmur checks for a newer stable release at startup. If it finds one, the tray menu offers an action that opens that release page. It does not update itself. Beta releases are excluded from this check; check the [Releases page](https://github.com/devjenjen/murmur-windows/releases) manually for beta updates.

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

- Captures microphone audio while your configured push-to-talk key is held.
- Runs Parakeet v3 for a final transcript after key release, or Nemotron 3.5 for live transcript previews in the recording overlay. One model is resident at a time.
- Offers Standard and Math profiles. **F8** switches profiles by default; you can change that key or switch through Settings and the tray menu.
- Converts spoken German math expressions into Unicode or LaTeX in the Math profile. For example, `x hoch zwei` becomes `x².` in Unicode mode. Formatting uses fixed rules; it does not solve equations or rewrite text with a language model.
- Applies punctuation and optional filler removal, with a separate filler setting for each profile. Your personal dictionary supplies text corrections before insertion; edit or disable its entries in Settings.
- Types at the current cursor or the end of the document. If insertion fails after transcription, Settings offers the finished text for copying. This recovery text is temporary and does not survive an app restart.
- Keeps an optional dictation history with copying and deletion of individual entries or the whole history. Choose 7, 30, or 90 days of retention, or no age limit, and a 1,000-entry limit or no entry limit. History is disabled by default.
- Shows a recording overlay without taking focus from the target application; its screen position is configurable.
- Lets you select and test a microphone, choose an English or German interface, and enable startup with Windows.

Both [Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) and [Nemotron 3.5](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b) list German and English among their supported languages. Recognition detects the spoken language automatically; changing the interface language does not change the speech model. The math formatting rules are German.

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
