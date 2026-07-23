# Security Policy

## Reporting a vulnerability

Please **don't open a public issue** for a security problem.

Use GitHub's private reporting instead:
[**Report a vulnerability**](https://github.com/daveniam/Video-Serial-Visualizer/security/advisories/new).
It goes straight to the maintainer and stays private until there's a fix.

This is a single-maintainer hobby project, so there's no SLA. Realistically: a first reply
within a week or two. If a report is valid and I can fix it, the fix ships in the next release
and you get credit in the advisory unless you'd rather not.

## Supported versions

Only the latest release. There are no backports — if you're on an older version, update.
Installed copies update themselves automatically on exit.

## What's in scope

Bear in mind what this app actually is: an offline desktop video player. It has no server, no
accounts, no telemetry, and it doesn't upload anything. That rules out most of what people
usually report.

What's genuinely in scope:

- **Malicious media files.** A video, subtitle, or playlist file that causes code execution or
  memory corruption when opened. This is the realistic attack surface, since parsing is done by
  libVLC.
- **The update mechanism.** Anything that could get a tampered package installed — the app
  fetches updates from GitHub Releases over HTTPS and Velopack verifies them before applying.
- **Path handling in the folder scanner.** Anything that makes the app write outside its own
  data directory, or modify the user's video files. The scanner is supposed to be strictly
  read-only over the user's library.
- **The local SQLite database**, if it can be made to execute or leak something it shouldn't.

Out of scope: findings that require an attacker who already has code execution on the machine,
the missing installer code signature (known — see the README, it's a cost problem, not an
oversight), and SmartScreen warnings that follow from it.

## About the bundled libVLC

This application ships libVLC **3.0.21** and its plugin set, unmodified, from VideoLAN's
official NuGet package. Vulnerabilities in libVLC itself therefore reach users through this
app until it's rebuilt against a patched version.

If you've found a flaw in libVLC or VLC rather than in this application's own code, please
report it to VideoLAN directly — they can fix it at the source and every downstream project
benefits:

- https://www.videolan.org/security/
- security@videolan.org

Telling me as well is appreciated, so I know to bump the dependency once a patched build is
published.
