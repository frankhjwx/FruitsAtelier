# FruitsAtelier

![FruitsAtelier](assets/branding/wordmark-en.svg)

**English** | [简体中文](README.zh-CN.md)

An osu!catch beatmap editor for Windows and macOS. Create patterns, reshape sliders, and try your changes with music and hitsounds.

**Current version: 0.8.5**

## Features

- **Beatmap library.** Browse and search your osu!stable Songs folder, import external folders or `.osz` archives, and resume saved projects.
- **Object editing.** Place fruits and banana showers; select, move, duplicate, flip, or delete groups of objects with undo and redo.
- **Slider tools.** Draw and reshape FSliders with control points or Bezier handles, add reverses, convert imported sliders, and turn slider paths into fruit streams.
- **Snapping.** Use beat subdivisions, a horizontal grid, and distance snapping to place patterns. Adjust new combos and hitsounds on objects or individual slider edges.
- **Music and preview.** Play MP3, OGG, and WAV audio with hitsounds at 10%, 25%, 50%, 75%, 100%, or 150% speed. Preview Catch objects with skins and NM, Easy, or Hard Rock settings.
- **Testplay.** Play from the current position with movement, dash, combo feedback, and optional autoplay. Movement keys are configurable.
- **Multiple difficulties.** Switch between difficulties in tabs, view star ratings, save editable projects, and export `.osu` files or new difficulties to osu!stable.
- **Skins and languages.** Use osu!stable skins or import `.osk` files. The interface supports English and Simplified Chinese.

The editor reads Catch `.osu` files in versions 12-14 and exports version 14. Video and storyboard playback and timing-point creation are not available in 0.8.

## Get started

### Windows

Download the Windows x64 ZIP from [Releases](https://github.com/frankhjwx/FruitsAtelier/releases), extract it, and run `FruitsAtelier.exe`. Keep the extracted files together. The package includes .NET and runs on Windows 10/11 with DirectX 11 support.

Open **Library > Settings** to choose a project workspace and, optionally, your osu!stable installation folder. Import a beatmap or create a new project to begin.

### macOS

Building requires .NET SDK **8.0.419** and Xcode Command Line Tools. Run `bash scripts/Install-Mac-SDK.sh` to install the SDK locally, then open [Run-Editor-Mac.command](Run-Editor-Mac.command). Run `bash scripts/Publish-Mac.sh` to create a standalone app. See the [macOS guide](docs/MACOS.md).

## User guide

The [user manual](docs/USER_MANUAL.md) covers setup, editing, saving, testplay, and keyboard shortcuts.

Save projects to retain editable sliders and difficulty data. Export creates `.osu` files for osu!. After linking a difficulty through export, **Ctrl+S also updates its linked `.osu`**; **Ctrl+E** opens the export choices.

## Development

Windows source builds use .NET SDK **10.0.400**, pinned in `global.json`, and the .NET 8 runtime. Run [Run-Editor.cmd](Run-Editor.cmd) to build and launch.

- [Building and testing](docs/TESTING.md) · [Packaging and releases](docs/RELEASING.md)
- [Editing controls](docs/EDITOR_UI.md) · [Workspace and files](docs/WORKSPACE.md)
- [Architecture](docs/ARCHITECTURE.md) · [Project model](docs/PROJECT_MODEL.md) · [File format](docs/STABLE_FORMAT.md)
- [Localization](docs/LOCALIZATION.md) · [Third-party licenses](THIRD_PARTY_NOTICES.md)
