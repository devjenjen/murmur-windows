# Murmur for Windows

Murmur is a Windows tray app for push-to-talk dictation. Hold your configured key, speak, then release it. Murmur formats the transcript and types it into the application that had focus.

The [0.2.2 beta release](https://github.com/devjenjen/murmur-windows/releases/tag/v0.2.2) includes two Windows x64 packages:

- `Murmur-Setup-vX.Y.Z.exe` installs per-user under `%LOCALAPPDATA%\Programs\Murmur`.
- `Murmur-<version>-win-x64.zip` is portable. Extract it and run `Murmur.App.exe`; it does not create an installation.
- `build-info.json` records the source commit and build details. `SHA256SUMS.txt` contains checksums for the downloads.

The Setup EXE is unsigned. SmartScreen may warn before it runs.

The supported beta target is Windows 11 x64. Model archives are about 465 MiB
(Parakeet) and 453 MiB (Nemotron). Extracted files use about 640 MiB and 653 MiB
in the local release measurement. The downloader checks free space for the
archive and extraction, but other programs can still use that space during a
download. RAM use depends on the model and PC; there is no guaranteed minimum
for every device.

## First run and updates

On first run Murmur shows the setup screen. Select a microphone, push-to-talk key, and speech model, then download the selected model. The download is required before dictation can start.

After setup, hold Right Ctrl to speak and release it to insert the transcript. F8 switches between the Standard and Math profiles. Both keys are configurable. Use the tray menu to open Settings or quit.

Release downloads include the .NET runtime, UI libraries, and native speech engine. The .NET SDK is only needed to build the source. Python, CUDA, and a dedicated GPU are not required; recognition runs on the CPU.

Allow microphone access for desktop apps in Windows privacy settings. The first model download needs internet access to GitHub; model weights are not included in Setup EXE or ZIP. Once downloaded, the model can recognize speech locally without internet. The startup update check still contacts GitHub. Extraction uses Windows 11's built-in `tar.exe`, so no separate archive utility is needed on a standard installation. Portable users must keep the EXE and all extracted libraries together.

Murmur checks GitHub for a newer stable release during startup. When an update is available, its tray-menu action opens the release page. It does not download or install updates itself. Check the Releases page manually for beta updates; the startup check excludes prereleases.

## Features

The [main README](../README.md#what-it-does) describes the user-facing features: Standard and German Math profiles, Unicode or LaTeX output, dictionary corrections, optional filler removal, cursor or document-end insertion, optional history with retention limits, temporary recovery text after insertion failures, microphone testing, HUD placement, English and German interface, and Windows startup.

The interface language does not select the speech language. Both models detect the spoken language automatically; their model cards list German and English among the supported languages. Math formatting uses German rules.

## Local files

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\Murmur\settings.json` | settings |
| `%LOCALAPPDATA%\Murmur\history.jsonl` | dictation history |
| `%LOCALAPPDATA%\Murmur\dictionary.txt` | personal dictionary |
| `%LOCALAPPDATA%\Murmur\murmur.log` | application log |
| `%LOCALAPPDATA%\Murmur\models\parakeet-v3` | Parakeet v3 model |
| `%LOCALAPPDATA%\Murmur\models\nemotron-3.5-streaming-160ms` | Nemotron 3.5 model |

## Speech models

Murmur downloads pinned model archives from the sherpa-onnx releases on GitHub:

| Model | Mode |
|---|---|
| [Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) (offline) | Final transcription after key release |
| [Nemotron 3.5](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b) (live, 160 ms) | Live HUD preview while speaking |

Murmur loads one model at a time and checks the archive's size and SHA-256 hash
when downloading. If files are missing or corrupt, download the model again
from Settings.

## Optional audio feedback

To play a sound after text is inserted, place your own WAV at
`Sounds\feedback.wav` beside `Murmur.App.exe` and enable audio feedback in
Settings. Murmur includes no sound files and stays silent if that file is
missing.

Transcription accuracy has not been measured. The default model was not chosen from an accuracy benchmark.

## Development

```powershell
dotnet restore Murmur.sln
dotnet build Murmur.sln --configuration Release --no-incremental -warnaserror
dotnet test Murmur.sln --configuration Release --no-build --filter 'Category!=RealModel&Category!=QualityCorpus'
```

This command runs the unit and UI tests used by normal CI. The release workflow
also downloads models and runs inference tests marked `RealModel`.
`.github/workflows/real-model.yml` runs model tests manually or nightly.
Tests marked `QualityCorpus` require a separately supplied corpus.

The default push-to-talk key is Right Ctrl. The keyboard hook observes keys without swallowing them, and the HUD never takes focus. Windows UIPI blocks `SendInput` when the target application has higher privileges than Murmur.

Automated checks do not establish compatibility with every microphone, PC, or foreground application.

## License

Murmur source code is MIT licensed. Model and bundled third-party notices are in [the repository NOTICE](../NOTICE.md).
