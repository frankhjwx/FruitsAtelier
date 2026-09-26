# FruitsAtelier

User Manual | Version 0.8.2

## 01 / Getting started

FruitsAtelier is an osu!catch beatmap editor for Windows and macOS. Use it to edit fruits, sliders and banana showers, preview patterns with music, and testplay your changes.

### First launch

1. On Windows, extract the entire release ZIP and open `FruitsAtelier.App.exe`. Keep the DLLs and assets beside it. No separate .NET installation is needed. Windows 10/11 x64 and DirectX 11 are required; Windows N needs the Media Feature Pack for MP3 playback.
2. In **Library > Settings**, choose a workspace for your projects. You may also select your osu!stable installation folder to use its Songs and Skins folders. Keep the workspace separate from Songs.
3. Right-click the library to import a folder or a beatmap/OSZ, or choose **New project**. Double-click a library entry to start or continue editing.
4. Choose the interface language in **Settings → Appearance** and use the top-bar Skin control to set up the display. Testplay movement keys can be changed in Settings.

On macOS, open the standalone `FruitsAtelier.app`. Source-build instructions are in the repository's macOS guide. Command can be used in place of Ctrl for editor shortcuts.

### What you can do

- Browse and search songs by title, artist, mapper, difficulty or tags; resume work from **My projects**.
- Place fruits and banana showers; move, duplicate, flip and delete groups with undo/redo.
- Draw FSliders with control points or Bezier handles; add reverses and create fruit streams.
- Align patterns with beat, grid and distance snapping; edit combos and hitsounds.
- Play music and hitsounds at 10%–150% speed, preview skins and mods, and testplay with optional autoplay.
- Work on several difficulties in one project, view star ratings, and export Catch beatmaps.

### Supported files

| File | Use |
| --- | --- |
| `.osu` / `.osz` | Import Catch difficulties (Mode 2, osu versions 12-14); export version 14 `.osu`. |
| `.catchdiff` | Saved workspace projects and their editable difficulties. |
| `.catchproj` | Open older editor projects. Subsequent saves use the workspace format. |
| `.osk` | Import Catch skin assets. |
| MP3 / OGG / WAV | Music playback; replace audio through the File menu. |

## 02 / Editing patterns

### Navigate and select

The main canvas shows horizontal placement and note timing; later notes are higher on the screen. On the canvas and both timelines, each wheel notch moves one full beat (1/1) during playback or one current Snap subdivision while paused: up to the preceding grid line and down to the following one, regardless of zoom. The canvas and playhead move together, including while paused. Middle-drag pans, and Alt+wheel over the canvas zooms. Ctrl+wheel changes Snap; Shift+wheel seeks four times as far. Click empty canvas in Select mode to clear selection without seeking. The bottom timeline also supports seeking; click the timestamp above its left-side Play, Pause, Stop, and Testplay controls to jump to an exact time. Stop pauses playback and returns to the start. Hover over the timeline to reveal the fixed bookmark toolbar above it: add or remove a bookmark at the playhead, seek to the previous or next bookmark, or reset all bookmarks. Bookmark edits can be undone. The canvas left axis displays blue bookmark markers and red timing markers; faint bookmark lines cross the canvas. Dense markers share display rows and reveal counts and time ranges on hover without changing their stored timestamps. Ordinary canvas time labels are blue.

The Timing menu can set the current position as the song preview point. A long yellow line marks it on the bottom timeline. Red and green timing marks appear on the upper object timeline, while shaded break intervals appear there and on the canvas's left time axis. To insert a break, place the playhead between two objects with enough space and click **Insert Break Time** next to Movement Analysis. Undo removes the inserted break.

Press **F3** for the Timing page: edit BPM, offset and Slider Tick Rate, or tap with
**T** during playback and apply the measured tempo. The metronome plays each beat;
hold **Ctrl** to hear the current Snap subdivisions. **F1** returns to Compose.
**F6 / Timing Setup** opens red/green control-point editing, sample settings,
volume and Kiai. OK applies the draft as one undo step; Cancel discards it.
The apply options can scale or resnap objects, recalculate slider lengths, and
adjust bookmarks and the preview point. See [Timing editing](EDITOR_UI.md#timing-editing)
for selection, clipboard, section commands and transformation rules.

When the playhead is inside kiai time, a small Kiai badge appears in the upper-left of the editing plot. It brightens on each full beat and fades until the next beat.

Break shading on the upper timeline extends lightly to the notes before and after the stored break. Drag either edge of its darker center to adjust the range; with Snap on, the edge follows the current beat subdivision. Shortening it below 400 ms removes it; Esc cancels a drag, and Undo restores the previous range.

During Testplay, Ctrl+B adds a bookmark at the current time and Ctrl+Shift+B removes a nearby bookmark. Both actions remain undoable after returning to the editor.

Select an object with **1**. Drag empty space to box-select, or Ctrl-click to toggle selection. Drag selected objects to move them together. Selecting a slider fruit or droplet selects its parent slider. The horizontal object timeline also lets you select and move objects in time.

### Place objects

| Tool | How to use it |
| --- | --- |
| Fruit: F / 2 | Left-click to place a fruit at the current snap position. |
| FSlider: B / 3 | Click a start, then add points. Right-click away from placed points or press Enter to finish. Esc cancels. |
| Banana shower: N / 4 | Left-click the start, then right-click at a later time to finish. Drag its body to move it, or its ends to resize it. |

Fruit and slider placement previews show hyperdash markers before you confirm placement, including changes to the preceding object. Moving the pointer updates the markers; cancelling placement removes the temporary preview.

### Shape sliders

Hover over the FSlider tool to choose **osu legacy mode** or **pen tool mode**. Both edit the same FSlider. Legacy mode uses control points; pen mode lets you drag Bezier handles while placing anchors. Ctrl-click adds a straight segment. Click the last draft point again to begin a new segment.

Double-click an FSlider, or select it and press B, to edit its points. Drag points or handles to reshape it. Ctrl-click within its time range to insert a point; Ctrl-click an existing point to make it straight. Right-click a straight point to make it curved, then right-click again to delete it. A right-click on the slider body away from points deletes the slider.

Hold the mouse button on an imported slider until its actions appear, then choose **Convert to FSlider** to edit its shape. **Edit > Convert all sliders to FSliders** converts the active difficulty. Conversion can approximate the imported path; undo restores the original.

**Ctrl+= / Ctrl+-** adds or removes a reverse. **Ctrl+G** reverses path direction. **Ctrl+J** extends a selected FSlider to the pointer at a time after its final end. Repeated spans share one base path, so extending it lengthens every span.

### Fruit streams and snapping

Select sliders and press **Ctrl+Shift+F**, choose a beat subdivision, and confirm to create a fruit stream. It remains an editable slider shape in the project and exports as individual fruits. Click a stream fruit once to select the whole slider, which can then be dragged in time and X. Click a fruit again without dragging to select that event; its bright outer ring shows which fruit will move when dragged horizontally. Hold on a stream to change snapping or convert it back to a slider.

Beat Snap offers 1/1 through 1/9, plus 1/12 and 1/16. Grid Snap controls horizontal placement. Distance Snap spaces objects relative to the previous object; use Configure DS… to edit its multipliers, or hold Alt to temporarily invert snapping. New Combo and Whistle/Finish/Clap are available on the right toolbar. Selecting a slider edge lets you edit that edge's hitsound.

## 03 / Preview, testplay and saving

### Listen and preview

Press **Space** to play or pause. Playback speeds are 10%, 25%, 50%, 75%, 100% and 150%, with music pitch preserved. Hitsounds include beatmap samples and slider ticks. Editing and navigation remain available without playable audio.

Open **Catch Preview** using the button on the right edge of the canvas. Drag the divider to resize it. Choose 4:3, 16:9 or Fit, and NM, Easy or Hard Rock. These preview settings do not change the saved beatmap. Choose an osu!stable skin or import an `.osk`; missing images fall back to the default appearance.

### Try the map

Press **F5** to testplay using the selected preview mod and speed. Testplay immediately begins one second before the current position by default; change the lead-in from 0 to 5 seconds in **Settings > Testplay keys**. A lead-in that reaches before the song starts begins at zero. Esc returns to the selected position. Move with **Left / Right**, and hold **Shift** to dash. Catch fruits and droplets to build combo. **Tab** toggles autoplay; **Ctrl+P** pauses or resumes.
Press **Ctrl+B** during testplay to add a bookmark at the current position. The shortcut appears with the other testplay controls in the upper-left corner.

**F1 / Esc** exits to the testplay start; **F2** exits at the current position. Losing window focus releases held keys while playback continues. A movement or dash key exits autoplay; a centered banner briefly announces entering or leaving autoplay. Testplay does not change your objects or undo history. Change movement and dash bindings in **Library > Settings**.

Bindings accept letters, digits, punctuation (including `;`, `'`, `[` and `]`), arrow and navigation keys, Backspace, Enter, Space, Shift, Ctrl, Alt, lock keys, numpad keys, and F3–F24. Esc cancels capture; Tab, F1 and F2 remain reserved for testplay controls. Windows/Command, media and other system keys are not offered. Left and right modifier keys share a binding, as do main and numpad Enter. Numpad input follows Num Lock; punctuation labels use US keyboard names. OS shortcuts and Ctrl+P retain their normal behavior.

### Difficulties and project files

Click a difficulty tab or use Ctrl+Tab to switch. The **+** button adds a blank difficulty or imports an `.osu`. Blank difficulties inherit the active difficulty's audio, timing and settings. Tabs show live No Mod star ratings and an unsaved-change dot.

The workspace contains a `project.catchdiff` manifest and separate `.catchdiff` difficulty files. Save the whole project folder and keep its referenced resources available. External folders remain in place; OSZ resources are extracted into the workspace's Resources folder. Moving just a difficulty file may break resource links.

### Save and export

| Action | Result |
| --- | --- |
| Ctrl+S: Save | Saves the project's editable difficulty data. Workspace-only projects offer an optional Songs export after saving; choosing to keep the project in the workspace completes the save. For projects already in Songs, an imported difficulty’s first save opens export choices, and subsequent saves update its linked `.osu`. |
| Ctrl+Alt+E: Export | Opens choices for the active difficulty: a standalone `.osu`, overwriting an associated difficulty, or creating a new difficulty in Songs. |

Exporting a new difficulty to Songs saves your edits in a new workspace difficulty and activates it. The original difficulty keeps its last saved content. Exported `.osu` files do not preserve all editor-specific controls, so keep the workspace project for further editing.

Use **Library** or Esc to return to the library. Unsaved work prompts for Save, Discard or Cancel. Missing-resource messages indicate that a referenced file needs to be restored or relinked.

Version 0.8 does not provide video or storyboard playback. Imported timing and slider velocity are supported. Testplay is for checking patterns; star ratings and exported behavior may differ between osu! versions.

## 04 / Keyboard reference

Shortcuts below apply while editing, outside text fields and dialogs. On macOS, Command also works for Ctrl shortcuts; Backspace also deletes. Some Mac keyboards require Fn for function keys.

### Files, selection and editing

| Keys | Action |
| --- | --- |
| Ctrl+O | Choose a difficulty in the current project. |
| Ctrl+Shift+O | Open a beatmap, OSZ or older project. |
| Ctrl+S | Save project. |
| Ctrl+Alt+E | Open export choices. |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous difficulty. |
| 1 / F / B / N | Select / Fruit / FSlider / Banana tools. |
| 1 / 2 / 3 / 4 | The same four tools. |
| Ctrl+A | Select all objects. |
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / copy / paste selected objects. |
| Ctrl+D | Clone selection one measure after its last start. |
| Delete | Delete selected objects or edited points. |
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Undo / redo / redo. |
| Ctrl+H | Flip selected objects horizontally. |
| Ctrl+Left / Ctrl+Right | Seek to the previous / next bookmark. |
| Ctrl+Shift+Left / Ctrl+Shift+Right | Move selected objects one horizontal unit. |
| J / K | Move selection back / forward one beat subdivision. |
| Esc | Cancel the current action; otherwise return to Library. |

Object paste works within the same difficulty session and aligns the earliest selected start with the playhead. Copy also places an osu! timestamp reference on the system clipboard.

### Playback and navigation

| Keys or mouse | Action |
| --- | --- |
| Space / C | Play or pause. |
| X / Home | Play from song start / seek to song start. |
| Z / V (also End) | First object's start / last object's end; repeat for song start / end. |
| Left / Right | Seek one beat subdivision; Shift multiplies by four. |
| Up / Down | Previous / next timing point. |
| Ctrl+Up / Ctrl+Down | Increase / decrease playback speed by 25 percentage points (10%–150%). |
| Ctrl+Shift+Up / Ctrl+Shift+Down | Increase / decrease playback speed by 5 percentage points. |
| Wheel / middle-drag | Wheel up moves the playhead and canvas earlier; down moves both later by the same relative amount. Middle-drag pans the canvas. |
| Ctrl+wheel | Change Snap across all supported subdivisions. |
| Alt+wheel (canvas) | Zoom the canvas. |
| Alt+wheel (upper timeline) | Zoom the object timeline. |
| Shift+wheel | Seek four times as far. |
| Ctrl+Alt+wheel (canvas / upper timeline) | Cycle placement tools. |
| Click the current timestamp | Open Jump to time; accepts timestamps or milliseconds. |

## 05 / Slider, snap and testplay keys

### Sliders

| Keys | Action |
| --- | --- |
| Enter / Esc | Finish / cancel a slider draft. |
| F3 / F1 | Open Timing / return to Compose. |
| F6 | Open Timing and Control Points. |
| Ctrl+P / Ctrl+Shift+P | Add a red / green timing point. |
| Ctrl+I | Delete the current timing section. |
| Ctrl+Shift+I | Insert a point on the curve under the pointer. |
| Ctrl+L | Toggle the selected point between straight and curved. |
| Ctrl+= / Ctrl+- | Add / remove a reverse. |
| Ctrl+G | Reverse the selected FSlider's path direction. |
| Ctrl+J | Extend the selected FSlider to the pointer. |
| Ctrl+Shift+F | Convert sliders to a fruit stream, or change stream snap. |

### Snapping, combos and hitsounds

| Keys | Action |
| --- | --- |
| Shift+1 through Shift+9 | Choose beat subdivision 1/1 through 1/9. |
| Ctrl+M | Cycle 1/3, 1/4, 1/6 and 1/8; enter at 1/3 from another divisor. |
| Ctrl+1 / 2 / 3 / 4 | Set horizontal grid size to 4 / 8 / 16 / 32. |
| G / T | Cycle grid size / toggle Grid Snap. |
| Y | Toggle Distance Snap. |
| Hold Alt / hold Shift | Temporarily invert Distance Snap / Grid Snap. |
| Q | Toggle New Combo. |
| W / E / R | Toggle Whistle / Finish / Clap hitsounds. |
| L | Toggle Lock Notes. |

Lock Notes prevents moving, reshaping or deleting existing objects. You can still select, play, add objects, edit flags and use undo/redo. Shift does not invert Grid Snap while Alt is held.

### Testplay

| Keys | Action |
| --- | --- |
| F5 (in the editor) | Start testplay at the playhead. |
| Left / Right | Move the catcher (default bindings). |
| Shift | Dash (default binding). |
| Tab | Toggle autoplay. |
| Ctrl+P | Pause / resume testplay. |
| Ctrl+B | Add a bookmark at the current position. |
| F1 / Esc | Return to the editor at the testplay start. |
| F2 | Return to the editor at the current position. |

For detailed editing behavior, see `docs/EDITOR_UI.md` in the repository. Project and resource management are described in `docs/WORKSPACE.md`.

### Audio volume

In the editor, click the bottom-right **Volume** button or **View → Volume** for three vertical bars: **Master**, **Music**, **Effect**. Drag a bar, use **Alt+Left/Right** to choose a channel, or **Alt+Up/Down** to adjust it by 5%. The same Alt+arrow shortcuts show the bars during testplay without moving the catcher. The controls fade in, stay visible while hovered or adjusted, and wait 0.3 seconds before fading out. In the editor, Esc closes them. Drawing drafts remain active.

Open **Library > Settings** to adjust **All**, **Song** and **Hitsound** from 0% to 100%. Values apply immediately and are saved when you release the slider. All multiplies both other channels. Setting Song to 0% leaves hitsounds audible; setting Hitsound to 0% leaves the song audible. Custom skin samples apply in both preview and testplay, with beatmap custom samples taking priority.

### Application updates (Windows)

Open **Library → Settings → Application updates** to check for a new stable
version, view release notes, and download it from GitHub Releases. Automatic
startup checks are enabled by default and run on every launch; the setting
can be disabled. Available updates show a clickable notification in the library
and editor. Opening the update page checks immediately unless an update is
already available; the separate **Check for updates** button also supports
immediate retries. Downloads do not interrupt editing. Choose **Save and restart
to update** when ready. All unsaved difficulties are saved before restart; a
save failure leaves the editor open. A downloaded update waits for this explicit
action even after restarting the editor. Close other windows of the same
installation first. Keep projects and custom skins outside the program
`current/` folder, which is replaced during updates.
