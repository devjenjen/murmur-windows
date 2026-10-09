# Murmur 0.2.0 beta

Beta release candidate for Windows 11 x64.

## Included

- Self-contained portable ZIP and per-user installer.
- Offline Parakeet v3 and live Nemotron 3.5 model support; models download on first use.
- Text insertion at the cursor or document end, history limits, and bounded logs.
- Model changes wait for recognition work to stop before releasing native resources.
- English and German interface.

## First run

Windows 11 x64 and a microphone are required. Release downloads include the .NET runtime and native speech engine; no .NET SDK, Python, CUDA, or dedicated GPU is required. Allow desktop microphone access in Windows privacy settings.

Download one speech model inside Murmur before dictating. Internet access to GitHub is required for that download; model weights are not bundled. Recognition then runs locally without internet, while the startup update check still contacts GitHub. Keep all extracted files together if using the portable ZIP.

## Beta limits

- Installer is unsigned. Windows SmartScreen may display a warning.
- Stable update checks ignore draft and prerelease releases. Beta users use the
  release page manually.
- Transcription quality and speed depend on the model, microphone, and PC.
  Compatibility with every device or target application is not guaranteed.

## Assets

- `Murmur-0.2.0-win-x64.zip`
- `Murmur-Setup-v0.2.0.exe`
- `build-info.json`
- `SHA256SUMS.txt`
