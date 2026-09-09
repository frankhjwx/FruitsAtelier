# Editing Controls

## Workspace

The object list is on the left, the time–X canvas in the center, properties and Catch Preview on the right, and time navigation at the bottom. Playfield X spans `0..512`; time increases upward. Startup displays an editable demo beatmap.

Adjust AR and CS at the top right. The preview uses AR for falling speed. The main canvas initially follows beatmap AR; the demo defaults to AR 8. Click or drag the canvas's **Zoom** slider to change time spacing, shown as AR 0–10 independently of beatmap AR. **Restore AR scale** and **Reset view** restore the beatmap scale. Scroll to browse time, drag with the middle button to pan, and Ctrl+scroll to zoom around the pointer's time while updating the slider. When paused, slider zoom centers on the viewport; during playback, it preserves the play line.

During playback and seeking, the play line stays 25% above the bottom of the drawing area while content moves. Paused navigation is free; playback or seeking resumes following.

## Tools and selection

| Input | Action |
| --- | --- |
| V / F / B / N | Select / Fruit / FSlider / Banana shower |
| Click an object in V/F mode | Select the complete object; slider children belong to the parent slider |
| Drag empty space | Box-select objects in V/F mode or anchors in FSlider edit mode |
| Ctrl+click / Ctrl+box-select | Toggle selection / add to selection |
| Drag selected objects | Move the group by a shared X and time offset |
| Ctrl+X / C / V | Cut / copy / paste complete objects |
| Delete | Delete selected objects, or selected anchors in anchor-edit mode |
| Ctrl+Z / Y | Undo / redo |
| Esc | Cancel an active drag, box selection, draft, or numeric edit |

Mac accepts both Command and Ctrl shortcuts; Delete and Backspace both delete objects.

A slider's Fruit, Droplet, and TinyDroplet share parent selection. Selecting several children counts as one slider. Even with curves hidden, actual objects can select their complete track.

The clipboard is internal to the application. Paste aligns the earliest start to the playhead, preserves other objects' relative times and positions, and assigns new IDs. Each batch move, delete, cut, or paste is one undo step.

## Fruits and beats

The Fruit tool places fruits in empty space. Snapping offers 4, 5, 6, 7, 8, 9, 12, and 16 subdivisions per beat, plus free time, using the active red point's BPM and offset. Clicking empty canvas in Select mode to position the play line uses the current beat subdivision, as does the empty-space click that exits FSlider anchor editing. **Free** enables continuous positioning. Changing snapping does not move existing objects.

`Tick ×…` changes beatmap SliderTickRate and affects generated slider-object spacing independently of editing snap.

## FSliders

Press B with no track selected to start drawing. Click to place anchors without handles; hold and drag upward to pull direction handles. One track may mix straight and Bezier segments. Enter finishes; Esc cancels the draft. While editing an existing track, use **New Slider** in properties to start another.

Select an FSlider and press B, or double-click its track, to edit anchors. Clicking an anchor on an already selected complete track also enters editing. Drag anchors/handles or edit their numeric properties. Anchor dragging is free by default. Enable **Snap dragged anchors** in the FSlider properties to snap anchor times to the selected beat subdivision; this checkbox is independent of the general Free setting. Handles remain free, and the option does not change placement or whole-object snapping. Times remain increasing, and control-point X stays within the playfield.

Right-click an anchor to convert between curved and straight control points; right-click the track to insert a handle-free point. Ordinary insertion may change shape; the shape-preserving split action retains it. Batch deletion may include endpoints. Fewer than two remaining anchors deletes the complete track.

Span count applies to the entire FSlider; later traversals reuse the first span's nodes in alternating directions. Convert Legacy Sliders from properties or the context menu. Conversion preserves start time, total duration, and span count. It first fits a small set of straight/Bezier anchors within 0.25 playfield units, then relaxes TinyDroplet alignment or uses a linear approximation if needed to complete the conversion. Exact nested-object positions and sequences are not required to match. Invalid or unrepresentable input retains its original object with a reason.

The first workspace import from Songs, an external folder, `.osu`, or `.osz` asks whether to convert sliders in all newly imported difficulties if they contain Legacy Sliders. Keeping Legacy preserves that representation. Reopening an existing project does not prompt again. Importing one difficulty into a project prompts only for that difficulty.

**Edit → Convert all sliders to FSliders** processes the current difficulty and is disabled when it has no Legacy Sliders. Batch conversion runs in the background with a cancellable prompt and blocks content editing. Cancellation applies no partial results. Conversions per difficulty can be undone together; valid sliders that previously failed strict alignment now convert approximately. Invalid input and reasons appear in paginated results. Saving persists conversion in the workspace without modifying original Songs/external files; Export is still required to write `.osu`.

The generator handles FSlider TinyDroplet alignment. The Tiny alignment toggle only affects older project data without a saved per-track policy.

## Banana showers

With N, left-click to set the start, then right-click at a later time to finish. Esc or switching tools cancels. A banana shower appears as a time rectangle spanning the playfield. Drag its body to move it or its top/bottom handles to change start/end times; properties also accept numeric values. RNG generates individual banana X positions.

## Files and playback

| Input | Action |
| --- | --- |
| Ctrl+O | Open `.osz` / `.osu` / `.catchproj` |
| Ctrl+S / Ctrl+Shift+S | Save / Save As project |
| Ctrl+E | Export `.osu` |
| Space | Play / pause |
| Click, drag, or scroll the bottom timeline | Seek while preserving play/pause state |
| Home | Return to the start |

The File menu can replace MP3 / OGG / WAV audio. Manual seeking and editing remain available without playable audio. Save the editor project to retain editable data; further changes after `.osu` export still require a project save.

## Display settings

The top-bar language button switches English and Chinese. Existing beatmap titles and object names retain their values. The main canvas can hide curves and nodes, while the right-hand preview has a separate debug-curve toggle.

The skin picker imports Catch images and configuration from `.osk`. Missing skins or textures fall back to basic shapes; see [Skins](../assets/skins/README.md). Drawing and hit-test sizes are described in [Catch Rendering and Conversion](CATCH_RENDERING.md).

## Multiple difficulties

**File → New project** creates a project with one blank difficulty. Opening `.osz` loads all Catch (Mode=2) difficulties into one project and skips other modes. A damaged Catch file does not replace the current project. Opening a single `.osu` or older `.catchproj` creates a single-difficulty project.

A separate row below the main toolbar displays Chrome-style difficulty tabs with the official Catch icon, Version, live No Mod stars, and an unsaved dot. Icon color follows stars. Active tabs have rounded top corners and spread outward at the bottom to join the content below. Tabs use actual text widths rather than filling the row. Names longer than 16 Unicode characters show the first 16 plus an ellipsis; stored names remain complete. Click to switch; use arrows or the tab-row wheel when tabs overflow. Ctrl+Tab / Ctrl+Shift+Tab cycle and reveal the active tab. The **+** button opens the add/import menu. A new blank difficulty inherits the active difficulty's audio, timing, settings, and resource context but clears objects. Importing an `.osu` adds one file. Difficulties may reference different audio.

Switching commits valid pending edits first; unfinished banana drafts or invalid input prevent switching. It pauses playback and retains each difficulty's playhead, time-viewport start, and undo/redo history. Selection and the active tool reset. Title/status dirty indicators cover the whole project, including hidden difficulties. One save writes every difficulty and updates baselines without clearing undo history. Unsaved confirmation on new/open/close applies to the whole project.

`.osu` export applies to the active difficulty; suggested filenames include its name. Workspace project saving is described in [Workspace](WORKSPACE.md), and the compatible `.catchproj` format in [Project Model](PROJECT_MODEL.md). Resource paths remain references rather than embedded project-file contents.

See [Catch Star Rating](CATCH_DIFFICULTY.md) for calculation, cache invalidation, and export/website-version limits.

## Library and workspace

**Library** opens a separate page for workspace/stable Songs settings, bilingual metadata search, difficulty browsing, and project opening. Editor difficulty tabs retain their layout. Missing resource references show an error bar. Saving never writes to Songs; Export offers a standalone `.osu` save dialog, associated-difficulty overwrite, or a new difficulty in Songs. See [Workspace](WORKSPACE.md).

Below the Catch Preview title, one line shows `AR … · CS … · NM`. It omits fall time, generation status, and skin name. Preview scrolling and object drawing still follow AR/CS.

A new project's blank difficulty starts unmodified, so directly opening or importing an external beatmap does not trigger an unsaved prompt. Content edits, audio binding, and added/imported difficulties do prompt. Undoing to the initial blank state clears the dirty marker.

The bottom status bar shows current action feedback, such as save results or operation limits, with conversion errors taking priority. It is not a log viewer. Platform details, internal zoom percentages, and duplicate dirty indicators are omitted.

FSlider properties expose **Reverses** in both whole-slider and anchor editing: 0 plays the path once, 1 returns once, and higher counts continue alternating. The slider context menu offers **Add reverse** and **Remove reverse**. With a completed FSlider selected, right-click empty canvas at a time after its final end and choose **Extend slider to here**. A new anchor is placed at the clicked position using the placement snap setting, with a straight segment from the base path endpoint. Existing segments remain unchanged. Extending a repeated slider lengthens its base path for every span; it does not append after the repeats. Each operation is undoable.
