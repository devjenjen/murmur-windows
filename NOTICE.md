# Murmur notices

Murmur source code is licensed under the MIT License; see [LICENSE](LICENSE).

## Speech models

Murmur does not include speech-model archives in the repository or release
assets. The application downloads the selected archive from sherpa-onnx on
first run. Model terms remain separate from the Murmur source license:

- [Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3): NVIDIA model card; CC BY 4.0.
- [Nemotron 3.5](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b): NVIDIA model card; OpenMDW-1.1.

Users must follow the applicable model terms when downloading or using a
model.

## Optional feedback sound

No ElevenLabs feedback WAV ships in this repository, the Setup EXE, or the
Portable ZIP. Users may optionally place their own licensed
`Sounds\feedback.wav` beside `Murmur.App.exe`. If the file is missing, Murmur
plays no feedback sound. Users remain responsible for the sound's license and
terms; Murmur's MIT License covers Murmur source code only and does not cover
user-owned sounds.

For any user-supplied ElevenLabs sound, consult the
[Sound Effects Terms](https://elevenlabs.io/sound-effects-terms), the
[EEA Terms of Service](https://elevenlabs.io/terms-of-use-eu), and the
[Prohibited Use Policy](https://elevenlabs.io/use-policy).

## Dependencies

Bundled third-party libraries remain under their own licenses. Package versions
are pinned in `windows/Directory.Packages.props`; their license notices are not
replaced by Murmur's MIT License.
