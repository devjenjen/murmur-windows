# Security policy

Murmur is beta software. Keep passwords, tokens, private audio, transcripts,
and unredacted logs out of public issues and pull requests.

## Private reporting

Use GitHub's [private vulnerability report](https://github.com/devjenjen/murmur-windows/security/advisories/new)
when the repository offers that form.

The form is available only when the owner has enabled GitHub Private
Vulnerability Reporting. If it is unavailable, ask the owner for a private
contact before sharing vulnerability details.

Include the affected version or commit, Windows version, reproduction steps,
and impact. Use a small example with invented data. Remove personal data,
including usernames, private paths, tokens, transcripts, and recordings.

## Automated secret checks

The Secret scan workflow runs Gitleaks on pushes and pull requests. It scans
the checked-out Git history using the standard rules in `.gitleaks.toml`.
Findings fail the check, and secret values are redacted in its logs.

## Data and Windows restrictions

- Speech recognition runs locally. Model downloads and startup update checks contact GitHub.
- `SendInput` is subject to Windows UIPI and foreground-window behavior.
- Settings, history, dictionary, models, and logs are local files and are not
  encrypted by Murmur.
