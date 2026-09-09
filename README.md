# FruitsAtelier

![FruitsAtelier](assets/branding/wordmark-en.svg)

**English** | [简体中文](README.zh-CN.md)

An independent osu!catch beatmap editor for Windows and macOS. Edit fruits and FSliders on a time–X canvas and preview Catch objects alongside the music.

The project is under active development.

## Features

- Preview [Catch hitsounds](docs/HITSOUNDS.md), including beatmap samples, slider ticks and banana sounds.

- Open `.osz` archives, v14 / Mode=2 `.osu` beatmaps, and `.catchproj` projects.
- Edit fruits, FSliders, and banana showers with beat snapping, multi-selection, group movement, cut/copy, and undo/redo.
- Convert imported Legacy Sliders into editable FSliders, then adjust anchors, Bézier handles, and span counts.
- Play MP3 / OGG / WAV audio and seek using the timeline. Preview objects with AR, CS, and Catch skins.
- Manage projects in a configurable workspace with `project.catchdiff` and automatically named difficulty files. Resources stay referenced; only explicit export writes to osu!stable Songs.
- Browse and search the local Catch library by original/romanised title, artist, creator and tags. [Workspace guide](docs/WORKSPACE.md).
- Import external folders by reference, or fully extract OSZ archives into managed workspace resources; sources stay available in the library across restarts.
- English interface by default, with a saved preference for English or Simplified Chinese.

## Running

### macOS

Requires .NET SDK **8.0.419** and Xcode Command Line Tools. To install the SDK locally within the repository, run:

```bash
bash scripts/Install-Mac-SDK.sh
```

Double-click [Run-Editor-Mac.command](Run-Editor-Mac.command) to build and launch the editor. To create a standalone application:

```bash
bash scripts/Publish-Mac.sh
```

The output is `artifacts/macos/FruitsAtelier.app`, including the .NET runtime. See the [macOS guide](docs/MACOS.md) (Chinese).

### Windows

Requires .NET SDK **10.0.400** (pinned in the root `global.json`) and the **.NET 8 runtime**. Double-click [Run-Editor.cmd](Run-Editor.cmd) to build and launch the editor.

The compiled application is located at `src/FruitsAtelier.App/bin/Release/net8.0-windows/FruitsAtelier.App.exe`.

## Documentation

The following guides are in Chinese; third-party notices are in English.

- [Editing controls](docs/EDITOR_UI.md)
- [Features and files](docs/PRODUCT.md)
- [Building and testing](docs/TESTING.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Project data model](docs/PROJECT_MODEL.md) · [Catch rendering and conversion](docs/CATCH_RENDERING.md) · [File format](docs/STABLE_FORMAT.md)
- [Localization maintenance](docs/LOCALIZATION.md)
- [Third-party dependencies and licenses](THIRD_PARTY_NOTICES.md)
