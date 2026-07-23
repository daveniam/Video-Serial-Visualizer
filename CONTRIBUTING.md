# Contributing

Thanks for taking a look. This is a small, single-maintainer project, so the process is
informal — but a few things are worth knowing before you spend time on a change.

## Language

**The codebase is in Spanish.** Code comments, XML doc comments, commit messages, and every
user-facing string in the app are Spanish. Only the outward-facing documentation (this file,
the README, `SECURITY.md`, issue templates) is in English, so that people who don't read
Spanish can still evaluate the project.

That split is deliberate, not an accident mid-translation. If you contribute code, keep new
comments and UI strings in Spanish so the codebase stays consistent. Issues and pull requests
are welcome in either language.

Identifiers (class, method, and variable names) are English, with a few domain words that
stayed Spanish because the UI uses them (`Completado`, `Categoria`). Follow whatever the
surrounding file already does.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download) and Windows — the app is WPF
and x64-only.

```bash
git clone https://github.com/daveniam/Video-Serial-Visualizer.git
cd Video-Serial-Visualizer
dotnet build VideoSerialVisualizer/VideoSerialVisualizer.csproj
```

The first restore downloads over 100 MB of libVLC binaries, so give it a minute.

To run it:

```bash
dotnet run --project VideoSerialVisualizer/VideoSerialVisualizer.csproj
```

Note that the auto-updater deliberately does nothing outside an installed build — see
`UpdateService.IsInstalled`. That is expected when running from source.

## Licensing of contributions

The project is **GPL-3.0-or-later**, and it has to stay that way: the app ships VLC's plugin
set, and some of those plugins are GPL. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
for the full reasoning.

By opening a pull request you agree that your contribution is licensed under GPL-3.0-or-later.
Please don't paste in code you don't have the right to relicense.

New source files need the license header that every other file carries:

```csharp
// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.
```

If you add a new NuGet dependency, it must be added to `THIRD-PARTY-NOTICES.md` along with its
license text in `licenses/`. This is a licensing obligation, not bookkeeping — a dependency
that ships with the binary but isn't attributed puts the whole distribution out of compliance.
CI checks that the license files reach the build output, but it cannot check that a new
dependency was documented. That part is on us.

## Pull requests

- Branch off `main`.
- Keep changes focused. A PR that fixes one thing gets reviewed; a PR that fixes five things
  and reformats two files does not.
- CI has to be green. It builds in Release on Windows and verifies the license files ship.
- There is no test suite yet. If you're touching progress tracking or the folder scanner, say
  in the PR how you verified the change by hand.

## Things worth knowing before you dig in

- **Video rendering** uses a native child window handed straight to libVLC, not a WPF overlay.
  This is intentional — the overlay approach breaks on multi-monitor setups. See
  `Helpers/VideoSurfacePanel.cs`.
- **libVLC crashes natively.** Some failure modes (GPU decoding, thumbnail generation) cannot
  be caught from managed code and take the process down. If you're touching playback or
  thumbnails, assume you cannot `try/catch` your way out of a bad call — the fix is to avoid
  making it.
- **The database** is SQLite via EF Core, created on first run in the user's local app data.
  Adding folders never moves, renames, or modifies the user's video files, and it should stay
  that way.

## Reporting bugs

Open an issue. Include your Windows version, the app version (it's in the **Acerca de**
window), and what you were doing. For anything security-related, read
[SECURITY.md](SECURITY.md) first — please don't open a public issue for that.
