# Murmur third-party notices

The installer and portable ZIP include the following components. Their original
license and notice texts are in `licenses/`:

- Avalonia and its Windows/Skia support stack, including ANGLE, MicroCom, and
  Tmds.DBus.Protocol.
- NAudio.
- SkiaSharp and HarfBuzzSharp, including their bundled third-party notices.
- sherpa-onnx and the ONNX Runtime native library it distributes.
- .NET and Windows Desktop runtime packs.
- Inter font assets.

The [release inventory](https://github.com/devjenjen/murmur-windows/blob/main/windows/installer/third-party-components.json)
maps components to their source URLs and license files. For the versions in a
particular package, see its `Murmur.App.deps.json` and
`Murmur.App.runtimeconfig.json`.

Murmur source code uses the MIT License in [LICENSE](LICENSE). Speech models
download separately; their licenses are listed in [NOTICE.md](NOTICE.md).
