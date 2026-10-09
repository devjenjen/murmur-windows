# Security policy

Murmur is beta software. Do not put secrets, complete transcripts, private
audio, or unredacted logs in a public issue or pull request.

## Private reporting

Use GitHub's **Security** tab and **Report a vulnerability** flow:
<https://github.com/devjenjen/murmur-windows/security/advisories/new>

This channel works only when GitHub Private Vulnerability Reporting is enabled
for the repository. The repository owner must enable and verify that setting
before public publication. If the page does not offer a private submission,
do not disclose the issue publicly; wait for the owner to provide a verified
private channel.

Include affected version/commit, Windows version, exact reproduction steps,
impact, and a minimal synthetic example. Redact usernames, paths, tokens,
transcripts, history, and microphone data.

## Scope notes

- ASR runs locally after model download; stable update checks contact GitHub.
- `SendInput` is subject to Windows UIPI and foreground-window behavior.
- Settings, history, dictionary, models, and logs are local files and are not
  encrypted by Murmur.
