# Murmur 0.2.2 beta

Public beta for Windows 11 x64.

## Changes in 0.2.2

- Complete the English and German feature and setup instructions, including beta update behavior.
- Add direct installer and portable download links in the README.
- Retire old 0.1.0 binary downloads that lacked license notices. Source tags remain available.

## Included

- Self-contained portable ZIP and per-user installer.
- Offline Parakeet v3 and live Nemotron 3.5 model support; models download on first use.
- Standard and German Math profiles with Unicode or LaTeX output, configurable hotkeys, optional filler removal, and dictionary corrections.
- Text insertion at the cursor or document end, temporary text recovery after insertion failures, optional history with copying, deletion, and retention limits, and bounded logs.
- Model changes wait for recognition work to stop before releasing native resources.
- English and German interface, microphone selection and testing, configurable recording-overlay position, and optional Windows startup.
- Bundled license notices, dependency inventory, and automated repository secret scans.

## First run

Windows 11 x64 and a microphone are required. Release downloads include the .NET runtime and native speech engine; no .NET SDK, Python, CUDA, or dedicated GPU is required. Allow desktop microphone access in Windows privacy settings.

Download one speech model inside Murmur before dictating. Internet access to GitHub is required for that download; model weights are not bundled. Recognition then runs locally without internet, while the startup update check still contacts GitHub. Keep all extracted files together if using the portable ZIP.

Hold Right Ctrl to speak and release it to insert the transcript. F8 switches between Standard and Math profiles. Both keys can be changed in Settings. Math formatting uses German rules.

## Beta limits

- Installer is unsigned. Windows SmartScreen may display a warning.
- Stable update checks ignore draft and prerelease releases. Beta users use the
  release page manually.
- Transcription quality and speed depend on the model, microphone, and PC.
  Compatibility with every device or target application is not guaranteed.

## Assets

- `Murmur-0.2.2-win-x64.zip`
- `Murmur-Setup-v0.2.2.exe`
- `build-info.json`
- `SHA256SUMS.txt`
