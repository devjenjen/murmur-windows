# Changelog

Newest release first.

## [Unreleased]

- Add English number words and math expressions to the existing Math profile. German and English dictation work regardless of interface language in Unicode and LaTeX modes.
- English number words cover whole numbers from 0 through 999. Formatting uses fixed rules and does not solve equations.

## [0.2.2] - 2026-10-09

- Publish the Windows beta with a Setup EXE, portable ZIP, build metadata, and checksums.
- Document profiles, German math formatting, hotkeys, history controls, and recovery text in the English and German READMEs.
- Add direct beta download links and explain how beta updates are checked manually.
- Exclude additional personal audio formats and private key files from source control.
- Retire the old 0.1.0 binary downloads, which lacked bundled license notices. Historical source tags remain available.

## [0.2.1] - 2026-10-09

- Add automated Gitleaks scans of Git history on pushes and pull requests.
- Remove obsolete sound-provider references from notices and setup instructions.
- Clarify how to add a personal feedback WAV and when Murmur stays silent.
- Update the .NET runtime inventory to match the 10.0.12 packages in the beta build.
- Fix the prerelease flag passed to the draft publication script.
- Update the GitHub artifact download action and Microsoft.NET.Test.Sdk.

## [0.2.0] - 2026-10-09

### Added

- Text insertion at the cursor or document end, with migration of existing settings.
- Optional dictation history with age and entry limits.
- Model downloads check archive integrity and clean up after cancellation.
- Beta release verification, package inventory, checksums, and build metadata.

### Improved

- Cancellation, model changes, and shutdown wait for session work before releasing native resources.
- Streaming keeps audio in its recognition queue. Model loading checks integrity once when constructing the recognizer.
- Timing records streaming completion separately from offline inference and measures the delay from key release to text insertion.
- History saves after the insertion attempt and rewrites files only when retention removes entries.
- Model status callbacks ignore stale selections and report download phases.
- Settings writes preserve the previous valid file when storage fails.
- Real-model tests use isolated roots, robust WAV parsing, and measured benchmarks.

### Release notes

- Version 0.2.0 is a beta release candidate.
- The Windows installer is unsigned; SmartScreen may show a warning.
- Speech models download on first setup and are not bundled in release assets.

## [0.1.0] - 2026-08-27

### Added

- Settings pages for setup, history, and dictionary editing.
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

[0.1.0]: https://github.com/devjenjen/murmur-windows/tree/v0.1.0
[0.2.0]: https://github.com/devjenjen/murmur-windows/tree/v0.2.0
[0.2.1]: https://github.com/devjenjen/murmur-windows/tree/v0.2.1
[0.2.2]: https://github.com/devjenjen/murmur-windows/releases/tag/v0.2.2
