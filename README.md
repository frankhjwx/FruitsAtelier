# FruitsAtelier

![FruitsAtelier](assets/branding/wordmark-en.svg)

**English** | [简体中文](README.zh-CN.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

An independent osu!catch beatmap editor for Windows and macOS. Find a track, shape a pattern, and play it back—all in one workspace.

[Website](https://fruitsatelier.himiko.moe/) · [Download](https://github.com/frankhjwx/FruitsAtelier/releases/latest) · [Discord](https://discord.gg/Dwe7bshYHY) · [Himiko on osu!](https://osu.ppy.sh/users/1806962)

**Current version: 0.9.9**

![FruitsAtelier's editing canvas, object timeline, and Catch preview](website/public/assets/eureka.png)

*Σvreka — Halv vs. kuro · Ascendance [Chronosync]. Beatmaps and skin artwork belong to their respective creators.*

## Make your next Catch beatmap

- **Start from your library.** Browse and search osu!stable Songs, import folders or `.osz` archives, and resume saved projects. Work on multiple difficulties in tabs and view star ratings.
- **Shape patterns directly.** Place fruits and banana showers; move, duplicate, flip, and delete selections with undo and redo. Draw FSliders with control points or Bézier handles, add reverses, convert imported sliders, and create fruit streams from paths.
- **Tune placement and timing.** Use beat subdivisions, horizontal grids, and distance snapping. Edit red and green timing points, tap tempo, resnap objects, and hear the beat metronome. Set new combos and hitsounds, including individual slider edges.
- **Listen, preview, and testplay.** Play MP3, OGG, and WAV with hitsounds at 10%, 25%, 50%, 75%, 100%, or 150% speed. Preview NM, Easy, and Hard Rock; testplay from the current position with configurable movement keys, dash, combo feedback, and optional autoplay.
- **Use your own skin and language.** Load osu!stable skins or import `.osk` files. The interface supports English, Simplified Chinese, Traditional Chinese, Japanese, Korean, Russian, Spanish, French, Polish, Dutch, Filipino, Indonesian, and Thai.
- **Keep editing, then export.** Save projects to retain editable sliders and difficulty data. Export `.osu` files or add new difficulties to osu!stable.

Catch `.osu` import supports versions 12–14 and stable-compatible lazer v128; export uses version 14. Video and storyboard playback are not available in 0.9.

## Get started

### Windows

1. Download the Windows x64 ZIP from [Releases](https://github.com/frankhjwx/FruitsAtelier/releases/latest).
2. Extract the entire ZIP and run `FruitsAtelier.exe`. Keep the extracted files together. The package includes .NET; Windows 10/11 and DirectX 11 are required.
3. Follow the first-launch setup to choose folders, language, skin, audio, and testplay settings. These options remain available in **Library > Settings**.
4. Import a beatmap or create a new project, then open a difficulty to start editing.

### macOS

Build from source with .NET SDK **8.0.419** and Xcode Command Line Tools. From the repository root:

```bash
bash scripts/Install-Mac-SDK.sh
./Run-Editor-Mac.command
```

Run `bash scripts/Publish-Mac.sh` to create a standalone app. See the [macOS guide](docs/MACOS.md) for details.

## Learn the editor

The [user manual](docs/USER_MANUAL.md) covers setup, editing, saving, and testplay. The [keyboard and mouse reference](docs/KEY_BINDINGS.md) lists the full controls.

After linking a difficulty through export, **Ctrl+S also updates its linked `.osu`**. Use **Ctrl+Alt+E** to open the export choices.

Join [Discord](https://discord.gg/Dwe7bshYHY) to test the alpha and share feedback. Report bugs on [GitHub Issues](https://github.com/frankhjwx/FruitsAtelier/issues), or contact [Himiko on osu!](https://osu.ppy.sh/users/1806962).

## Development

The editor uses C# 12 and .NET 8. Windows source builds use SDK **10.0.400**, pinned in `global.json`; run [Run-Editor.cmd](Run-Editor.cmd) to build and launch. macOS scripts use the SDK pinned under `macOS/`.

- [Building and testing](docs/TESTING.md) · [Packaging and releases](docs/RELEASING.md)
- [Editing controls](docs/EDITOR_UI.md) · [Workspace and files](docs/WORKSPACE.md)
- [Architecture](docs/ARCHITECTURE.md) · [Project model](docs/PROJECT_MODEL.md) · [File format](docs/STABLE_FORMAT.md)
- [Localization](docs/LOCALIZATION.md) · [Third-party licenses](THIRD_PARTY_NOTICES.md)

Technical documentation and the user manual are maintained in English.
