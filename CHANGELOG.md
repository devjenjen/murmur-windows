# Changelog

Release history for Murmur. Newest release first.

## [0.1.0] - 2026-08-27

### Added

- Lens-inspired settings redesign across setup, history, and dictionary views.
- Offline Parakeet v3 and live Nemotron 3.5 speech-model support.
- English and German interface localization.
- Portable ZIP and per-user Windows installer release assets.

### Improved

- Bounded HUD and settings layouts for narrow windows and localized text.
- Local history metadata for audio and inference duration.
- Separate CI path for real-model download and inference checks.

### Fixed

- Stale asynchronous readiness updates overriding current model state.
- Flaky timing in streaming and text-injection tests.
- Missing or optional audio feedback files no longer cause failures.

### Release notes

- The Windows installer is unsigned; SmartScreen may show a warning.
- Speech models download on first setup and are not bundled in the release assets.
- The release does not auto-update the application.

[0.1.0]: https://github.com/devjenjen/murmur-windows/releases/tag/v0.1.0
