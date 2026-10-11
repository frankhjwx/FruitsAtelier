# FruitsAtelier

![FruitsAtelier](assets/branding/wordmark-en.svg)

**English** | [简体中文](README.zh-CN.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

An independent osu!catch beatmap editor for Windows and macOS. Find a track, shape a pattern, and play it back—all in one workspace.

[Website](https://fruitsatelier.himiko.moe/) · [Download](https://github.com/frankhjwx/FruitsAtelier/releases/latest) · [Discord](https://discord.gg/Dwe7bshYHY)

**Current version: 0.9.9**

![FruitsAtelier's editing canvas, object timeline, and Catch preview](website/public/assets/eureka.png)

*Σvreka — Halv vs. kuro · Ascendance [Chronosync].*

## Make your next Catch beatmap

- **Start from your library.** Browse and search osu!stable Songs, import folders or `.osz` archives, and resume saved projects. Work on multiple difficulties in tabs and view star ratings.
- **Shape patterns directly.** Place fruits and banana showers on a vertical canvas where horizontal position is Catch placement and vertical position is time. Pan and zoom, then move, duplicate, flip, and delete selections with undo and redo.
- **Tune timing and hitsounds.** Edit red and green timing points, tap tempo, resnap objects, and hear the beat metronome. Set new combos and hitsounds, including individual slider edges. Hitsound Copier (Beta) copies samples from another difficulty, with a preview before applying.
- **Listen, preview, and testplay.** Play MP3, OGG, and WAV with hitsounds at 10%, 25%, 50%, 75%, 100%, or 150% speed. Preview NM, Easy, and Hard Rock; testplay from the current position with configurable movement keys, dash, combo feedback, and optional autoplay.
- **Use your own skin and language.** Load osu!stable skins or import `.osk` files. The interface supports English, Simplified Chinese, Traditional Chinese, Japanese, Korean, Russian, Spanish, French, Polish, Dutch, Filipino, Indonesian, and Thai.
- **Keep editing, then export.** Save projects to retain editable sliders and difficulty data. Export `.osu` difficulties or `.osz` sets, and synchronize linked difficulties with osu!stable. Review conflicting external changes and restore saved difficulty snapshots from Version history.

## Tools built for Catch

- **FSliders.** Draw horizontal movement over time using control points or Bézier handles, add repeats, or convert imported Legacy Sliders. FSliders export as ordinary osu! sliders; workspace projects retain their editable curves.
- **Direct droplet editing.** Select individual slider fruits and droplets, drag them horizontally, or enter their X positions. Use grid and distance snapping to refine their spacing.
- **Randomization and derandomization.** Compensate TinyDroplet randomness for NM or HR, or customize randomization strength and seed. Inspect the result in both preview modes when targeting HR.
- **DPB and Distance Snap.** Set Distance Per Beat as the horizontal spacing reference, with up to eight simultaneous distance presets for different patterns. Beat Snap controls time, while Grid Snap controls horizontal alignment.
- **Movement indicators and analysis.** Read incoming and outgoing spacing and see Stand, Walk, Dash, and Hyperdash connections on the canvas. Use the whole-song Movement strain graph to find and jump to demanding sections. AiMod checks overlapping object starts.
- **Editable streams and stacks.** Turn a slider curve into a fruit stream at a chosen beat subdivision, or an alternating stack with a variable width envelope. Keep the parent curve editable, adjust individual fruits, or separate the pattern into independent fruits. Both export as hit circles.

Catch `.osu` import supports versions 12–14 and stable-compatible lazer v128; export uses version 14. Video and storyboard playback are not available in 0.9.

## Get started

### Windows

1. Download the Windows x64 ZIP from [Releases](https://github.com/frankhjwx/FruitsAtelier/releases/latest).
2. Extract the entire ZIP and run `FruitsAtelier.exe`. Keep the extracted files together. The package includes .NET; Windows 10/11 and DirectX 11 are required.
3. Follow the first-launch setup to choose folders, language, skin, audio, and testplay settings. These options remain available in **Library > Settings**.
4. Import a beatmap or create a new project, then open a difficulty to start editing.

Updater-enabled installations can use **Settings > Application updates > Check for updates**, then download, save, and restart. Keep projects and custom skins outside the application's `current/` folder.

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

Workspace `.catchdiff` files retain authoring data; older `.catchproj` projects can also be opened. Keep the complete project folder and referenced resources. Version history lets you compare snapshots and restore a difficulty; its retention policy normally limits history to 30 days and 100 rounds per project. See [synchronization and recovery](docs/SYNCHRONIZATION.md) for details.

Join [Discord](https://discord.gg/Dwe7bshYHY) to test the alpha and share feedback. Report bugs on [GitHub Issues](https://github.com/frankhjwx/FruitsAtelier/issues), or [contact me on osu!](https://osu.ppy.sh/users/1806962).

## Development

The editor uses C# 12 and .NET 8. Windows source builds use SDK **10.0.400**, pinned in `global.json`; run [Run-Editor.cmd](Run-Editor.cmd) to build and launch. macOS scripts use the SDK pinned under `macOS/`.

- [Building and testing](docs/TESTING.md) · [Packaging and releases](docs/RELEASING.md)
- [Editing controls](docs/EDITOR_UI.md) · [Workspace and files](docs/WORKSPACE.md)
- [Architecture](docs/ARCHITECTURE.md) · [Project model](docs/PROJECT_MODEL.md) · [File format](docs/STABLE_FORMAT.md)
- [Localization](docs/LOCALIZATION.md) · [Third-party licenses](THIRD_PARTY_NOTICES.md)

Technical documentation and the user manual are maintained in English.

## Credits and license

- [ppy/osu](https://github.com/ppy/osu) — osu!catch algorithms and reference behavior for conversion, gameplay, difficulty calculation, and compatibility.
- [Exsper/osucatch-editor-realtimeviewer](https://github.com/Exsper/osucatch-editor-realtimeviewer) — inspiration for real-time Catch gameplay preview while mapping.
- [Phob144/DropletDerandomizer](https://github.com/Phob144/DropletDerandomizer) — inspiration for droplet derandomization and authoring Catch slider patterns.

FruitsAtelier's original source code is licensed under the [MIT License](LICENSE). Third-party code and assets retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md) for attribution and retained license texts. In particular, the included osu! resources under CC BY-NC 4.0 retain their non-commercial restriction, and SoundTouch.Net remains under LGPL-2.1-or-later.
