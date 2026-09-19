# Features and Files

FruitsAtelier edits osu!catch beatmaps on a time鈥揦 canvas and previews object placement alongside music. The application has separate Windows and macOS desktop hosts.

## Editable objects

| Object | Editing |
| --- | --- |
| Fruit | Place, move, and delete at a specific time and X position |
| FSlider | Define a path with increasing-time anchors and Bezier handles; mix straight and curved segments and repeat spans |
| Legacy Slider | Preserve imported L/B/P/C paths and samples; convert to FSlider to edit nodes |
| Banana shower | Edit start and end times; individual banana positions come from the beatmap RNG |

The main canvas and right-hand preview show converted Fruit, Droplet, TinyDroplet, and Banana objects. FSliders generate the corresponding Catch slider sequence; auxiliary anchors do not add ticks.

Converting a Legacy Slider to FSlider preserves its parent ID, source order, span count, and sample information. Conversion is constrained by path boundaries, SV, and the shared repeat path. On failure, the original object is retained and the reason is displayed.

See [Editing Controls](EDITOR_UI.md) for shortcuts, selection, and control-point operations.

## Files

| Format | Purpose |
| --- | --- |
| `.osz` | Open a beatmap archive with its difficulties and associated resources |
| `.osu` | Read v12–v14 / Mode=2 Catch beatmaps; export v14 |
| `.catchproj` | Compatible editor project format containing nodes, handles, timing, imported context, and resource references |
| `.catchdiff` | Workspace project manifest and separate difficulty documents; see [Workspace](WORKSPACE.md) |
| `.osk` | Import Catch skin images and configuration |

Projects reference audio by path. Preserve the relative resource locations when moving a project. Exporting `.osu` quantizes times and path coordinates and reports read-back results; save the editor project to retain editable data.

## Current scope

Supported features include multiple timing points, inherited SV, beat snapping, batch object operations, undo/redo, English and Chinese interfaces, and MP3 / OGG / WAV playback and seeking. The preview uses AR, CS, and the selected skin to display objects and hyperdash markers.

Song playback supports 10%, 25%, 50%, 75%, 100%, and 150% speed while preserving pitch; hitsounds keep their original speed. Testplay starts at the playhead and supports movement, dash, combo feedback, pause and autoplay. Preview and testplay animate fruit and banana sprites and catcher effects; the editing canvas uses static sprites. Slider-to-stream conversion retains editable geometry and exports individual fruits.

Audio waveforms, timing-point creation, bookmarks, video and storyboard playback are not provided. See the [user manual](USER_MANUAL.md) for a concise feature guide and keyboard reference.
