# Video Serial Visualizer

A VLC-based video player that remembers **exactly** where you left off — across sessions, across
your whole library. Built for working through downloaded tutorial series and course libraries,
where "resume where I was" actually matters.

It also acts as a hub: point it at folders of videos and it turns each one into a browsable series,
so an entire course library lives in one organized place instead of scattered across File Explorer.

Free and open source. Works fully offline — no account, no cloud, no telemetry.

> **Note on language:** the application interface is in Spanish, and so is the codebase — code
> comments and commit messages included. Only the outward-facing docs (this README,
> [CONTRIBUTING](CONTRIBUTING.md), [SECURITY](SECURITY.md)) are in English. That split is
> deliberate; see [CONTRIBUTING.md](CONTRIBUTING.md#language). Issues and pull requests are
> welcome in either language.

---

## Features

- **Exact progress tracking** — every video remembers its position to the millisecond. Finished
  videos restart from the beginning instead of getting stuck at the end.
- **Folders as series** — each folder you add becomes a group, sorted in natural order
  (`2` before `10`, not after), with the last video's frame as its cover.
- **Custom categories** — create your own labels (Blender, ZBrush, whatever) and assign them to
  groups with a right click, then filter by them.
- **Favorites and search** across both groups and videos.
- **Grid and list views** with progress bars on every item.
- **Built-in player** — seek bar, volume, click-to-pause, previous/next, and subtitle support
  (embedded tracks or external files, with a track picker when there's more than one).
- **Auto-advance** — a "next" button fills up over the last 30 seconds and chains into the next
  video when the current one ends.
- **Automatic updates** — checks in the background and installs on exit, never mid-playback.

Your library lives entirely on your machine, in a local SQLite database. Adding folders never
moves, renames, or modifies your video files.

---

## Installation

Download `VideoSerialVisualizer-win-Setup.exe` from the
[latest release](https://github.com/daveniam/Video-Serial-Visualizer/releases) and run it.

There's also a portable `.zip` if you'd rather not install anything.

**Requirements:** Windows 10 or later, 64-bit. Nothing else — the .NET runtime and VLC are bundled.

> Windows SmartScreen may warn you the first time, because the installer isn't code-signed yet.
> Click **More info → Run anyway** if you're comfortable doing so.

---

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/daveniam/Video-Serial-Visualizer.git
cd Video-Serial-Visualizer
dotnet run --project VideoSerialVisualizer/VideoSerialVisualizer.csproj
```

To produce an installer and update packages:

```powershell
dotnet tool install -g vpk
.\build-release.ps1
```

Output lands in `Releases/`. The `vpk` tool version must match the `Velopack` NuGet package
version in the project.

---

## Tech stack

WPF on .NET 8, [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) for playback,
EF Core + SQLite for the library, [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
for MVVM, and [Velopack](https://velopack.io/) for installation and updates.

Video is rendered into a native child window handed to libVLC directly, rather than through a
WPF overlay — this avoids the positioning bugs that overlay approaches hit on multi-monitor setups.

---

## Contributing

Bug reports and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers the build,
the language conventions, and a few things about the video rendering and libVLC that are worth
knowing before you dig in. For security issues, see [SECURITY.md](SECURITY.md) — please don't
open a public issue for those.

---

## License

Licensed under the **GNU General Public License v3.0 or later** — see [LICENSE](LICENSE).

The GPL is not an arbitrary choice here: the app ships VLC's plugin set, and some of those plugins
are GPL-licensed, which carries over to the work as a whole. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for full attribution of every bundled component
and for where to get the corresponding source of each one, including libVLC itself.

The full license texts of the bundled dependencies live in [licenses/](licenses/). Both that
folder and `LICENSE` are copied next to the executable, so every distributed copy carries them.

Copyright © 2026 David Nieves
