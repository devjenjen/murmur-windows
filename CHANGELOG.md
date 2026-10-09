# Changelog

Release history for Murmur. Newest release first.

## [0.2.0] - 2026-10-09

### Added

- Cursor-safe or document-end text insertion with legacy settings migration.
- Optional bounded dictation history with retention and entry limits.
- Isolated, integrity-checked model downloads with cancellation-safe extraction.
- Beta release verification, package inventory, checksums, and build metadata.

### Improved

- Dictation cancellation, model replacement, and shutdown await session work before freeing native resources.
- Streaming retains audio only in its recognition queue; model loading verifies integrity once per construction.
- Timing distinguishes streaming completion from offline inference and measures key release through text insertion.
- History runs after the insertion attempt and avoids unnecessary retention rewrites.
- Model status callbacks ignore stale selections and report download phases.
- Settings writes preserve the previous valid file when storage fails.
- Real-model tests use isolated roots, robust WAV parsing, and measured benchmarks.

### Release notes

- This candidate is intended for a GitHub draft/prerelease beta.
- The Windows installer is unsigned; SmartScreen may show a warning.
- Speech models download on first setup and are not bundled in release assets.

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
[0.2.0]: https://github.com/devjenjen/murmur-windows/releases/tag/v0.2.0
