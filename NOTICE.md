# Murmur notices

Murmur source code uses the MIT License. See [LICENSE](LICENSE).

## Speech models

Download a speech model in Murmur before dictating. The app fetches the selected
archive from the sherpa-onnx releases on GitHub. The repository, installer, and
portable ZIP contain no model weights. Each model has its own license:

- [Parakeet v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3): CC BY 4.0.
- [Nemotron 3.5](https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b): OpenMDW-1.1.
- [Whisper Large-v3-Turbo](https://huggingface.co/openai/whisper-large-v3-turbo): MIT.

Read the linked model cards for the terms that apply when downloading or using
these models.

## Dependencies

Bundled libraries and fonts keep their own licenses. [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
lists the components, and `licenses/` contains their original license and notice
texts. The [release inventory](https://github.com/devjenjen/murmur-windows/blob/main/windows/installer/third-party-components.json)
maps components to those files. The package's `Murmur.App.deps.json` and
`Murmur.App.runtimeconfig.json` record the versions included in that build.
