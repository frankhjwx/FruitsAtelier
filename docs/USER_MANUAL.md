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

The main canvas shows horizontal placement and note timing; later notes are higher on the screen. On the canvas and both timelines, each wheel notch moves one full beat (1/1) during playback or one current Snap subdivision while paused: up to the preceding grid line and down to the following one, regardless of zoom. The canvas and playhead move together, including while paused. Middle-drag pans, and Ctrl+wheel zooms. Click empty canvas in Select mode to seek. The bottom timeline also supports seeking; click the timestamp to jump to an exact time.

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

Select sliders and press **Ctrl+Shift+F**, choose a beat subdivision, and confirm to create a fruit stream. It remains an editable slider shape in the project and exports as individual fruits. Hold on a stream to change snapping or convert it back to a slider.

Beat Snap offers 1/1 through 1/9, plus 1/12 and 1/16. Grid Snap controls horizontal placement. Distance Snap spaces objects relative to the previous object; use Configure DS… to edit its multipliers, or hold Alt to temporarily invert snapping. New Combo and Whistle/Finish/Clap are available on the right toolbar. Selecting a slider edge lets you edit that edge's hitsound.

## 03 / Preview, testplay and saving

### Listen and preview

Press **Space** to play or pause. Playback speeds are 10%, 25%, 50%, 75%, 100% and 150%, with music pitch preserved. Hitsounds include beatmap samples and slider ticks. Editing and navigation remain available without playable audio.

Open **Catch Preview** using the button on the right edge of the canvas. Drag the divider to resize it. Choose 4:3, 16:9 or Fit, and NM, Easy or Hard Rock. These preview settings do not change the saved beatmap. Choose an osu!stable skin or import an `.osk`; missing images fall back to the default appearance.

### Try the map

Press **F5** to testplay from the current position using the selected preview mod and speed. Move with **Left / Right**, and hold **Shift** to dash. Catch fruits and droplets to build combo. **Tab** toggles autoplay; **Ctrl+P** pauses or resumes.

**F1 / Esc** exits to the testplay start; **F2** exits at the current position. Losing window focus also exits. Testplay does not change your objects or undo history. Change movement and dash bindings in **Library > Settings**.

Bindings accept letters, digits, punctuation (including `;`, `'`, `[` and `]`), arrow and navigation keys, Backspace, Enter, Space, Shift, Ctrl, Alt, lock keys, numpad keys, and F3–F24. Esc cancels capture; Tab, F1 and F2 remain reserved for testplay controls. Windows/Command, media and other system keys are not offered. Left and right modifier keys share a binding, as do main and numpad Enter. Numpad input follows Num Lock; punctuation labels use US keyboard names. OS shortcuts and Ctrl+P retain their normal behavior.

### Difficulties and project files

Click a difficulty tab or use Ctrl+Tab to switch. The **+** button adds a blank difficulty or imports an `.osu`. Blank difficulties inherit the active difficulty's audio, timing and settings. Tabs show live No Mod star ratings and an unsaved-change dot.

The workspace contains a `project.catchdiff` manifest and separate `.catchdiff` difficulty files. Save the whole project folder and keep its referenced resources available. External folders remain in place; OSZ resources are extracted into the workspace's Resources folder. Moving just a difficulty file may break resource links.

### Save and export

| Action | Result |
| --- | --- |
| Ctrl+S: Save | Saves the project's editable difficulty data. Workspace-only projects offer an optional Songs export after saving; choosing to keep the project in the workspace completes the save. For projects already in Songs, an imported difficulty’s first save opens export choices, and subsequent saves update its linked `.osu`. |
| Ctrl+E: Export | Opens choices for the active difficulty: a standalone `.osu`, overwriting an associated difficulty, or creating a new difficulty in Songs. |

Exporting a new difficulty to Songs saves your edits in a new workspace difficulty and activates it. The original difficulty keeps its last saved content. Exported `.osu` files do not preserve all editor-specific controls, so keep the workspace project for further editing.

Use **Library** or Esc to return to the library. Unsaved work prompts for Save, Discard or Cancel. Missing-resource messages indicate that a referenced file needs to be restored or relinked.

Version 0.8 does not provide timing-point creation, bookmarks, video or storyboard playback. Imported timing and slider velocity are supported. Testplay is for checking patterns; star ratings and exported behavior may differ between osu! versions.

## 04 / Keyboard reference

Shortcuts below apply while editing, outside text fields and dialogs. On macOS, Command also works for Ctrl shortcuts; Backspace also deletes. Some Mac keyboards require Fn for function keys.

### Files, selection and editing

| Keys | Action |
| --- | --- |
| Ctrl+O | Open a beatmap, OSZ or older project. |
| Ctrl+S | Save project. |
| Ctrl+E | Open export choices. |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous difficulty. |
| 1 / F / B / N | Select / Fruit / FSlider / Banana tools. |
| 1 / 2 / 3 / 4 | The same four tools. |
| Ctrl+A | Select all objects. |
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / copy / paste selected objects. |
| Ctrl+D | Clone selection one measure after its last start. |
| Delete | Delete selected objects or edited points. |
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Undo / redo / redo. |
| Ctrl+H | Flip selected objects horizontally. |
| Ctrl+Left / Ctrl+Right | Move selected objects one horizontal unit. |
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
| Ctrl+Up / Ctrl+Down | Next faster / slower playback speed (10%–150%). |
| Wheel / middle-drag | Wheel up moves the playhead and canvas earlier; down moves both later by the same relative amount. Middle-drag pans the canvas. |
| Ctrl+wheel | Zoom the canvas, or the object timeline under the pointer. |
| Click the current timestamp | Open Jump to time; accepts timestamps or milliseconds. |

## 05 / Slider, snap and testplay keys

### Sliders

| Keys | Action |
| --- | --- |
| Enter / Esc | Finish / cancel a slider draft. |
| Ctrl+I | Insert a point on the curve under the pointer. |
| Ctrl+L | Toggle the selected point between straight and curved. |
| Ctrl+= / Ctrl+- | Add / remove a reverse. |
| Ctrl+G | Reverse the selected FSlider's path direction. |
| Ctrl+J | Extend the selected FSlider to the pointer. |
| Ctrl+Shift+F | Convert sliders to a fruit stream, or change stream snap. |

### Snapping, combos and hitsounds

| Keys | Action |
| --- | --- |
| Shift+1 through Shift+9 | Choose beat subdivision 1/1 through 1/9. |
| Ctrl+M | Cycle all beat subdivisions, including 1/12 and 1/16. |
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
| F1 / Esc | Return to the editor at the testplay start. |
| F2 | Return to the editor at the current position. |

For detailed editing behavior, see `docs/EDITOR_UI.md` in the repository. Project and resource management are described in `docs/WORKSPACE.md`.

### Audio volume

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
