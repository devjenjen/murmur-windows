# Murmur 0.3.0 beta

Public beta for Windows 11 x64.

## Changes in 0.3.0

- **Bilingual Math Dictation (DE & EN)**: Unified `MathFormatter` and `EnglishNumberParser` recognizing English numbers (0 to 999, compound numbers like `twenty-one`, decimals like `five point zero five`), square roots, fractions, powers, and mathematical symbols in both Unicode and LaTeX modes.
- **Whisper Large-v3-Turbo speech model**: Added as a third offline speech model option (~538 MiB archive / ~989 MiB unpacked) providing state-of-the-art multilingual transcription on modern PCs.
- **Conversational self-correction & speech repair**: Integrated `ISpeechRepairer` pipeline before formatting. Filters speech repairs and filler words in German and English (e.g., "nicht X sondern Y", "ach nee", "ich meinte", "oder besser", "scratch that", "i mean", "ähm", "uh") with deterministic rule-based repair and optional local LLM repair.
- **Target window focus protection & restoration**: `SendInputInjector` automatically detects focus changes during dictation, restores focus to the original target window before typing, and re-checks keyboard modifier states to prevent stuck modifier keys.
- **User setting for self-correction**: Localized toggle in Settings (`EnableSelfCorrection`, enabled by default) allowing users to turn off repair filtering when raw verbatim transcripts are needed.
- **Full LLamaSharp bundling**: Integrated LLamaSharp 0.22.0 CPU backend runtimes (`llama.dll`, `ggml*.dll`, `llava_shared.dll`) into release packaging, accompanied by complete third-party component licenses and inventory tracking.

## Included

- Self-contained portable ZIP and per-user installer.
- Offline Parakeet v3, live Nemotron 3.5, and offline Whisper Large-v3-Turbo model support; models download on first use.
- Standard and Math profiles with German and English dictation, Unicode or LaTeX output, configurable hotkeys, optional filler removal, and dictionary corrections.
- Text insertion at the cursor or document end, temporary text recovery after insertion failures, optional history with copying, deletion, and retention limits, and bounded logs.
- Target window focus restoration and SendInput modifier safety protection.
- Model changes wait for recognition work to stop before releasing native resources.
- English and German interface, microphone selection and testing, configurable recording-overlay position, and optional Windows startup.
- Bundled license notices, dependency inventory, and automated repository secret scans.

The Math profile lets you dictate numbers and formulas in German or English for study notes or technical work. For example, `five times three equals fifteen` becomes `5 × 3 = 15.` and `x squared` becomes `x².` in Unicode mode. LaTeX output is also available.

## First run

Windows 11 x64 and a microphone are required. Release downloads include the .NET runtime and native speech engine; no .NET SDK, Python, CUDA, or dedicated GPU is required. Allow desktop microphone access in Windows privacy settings.

Download one speech model inside Murmur before dictating. Internet access to GitHub is required for that download; model weights are not bundled. Recognition then runs locally without internet, while the startup update check still contacts GitHub. Keep all extracted files together if using the portable ZIP.

Hold Right Ctrl to speak and release it to insert the transcript. F8 switches between Standard and Math profiles. Both keys can be changed in Settings. Math formatting accepts both German and English expressions.

## Beta limits

- Installer is unsigned. Windows SmartScreen may display a warning.
- Stable update checks ignore draft and prerelease releases. Beta users use the
  release page manually.
- Transcription quality and speed depend on the model, microphone, and PC.
  Compatibility with every device or target application is not guaranteed.

## Assets

- `Murmur-0.3.0-win-x64.zip`
- `Murmur-Setup-v0.3.0.exe`
- `build-info.json`
- `SHA256SUMS.txt`
