# Murmur third-party notices

Murmur release packages bundle third-party runtime components. The exact
versions, source URLs, runtime asset groups, and copied license/notice files
are recorded in [`windows/installer/third-party-components.json`](windows/installer/third-party-components.json).

The `licenses/` directory is copied into every publish output. It contains the
corresponding original package or upstream license and notice text for:

- Avalonia and its Windows/Skia support stack, including ANGLE, MicroCom, and
  Tmds.DBus.Protocol.
- NAudio.
- SkiaSharp and HarfBuzzSharp, including their bundled third-party notices.
- sherpa-onnx and the ONNX Runtime native library it distributes.
- .NET and Windows Desktop runtime packs.
- Inter font assets.

Murmur source code remains MIT licensed under [`LICENSE`](LICENSE). Speech
model archives are downloaded separately at runtime and are not covered by
this notice file; model terms are documented in [`NOTICE.md`](NOTICE.md).
