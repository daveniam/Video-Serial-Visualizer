# Video Serial Visualizer

A VLC-based video player that remembers **exactly** where you left off — across sessions, across
your whole library. Built for working through downloaded tutorial series and course libraries,
where "resume where I was" actually matters.

It also acts as a hub: point it at folders of videos and it turns each one into a browsable series,
so an entire course library lives in one organized place instead of scattered across File Explorer.

Free and open source. Works fully offline — no account, no cloud, no telemetry.

> **Note on language:** the interface ships in seven languages (see Features below). The
> **codebase** is in Spanish, however — code comments and commit messages included — while the
> outward-facing docs (this README, [CONTRIBUTING](CONTRIBUTING.md), [SECURITY](SECURITY.md)) are
> in English. That split is deliberate; see [CONTRIBUTING.md](CONTRIBUTING.md#language). Issues and
> pull requests are welcome in either language.
>
> Translations live in [`VideoSerialVisualizer/Localization/`](VideoSerialVisualizer/Localization/)
> as one JSON file per language. Fixing an awkward phrase means editing a single line — corrections
> from native speakers are very welcome, especially for Chinese, Japanese and Russian.

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
- **Seven interface languages** — English, Spanish, Simplified Chinese, Japanese, German, Russian
  and French. Picked automatically from your Windows language on first run, changeable in Settings,
  and applied instantly without restarting.
- **Automatic updates** — checks in the background and installs on exit, never mid-playback.

Your library lives entirely on your machine, in a local SQLite database. Adding folders never
moves, renames, or modifies your video files.

---

## Installation

Download `VideoSerialVisualizer-win-Setup.exe` from the
[latest release](https://github.com/daveniam/Video-Serial-Visualizer/releases) and run it.

There's also a portable `.zip` if you'd rather not install anything.

**Requirements:** Windows 10 or later, 64-bit. Nothing else — the .NET runtime and VLC are bundled.

The installer needs no administrator rights: it installs into your user folder and creates the
shortcuts for you.

### About the SmartScreen warning

Windows will show **"Windows protected your PC"** the first time you run the installer. To proceed,
click **More info → Run anyway**.

This happens because the installer is **not code-signed yet**, and it's fair to be cautious about
that — so here's the honest explanation rather than a "just click through it".

A code-signing certificate costs a few hundred dollars a year, which is hard to justify for a free
project. [SignPath.io](https://signpath.org) grants them at no cost to open-source projects, and
this one has been submitted for review; the signature will be in place once that's approved.
Until then Windows has no publisher identity to check, so it warns — the same warning any unsigned
program gets, whether or not there's anything wrong with it.

**If you'd rather not take my word for it**, you don't have to:

- Every line of this app is in this repository, and the release is built from it with
  [`build-release.ps1`](build-release.ps1) — no hidden steps.
- You can [build it yourself](#building-from-source) and skip the download entirely. The result is
  the same application.
- The installer is unsigned, **not** signed by an unknown party. If Windows ever reports a
  publisher on this app that isn't listed here, that build did not come from this project.

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

## Supporting the project

Video Serial Visualizer is free and always will be — no paid tier, no locked features, no ads.

If it saves you time and you'd like to give something back, there's a
**[pay-what-you-want page on Gumroad](https://davenieves.gumroad.com/l/ksehea)** (you can also find
it under *Settings → About*). Any amount is appreciated and none is expected.

Contributing code, reporting a bug, or fixing a translation helps just as much, and costs nothing.

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
