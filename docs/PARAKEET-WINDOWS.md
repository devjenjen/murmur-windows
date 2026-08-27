# Speech models on Windows

Murmur downloads its speech model during first-run setup. Choose a model in Settings, then select the download action. The app verifies the archive size and SHA-256 hash before extracting it. Releases name the installer `Murmur-Setup-vX.Y.Z.exe`; the portable asset remains `Murmur-<version>-win-x64.zip`.

## Pinned models

| Model | Archive | Local directory |
|---|---|---|
| Parakeet v3 (offline) | `sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2` | `%LOCALAPPDATA%\Murmur\models\parakeet-v3` |
| Nemotron 3.5 (live, 160 ms) | `sherpa-onnx-nemotron-3.5-asr-streaming-0.6b-160ms-int8-2026-06-11.tar.bz2` | `%LOCALAPPDATA%\Murmur\models\nemotron-3.5-streaming-160ms` |

Both archives come from [sherpa-onnx releases](https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models). The application requires `encoder.int8.onnx`, `decoder.int8.onnx`, `joiner.int8.onnx`, and `tokens.txt` in the selected model directory.

Parakeet transcribes after key release. Nemotron provides a live transcript preview while the key is held. Murmur keeps only the selected model resident in memory.

Do not use these notes as an accuracy comparison. Transcription quality is unmeasured.

## Troubleshooting

If Settings reports that a model is missing or corrupt, download that selected model again. If the download is cancelled, start it again from Settings. The application does not need a manual model-folder setup.

For release installation choices and other local data paths, see [the Windows README](../windows/README.md).
