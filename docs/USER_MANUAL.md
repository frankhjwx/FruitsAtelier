# FruitsAtelier

User Manual | Version 1.3.0

## Using this manual

This manual describes FruitsAtelier v1.3.0. Browse by feature, search for a tool or shortcut, or follow the tutorials for a guided workflow. Menu names and shortcuts use the English interface.

### Choose an editing canvas

Use Catch to edit horizontal movement over time, osu! to edit geometric slider paths, and experimental lazer editing for a gameplay-oriented Catch view. See Editing canvases for controls and object types.

### Sample beatmaps

The tutorials use these beatmaps. Follow a link to download the set, then open the indicated difficulty in FruitsAtelier.

| Beatmap | Difficulty | Mapper |
| --- | --- | --- |
| [Gardens — Twin Bloom](https://osu.ppy.sh/beatmapsets/2629726#fruits/5912778) | Double Radiance | Yumeno Himiko |
| [Okura Asuka — Prime number ~Kimi to Deaeru Hi~ (TV Size)](https://osu.ppy.sh/beatmapsets/2632944#fruits/5924748) | Rain | Yumeno Himiko |
| [kz (livetune) feat. Hatsune Miku — Yellow](https://osu.ppy.sh/beatmapsets/2633458#fruits/5926879) | TAG2 | Yumeno Himiko |
| [Mami Kawada — Wings of Courage -Sora o Koete-](https://osu.ppy.sh/beatmapsets/245160#fruits/890755) | Platter | examination |

Prime number introduces editing and timing. Twin Bloom demonstrates Pattern Gallery. Wings of Courage accompanies the video and storyboard tutorial.

### Find a workflow

Start with installation and setup, then interface and navigation. See Save, export and recover for workspace files and osu! synchronization, or Portable projects to share editable work.

## Installation and setup

Fruits Atelier edits osu!catch beatmaps. Keep the application, your editable workspace and your osu! installation in separate folders.

### Install on Windows

1. Download the Windows ZIP from the Download page.
2. Extract the complete ZIP. Keep `FruitsAtelier.exe`, `Update.exe` and `current/` together.
3. Run **FruitsAtelier.exe**. The package includes its .NET runtime.

Windows 10/11 x64 and DirectX 11 are required. Windows N installations need the Media Feature Pack for MP3 playback. macOS source-build instructions are linked from the Download page.

### Complete first-launch setup

The setup guide covers folders, language, skin, sound levels, droplet defaults and Testplay. Back and Next retain your draft choices. **Finish** saves them; exiting an unfinished guide discards that draft.

Choose a **Workspace folder** outside the application folder. Optionally choose your **osu!stable folder**, the installation folder containing Songs and Skins. The workspace must be separate from Songs. These paths remain available in Settings.

### Open or create a map

- Right-click the Library and choose **Import beatmap / OSZ…** or **Import folder…** to open an existing Catch set.
- To start with music, drop one MP3, OGG or WAV into the Library or editor. Fill in title, artist, mapper and difficulty name, then choose **Create**.
- When **Also create in osu! Songs** is selected, the new difficulty is linked to a Songs copy. Clear it to keep the project local to FA.

Open a saved project from **My projects**. For a guided introduction, start with Meet the Editor.

### Keep the editable project

FA workspace files retain editing controls that an exported `.osu` cannot. Keep the complete project and its resources when backing up your work. See Save, export and recover.

## Library and projects

The Library is the entry point for maps and saved FA projects. **All songs** lists indexed Catch sets; **My projects** lists your editable workspace projects.

### Find and open a map

Search by title, artist, mapper, difficulty or tags. **Ctrl+F** focuses the search field; **F5** rescans the Library. Double-click a set to open it, or select it and press Enter with no text field focused. A single click selects a card without opening it.

Use **← Library** to return from the editor. Unsaved changes offer Save, Discard or Cancel. Search text and list positions are remembered separately for each Library category.

### Import files

| Input | How to open it | Where resources live |
| --- | --- | --- |
| `.osu` or a beatmap folder | Right-click → Import beatmap / OSZ… or Import folder… | Original source folder |
| `.osz` | Import beatmap / OSZ… or drop onto the Library | Extracted into workspace Resources |
| MP3, OGG or WAV | Drop one audio file to create a project | Copied into workspace Resources |
| `.osk` | Drop onto the Library or import through Appearance | Workspace Skins |

Only Catch difficulties are indexed. Importing a set does not export it to Songs. Existing linked sources resume their workspace project when reopened.

### Work with difficulties

Click a difficulty tab to switch. **Ctrl+Tab** moves to the next tab; **Ctrl+Shift+Tab** moves to the previous one. The **+** button adds a blank difficulty or imports an `.osu`. A blank difficulty inherits the active difficulty's audio, timing and settings.

Right-click a tab to locate its saved file, workspace folder or associated osu! folder. Tabs show the difficulty's No Mod star rating and an unsaved-change indicator.

### Project and resource locations

A saved project folder contains `project.catchdiff`, separate `.catchdiff` difficulty files and synchronization data when associated with osu!. Keep the folder together. Referenced audio, backgrounds and samples may live outside it.

Changing the workspace path in Settings opens another workspace; it does not move your existing files. See saving and backups.

### Collections, My Maps and templates

Use Difficulty templates and Library organization for FA collections, read-only osu! collections, mapper filtering, difficulty copies and saved templates. Portable projects documents FAZ/FAD import choices; they are distinct from OSZ/OSU import.

## Interface and navigation

### Panels and tools

Use Layout to arrange dock panels, including Preview, Song Setup, Inspector, Notes and diagnostics. Mapping and Timing keep separate layouts. Tool keys 1–4 select Select, Fruit, Slider and Banana Shower. Their representation follows the canvas.

Press Tab in Compose to switch between Catch time–X editing and osu! geometric editing. Settings → Experimental offers the alternate lazer Catch presentation. Editing canvases explains their different coordinates and editing rules.

### Seek and zoom

Wheel navigates time; Shift+wheel moves farther. Alt+wheel changes canvas zoom; the upper timeline has independent zoom. Middle-drag pans. Catch supports independent X/time scales or a uniform lock. UI scale changes interface size separately. Read Zoom and canvas navigation.

Click the transport timestamp to enter an exact time or paste an osu! edit link. Ctrl+wheel changes Beat Snap within enabled families. Text inputs, dialogs, Testplay and embedded webpages have their own focus handling.

### Playback and bookmarks

Space or C plays/pauses in the editor. Speed buttons cover 10%, 25%, 50%, 75%, 100% and 150%; volume controls separate master, music and effects. Holding the song overview pauses audio while seeking. Ctrl+B adds a bookmark and Ctrl+Shift+B removes one nearby. Ctrl+Left/Right seeks bookmarks when no movable Compose selection takes precedence.

### Breaks and screenshots

Insert Break Time operates in a sufficiently long empty gap. Drag break edges on the timeline, cancel with Escape or undo the committed change. See Bookmarks and breaks.

F12 copies an editor/paused-Testplay screenshot to the clipboard. Screenshot capture does not edit the map. See Layout and Notes for panel workflows.

## Settings and skins

Open **Settings** from the Library or editor. Switching categories retains pending choices. **Apply** saves draft settings; closing discards unapplied changes. Skin selection and audio sliders apply immediately.

### Folders

In **Workspace**, choose a workspace folder for projects and resources. Optionally select the osu!stable installation folder containing Songs and Skins. Keep the workspace outside Songs and outside the application directory.

Changing the path opens a different workspace without moving existing projects. **Open workspace folder** locates the current files. Storage controls refresh usage, clean eligible recovery history or clear generated caches.

### Appearance and skins

Choose a skin in **Appearance → Skin** or the editor's top-bar Skin menu. **Import .osk…** copies a skin into the workspace and selects it. Dropping an `.osk` onto the Library also imports it.

**Default skin .osk file (optional)** provides fallback images when the selected skin lacks them. Set its path and Apply. Check the result in Catch Preview or Testplay.

In **Mapping Canvas**, **Movement indicator colours** changes Stand, Walk, Dash and Hyperdash colours in FA's mapping aids. Choose a swatch or enter HEX, click Done, then Apply. Reset colours restores the default draft.

### Audio

**All**, **Song** and **Hitsound** adjust master, music and effect levels. They save immediately. **Use skin's sound samples** chooses skin samples or built-in defaults; beatmap-specific samples still take priority.

For playback controls, click **Volume** at the bottom right. **Alt+Left / Right** selects a channel and **Alt+Up / Down** changes its level.

On Windows, try General's Display mode if sound and picture seem delayed. Audio delay diagnostics can record a report: enable recording, Apply, reproduce the problem, mark it with **Ctrl+Shift+F8**, then export the ZIP. Applying changed diagnostic options saves the project and restarts FA.

### Testplay preferences

In **Testplay**, change movement and dash bindings, lead-in (0–5 seconds), starting speed, combo count and background dim. Click a binding and press a key; Escape cancels capture. Reusing a bound key swaps the assignments. Apply saves the draft.

**Dim Background** makes preview and Testplay backgrounds completely black. Turning it off restores the chosen dim percentage.

### General and updates

General includes language, romanised title/artist display, fullscreen, mapper aliases and import defaults. **Alt+Enter** toggles fullscreen directly. Changing display language or romanised metadata display does not rewrite beatmap content.

Where supported, **Application updates** checks releases and downloads updates. **Save and restart to update** saves pending work before restarting. Keep projects and custom skins outside the application's `current/` directory.

### Settings categories

UI scale and selection highlighting live in Appearance. Mapping Canvas contains its own background dim, reverse scrolling, movement colours and grid appearance. General includes mapper names, standard-map scanning and import preferences. Experimental contains lazer editing, fine snap choices and same-time placement behaviour. osu! Links & Web combines protocol handling and embedded browser controls.

Your osu! Web sign-in data, cookies and browser storage are kept only on your computer. You can clear them in **Settings → osu! Links & Web → Clear local user data and cookies**.

Set UI scale from 50% to 200% in 25% steps, or use Ctrl+plus/minus variants listed in the keyboard reference. Skin/language/volume and some preferences save immediately; draft Settings values use Apply. Changing view or preferences does not modify beatmap objects.

Enable Video/Storyboard in Testplay shares its preference with View and the canvas control; Apply commits the Settings draft. See Video and storyboard.

## Layout, panels and Notes

### Arrange the workspace

Use **Layout** to show a panel. Drag dock tabs to move, group or reorder them. Resize side docks at their dividers and collapse groups to give the canvas more room. A dock tab context menu also lists available panels. Mapping and Timing retain independent layouts across launches.

Scroll over an overflowing tab bar to reveal other tabs without changing the selected panel. Layout brings the requested panel's tab into view. Canvas and timeline scale are separate from overall UI scale.

### Editing panels

Song Setup, Slider Inspector, Stream/Stack, Droplet Settings, AiMod, Collab Mapping, Pattern Gallery, Discussion and Mapset Verifier can occupy docks. Valid content fields generally apply immediately with undo; a drag commits on release. A Settings dialog still uses Apply where indicated.

Use **Shift+F** or long-press an object to open selection-aware actions. In experimental lazer editing, right-clicking an object opens the same action list. **Ctrl+Shift+F** opens Stream/Stack in Catch; in osu! it converts the selection to individual notes along geometric paths. **Ctrl+Shift+M** opens merge controls in Inspector.

### Project Notes

Notes belongs to the entire project, across its difficulties, and is saved with the project. Paste copied object references to retain timestamp/object links. Click a link to seek and, when identifiable, select its objects. Navigating does not alter beatmap content.

Notes has its own text undo/redo. When a text field is focused, clipboard and text shortcuts operate on that field rather than the canvas. Keep task reminders in Notes; export a FA project if the notes must travel with it.

## Difficulty templates and Library organization

### Find your maps

Library supports search, grouping, sorting, favourites and FA collections. Configure mapper usernames in General to populate **My Maps**; aliases can include Yumeno Himiko and examination. Optional standard-mode scanning controls whether Mode=0 maps are indexed. osu! collections are read-only and show only their matching difficulties.

Open an individual difficulty from the expanded set. Tabs start in No Mod star order; drag to reorder or pin them for the current open-project session. Ctrl+Tab follows the displayed tab order. Visit webpage opens the official map page in the system browser.

### Create a difficulty

Choose **+ → Add new difficulty**, enter its name and choose whether it is Fruits Atelier Only. With Create from a difficulty or template disabled, blank creation retains current settings/timing and clears objects. The source-enable preference is remembered.

Enable the source option and choose **From difficulty** for an independent copy of an existing difficulty. It copies objects, timing, sounds and settings but resets the online BeatmapID. **Map templates** instead applies HP, CS, AR, OD, base SV and SliderTickRate to a blank difficulty.

### Manage templates

Open File → Map templates, the Library header action or the creation dialog's template manager. Create a template or capture the current difficulty's six settings. Rename, edit values, choose a Catch icon and drag to reorder entries. Save commits the manager; Cancel discards its draft. Templates are global preferences, not beatmap content.

### Copy between difficulties

Copy a selection, switch tabs and paste into the destination. The destination retains its settings and timing. Legacy geometry and duration are retained where representable. Check the result with the destination's grid, preview and sounds, then undo if it does not fit.

## Zoom and canvas navigation

Use the inline canvas zoom controls to change how much of the map you can see. The canvas places X horizontally and time vertically, with later objects higher up.

### Canvas Zoom

Use the X and Y/time controls independently, or lock their scales together. **Alt+wheel** scales both axes together. Zooming out reveals more time; objects and hit regions are capped at their 100% size. View scaling does not change positions, AR or CS.

The percentage is relative to the available canvas width. Opening a sidebar or resizing the window changes the available space, so the percentage is not a fixed pixel size.

Alt+wheel zooms around the time beneath the pointer. The toolbar slider keeps the viewport centre stable while paused and the play line stable during playback.

### Independent view scales

| View | How to zoom |
| --- | --- |
| Editing canvas | Zoom slider or Alt+wheel over the canvas |
| Upper object timeline | Its + / − controls or Alt+wheel over that row |
| Timing waveform | Alt+wheel over the waveform in Timing Panel |

These scales are remembered independently across difficulty changes and restarts. To enlarge only the timeline's objects, zoom the timeline rather than the canvas.

### Seek and pan

Scroll up to seek earlier and down to seek later. Each wheel step moves one beat during playback or one current Snap subdivision while paused; **Shift+wheel** moves four times as far. **Settings → Mapping Canvas → Reverse canvas scrolling** reverses the canvas wheel direction.

Middle-drag pans the canvas. **Left / Right** steps through time and **Shift+Left / Right** moves four steps. For an exact position, click the bottom timestamp and use Jump to time.

### Play-line position and Reset view

While paused, drag the yellow handle at the play line's left edge to set its height. The current time stays the same while the surrounding content moves. The position is remembered as a view preference; Escape cancels an unfinished drag.

Choose **View → Reset view** to return to the playhead using the remembered canvas zoom. This does not reset your map or change its difficulty settings.

## Canvas display controls

The buttons above the canvas control editing aids. Their highlighted state indicates that the corresponding aid is enabled.

### Slider Path

Click **Slider Path**, or its checked entry in **View**, to show or hide slider curves. Enable it when editing FSlider controls. If a control overlaps a generated fruit, hiding the path lets you target the fruit instead.

This only hides the path display; the slider remains in the map, preview and export. See FSlider controls.

### Selection Rect

**Selection Rect** shows the selected objects' bounding box and its move/scale interaction regions. It starts enabled. Turn it off to hide these regions without clearing the selection.

When enabled, drag empty space inside the selection box to move the group, or use its resize controls to scale it. Hiding the box does not lock notes: use **L / Lock Notes** to prevent moving, reshaping and deleting existing objects.

### Analysis overlays

**Movement Analysis** adds coloured connections and spacing labels to the canvas. **View → Include tiny droplets** controls whether the analysis also includes TinyDroplets. See Movement Analysis for the categories and readouts.

Open Movement strain and Catch Preview through Layout. They can remain open together, and their dock tabs can be moved and grouped.

These display choices do not edit the beatmap or create undo steps.

### AR, CS and DPB readouts

The Details header shows the current **AR**, **CS** and **DPB** as readouts. Change AR and CS in Song Setup; configure the spacing reference through Distance Snap. Canvas Zoom changes the view independently of these values.

### Background and media

Mapping Canvas settings control its dim level and X-axis grid colours/opacity independently of Catch Preview/Testplay. Drop a background image to replace its Events reference after confirmation; the old file remains on disk and the content change supports undo.

the shared Enable Video/Storyboard preference controls visuals across mapping, preview and Testplay. Static backgrounds and storyboard samples are separate. See Video and storyboard.

## Object timeline and song overview

FA has two horizontal timelines. The upper object timeline lets you edit a local section in time. The bottom overview shows the whole song and provides navigation and markers.

### Upper object timeline

Circles represent source objects; capsules span the duration of sliders and banana showers. A slider's head shows its combo number, repeat boundaries show reverse arrows, and its final tail is unnumbered. Beat-grid colours follow the current Snap subdivision.

| Action | Result |
| --- | --- |
| Click an object | Select it without seeking |
| Double-click an object | Select it and seek to its start |
| Ctrl-click | Toggle an object in the selection |
| Drag empty space in the note row | Box-select; hold Ctrl or Shift to add to the selection |
| Drag an object head or body | Move selected parents in time while preserving X and relative timing |
| Shift-drag an object | Move without beat snapping |
| Right-click an object | Ordinary mapping uses its deletion action; experimental lazer opens the selection-aware object menu |
| Click empty space | Clear selection without seeking |

Beat snapping uses the earliest selected start. Release commits the drag; Escape cancels it. Finish an active drawing draft before editing this row.

### Slider and banana edges

Drag a slider's final tail to change its traversals. Drag a banana shower's start or end independently to change that boundary. For slider hitsounds, click the head, reverse marker or tail to target that edge; clicking the body targets the whole slider. See Combos and hitsounds.

The timeline's **+ / −** controls and **Alt+wheel** change its scale independently of canvas Zoom.

### Bottom song overview

Click or drag through the overview to seek. Its markers help identify the structure of the map:

| Marker | Meaning |
| --- | --- |
| Red / green ticks | Timing and inherited control points |
| Yellow intervals | Kiai sections |
| White intervals | Breaks |
| Blue ticks | Bookmarks |
| Full-height yellow line | Song preview point |

Hover the overview to reveal its bookmark toolbar. **Ctrl-click** toggles a bookmark at the clicked position; **Shift-drag** adds a break. See Bookmarks, breaks and preview point.

### Jump to time

Click the current timestamp beside the playback controls. Enter milliseconds, a timestamp such as **03:03:311**, or an osu! timestamp reference such as **03:03:311 (2,3) -**.

Press **Enter** or **Jump** to seek without beat snapping. Escape cancels. Invalid input leaves the dialog open; a time beyond the track is limited to its end. Copy and Paste use the system clipboard.

## Playback speed and volume

Use the transport controls beside the bottom song overview to listen to a section, slow down its rhythm or start Testplay. Playback adjustments do not change the map's BPM, object times or exported timing.

### Play, pause and stop

| Control | Action |
| --- | --- |
| Play / Pause, Space or C | Start or pause playback |
| Stop | Pause and seek to the song's beginning |
| X | Seek to the beginning and play |
| Home | Seek to the beginning without forcing playback |
| Z | Seek to the first object; if already at or before it, seek to zero |
| V / End | Seek to the end of the last object by start order; if already there or later, seek to the song's end |
| Testplay / F5 | Start a playable section from the playhead |

In Compose with Beat Snap enabled, pausing aligns to the nearest current Snap grid line. Timing's metronome mode pauses at the actual audio position.

### Playback speed

Choose **10%, 25%, 50%, 75%, 100% or 150%** in the transport. For finer adjustments, use **Ctrl+Up / Down** to change speed by 25 percentage points, or **Ctrl+Shift+Up / Down** for 5 points. The range is 10%–150%.

Changing speed preserves the current map position and play/pause state. The song changes tempo while retaining its pitch. Hitsounds retain their original pitch and real-time duration, with their triggers following the music clock.

Choose **100%** to return to normal speed. Slowing playback does not alter the categories shown by Movement Analysis.

### Testplay starting speed

**Settings → Testplay → Testplay speed** sets an independent starting speed from 10% to 150%, in 5-point steps. Apply the setting, seek to the desired section and press **F5**. Returning to the editor restores its previous playback speed.

Speed changes made inside Testplay apply to that session and retries. They do not overwrite the saved starting-speed preference. See Testplay.

### Volume

Click **Volume** in the status bar, or choose **View → Volume**. The controls are **Master**, **Music** and **Effect**, each from 0% to 100%. Master affects both channels; Music changes the song and Effect changes preview/Testplay samples.

**Alt+Left / Right** selects a channel. **Alt+Up / Down** adjusts it by five percentage points. The shortcuts also work during Testplay. Changes apply immediately and persist; muting does not pause the song. Escape dismisses the controls in the editor.

## Bookmarks, breaks and preview point

Bookmarks mark places to revisit, breaks define rest intervals, and the preview point sets where the song preview begins. These are map metadata: changes support Undo and are retained when saving/exporting.

### Add and remove bookmarks

Seek to a section and press **Ctrl+B**. **Ctrl+Shift+B** removes the nearest bookmark within two seconds. On the bottom overview, **Ctrl-click** adds a bookmark at the clicked time; clicking close to an existing one removes it.

Hover the overview to reveal **Add**, **Remove**, **Previous**, **Next** and **Reset**. Reset clears the bookmarks; Undo restores them.

With no movable Compose selection, **Ctrl+Left / Right** jumps between bookmarks. With a selection, these keys nudge objects instead. Timing uses them for bookmark navigation. In Testplay, Ctrl+B marks the live play position so you can return to it after testing.

### Insert a break

Place the playhead in an empty gap and click **Insert Break Time** above the canvas. FA places the start 200 ms after the previous object's end and the end before the next object's AR approach window. The available interval must be at least 400 ms and must not overlap an existing break.

Alternatively, **Shift-drag** across the bottom overview to create a break interval. Escape cancels an unfinished gesture.

### Adjust or remove a break

Drag the edges of the darker break region in the upper timeline to change its boundaries. Reducing its duration below 400 ms removes it. You can also right-click inside a break on the bottom overview to remove it. Undo restores the previous interval.

Breaks appear as white intervals on the bottom overview. Movement Analysis omits connections that cross them.

### Set the song preview point

Seek to the desired start and choose **Timing → Set Current Position as Preview Point**. It records the playhead rounded to milliseconds and appears as a full-height yellow line on the overview.

This is the song's preview start metadata. For the live gameplay panel used while editing, see Catch Preview.

### Breaks around objects

Overlapping breaks are merged. Editing objects can split a break around notes; removing those notes restores the eligible edited fragments.  Synchronization review compares individual break differences in aligned local/external columns.

## Catch Preview

Catch Preview displays the map with a catcher beside the editing canvas. Use it to inspect falling objects, spacing and the selected skin while editing or listening. To control the catcher yourself, start Testplay with F5.

### Open and resize

Open **Layout → Preview**. Move/group its dock tab, drag the dock divider to resize, or collapse the group when you need more canvas space.

The preview follows the editor's current time when playing or seeking, with an automatic catcher. **Space / C** plays or pauses. It can remain open alongside the left Movement strain sidebar.

### Aspect ratio

Choose **4:3**, **16:9** or **Fit** in the preview controls. Fit uses the available sidebar height, allowing more upcoming objects to be visible. Resizing the panel retains object proportions.

These choices change the preview layout, not the map's coordinates. To change the authoring view, use canvas Zoom.

### NM, Easy and Hard Rock

Select **NM**, **Easy** or **Hard Rock** to inspect the corresponding difficulty and position rules. The effective **AR** controls falling speed; slider SV does not set the preview's falling speed.

Preview mods do not rewrite the saved AR, CS or authored objects. Testplay starts with the selected preview mod. The Movement strain graph continues to show the normal-mode calculation.

### Skin, background and audio

The preview uses the selected skin and beatmap samples. Choose a skin from the editor's top bar or **Settings → Appearance**. The background dim settings also apply here; **Dim Background** makes the background fully black.

Use the transport's playback speed and volume controls to slow a section or adjust music and hitsounds. Video and storyboard playback follows the song transport; see the media section below.

### Canvas and media context

Preview shows playable Catch output while editing either Catch or osu! geometry. Changing canvas does not convert the difficulty. enabled video/storyboard visuals follow the same transport; preview/testplay dim is separate from Mapping Canvas dim. See Video and storyboard.

## Movement strain

The **Movement strain** sidebar plots movement strain across the active difficulty. Use its peaks to find sections to inspect, then jump directly to those sections on the canvas.

### Open the left sidebar

Open **Layout → Movement strain**. Its dock can be collapsed, resized, moved or grouped with other panels. It can stay open together with Catch Preview; dock widths share the available canvas space.

Opening or closing the graph changes only the view, not the beatmap.

### Read the curve

Time runs **upward**, matching the canvas: the bottom is the beginning and the top is the end. The curve extends farther right where calculated strain is higher. Its width is scaled to the current difficulty's maximum, so equal-looking peaks in different maps need not have equal values.

Highlighted areas indicate high strain relative to the map's peak. Hover over the graph to read the time and strain value. The amber horizontal marker shows the current playback position.

The graph shows **normal-mode** movement strain. It stays in NM when you select Easy or Hard Rock in Preview. Use Movement Analysis for per-object spacing.

### Seek and inspect a section

Click a point on the graph to seek to that time, or drag vertically to seek continuously. Use canvas Zoom to inspect the nearby pattern, enable Movement Analysis to see individual connections, or press **F5** to test the section.

Add a bookmark with **Ctrl+B** to revisit it. After content edits, the curve refreshes with the difficulty calculation; an unfinished drawing gesture can retain the previous complete result until the edit finishes.

## Editing canvases

### Catch canvas

Catch shows horizontal Catch position against vertical time. Edit FSlider curves, stream and stack parents, fruits and banana showers here. The playfield width is X=0–512. Time and geometric slider Y are different quantities.

### osu! canvas

Press **Tab** in Compose to switch between Catch and the 512×384 osu! geometric canvas. Catch Preview remains available. The switch does not change content, undo history or object representation.

Use the Fruit tool for circles, Slider for native Legacy Sliders and Banana for spinners. Placement time follows the snapped playhead; pointer Y is a geometric coordinate. Edit Legacy control points here; edit FSlider shapes in Catch. Whole sliders may be deleted in either canvas. Conversion is explicit and undoable.

Click slider controls, double-click for a red segment boundary, and right-click to finish. Three controls without a red boundary form a circular arc; collinear controls form a line. Ctrl+click an interior control to change its segment boundary; right-click a control to remove it. Grid Snap uses the geometric lattice. Alt+wheel zooms and middle-drag pans.

The osu! Distance Snapping multiplier is independent of Catch DS. The SV control sets the default for new Legacy Sliders; it does not rewrite existing sliders. Click the number for direct input.

### Experimental lazer editing

Enable **Settings → Experimental → lazer editing** for the alternate Catch presentation. It uses a fixed judgement line, an automatic catcher, gameplay visibility brackets and movement guides. It still edits Catch objects; it is not Testplay.

Alt+wheel zooms around the catch line; middle-drag pans horizontally. Its X/Y zoom and distance/speed preset choice are retained separately from ordinary mapping. Switch back to compare the same passage without changing the map.

### Rotation and selection

**Ctrl+Shift+R** opens Rotate by with a live preview, signed angle and origin/direction choices. OK commits one undo step; Escape restores the original selection. Catch rotation preserves the selection's time interval and rejects curves that would reverse time or produce invalid endpoints. Catch curves with repeats, stacks or manual droplet adjustments have additional restrictions. In osu! view rotation retains slider duration and length.

See Objects and selection and Snapping and spacing.

## Objects and selection

Use the left palette or **1 / 2 / 3 / 4** for Select, Fruit, FSlider and Banana Shower. Beat Snap controls time; Grid Snap controls horizontal placement.

### Place fruits and bananas

**Fruit (2 or F):** left-click to place a fruit at the snapped position. The placement preview shows nearby hyperdash markers. With experimental same-time preservation disabled, placement follows osu!-style replacement at an existing parent start within ±2 ms. Enabling preservation changes this behaviour; moving existing objects and cancelling drafts do not perform a new placement replacement.

**Banana Shower (4 or N):** left-click the start, then right-click at a later time to finish. Drag its body to move the interval or its end controls to change duration. Escape cancels an unfinished shower. Individual banana positions are not authored separately.

For sliders, see FSliders.

### Select and move

Press **1**. Click an object, drag empty space to box-select, or Ctrl-click to toggle an object in the selection. **Ctrl+A** selects all objects. Clicking a slider fruit or droplet first selects its parent.

Drag selected objects to move the group. **J / K** moves it earlier or later by one Snap subdivision. **Ctrl+Left / Right** nudges horizontally by one X unit; add Shift to use the grid step.

Enable **Selection Rect** to show the group's transform box. Drag its empty interior to move the group, or use resize controls to scale it. Escape cancels a drag.

### Copy and transform

| Command | Result |
| --- | --- |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy, cut or paste within the current mapset, including between difficulties; pasted starts align to the playhead |
| Ctrl+D | Clone after the selection, starting one measure after its last object start |
| Ctrl+H | Mirror horizontally |
| Ctrl+G | Reverse selection timing and each selected slider's path |
| Delete | Delete selected objects or controls |
| Ctrl+Z / Ctrl+Y | Undo / redo |

### Lock Notes and droplet selection

**L** toggles Lock Notes. It blocks moving, reshaping and deleting existing objects while allowing selection, placement, playback, flags and undo.

Hover Lock Notes and disable **Lock Droplet Selection** to select droplets. Click a droplet once to select the parent, then again to select that event. Horizontal dragging changes X while keeping its time. Ordinary droplets reshape the nearby path; randomized TinyDroplets can store a manual correction. Repeated path samples may move together.

### Object actions

Shift+F or long-press opens the selection's available actions; experimental lazer also supports object right-click. Use Slider Inspector for conversion and properties. Ctrl+Shift+R opens rotation with a live preview and undo. Tab changes canvas, while Lock Sliders to Canvas and independent endpoint snapping affect editing constraints. See Editing canvases and Slider Inspector.

## FSliders

An **FSlider** is FA's editable slider. Its curve describes horizontal position over time on the catch canvas. A **Legacy Slider** retains an imported osu! slider's geometric path, length and repeats. In osu!, geometric Y belongs to the path; in FA's authoring canvas, vertical position represents time.

**FSliders export as ordinary osu! sliders.** Keep the FA workspace project to retain their editable controls.

### Convert Legacy Sliders

Select an imported slider, press **Shift+F** and choose **Convert to FSlider**. For the active difficulty, use **Edit → Convert all sliders to FSliders**. The batch dialog offers a droplet policy; see Droplet randomization.

Conversion constructs an editable catch path; it does not recover the original author's control points. Inspect the converted pattern and use Undo if needed. Selecting an imported slider without editing it preserves its Legacy representation.

### Draw a curve

Hover the FSlider tool and select a mode. Both modes create FSliders; **osu legacy mode** names an editing style, not a different output object.

| Mode | Placement |
| --- | --- |
| osu legacy mode | Press 3, click the start, then add control points at later times |
| pen tool mode | Click anchors; hold and drag while placing one to pull out direction handles |

Press **Enter** or right-click away from existing points to finish. **Escape** cancels the draft. Ctrl-click creates a straight segment in pen mode.

### Edit control points and handles

Press **1**, enable **Slider Path** and double-click the FSlider. Drag controls to reshape it. Pressing 3 starts another slider rather than entering Select mode.

- Ctrl-click within the slider interval to insert a control point.
- Ctrl-click an existing control to make it straight.
- Right-click a straight control to make it curved; right-click again to delete it.
- Delete removes selected controls. Right-clicking the slider body away from controls deletes the parent slider.

### Reverses and extension

Drag the final tail in the upper timeline to change traversals. **Ctrl+= / Ctrl+-** adjusts UI scale in the current editor; it does not add/remove a reverse.

To extend, select the slider, point to empty canvas after its final end and press **Ctrl+J**. FA adds a straight segment to a new anchor. Repeats share the base path, so extending it lengthens every traversal.

### Edit fruits and droplets

Unlock droplet selection when necessary, then click an event twice to select the parent and the event. Drag horizontally or edit its **X** readout. Some moves are constrained by repeated geometry or valid export positions. See Objects and selection.

### Clear internal controls

Select sliders and use the Inspector/action-list **Clear internal anchors** action. **Ctrl+Shift+A** opens AiMod. FA replaces their paths with straight endpoint-to-endpoint segments. This removes internal anchors and handles while retaining endpoint positions and times, repeats and object metadata. Selected Legacy Sliders convert to FSliders as part of the operation.

For all FSliders in the active difficulty, use **Edit → Clear all internal slider nodes**. Convert Legacy Sliders first if they should be included in this batch. Either operation supports Undo; Lock Notes blocks it.

### Merge into slider

Select at least two consecutive fruits or sliders, then press **Ctrl+Shift+M**, or long-press the selection and choose **Merge into slider**. No intervening fruit or slider may be omitted, and banana showers cannot be included.

Choose straight segments or a curved path. A selection containing a curved slider offers only the curved option. Existing slider paths are retained with repeats unfolded; fruits become anchors. Overlapping objects or incompatible endpoints cannot be merged, and FA retains the originals if conversion fails.

Confirming replaces the selection with one FSlider. Its generated catch events can differ from the originals, so inspect the result in Catch Preview. Undo restores the original objects during the session; saving and reopening does not preserve that undo history.

### Reverse path direction

**Edit → Reverse path direction** reverses the active FSlider's path while retaining its time range. **Ctrl+G / Edit → Reverse selection** also reflects selected objects within the complete selection time range. These commands differ from adding a reverse, which changes the number of traversals. Repeated spans share the same base path.

### Legacy Sliders and Inspector

FSlider shapes belong to Catch canvas; Legacy original geometry belongs to osu! canvas. Tab switches views without converting. Use an explicit conversion action when changing representation. Inspector distinguishes the original Drawn path from processed output and exposes independent Legacy SV. Follow map/Derandomize/Randomize overrides and manual droplet resets are described in Slider Inspector.

## Streams and stacks

Streams and stacks use a slider curve to generate fruits. They remain editable as one parent in FA and export as individual hit circles in `.osu`.

### Open the Stream/Stack panel

Select an FSlider or Legacy Slider and press **Ctrl+Shift+F**, or use **Edit → Convert to Stream/Stack**. Choose Stream or Stack and inspect its live canvas preview. Valid parameter edits apply with undo; graph/snap drags commit on release.

### Stream

Choose **Snap** for the generated fruits' beat subdivision. For example, 1/4 places fruits at quarter-beat intervals along the curve. Slider Tick Rate does not control stream spacing. The tail is included only when it falls on the subdivision.

Leave **Break into Fruits** off to retain the editable parent. Afterwards, select the stream to drag anchors or handles, adjust individual fruits horizontally, or move the whole pattern by dragging empty space inside its selection box. Middle-fruit edits preserve their times and neighbouring samples.

### Stack

On the **Stack** tab, choose Snap and the first fruit's left/right side. New stacks default to 1/16. The source slider becomes the centre curve; generated fruits alternate around it.

The width graph runs from **0% to 100%** of the pattern. Width is the distance from the centre curve, from **0 to 32 px**, before playfield-edge limits.

- Drag a point vertically to change width or an interior point horizontally to move its time.
- Click empty graph space to add a point; right-click an interior point to remove it.
- Select a point to enter **Time (%)** and **Width (px)** numerically. Endpoint times stay fixed.
- Drag a fruit horizontally in the preview for an individual adjustment. Right-click its manual marker in the graph to remove that adjustment.

Use project Ctrl+Z/Ctrl+Y to undo/redo committed edits. There is no dialog-wide OK/Cancel step in the docked workflow.

### Revise or separate the pattern

Select the pattern and press **Shift+F → Edit Stream/Stack** to reopen its controls. Hide Slider Path when overlapping handles prevent direct fruit selection.

**Break into Fruits** separates the pattern into independent objects and removes its parent controls. **Convert back to slider** returns to ordinary slider output; for stacks, this clears the width envelope and retains the centre geometry. Keep the workspace project to preserve editable pattern settings.

### Docked editing and individual fruit offsets

Ctrl+Shift+F in Catch opens the docked Stream/Stack workflow. Its draft is visible on the main canvas and valid parameter changes apply with undo. Inspect beat subdivision, spacing and width before leaving the selection. Drag an interior Stack fruit horizontally to adjust its own offset while retaining time, neighbours and the parent centre path.

Keep the parent for later curve editing, separate into independent fruits to remove the relationship, or use the offered restoration action for supported slider/banana output. In osu! canvas the same shortcut converts along the geometric slider path; switch to Catch for managed Stream/Stack editing.

## Droplet randomization

TinyDroplets normally have horizontal random offsets. FA can retain/customize them or compensate toward an authored path. Global defaults, the difficulty policy and slider overrides are separate. Managed streams/stacks export as circles and do not use ordinary slider tiny-droplet randomization.

### Derandomize a difficulty

Open **Droplet Settings** from Layout or the relevant Edit action. Choose the map default and NM/HR compensation target. The dock applies valid content changes with undo. Importing does not automatically convert every Legacy Slider; Inspector distinguishes original geometry and processed output.

### Hard Rock compensation

HR compensation targets HR's tiny-droplet sequence. It can produce shifted tiny droplets in NM. Inspect both modes and the surrounding passage. Repeated geometry and valid export limits can constrain the compensation.

### Strength and Seed

For randomized output, choose a Strength and repeatable Seed. **Strength 20** and **Seed 1337** reproduce native NM randomization without manual corrections. The sequence traverses droplets, tiny droplets and bananas, including earlier RNG-consuming objects. Adding/removing those objects can change later offsets even with the same seed.

### Individual sliders and droplets

On a supported slider, Inspector offers **Follow map**, **Derandomize** and **Randomize**. Mode changes retain manual offsets. **Reset manual droplet adjustments** clears offsets separately; corrections are effective only when the applicable randomization is active.

Unlock droplet selection from the Lock Notes flyout when required. Select the parent, then the event; horizontal dragging or X input stores a supported manual adjustment while retaining time. Repeated geometry and slider speed can constrain a move.

### Legacy conversion

Use explicit conversion when an FSlider is needed. Import questions can recover compensated paths where representable, retaining unsupported Legacy geometry. Global import defaults and preferred new-slider mode are remembered separately from existing project data. Old project output remains retained until an edit rebuilds it. Preview NM/HR after changing representation, timing or policy.

## Snapping and spacing

Beat Snap, Grid Snap and Distance Snap control different aspects of placement. Movement Analysis helps inspect what the resulting pattern asks the player to do.

### Snap controls

| Control | Purpose | Shortcut |
| --- | --- | --- |
| Beat Snap | Align time to a beat subdivision | Shift+1–9; Ctrl+wheel changes subdivision family |
| Grid Snap | Align X to a fixed horizontal grid | T toggles; G cycles grid size |
| Distance Snap | Align distance relative to the preceding catch object and time gap | Y toggles |

Beat Snap also offers 1/12 and 1/16 in its bar. Hold Alt to temporarily invert Distance Snap, or Shift to temporarily invert Grid Snap. Turn Grid Snap off when inspecting spacing without grid rounding.

### DPB and DS

**DPB (Distance Per Beat)** defines the horizontal distance of FA's 1.0x spacing reference over one beat. **DS (Distance Snap)** expresses distance relative to that reference and the time available.

At 192 DPB, 1.0 DS is 192 px over a beat or 96 px over half a beat. The shared base field uses DPB = 100 × SV. Changing its value validates base SV, DPB and rescaled DS presets together as one undoable edit, compensating slider output to retain playable positions and times. Switching the displayed SV/DPB units alone does not edit content.

**SV (Slider Velocity)** describes travel along osu!'s geometric slider path. It does not control the falling speed in Catch Preview, which depends on AR. See base SV override.

### Configure Distance Snap

Hover **Distance Snap** and open **Configure DS…** to adjust its presets and base reference. Apply keeps the draft; Cancel discards it.

Place a fruit or new slider start near the desired spacing. FA uses the preceding object's position and time; a preceding slider contributes its final tail.

### Read or edit Prev DS

Selecting an object shows **Prev** and **Next** in the floating movement panel. A whole slider uses its head for Prev and final tail for Next. Select a single fruit or slider child with a valid preceding neighbour to edit Prev DS; multi-object readouts are read-only.

Click **Prev DS**, adjust the slider or numeric field, and press Enter to confirm. Escape cancels. Time stays fixed while X changes on the object's original side of its reference. Next is read-only. The **X** row also accepts a direct position.

### Movement Analysis

Enable **Movement Analysis** above the canvas. Connections identify Stand, Walk, Dash and Hyperdash. A DS ratio describes spacing; the movement category describes gameplay requirements. A series of Stand connections does not guarantee one stationary catcher position works for the entire sequence.

TinyDroplets are excluded by default; enable **View → Include tiny droplets** to include them. Breaks and banana showers interrupt connections. See Movement Analysis for categories, label visibility and the floating panel, or Movement strain to locate sections across the song.

### Global speed presets and object alignment

Choose map-specific Distance Snap presets or Global speed presets independently in ordinary Catch and experimental lazer editing. Global presets include 0.5 and 1.0 px/ms plus up to eight custom values. Movement labels and Inspector readouts use the selected units. Guide visibility and collinear snapping work with either system.

Object Snap aligns X to nearby objects without moving placement time. Configure earlier/later search windows in beats; it is disabled by default. Whole-object Distance Snap, collinear and object targets compete by their pointer correction distance. osu! geometric object alignment instead uses visible circles/slider endpoints and its own DS multiplier.

Optional experimental divisions add fine grids; tenth/eleventh families are also available where supported. Beat Snap governs time, Grid Snap governs position, and SliderTickRate governs derived ticks. Slider head and tail snapping have independent controls; inspect them before editing duration.

## Movement Analysis

Movement Analysis draws connections between catch objects on the editing canvas. It helps inspect individual movement requirements and spacing. For a curve covering the whole song, use the separate Movement strain sidebar.

### Enable connections

Click **Movement Analysis** beside Selection Rect above the canvas, or choose **View → Movement Analysis**. The button highlights when enabled. Toggle it off to hide the overlay without editing objects.

### Movement categories

| Category | Meaning |
| --- | --- |
| Stand | The target is within catching range from the departure object's centre |
| Walk | The estimated movement can be made without dashing from the assumed departure position |
| Dash | The connection requires dashing under the movement estimate |
| HDash / Hyperdash | The converted catch sequence requires hyperdash |

The calculation uses the map's CS and converted catch sequence. Each Stand connection assumes its own departure-centre position: a chain of Stand connections does not guarantee one stationary position catches the whole pattern. Walk likewise does not guarantee success from every possible catcher position.

Change indicator colours in **Settings → Appearance → Movement indicator colours**. Canvas Zoom, playback speed and the Distance Snap spacing reference do not change the movement classification.

### DS labels and excluded intervals

Labels show horizontal distance relative to the DPB spacing reference and the time between objects. **DS** describes spacing, while Stand/Walk/Dash/HDash describes movement. See DPB and DS.

TinyDroplets are excluded by default. Enable **View → Include tiny droplets** to include them in this overlay; it does not change gameplay hyperdash rules or droplet selection locks.

Breaks and banana showers interrupt connections, including links that span across them. Simultaneous objects have no usable time interval. Very short gaps of 37.5 ms or less omit DS labels; labels can also be hidden when they would overlap or fall outside the view. Kiai sections do not suppress connections.

### Floating movement panel

Select an object or preview a placement to show the panel near the bottom centre of the canvas. It displays incoming and outgoing movement along with **Prev / Next DS**. A whole slider uses its head for Prev and final tail for Next; an individually selected fruit or droplet uses its own neighbours.

Multiple selected parents show read-only incoming spacing at the earliest selected start and outgoing spacing at the latest selected end, excluding selected parents from the neighbour lookup. Missing or zero-duration intervals show a dash. Prev and Next highlight when their DS values differ by at most 0.02.

For a valid single fruit or slider child, click **Prev DS** to edit its horizontal spacing, or the **X** row to enter its position. Next is read-only, and Lock Notes blocks edits. See Read or edit Prev DS for confirmation and cancellation.

## Slider Inspector

### Inspect the selection

Open **Layout → Slider Inspector** or choose an editing action from **Shift+F**. Read type, start/end time, duration, spans, control count, exported SV and path length. Multiple sliders show ranges. Streams and stacks export as circles and therefore have no exported slider SV.

### Native Legacy editing

Select a Legacy Slider to edit **Legacy SV (×)**. This is the object's own multiplier: changing it retains length and changes duration without adding authoring green lines. It is distinct from the remembered default SV for newly drawn osu! sliders and from the map's base SliderMultiplier.

Drawn and processed views distinguish the retained original geometry from droplet-processed output. Edit native controls in osu! view. Holding Shift while dragging a Legacy control adjusts length and authored SV together to preserve duration within the allowed SV range. Ordinary control dragging retains SV and changes duration according to the snapped length.

### Conversion and pattern actions

Inspector offers conversion, clearing interior controls, merging, breaking generated patterns into independent objects, and restoring slider or banana-shower output. Inspect the selection before applying an action. Conversion validates output and leaves the original intact on failure. Undo reverses a successful content change.

### Droplet policy

Set a supported slider to Follow map, Derandomize or Randomize. The map default lives in Droplet Settings; a per-slider override stays with that slider. Reset manual droplet adjustments independently when you want to remove individual offsets. Strength, seed and NM/HR compensation affect the playable output, so inspect both relevant preview modes.

See Droplet randomization, FSliders and Editing canvases.

## Banana shower patterns

### Edit the interval

Place a Banana Shower with tool 4. Its interval becomes a spinner in osu! output. Drag its start or end boundary independently on the canvas or upper object timeline. Check Beat Snap before dragging; undo restores a committed edit.

### Generate repeated showers

Select a shower and use **Ctrl+F** to open Banana Shower Edit. Set separate length and gap fractions, inspect the full-map banana preview, and use Alt+wheel to zoom the conversion preview. Commit only after checking both the repeated intervals and the space around neighbouring notes.

Keep a grouped pattern to adjust its parameters later. Separating the pattern creates independent objects and removes the shared editing relationship. The conversion dock offers restoration actions for supported managed patterns. Save editable project data to retain the editable grouping.

### Check the playable result

Use Catch Preview and Testplay to inspect the shower boundaries and surrounding movement. The preview shows bananas generated from the shower interval. FSlider streams and stacks use their own conversion settings.

## Pattern Gallery

### Capture and browse

Open **Layout → Pattern Gallery**. Select a consecutive passage on the main canvas, then choose Save selection. The gallery stores an independent snapshot, including supported editable project data; later edits to the source do not rewrite it.

Browse skinned cards or open an enlarged preview. Search matches names and source metadata. Organize cards in folders and apply coloured tags; selected tags match any of those tags. Tags can exist independently of assigned patterns. Resize cards and scroll or zoom the enlarged preview to inspect dense patterns.

### Place a pattern

Drag a pattern from its card or preview into the Catch canvas. Choose the destination timestamp against the active beat grid. Placement adapts beat timing to the destination BPM. Choose whether to keep horizontal spacing or preserve horizontal speed; inspect the resulting preview before completing the drop. Placement is undoable.

### Edit or return to the source

Choose **Edit Pattern** in the details to open its isolated editing session. The previous map's unsaved work is retained. Save the pattern edit to update that gallery snapshot, then return to the map. Source navigation opens the original map when it remains available; it is separate from editing the stored snapshot.

### Organize and share

Select ranges of cards and use the context actions to move, copy, merge or export. Folder actions handle selected contents. Share **.fpt** packages; import into a new or existing folder, or drop a package onto the editor. Older .fap drag imports remain accepted. Exporting a pattern is different from exporting a difficulty (.fad) or complete project (.faz).

Try capturing and placing a phrase from the sample beatmaps.

## Timing and control points

Use **Timing Panel (F3)** for BPM and offset, or **Timing and Control Points (F6)** for red/green points, samples, volume and Kiai. **F1** returns to Mapping.

### BPM and offset

In Timing Panel, click a red line on the waveform to select its section. Edit **BPM** and **Offset**; BPM accepts up to two decimal places and offset uses whole milliseconds. Click the waveform to seek, Alt+wheel to zoom and Ctrl+wheel to change Snap.

**Move notes with offset / BPM changes** keeps objects at their beat positions in the edited section. **Move greens / bookmarks with offset** separately moves inherited points and bookmarks by the offset change; it does not move them for BPM changes.

### Tap timing and metronome

Play the music and press **T** or click **Tap Here** on the beat. The tenth tap applies a fitted BPM and offset; subsequent taps refine them. **Apply timing** can apply a shorter measurement with at least two taps. **Reset taps** clears the measurement.

**Metronome Clicks** plays beat ticks instead of object hitsounds in Timing. Holding Ctrl adds subdivision ticks according to the Snap family. Space pauses at the actual audio position in this mode.

### Red and green points

Press **F6**. Select a row, Ctrl-click to toggle rows, or Shift-click for a range. All, Timing Points and Inherited Points filter the list.

| Point | Editable role |
| --- | --- |
| Red | Timing section: offset, BPM and meter |
| Green | Inherited sample bank/index, volume and Kiai settings; imported SV is retained |

**Ctrl+P** adds a red point at the playhead; **Ctrl+Shift+P** adds a green one. **Ctrl+I** deletes selected rows in the dialog or the current section outside it. Ordinary deletion protects the first red point.

The dialog supports timing-row copy/cut/paste. **OK** commits the draft as one undo step; Cancel or Escape discards it. Review apply options before confirming: object scaling, resnapping, slider-length changes and marker adjustments are separate choices.

### Slider Tick Rate

Slider Tick Rate controls slider tick spacing. It is separate from the editor's placement Snap and the subdivision used by streams and stacks.

### Base SV

In Timing Panel use **Slider Velocity → Adjust** to open Configure DS. Its shared numeric field edits base SV or DPB: select the unit button; DPB = 100 × SV. SV accepts 0.4–3.6 (DPB 40–360). The per-object Legacy SV in Inspector is a different value.

Apply validates base SV, DPB and rescaled DS presets together before one undoable edit. Inherited SV compensation preserves exported NM/HR positions and times. The prior confirmed value remains active until validation succeeds; an invalid adjustment leaves the difficulty unchanged. Cancel discards the full draft.

### Section commands

The Timing menu includes time-signature presets, section/all-object resnap, time translation, slider-length recalculation and the song preview point. Resetting or clearing timing requires confirmation. These content operations support Undo; inspect the affected pattern after applying them.

### Exported SV and editing lock

Enable **Show SV lines** to display exported timing, including SV-only changes. **Lock SV** independently protects SV-only rows from structural editing. Derived rows without a corresponding authoring line remain read-only even when unlocked. Distinguish these lines from the authored BPM/sample timeline and individual Legacy SV. Editing a generated SV must follow the permitted editing state; merely displaying it does not rewrite the map.

### Optional one-millisecond alignment

Opening a difficulty may offer Align automatically or Keep original after import questions. Alignment shows separate hit-object/timing-line counts, commits one undo step and marks the difficulty edited without automatically saving. Keep original or Escape retains the source timing. The first red line remains the anchor. Larger offsets and collisions are not indiscriminately snapped. Mapset Verifier describes the available alignment fixes.

## Song Setup

Open **Layout → Song Setup** or press **F4** to select its dock. General, Difficulty, Colors and Design are horizontal categories. Valid edits apply immediately and mark content dirty; use project Undo and Save. There are no Apply/Cancel buttons, and opening it does not pause playback.

### General and metadata

Artist, title, creator, source and tags are shared across the project's difficulties. Difficulty Name applies only to the active difficulty.

Non-ASCII artist/title text allows a separate romanised spelling. ASCII text supplies both forms. Changing General's romanised display preference in Settings changes how metadata is shown, not the stored map text.

### Difficulty

Set HP, CS, AR and OD from 0 to 10. Drag in whole steps, hold Shift for 0.1 steps, or type a decimal value.

CS determines catcher size; AR affects the preview's approach timing. Preview mods can change the effective values without rewriting these saved settings.

### Combo colours

In **Colors**, enable a custom palette, select a swatch and edit saturation/value, hue or its six-digit HEX value. Add up to eight colours or remove a swatch while keeping at least one.

Disabling custom colours removes the map's combo overrides, allowing skin fallback. The confirmed palette applies to the canvas, preview and Testplay.

### Design

Design stores countdown speed and offset, widescreen storyboard support, letterboxing in breaks and the flashing-light warning. These values are preserved on export. Countdown settings are retained for export. video and storyboard visuals are rendered during transport playback; see Video and storyboard.

### Randomize droplets

Open the separate **Droplet Settings** dock for the difficulty default, HR compensation, Strength and Seed; Slider Inspector holds supported per-slider overrides. See Droplet randomization for the effect of each option and the defaults for new sliders.

### FA-only settings

Open Song Setup through Layout. Valid content edits apply with undo. FA-only difficulty settings are stored in the FA project. New difficulties can copy an existing difficulty or apply a global six-value template; see Difficulty templates.

## Combos and hitsounds

Use the right toolbar to set New Combo and additional hitsounds. Beatmap samples, skin samples and editor volume have separate roles.

### Object and edge flags

| Control | Shortcut | Effect |
| --- | --- | --- |
| New Combo | Q | Start a new combo group |
| Whistle | W | Toggle the whistle addition |
| Finish | E | Toggle the finish addition |
| Clap | R | Toggle the clap addition |

Select an object before changing its flags. Selecting a slider edge targets that head, repeat or tail; selecting the whole slider affects its edges. Mixed selections show a mixed state. During placement, controls set flags for the next object. Banana showers use their fixed banana sound.

### Timing sample settings

Press **F6**, select the relevant timing points and open **Audio**. Choose Normal, Soft or Drum, set the sample index and volume, then audition Hit, Whistle, Finish or Clap. Each button plays the named sound with the selected settings.

Confirm with OK to apply the timing draft. Imported slider velocity and unrelated effect data are retained. See Timing and control points.

### Skin and beatmap samples

In **Settings → Audio**, **Use skin's sound samples** enables skin sounds; disabling it uses FA's built-in defaults. Beatmap-specific samples take priority in either case.

Keep custom sound files with the resources referenced by the map. Standalone `.osu` export does not copy audio or samples; OSZ export packages a playable set.

### Playback volume

The bottom-right Volume control has Master, Music and Effect channels. **Alt+Left / Right** selects one and **Alt+Up / Down** adjusts it. Settings' All, Song and Hitsound sliders control the same levels. These are listening preferences; timing-point volume is map content.

To transfer samples from another map, use Hitsound Copier.

## Hitsound Copier

Open **Timing → Hitsound Copier (Beta)** to copy sample settings from another difficulty or clear them. **Check** previews the operation before Apply.

### Choose a source

Use an external osu!standard or Catch `.osu`, or another difficulty in the current set. Copying requires matching red timing times, BPM and meter. Events match the nearest playable time within 2 ms; unmatched events are skipped.

Target SV and Kiai state are retained. Source sample bank, index and volume changes become timing data in the target. Existing timing-point sample fields are overwritten.

### Inspect and apply

1. Choose the source and target mode.
2. Run **Check** and read the matched-event count and planned file changes.
3. Scroll through the file list, including any new names for conflicting samples.
4. Choose overwrite or a named new difficulty, then Apply.

Creating a new difficulty preserves the original. Overwriting applies as one undo step, including associated file changes. If the map changes after Check, rebuild the preview before applying. Legacy sliders have sample-format constraints; an incompatible preview reports them instead of partially applying the copy.

### Multiple difficulties

**All target Diffs** copies to every difficulty in the project, excluding a selected same-set source. Check validates every target first. Overwrite creates an undo step per difficulty; Create new Diff produces separate named copies.

### Clear hitsounds

The **Clear** tab restores default samples and removes Whistle, Finish, Clap and custom object samples. It can delete unshared referenced hitsound files while retaining music and resources referenced by other difficulties. Review the planned changes before Apply. Deleted files are backed up under `.hitsound-copier-backups`.

## Collab Mapping

### Configure source sections

Open **Layout → Collab Mapping** in the receiving difficulty. All referenced difficulties must be in the same project. Add Section, choose a source difficulty and enter Start/End timestamps or integer milliseconds. Unassigned sections are saved but left untouched by sync. Overlaps and self-references are rejected; adjacent sections are allowed.

Generate sections from bookmarks creates unassigned ranges from zero through the final timeline position. Assign sources before syncing. Removing a reference leaves already merged objects in place.

### Sync references

**Sync references** reads current source documents, including unsaved edits, and replaces target objects in assigned sections. Sources are not synchronized recursively. Enable **Sync object hitsounds** to copy source sounds; disable it to retain matching target sounds at the same exported time/event kind. New unmatched events use target merged-timeline defaults.

Actual source red points, BPM, meter and sample transitions are used. A section boundary does not invent a red point. Sliders and spinners must fit wholly inside a section; partial intersections are skipped and reported. Click an exclusion to inspect its source. Earlier ranges include Start and exclude End; the final range includes End. An object may end exactly at a section boundary.

Each successful synchronization is one undo step. Review timing, movement and hitsounds before saving or exporting. Changes to a referenced difficulty are not automatically copied merely because the reference exists.

### Reverse synchronization

In the guest difficulty, **Maps referencing this difficulty** lists receiving difficulties. **Sync from this difficulty** copies a listed receiver's referenced sections back into the active guest, with optional sounds and song metadata. The guest retains its difficulty name, online difficulty ID and references.

Successful reverse sync also protects all time outside the guest's referenced sections in the same undo step. This is useful when returning a guest part while retaining surrounding context.

### Protected ranges

Lock all sections other than referenced sections protects objects, timing and empty gaps outside the captured ranges. Objects must fit completely within an editable range. Apply the lock again after changing references; it is a snapshot of the ranges. Unlock sections removes protection. Remove all notes other than collab sections is a separate content cleanup action; inspect the ranges before using it.

## Portable projects and difficulties

### Export a project as .faz

Use **File → Export project (.faz)** or the Library card action. Choose a complete archive (FA project plus playable OSZ) or an FA-only archive (FA project and resources). Editor export includes unsaved documents without marking them saved. Library export uses persisted content.

FAZ retains editable curves, patterns, difficulty data and project Notes. Available audio, samples, images, video and storyboard files travel with it. Missing required audio stops export. Keep the archive independently of version history.

### Import a project

Open or drop one FAZ and choose Independent project, Link existing maps, or Install in Songs when a complete archive permits it. Independent import writes to the workspace without linking Songs. Linking requires one Songs folder containing a unique match for every archived difficulty; extra difficulties are allowed. Installation creates a new uniquely named Songs folder.

Linking preserves editable project data and reviews differences against the local .osu files. Compare the imported version with the linked difficulty and choose the changes to keep. Cancellation preserves the existing maps.

### Exchange one difficulty as .fad

Use **File → Export as .fad…** or the difficulty tab menu. Open the corresponding project before importing a FAD. It does not create a standalone Library project. Open, drop or use **File → Import difficulty (.fad)…** in that project.

An existing matched difficulty enters merge review; a missing difficulty can be added. Objects and FA settings are reviewed together; metadata and timing have separate choices. Review the merged difficulty, then save or export it. Export includes unsaved work without changing the local Songs association.

### Choose the appropriate file

Use FAZ for an entire FA project, FAD for one FA difficulty and FPT for reusable patterns. OSU/OSZ are playable exports and do not retain all editable FA structures. Keep the workspace manifest and .catchdiff files together for an unpackaged FAZ backup.

## Discussion and osu! Web

### Read public discussions

Open **Layout → Discussion**. Read only mode shows public Reviews, General (all difficulties), General (current difficulty) and Timeline threads for maps with online IDs. Cached discussions remain readable offline; Refresh requests a new snapshot. General and Reviews can sort by creation/update time; Timeline can sort by timestamp/update time.

Click a thread or timestamp to seek. Object-number references also select matching objects when identifiable; stale or ambiguous references still seek. Drag across post text and Ctrl+C to copy it. Permalink copies a post link; author names open profiles. Click a post image for zoom/pan preview and Escape to close it.

### Canvas annotations

Open Discussion to show linked note intervals and cards on Catch canvas. The lower-left Show canvas annotations control hides cards/markers while retaining note-interval bands. It saves a view preference without changing content. Click a card or marker to reveal the corresponding thread.

### Official website mode

Right-click the Discussion dock tab and choose **osu! Web**. Sign in to your osu! account to post mods, reply to threads, resolve issues and hype beatmaps inside the editor. Available actions follow your account's permissions and the beatmap's status. Your website session is retained across app restarts.

Your osu! Web sign-in data, cookies and browser storage are kept only on your computer. You can clear them in **Settings → osu! Links & Web → Clear local user data and cookies**. Clearing this data signs you out of osu! Web without deleting your beatmap projects.

Use Problem or Suggestion to leave a mod, reply to discuss a change, and Resolve to close an addressed issue. Hype is available on eligible beatmaps. Timestamp links seek to the corresponding passage and select matching objects in the editor.

The toolbar provides Back, Forward, Refresh, a copyable address and Open in browser. Refresh returns to the active difficulty's discussion page. Choose **Read only** from the dock tab menu to return to the discussion reader.

### osu! links

Settings → osu! Links & Web controls protocol routing, a fallback app and embedded browser data. When FA handles osu://, edit links navigate the active difficulty; they do not identify/open a missing map. Other commands forward to the chosen fallback. The time-jump dialog also accepts an edit URL.

## Testplay

Catch Preview shows the map while you edit; Testplay lets you try a section with a controllable catcher. Both use the selected skin and beatmap samples.

### Catch Preview

Open it through **Layout → Preview**, or use the existing edge control. Choose the preview mod before starting Testplay. See Catch Preview for resizing, aspect ratios, mods and background settings.

### Start a test

Seek to the section and press **F5**. Testplay uses the selected preview mod. **Settings → Testplay → Testplay speed** sets its independent starting speed from 10% to 150%; returning restores the editor's previous speed.

**Testplay lead-in** provides 0–5 seconds of preparation, one second by default. At the song's beginning, FA also provides opening preparation. Space activates Skip while a long intro offers it.

### Controls and retry

| Key | Action |
| --- | --- |
| Left / Right | Move the catcher with default bindings |
| Hold Shift | Dash with the default binding |
| Esc / Ctrl+P | Pause or resume |
| Ctrl+R | Retry from this session's start with its lead-in |
| Hold backtick / ~ for 300 ms | Retry after dimming; release early to cancel |
| F1 | Return to the editor at the selected start |
| F2 | Return at the current Testplay position |

Change movement and dash bindings in Settings. The pause menu's Continue, Retry and Back also accept Up/Down and Enter.

### Autoplay and bookmarks

**Tab** toggles autoplay. Pressing a movement or dash key returns to manual play. During autoplay, **F3** switches between 1.0x and 1.5x; Ctrl+Up/Down adjusts speed by 25 percentage points, or 5 with Shift. These changes affect the session and retries, not the saved starting-speed preference.

**Ctrl+B** bookmarks the current time; **Ctrl+Shift+B** removes a nearby bookmark. Return with F2 to edit a problem spot immediately.

### Background

Settings and the pause menu control background dim. **Dim Background** gives a fully black background, including intros and breaks. Video and storyboard playback follows the song transport; see the media section below. Before publishing, inspect the exported map in your target osu! client.

### Video and storyboard

Video and storyboard playback follows the song transport in editing, Preview and Testplay. Use Enable Video/Storyboard to show visuals and Background dim to adjust their brightness. Paused seeking does not fire gameplay triggers. Use Wings of Courage to inspect the scene with complete resources.

## AiMod checks

### Run the check

Press Ctrl+Shift+A or open the AiMod dock. Inspect Compose, Design, Timing, Meta and Mapset findings. Checks use exported Catch objects and leave map content and undo history unchanged. Refresh after edits; changing difficulty checks its current data. A successful collaboration sync refreshes an open report.

### Read and navigate

Select/copy report rows, resize or reorder columns and click timestamps or affected objects to inspect the finding. Read the complete message before changing an intentional pattern. Resource, metadata and timing findings need different remedies; movement difficulty itself is not an automatic error.

### What counts as an overlap?

Object-start and boundary rules use playable Catch output, including derived slider events. Slider endings and intentional simultaneous structures need context. Compare the location with the actual canvas/preview rather than deleting every reported note.

### Check movement separately

Use Movement Analysis, the strain graph, Catch Preview and Testplay for playability. Mapset Verifier supplies a separate mapset/difficulty report. Neither checker replaces listening to hitsounds or playing the relevant section.

## Save, export and recover

### File formats

| Format | Purpose |
| --- | --- |
| .catchdiff and workspace manifest | Native editable difficulty and project organization; keep the complete folder |
| .catchproj | Older native format, accepted and migrated on save |
| .faz | Portable FA project with available resources; optionally includes playable OSZ |
| .fad | One FA difficulty and resources for import into an open project |
| .fpt | Reusable gallery pattern package |
| .osu / .osz | Playable difficulty / set; does not retain all FA authoring controls |

### Save and Songs association

Finish drawing or dragging and press Ctrl+S. Saving persists the current FA difficulty in the background. A confirmed Songs link can also publish changes to the linked .osu. Press Ctrl+S to save pending edits after synchronization.

The initial Songs export offer is shown once per workspace project. Keep in workspace saves without creating a Songs export. Songs associations apply per difficulty; FA-only difficulties remain in the FA project without publishing to Songs. Read the destination and any synchronization choices before accepting them.

### Export a difficulty

Use File → Export .osu or Ctrl+Alt+E. Create a new difficulty in osu! produces a separate difficulty; Update an existing difficulty replaces the chosen target after confirmation. Export .osu to another location writes a playable file without creating a Songs link or packaging resources. Check Song Setup metadata and the destination difficulty name.

### Export a set

The Library card's Export .osz packages playable difficulties and resources. Use FAZ/FAD for editable project sharing. Saving and exporting have separate purposes; a successful standalone export does not establish a Songs synchronization link.

### Resolve external changes

FA compares linked external data with the shared baseline. Inspect the reviewed difficulty identity, category tabs, object details, normalized timing and result preview. Select local/external values for required differences, then apply. Linked resolutions can publish the selected result to the linked .osu; check the displayed destination. Cancel leaves the review unapplied.

Break ranges are shown in aligned columns and can be resolved independently. FAZ association and FAD import also use comparison; compare the imported version with the linked difficulty before applying changes. Review native objects/FA settings together where required to retain editable structure.

### Back up and recover

Ctrl+Z undoes a session edit. Ctrl+L restores the previous saved version after confirmation when available. Edit → Version history lets you compare and restore a difficulty. Retention normally limits history to 30 days and 100 rounds per project; cleanup can remove old snapshots. Restoration archives current authoring before applying the selected version.

Enable recovery backups to keep restorable versions. Export FAZ archives for long-term backups with the project resources. Clear cache removes generated data.

## Mapset Verifier

Load all difficulties you want to check before running a mapset review.

### Open and refresh

Use **Edit → Mapset Verifier** or Layout. Refresh checks exported snapshots of all loaded difficulties, including unsaved edits. It checks the loaded project, not every difficulty on disk. Checking does not save the original maps or add undo history. General covers set-level results; the current-difficulty tab follows the selected editor difficulty.

While the panel is visible, content commits and undo/redo schedule a check after one second without another edit. Selection, seeking and zooming do not rerun checks. Refresh requests one immediately. Pending edits supersede older reports.

### Read the report

Choose a severity pill to show that severity only; click it again to restore all. Category status reflects all findings even when filtered. Expand a check, click timestamps to inspect issues, or Copy all to copy grouped messages. Check titles and messages retain upstream English. Use the linked timestamps to review each finding in context.

### One-millisecond fixes

Eligible Unsnapped hit objects, Unsnapped timing lines and Unsnapped kiai groups offer Fix for me. When other categories also have eligible offsets, choose the current section or all three. Each accepted correction is undoable; Cancel preserves content. Occupied boundaries and invalid durations are retained. Unused timing lines is a read-only report.

Checking and export preserve those offsets until you apply a correction. The separate alignment prompt on opening a difficulty remains optional.

### AiMod and resources

Use AiMod for FA's Catch-specific checks and Mapset Verifier for its mapset/difficulty checks. Inspect both and then Testplay. Resource checks remain available when video and storyboard visuals are disabled. If the engine or a required resource is unavailable, the panel shows the error so you can correct it and refresh.

## Video and storyboard playback

### Example and resources

Use [Wings of Courage -Sora o Koete- — Platter, mapped by examination](https://osu.ppy.sh/beatmapsets/245160#fruits/890755). Keep the complete downloaded set, including its shared .osb, SB folder and referenced media. Open the complete mapset so all storyboard resources are available.

### Enable visuals

Enable **View → Enable Video/Storyboard**, the matching canvas background control, or **Settings → Testplay → Enable Video/Storyboard**. These share one saved preference for the mapping canvas, Catch Preview and Testplay. View/canvas changes apply immediately; Settings changes require Apply.

Playback, pause, speed and seeks follow the song transport. A paused seek displays the target's time-based scene. It does not simulate a hit or miss. Gameplay-triggered effects respond to hits and misses during playback. Start Testplay to see hit- and miss-triggered effects.

### Dim and disable

Mapping Canvas Background Dim is independent of preview/testplay dim. View → Dim Background forces opaque black in preview/testplay, including intros/breaks. Reduce dim and turn off forced black when examining the storyboard.

Disabling Video/Storyboard hides the video and animated sprites. Static beatmap backgrounds and storyboard samples continue to work. Use the audio controls to adjust sample volume.

### Loading and diagnostics

Opening waits for the prepared first frame. Seeking updates the playhead immediately while the previous complete media frame can remain until the target images are ready. Missing images or unsupported/unreadable video produce diagnostics rather than a complete playable scene. Check resource paths and the Mapset Verifier report; retain all media when sharing a native archive.

See Testplay to play the scene, or Portable projects to share the project with its resources.

## Keyboard and mouse shortcuts

In Compose, Tab toggles the Catch and osu! canvases. Text fields,
dialogs, Ctrl+Tab difficulty switching and Testplay retain their own Tab actions.
See osu! canvas for native slider controls and distance spacing.

This reference describes FruitsAtelier v1.3.0. Bindings depend on the active page,
focused field, selected object and drawing state. For differences from osu!stable
and proposed additions, see the compatibility review.

### Reading the tables

- `Ctrl` also accepts Command on macOS. `Alt` corresponds to Option. Some Mac
  keyboards require Fn for function keys.
- Number shortcuts use the main number row. UI scale accepts main-row and keypad plus/minus.
- Unless stated otherwise, editing shortcuts apply in Compose, outside text
  fields and dialogs, after releasing a drag and finishing a drawing draft.
- Selection-dependent commands require a compatible selection. Lock Notes blocks
  moving, reshaping and deleting existing objects, but allows selection, playback,
  placement, flags and undo/redo.
- Only testplay movement and dash bindings are configurable in Settings.

### Experimental lazer editing

Enable **Settings → Experimental → lazer editing**, then Apply, to use these
Compose overrides. The remaining tables describe Mapping controls. Existing
FruitsAtelier commands without a conflicting lazer assignment remain available;
testplay and focused text/dialog controls retain their own bindings. Testplay key
assignments and Mapping view preferences are preserved when switching modes.

| Input | lazer Compose action |
| --- | --- |
| T / Y | Toggle Distance Snap. Y remains an alias. |
| Alt+Left / Alt+Right | Seek previous / next bookmark. |
| Ctrl+Left / Ctrl+Right | Move selected objects by one X unit; with no selection, seek the previous / next parent start. |
| Ctrl+Shift+Left / Ctrl+Shift+Right | Seek previous / next sample point: slider head, first-span midpoint, reverses and tail, or the neighbouring parent's tail/start. Samples are edited through the existing sample controls. |
| Up / Down | Previous / next timing point. Backward navigation while playing allows one second at the current playback rate. |
| F3 / F4 | Open Timing / Song Setup. |
| - / = | Compress / expand canvas time spacing without changing its horizontal scale; available in both editing modes. |
| Alt+Q/W/E/R | Set the additions sampleset to Auto / Normal / Soft / Drum. |
| Ctrl+J | Reserved; use the slider's menu action to extend it. |
| Ctrl+wheel | Cycle beat-divisor presets within the current family. |
| Shift+1–9 | Select the corresponding divisor family. |
| Wheel / Left / Right | Seek along the lazer beat grid; Shift uses a coarser lattice. Playback seeks use the upstream BPM-dependent seek-distance adjustment. |
| Z / V | Seek first object / maximum object end; pressing at that boundary seeks song start / end. |
| Right-click object | Open its context menu. |
| Right-click during an FSlider draft | Finish the draft. |
| Shift+right-click object | Delete the object. Middle-click also deletes on the object timeline. |
| Middle-button drag on canvas | Pan horizontally without seeking, bounded by the enlarged game screen. |
| Middle-button drag on empty object timeline | Marquee selection. |
| Shift+click object timeline | Select the full range from the selection pivot; Ctrl adds it to the current selection. |
| Alt+wheel over canvas | Scale both axes together around the fixed catch line, with 200 ms easing; 25%–100% while locked, up to 200% when unlocked. |
| Ctrl+Alt+wheel | No Compose action. |

Ctrl+wheel uses the common family `1, 2, 4, 8, 16`, the triplet family
`1, 3, 6, 12`, or the integer divisors of a selected custom divisor. Short paused
seeks ease over 300 ms; running playback and jumps beyond 5000 ms seek immediately.
These view and navigation operations do not alter beatmap AR or content.

Alt+wheel over the upper object timeline changes its normalized zoom by 0.02 of
the allowed range per notch, easing over 200 ms. The visible range starts at
6000 ms and is limited to 500–10000 ms, bounded by song length. Mapping retains
its separately saved timeline scale.

Fruit/FSlider/Banana aliases F/B/N, Ctrl+M, Ctrl+G, Shift+F and the Stream/Stack
and merge shortcuts retain their FruitsAtelier functions. Commands that lose a
Compose shortcut remain accessible through their existing menus and controls.

### Files and pages

| Keys | Action |
| --- | --- |
| Alt+Enter | Toggle fullscreen and save the preference. Available in Library, Settings and testplay as well as the editor. |
| Ctrl+O | Choose a difficulty in the current project. |
| Ctrl+Shift+O | Open a supported beatmap, archive or project file. |
| Ctrl+S | Save the project; also update linked exported difficulties. |
| Ctrl+L | Confirm rollback of the current difficulty to its previous saved version. Requires a workspace save history; unavailable after only the first save. Undo restores the edits from before rollback. |
| Ctrl+Alt+E | Open export choices from Compose or Timing, outside focused Timing fields and modal dialogs. |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous difficulty. |
| F1 | Return from Timing to Compose / Details. |
| F3 | Open the Timing page. |
| F4 | Open Song Setup. |
| F5 | Start testplay at the playhead with the configured lead-in. |
| Ctrl+Shift+A | Open AiMod for the current difficulty. |
| F6 | Open Timing and Control Points. |
| F12 | Copy the current editor window content to the system clipboard as an image, including visible dialogs or testplay. Paste it into a chat or image editor. |
| Esc | Dismiss the current field, popup, menu or gesture first. From idle Compose, request return to Library; from Timing, return to Compose. Unsaved changes can prompt. |

F2 has no Compose page action. It has a separate meaning during testplay.
Windows Alt+F4 requests closing the application window; use Esc to return to
Library. Finish a text edit with Enter before using page or file commands.

### Tools and selection

| Keys | Action |
| --- | --- |
| 1 | Select tool. |
| 2 / F | Fruit tool. |
| 3 / B | FSlider tool. |
| 4 / N | Banana Shower tool. |
| Ctrl+A | Select all parent objects. |
| Ctrl+X / Ctrl+C / Ctrl+V | Cut / copy / paste selected parent objects. |
| Ctrl+D | Clone the selection, positioning its earliest start one measure after the last selected start. |
| Delete | Delete selected objects or selected slider controls. On macOS, Backspace also works outside text fields. |
| Ctrl+Z | Undo. |
| Ctrl+Y / Ctrl+Shift+Z | Redo. |
| Ctrl+H | Mirror selected objects around X=256. |
| Ctrl+Shift+Left / Ctrl+Shift+Right | Move selected objects by the current horizontal grid step. |
| J / K | Move selected objects earlier / later by one current Snap subdivision, using the earliest selected start's BPM. |
| L | Toggle Lock Notes. |

Undo and redo keep the current tool. Changed or removed objects lose their selection;
unchanged selected objects retain it. Restored objects are not automatically selected.
When all affected positions are outside the canvas, undo/redo reveals the nearest affected position. Visible changes keep the viewport in place. When no history step is available, selection stays unchanged. Existing slider controls
are edited in Select; B / 3 clears selection and prepares a new FSlider.

Object paste works across difficulties in the current mapset and aligns the earliest
copied start to the playhead. Copy also writes an osu! timestamp reference to the
system clipboard. Slider controls use their own editing selection; the object
clipboard works on complete parent objects.

During an FSlider draft, 1–4 finish valid geometry (or cancel insufficient geometry)
before changing tools. Pressing 3 prepares another slider. Shift+1–9 remains
available during drafting. An unfinished Banana Shower is cancelled on a tool
change. Other shortcuts may be blocked until the draft or drag ends.

### Playback, navigation and bookmarks

| Keys | Action |
| --- | --- |
| Space / C | Play / pause. Available while holding a canvas or upper-timeline marquee. With beat snapping enabled, pausing aligns to the nearest current Snap grid line; see pause behavior. |
| X | Seek to song start and play. |
| Home | Seek to song start without forcing playback. |
| Z | Seek to the first object's start; if already at or before it, seek to zero. |
| V / End | Seek to the end of the last object by start order; if already at or after it, seek to song end. |
| Left / Right | Seek earlier / later by one full beat during playback. While paused, move to the preceding / following Snap grid line, including timing boundaries. |
| Shift+Left / Shift+Right | Seek four full beats during playback. While paused, move four Snap grid lines in the chosen direction. |
| Up / Down | Next / previous timing point, including inherited points. |
| Ctrl+Up / Ctrl+Down | Increase / decrease speed by 25 percentage points, within 10%–150%. |
| Ctrl+Shift+Up / Ctrl+Shift+Down | Increase / decrease speed by 5 percentage points. |
| Ctrl+B | Add a bookmark at the playhead. |
| Ctrl+Shift+B | Remove the nearest bookmark within two seconds. |
| Ctrl+Left / Ctrl+Right | In Compose, move selected objects by one X unit; without a selection, seek the previous / next bookmark. Timing always navigates bookmarks. |
| Alt+Left / Alt+Right | Open volume controls and select Master, Music or Effect. |
| Alt+Up / Alt+Down | Open volume controls and change the selected channel by five percentage points. |

Click the current timestamp to open Jump to time. It accepts milliseconds,
`mm:ss:ms`, and copied osu! timestamp references. There is no keyboard shortcut to
open it. Enter seeks; Esc cancels. Volume shortcuts also work during testplay.

### Snapping, combos and hitsounds

| Keys | Action |
| --- | --- |
| Shift+1 through Shift+9 | Set Snap to 1/1 through 1/9. |
| Ctrl+M | Cycle 1/3, 1/4, 1/6, 1/8; start at 1/3 from another divisor. |
| Ctrl+1 / Ctrl+2 / Ctrl+3 / Ctrl+4 | Set horizontal grid spacing to 4 / 8 / 16 / 32. |
| G | Cycle those horizontal grid spacings. |
| T | Toggle Grid Snap in Compose. In Timing, T is tap tempo. |
| Y | Toggle Distance Snap. |
| Hold Alt | Temporarily invert Distance Snap. |
| Hold Shift | Temporarily invert horizontal Grid Snap, unless Alt is also held. |
| Q | Toggle New Combo for the selection or placement state. |
| W / E / R | Toggle Whistle / Finish / Clap for the selection or placement state. |
| Shift+Q/W/E/R | Set the normal sampleset to Auto / Normal / Soft / Drum for the selection or placement state, in both editing modes. |
| Ctrl+Q/W/E/R | Set the additions sampleset to Auto / Normal / Soft / Drum for the selection or placement state, in both editing modes. |

The standard Snap choices are 1/1–1/9, 1/12 and 1/16. The last two are accessible from
the Snap slider or Ctrl+wheel. Settings → Experimental
adds 1/24, 1/32 and 1/48; Ctrl+wheel can reach them within their doubling/halving
families. Beat snapping is independent of Slider Tick Rate.

### Slider editing

| Keys | Action |
| --- | --- |
| Enter | Finish a slider draft. |
| Esc | Cancel a draft or active gesture. |
| Shift+F | Immediately open the long-press actions for a slider under the canvas pointer, or a selected slider visible on the canvas. Selection from the upper object timeline is supported; a selected target retains the complete selection. |
| Edit → Toggle straight / curved point | Toggle the selected control between straight and curved. |
| Ctrl+Shift+I | Insert a control on the curve under the pointer in Compose. |
| Ctrl+= / Ctrl+- | Increase / decrease UI scale by 25 percentage points, within 50%–200%. |
| Ctrl+G | Reverse the selection in time, including each slider’s own path. A single FSlider retains its time range and repeats. |
| Ctrl+J | Extend the selected FSlider to the pointer at a valid later canvas time. |
| Ctrl+Shift+F | Open/focus the Stream/Stack dock, adding it if closed. Existing patterns retain their mode; ordinary sliders start on Stream. |
| Ctrl+Shift+M | Open/focus merge controls in Slider Inspector. Select at least two consecutive circles/sliders to merge. |

The Stream/Stack dock previews its output on the main canvas. Valid parameter
changes apply with project undo; graph and snap drags commit on release. A stream
retains an editable slider parent and exports fruit objects. **Break into Fruits**
is an explicit action that creates independent editable fruits.

Holding a selected object retains the complete selection for batch slider actions.
Merge offers straight segments and a curved path; a selection containing a curved
slider offers only the curved path. A selected banana shower blocks merging.
Enter applies the merge; Esc cancels. Saving stores the resulting slider without
the original objects; reopening cannot restore them, while session undo remains
available.

Ctrl+L restores the previous saved version with confirmation. Ctrl+J and Ctrl+Shift+I have editor-specific meanings; do not assume
osu!stable behavior. Ctrl++ (Ctrl+Shift+= on a US keyboard) and Ctrl+keypad plus/minus
also adjust UI scale. Slider reverses use the object-timeline tail drag, with no keyboard binding.
Scale changes use the saved 50%–200% range in 25% steps, outside text fields,
dialogs and active gestures.

### Mouse combinations

| Input and location | Action |
| --- | --- |
| Wheel over canvas, object timeline or overview | Up seeks earlier; down seeks later. One notch is a Snap subdivision while paused, or a whole beat while playing. |
| Shift+wheel | Seek four times the normal wheel distance. During marquee selection, wheel seeking remains single-step. |
| Ctrl+wheel over canvas or either timeline | Double / halve the current divisor within supported choices, stopping at the family boundary. |
| Alt+wheel over canvas | Scale X and Y together around the pointer's time, including when uniform scaling is unlocked. |
| Alt+wheel over bottom overview | Adjust the selected volume channel by five percentage points and show the volume overlay. |
| Alt+wheel over object timeline | Zoom that timeline independently. |
| Alt+wheel over Timing waveform | Zoom the waveform time scale. Ctrl+wheel on the waveform currently does nothing. |
| Ctrl+Alt+wheel over canvas or object timeline | Wheel down advances Select → Fruit → FSlider → Banana Shower; wheel up reverses this cycle. |
| Middle-button drag over canvas | Pan vertically; also pan horizontally when uniform scaling is unlocked. |
| Ctrl+click object / Ctrl+marquee | Toggle selection / add objects to selection; slider control actions take priority when applicable. |
| Shift+drag object in upper timeline | Move without beat snapping. |
| Double-click an object in the upper timeline | Select it and seek to its start without changing content. |
| Ctrl+click bottom overview | Toggle a bookmark at the clicked time; clicking near an existing bookmark removes it. |
| Shift+drag bottom overview | Create a break interval. Esc cancels. |
| Right-click a break in bottom overview | Remove that break. |
| Ctrl+click while drawing a slider | Add a straight segment. |
| Ctrl+click an existing slider control | Make it straight. |
| Ctrl+click within an editable slider's time range | Insert a control at the pointer; may change its shape. |
| Shift while adjusting a DS preset or Prev DS slider | Use 0.01 steps instead of 0.1. |
| Shift while dragging a Song Setup difficulty slider | Use 0.1 steps instead of whole units. |

Slider endpoint and timeline-tail drags accept ordinary wheel navigation, Ctrl+wheel Snap and Alt+wheel zoom; Space/C still plays or pauses. Release commits one edit; Esc cancels it.

Ctrl+Shift+wheel has no current canvas zoom binding. Alt+wheel over the canvas
changes zoom rather than DS presets. For right-click drafting, deletion and
double-click slider editing, see Editing Controls.

### Timing page and F6 dialog

| Context and keys | Action |
| --- | --- |
| Timing page: T | Register a tempo tap. Apply timing commits the measured BPM and offset. |
| Timing page: hold Ctrl during metronome playback | Use three ticks per beat for Snap divisors divisible by three, two for other even divisors, otherwise one. |
| Compose or Timing: Ctrl+P / Ctrl+Shift+P | Open the timing draft and add a red / green point at the playhead. |
| Compose or Timing: Ctrl+I | Delete the current timing section, protecting the first red point. |
| F6 list: Ctrl+A | Select all visible rows. |
| F6 list: Ctrl+click / Shift+click | Toggle rows / select a range. |
| F6 list: Up/Down, Page Up/Down, Home/End | Navigate rows, eight rows per page step, or first/last row. Shift extends selection; Ctrl toggles the destination row. |
| F6 list: Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste `.osu` timing-row text. |
| F6 list: Delete / Ctrl+I | Delete selected rows, protecting the first red point. |
| F6 list: Ctrl+P / Ctrl+Shift+P | Add a red / green point to the draft. |
| F6 list: Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Undo / redo / redo draft changes outside a text field. |
| F6: Tab | Focus the first numeric field; while editing, Tab / Shift+Tab commits and moves to the next / previous field. |
| F6: Enter | Commit the focused field; when no field is active, apply the dialog as one undo step. |
| F6: Esc | Cancel the focused field first; otherwise discard and close the dialog. |
| Timing command confirmation: Enter / Esc | Apply / cancel. |

When using BPM stepper buttons, normal/Ctrl/Shift steps are 1/0.25/5 BPM.
Offset steps are 2/1/10 ms. Ctrl takes precedence if both Ctrl and Shift are held.
Slider velocity is preserved on import but has no editable precision shortcut.

Playback, time navigation, bookmarks, speed, undo/redo and difficulty switching
remain available on the Timing page outside its fields. Use the visible Snap bar
or Shift+1–9 / Ctrl+M there. Ctrl+Shift+Left/Right is inactive in Timing; return
to Compose before moving selected objects.

### Testplay

| Keys | Action |
| --- | --- |
| Left / Right | Move catcher, by default. |
| Hold Shift | Dash, by default. |
| Tab | Toggle autoplay. |
| Hold `~` / backtick for 300 ms | Dim the screen, then retry once from this session's start, including its lead-in, with the pause-menu Retry click. Release early to cancel; available while running or paused. |
| Ctrl+R | Retry from this session's start including its lead-in, retaining autoplay and speed. One retry per press; available while running or paused. |
| F3 | During autoplay, toggle speed between 1.0x and 1.5x. |
| Ctrl+Up / Ctrl+Down | During autoplay, increase / decrease speed by 25 percentage points, within 10%–150%. Works while paused too. |
| Ctrl+Shift+Up / Ctrl+Shift+Down | During autoplay, increase / decrease speed by 5 percentage points. |
| Esc / Ctrl+P | Open the pause menu / resume the session. |
| Space | Skip the opening empty time while Skip is available; clicking Skip also works. |
| Up / Down, Enter (pause menu) | Select Continue, Retry or Back, then activate it. |
| Ctrl+B / Ctrl+Shift+B | Add a bookmark / remove the nearest within two seconds at the live position. |
| F1 | Exit and return to the selected testplay start position. |
| F2 | Exit at the current testplay position. |
| Alt+arrows | Select and adjust volume channels as above. |
| Up / Down (active testplay) | Open volume controls and change the selected channel by five percentage points. |

Combo visibility is configured in **Settings → Testplay keys** and has no shortcut.

Change movement and dash in **Settings → Testplay keys**: click a binding, press
the new key, then Apply. Esc cancels capture; reusing an assigned key swaps the
two assignments. Esc, Tab, F1–F3 and F12 are reserved. Supported keys include letters,
digits, arrows/navigation keys, modifiers, keypad keys, punctuation, F4–F11 and F13–F24.
OS/media keys are not supported. System shortcuts can intercept some combinations.
Prefer bindings that do not overlap Ctrl+P, Ctrl+B, Ctrl+R, Ctrl+Up/Down or Alt+arrows while held.

### Text fields, Library and other dialogs

| Context and keys | Action |
| --- | --- |
| Text: Ctrl+A, Ctrl+C/X/V | Select all, copy/cut selection, paste at the caret. |
| Text: Left/Right, Home/End | Move caret; hold Shift to extend selection. |
| Text: Backspace / Delete | Remove selection or the previous / next character. |
| Compose numeric field: Enter / Esc | Commit / cancel. Tab / Shift+Tab commits and cycles fields. |
| Prev DS or X input: Enter / Tab / Esc | Commit / commit / cancel the live adjustment. Ctrl+Z ends and undoes a changed preview. |
| Song Setup: Tab / Shift+Tab | Cycle editable fields on the active tab. |
| Song Setup | Valid dock fields apply immediately with project undo; there is no dialog-wide Apply/Cancel. |
| Library: Ctrl+F | Focus and select the search text. |
| Library: F5 | Rescan the library. |
| Library: Enter | Leave a text field; with no field focused, open the selected map. |
| Settings: Esc | Cancel key capture or field focus first, then close without applying pending drafts. |
| Export: Up / Down | Cycle export modes when no filename field is focused. |
| Export: Tab | Toggle filename focus for modes that accept a name. |
| Export: Enter | Leave filename editing first; with no field focused, submit the export. |
| Export: Esc | Close export choices. |
| Language dropdown in Settings | Up/Down chooses a language; Enter applies; Esc closes only the dropdown. Other background shortcuts are blocked while it is open. |
| DS configuration: Enter / Esc in base field | Leave base-field editing. Use Apply or Cancel for the dialog draft. |
| DS configuration: Esc outside base field | Cancel an active drag first; otherwise close the dialog. |
| Volume settings dialog: Esc | Close the dialog. |
| Error dialog: Enter / Esc | Dismiss the error. |
| Unsaved-change confirmation: Enter / Esc | Cancel the pending operation; does not choose Save or Discard. |
| Slider import/conversion panel: Esc | Decline import conversion, cancel a running conversion, or dismiss its errors, as applicable. |
| Updates page: Esc | Leave the page. |

Text fields do not currently implement word navigation/deletion or general text
undo/redo. Dialogs consume background editing keys. Some Compose page/file keys
are processed before its numeric-field handler, so explicitly commit the field
before invoking those commands.

Unlocking canvas Zoom shows two persistent toolbar sliders, X above Y, each up to 200%. Object drawing and hit regions are capped at 100%. See canvas scaling.

### References

- User manual
- Editing controls and mouse gestures
- Shortcut compatibility review and possible additions
- [osu!stable default shortcut reference](https://osu.ppy.sh/wiki/en/Client/Keyboard_shortcuts)
