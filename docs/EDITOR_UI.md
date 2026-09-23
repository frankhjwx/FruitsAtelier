# Editing Controls

## Workspace

The time–X canvas occupies the main area, read-only AR/CS/DPB are at the upper right, and time navigation is at the bottom. Select objects directly on the canvas. Playfield X spans `0..512`; time increases upward. Startup opens the Library without loading a demo beatmap. Open a beatmap set to enter the editor. The window title identifies the active difficulty as `Artist - Title (Mapper) [Diffname]`; the menu row does not repeat the project title. The compact **← Library** button at the top right returns to the library. Esc first dismisses an active menu, field, dialog, or gesture; otherwise it requests a return to the library. Unsaved changes prompt for Save, Discard, or Cancel before closing the editor; Cancel or a failed save keeps the editor open. See [Workspace](WORKSPACE.md) for navigation and position memory.

The Details header shows read-only beatmap AR, CS, and Distance Per Beat (DPB) in pixels. Catch Preview starts collapsed; the small button at the center of the canvas’s right edge opens it at the upper right. Drag the sidebar’s left boundary to resize it, and use the edge button to close it. NM, Easy and Hard Rock select preview-only difficulty and position rules; see [Catch rendering](CATCH_RENDERING.md). The preview uses its effective AR for falling speed. The main canvas uses the beatmap's AR timing ratio; its **Zoom** slider changes the displayed width of X=0..512 and scales object sizes and time spacing together. Zooming out shows more notes vertically without changing their coordinates, map AR, or CS. The playfield stays horizontally centered, from a minimum **256 DIP** wide to the full available width with CS0 edge padding. Percentages are relative to that available width; resizing preserves the zoom percentage except when the minimum width requires clamping. **Zoom defaults to 60%. View → Reset view** restores 60% and follows the playhead. Each wheel notch moves one full beat (1/1) during playback or one current Snap subdivision while paused: up to the preceding grid line and down to the following one, independent of zoom. Canvas scrolling shifts the viewport and playhead by the same relative amount, even while paused; each clamps at its bounds. Off-grid positions move to the adjacent grid line in the scroll direction, and BPM changes use the grid on the corresponding side of the timing boundary. High-resolution wheel input accumulates until it reaches one notch. Drag with the middle button to pan, and Ctrl+scroll to scale around the pointer's time. When paused, slider zoom preserves the viewport's center time; during playback, it preserves the play line.

`Catch Preview` offers 4:3, 16:9 and Fit display modes. Fit uses the entire available sidebar height, revealing more future notes as the window grows vertically. Drag the sidebar divider to adjust width. Objects retain their proportions, and an automatic catcher follows playback and seeking. Mode and Resolution controls stay above the picture; 4:3 and 16:9 pictures are centred in the remaining area. Caught fruit remains on the plate. Completing a combo group scatters the stack; seeking restores the plate effects.

During playback and seeking, the play line stays at its configured height, initially 25% above the bottom of the drawing area. While paused, drag its leftmost yellow handle vertically to set that height between 5% and 95% of the drawing area. The current time stays unchanged and canvas content scrolls with the line. Playback disables handle dragging and uses the chosen height. The height is saved as a global view preference when the drag finishes and restored after restarting the editor. It is retained across resizing and difficulty switches without editing map content or undo history. Esc or lost capture cancels a drag. Left-button marquee selection on the canvas and object timeline keeps playback scrolling and accepts wheel navigation while held. The start stays anchored to its original map time while the other end follows the pointer, so the box grows during scrolling even with a stationary pointer. Within 24 DIP of the canvas's top/bottom edge or the object timeline's left/right edge, a dragged selection automatically scrolls toward that edge, gradually increasing to 1200 DIP per second on the canvas or 600 DIP per second on the object timeline. Moving back inside, releasing the button, cancelling, or reaching the map boundary stops automatic scrolling. Objects inside the time range remain selected after they move outside the viewport. Paused middle-button panning is free; canvas wheel navigation preserves the current playhead-to-viewport offset, while playback and other seeking resume following.

## Song Setup

Editor text fields share one caret and selection model, including Song Setup,
Library search, settings paths, export names, time jump and numeric inputs.
Clicking places a blinking caret without selecting text; dragging selects a range,
and double-clicking selects the entire field. Ctrl+A (Command+A on macOS) also
selects all. Left/Right, Home and End move the caret; Shift extends the selection.
Backspace and Delete remove the selected range or one adjacent character.
Copy, cut and paste operate on the selected range and caret position. Keyboard
focus changes can request select-all explicitly, such as Tab between Song Setup
fields.

The **Song Setup** button immediately left of Skin opens a modal with General,
Difficulty, Colors and Design tabs. **OK** applies the draft in one undo step;
Cancel or Esc discards it. Playback pauses when opening the dialog. Editor
shortcuts, seeking and object input are blocked until it closes.

General edits artist, title, creator, source and tags across every difficulty in
the project. Non-ASCII artist/title text enables a separate romanised field;
ASCII text supplies both forms; the romanised value appears as unframed,
read-only text until a separate spelling is needed. Difficulty Name belongs only
to the current difficulty. Undo/redo in the difficulty where the change was made
also restores the affected shared fields in the other difficulties, retaining
their independent object histories. Unedited fields and source settings are
preserved.

Difficulty edits the current difficulty's HP, CS, AR and OD from 0 to 10. Each
slider has fine integer tick marks without labels. Drag in whole steps, hold
Shift for 0.1 steps, or type a decimal value. Colors
edits that difficulty's custom combo palette using a saturation/value palette,
hue strip and six-digit HEX input. Add up to eight colors, select a swatch to edit
it, or remove a selected color while retaining at least one. Disabling custom
colors removes the beatmap combo overrides, allowing the normal skin fallback.
The canvas, preview and testplay use the confirmed palette.

Design selects countdown speed (off, normal, half or double) from a dropdown and
stores a non-negative countdown offset in beats. Switches control widescreen
storyboard support, letterboxing in breaks and the flashing-light warning. These
options are preserved in project saves and
`.osu` exports; the editor does not render countdown or storyboard effects.
Audio, Advanced and preferred-skin controls are not part of Song Setup.

## Settings

The top-bar **Settings** button is available in both Library and Editor. Settings uses a left category sidebar and a right panel for Workspace, Appearance, Testplay keys, and Updates (when supported by the host). Switching categories retains pending path and key changes. **Apply** is enabled only while unapplied changes exist. It saves them, stays in the current settings category, and becomes disabled again; the top-right return button or Esc closes settings without applying those drafts. Esc first dismisses active text or key capture. Update preferences save immediately. Opening settings pauses playback and retains the editor document, undo history, selection, and viewport. **Appearance → Romanised artist / title** defaults to On and controls Library cards, Library details, and the editor window title. Off prefers the Unicode metadata; either mode falls back to the other spelling when its preferred field is empty. Apply persists the preference without changing beatmap data, filenames, or search matching.

**Appearance → Movement indicator colours** provides separate Stand, Walk, Dash, and HDash swatches. Click a swatch to drag within a saturation/value palette and hue bar, or enter an exact six-digit HEX value, as in Song Setup → Colors. These preferences colour Movement Analysis connections, the floating movement indicator, and Distance Snap reference regions; they do not change skin rendering or beatmap content. **Reset colours** restores the original silver, green, amber, and rose drafts. **Apply** saves the colours across launches; leaving Settings before applying discards draft changes.

## Testplay

Click **Testplay (F5)** in the transport bar or press **F5** to play from the current
playhead. Finish any active object draft or text input first. Testplay uses the
preview's NM/Easy/Hard Rock selection and the selected playback speed. With no
audio loaded, a silent clock drives the notes. Earlier notes are skipped and combo
starts at zero. A gap before future notes does not end the session.

Use **Left / Right** to move and hold **Shift** to dash. Catching a hyperdash fruit
enables its speed boost. Fruits and droplets increase combo; missing either resets
it. Tiny droplets and bananas do not affect combo. The combo number follows the
catcher, and its skin image faces the last movement direction in both preview and
testplay. Dash and hyperdash leave fading catcher trails. The combo uses the skin's
combo digits, pulses on catches and fades while idle or after a miss. Missed notes
fall past the catcher and fade out over 250 ms.

The upper-left corner shows **Tab** (autoplay), **Ctrl+P** (pause/resume), **Ctrl+B** (add a bookmark), **F1** (exit to the testplay start), and **F2** (exit at the current position). Pausing freezes gameplay and music; resuming continues the same session.

Press **Tab** during testplay to toggle autoplay. Press it again to resume manual
movement at the current time and position. Each new testplay starts in manual mode.
The right-side preview and testplay animate fruit rotation and banana rotation/size;
the main editing canvas remains static. Fruit bases use beatmap combo colours when
present, otherwise skin colours; overlays stay white.

Caught fruit stacks on the catcher using the preview effects and releases at combo ends.
The last remaining note (after miss and plate animations) or the end of the music returns to the editor. **Esc** or losing window focus also exits. Playback stops and the playhead
returns to the position where testplay began. Gameplay does not change hit objects or selection; bookmark shortcuts update metadata through undo history. The playfield fits the full window while preserving its
aspect ratio; it reserves no space for navigation controls. Release Esc before
pressing it again to navigate from the editor to Library.

In **Library → Settings**, click the left, right or dash binding and press a supported key. See the [user manual](USER_MANUAL.md#testplay) for supported keys and reserved shortcuts. Esc cancels capture; choosing an already assigned
key swaps the two bindings. **Apply** saves the bindings across restarts.

## Object timeline and playback speed

The bottom overview shows red and green timing points above a white center line, continuous yellow kiai intervals and white break intervals centered on that line, and blue bookmarks extending down from it. The center line is behind the break and kiai intervals, which are behind timing points and bookmarks. These timeline marks use 80% opacity and colors tuned against osu!legacy. Hovering over the overview reveals a fixed bookmark toolbar above its left edge with Add, Remove, Previous, Next, and Reset actions. The toolbar stays visible while moving from the overview to its controls, and its tooltip appears above it. The toolbar uses an ImageGen-created background texture. The time display and separate Play, Pause, Stop, and Testplay controls sit to the left of the overview; Stop pauses audio and seeks to the start. Ctrl+B adds a bookmark at the playhead; Ctrl+Shift+B removes the nearest bookmark within two seconds. Ctrl+Left/Right seeks to the previous/next bookmark; Ctrl+Shift+Left/Right moves selected objects one X unit. Ctrl-click adds or removes a bookmark at the clicked time; clicking within five pixels of an existing bookmark removes it. Shift-drag across the overview adds a break interval, and right-click inside a break removes it. These edits are undoable and persist in the `.osu` `[Editor] Bookmarks` and `[Events]` sections. Esc cancels an in-progress break drag.

The overview draws `[General] PreviewTime` as a full-height yellow marker when it is nonnegative. Red and green timing points meet the center line, and bookmark markers start on it. The horizontal object timeline also shows red and green timing points. Break intervals are clipped to each visible range and shaded across the full height or width of the object timeline and canvas left time axis. The Break label appears only on the object timeline; the canvas left axis shows break bands without timing or bookmark markers. The kiai fill uses a lighter orange. The Timing menu sets the preview point at the rounded playhead time through undo history. Insert Break Time sits between Movement Analysis and Snap; it inserts an undoable interval between the surrounding source objects, starting 200 ms after the previous object ends and ending when the next object's AR approach begins, if at least 400 ms remains and no break overlaps it. The Snap slider remains on one row. The time display uses a fixed position for the duration so changing digits do not move it. The bookmark toolbar is left aligned and vertically centered in the strip above the overview; its buttons are inset from the panel edge and remain visible while the pointer is held over the overview.

During a kiai interval, a small badge appears at the upper-left of the editing plot. It brightens at the interval start and on every full beat from the active red timing point, then fades through the beat. Green timing points that preserve the kiai state do not restart the pulse. Its pulse follows map time, including seeking and timing edits.

In the object timeline and canvas left time axis, each break has a grey core and lighter white and green transition regions extending to the adjacent source objects, without changing the stored break timestamps. Hovering a core edge in the object timeline shows a horizontal resize cursor. Dragging that edge snaps to the current beat subdivision when Snap is on and previews the new range; releasing commits one undoable edit, releasing with less than 400 ms remaining removes the break, and Esc cancels the preview.

Fruit and slider shapes on the upper object timeline use the same beatmap or skin combo colour as the preview throughout each New Combo group. Banana showers remain gold. Unselected heads, tails, and reverse boundaries have white rings. Object bodies draw before those rings so overlapping objects retain their outlines and reverse arrows. Selected objects draw above the stack with their combo fill, orange rings, and blue outer rings. Timing point markers use the same time-to-X coordinate as note centers. Insert Break Time uses the same text size as Movement Analysis; the Details header places AR, CS, and DPB in fixed columns on one baseline.

In testplay, Ctrl+B adds a bookmark at the live testplay time and Ctrl+Shift+B removes the nearest bookmark within two seconds. These commands enter undo history and consume the B key before gameplay input.

Newly opened maps start at 0 ms from their first frame. While audio loads, the total duration displays a placeholder and the overview waits for the final range before becoming interactive. If audio is unavailable, the overview uses the map duration. Switching back to an open difficulty retains its playhead and resumes the audio at that position when loading completes.

Library and Editor use the same 40-DIP header, logo geometry, 28-DIP button height, and navigation button positions. Difficulty tabs remain below the Editor header.

The row beneath Zoom spans both the left tools and canvas columns. It is a horizontal object timeline centered on the current playhead, with centered object numbers that restart at 1 on New Combo. Circles show source objects in time order; capsules show complete slider and banana-shower durations. The ruler and canvas use the current beat subdivision with osu! beat-snap colors: white for full beats, red for halves, purple for thirds/sixths, blue for quarters, yellow for fifths/sevenths/eighths/ninths, and grey for finer subdivisions. Classification uses the reduced fraction, so a half-beat stays red on a 1/12 grid. Full beats, halves and thirds have thicker lines; measure starts are strongest and follow the active red timing point’s meter and offset, including meter changes. See the [osu! beat snap divisor reference](https://osu.ppy.sh/wiki/en/Client/Beatmap_editor/Beat_snap_divisor). Slider repeat boundaries show a circle with a right-facing reverse arrow; the head shows its combo number and the final tail stays empty. The arrow uses reversearrow.png from the current skin (preferring @2x), with a geometric fallback when unavailable. Click an object or duration body to select its parent without moving the playhead; Ctrl/Shift-click toggles selection. Drag an object head or duration body to move the selected parents in time, preserving their X positions and relative timing. Beat snapping applies to the earliest selected start; release commits one undo step and Esc cancels. Click empty space in the object timeline to clear selection without moving the playhead. Drag empty space in the note row to box-select parent objects; Ctrl/Shift adds to the selection. Right-click a note to delete it, or delete the selected group if that note is already selected. Esc cancels a box selection and deletion is undoable. Scroll up steps to earlier times and scroll down to later times, using full beats during playback and the current Snap subdivision while paused; Ctrl+scroll or the +/− buttons changes this timeline's scale independently of canvas Zoom. Finish an active drawing draft before selecting or dragging in this row.

While editor playback is running, a single click on empty canvas space leaves playback time unchanged. Paused Select-mode clicks still seek to the snapped time.

The playback button and timestamp block are vertically centred in the transport bar. Click the current timestamp to open Jump to time with its value selected. Copy/Paste buttons and Ctrl+C/Ctrl+V (Cmd on macOS) use the system clipboard; Ctrl+A selects the full input. Enter or Jump seeks without beat snapping, and Escape cancels. Inputs accept `mm:ss:ms`, milliseconds, or an osu! timestamp reference such as `03:03:311 (2,3) -`. Invalid input keeps the dialog open; times beyond the track clamp to its end. The dialog blocks background editing and does not change beatmap content.

The transport offers **10%, 25%, 50%, 75%, 100%, and 150%** playback speed. Only song tempo changes, with pitch preserved. Hitsounds keep their original pitch and real-time duration, with trigger times mapped to the music clock. Changing speed preserves the map position and play/pause state. This setting does not edit or export beatmap timing.

In the editor, **View → Volume** opens a compact dialog with All, Song and Hitsound sliders from 0% to 100%. The dialog leaves playback running and closes with its Close button or Esc. Changes apply immediately and persist when the slider is released. All multiplies both channels; Song and Hitsound independently control music and all preview/testplay samples. Muting does not pause playback or change the beatmap.

## Tools and selection

The left palette has equally sized Select, Fruit, FSlider, and Banana buttons with transparent outer margins. The four-button group is vertically centred beside the canvas plot. The active icon is fully opaque; the other three use 45% opacity. Labels share one font size. Clicking FSlider starts placement; B also enters control editing for a selected slider. Finishing placement keeps the current tool active.

Snap offers 1/1, 1/2, 1/3, 1/4, 1/5, 1/6, 1/7, 1/8, 1/9, 1/12 and 1/16; the default is 1/4.

Fruit and FSlider placement display a 60%-opaque fruit under the pointer, with its time snapped to the current beat subdivision. Hover previews recalculate incoming and outgoing hyperdash markers, including the unconfirmed slider endpoint in both editing modes. This temporary calculation affects canvas markers only; it does not add playback sounds, change saved content, or enter undo history. Fruit left-click places immediately, including over an existing object. In Fruit mode, right-click empty canvas toggles **New combo** for the next fruit. During playback, right-click arms/toggles New combo even over a note; when paused, right-click on a note deletes it. The combo flag survives project saving, `.osu` export, copying, and undo/redo. It resets after placement or changing difficulty.

| Input | Action |
| --- | --- |
| 1 / 2 / 3 / 4 (also F / B / N for placement) | Select / Fruit / FSlider / Banana shower |
| Click an object in Select mode | Select the complete object; slider children belong to the parent slider |
| Drag empty space | Box-select objects in Select mode or anchors in FSlider edit mode |
| Ctrl+click / Ctrl+box-select | Toggle object selection / add to box selection; selected-slider point actions take priority |
| Drag selected objects | Move the group by a shared X and time offset |
| Ctrl+X / C / V | Cut / copy / paste complete objects |
| Delete | Delete selected objects, or selected anchors in anchor-edit mode |
| Ctrl+Z / Y | Undo / redo |
| Right-click a note / edited point | Delete an object; a straight point becomes curved, a curved point is deleted; no context menu |
| Ctrl+L | Toggle the selected point between straight and curved |
| Ctrl+I | Insert a control point on the curve under the pointer |
| Ctrl+D | Clone selected parents one measure after the last selected start |
| Ctrl+A | Select all objects |
| Ctrl+H | Flip selected parents horizontally around X=256 |
| Ctrl+Left / Right | Seek to the previous / next bookmark |
| Ctrl+Shift+Left / Right | Move selected parents by one X unit |
| J / K | Move selected parents backward / forward by one beat subdivision |
| Ctrl+1 / 2 / 3 / 4 | Set grid size to 4 / 8 / 16 / 32 |
| Shift+1…9 / Ctrl+M | Set beat subdivision directly / cycle subdivisions |
| C / Space / X | Pause or resume / pause or resume / play from song start |
| Z / V (also End) | Jump to the first object's start / last object's end; repeat to reach song start / end |
| Left / Right (Shift for 4×) | Seek backward / forward by one beat subdivision |
| Up / Down | Seek previous / next timing point |
| Ctrl+Up / Down | Select the next faster / slower playback speed, within 10%–150% |
| Ctrl+Shift+F | Open slider-to-stream snap confirmation |
| Ctrl+= / Ctrl+− | Add / remove one reverse |
| Ctrl+J | Extend the selected FSlider to the pointer |
| Esc | Cancel an active drag, box selection, draft, or text input |

Mac accepts both Command and Ctrl shortcuts; Delete and Backspace both delete objects.

A slider's Fruit, Droplet, and TinyDroplet share parent selection. Selecting several children counts as one slider. Even with curves hidden, actual objects can select their complete track.

Copy and cut write a legacy reference such as `02:27:094 (1,2,3) - ` to the system clipboard. Inside the editor, a separate snapshot retains complete objects for pattern pasting into the same difficulty session. Other difficulties and reopened projects cannot receive that pattern. Paste aligns the earliest start to the playhead, preserves other objects' relative times and positions, and assigns new IDs. Each batch move, delete, cut, or paste is one undo step.

## Distance spacing and object flags

The right toolbar contains New Combo (Q), Whistle (W), Finish (E), Clap (R), Grid Snap (T), Distance Snap (Y), and Lock Notes (L). The seven square buttons use generated icons with the same charcoal fill, mint outline and 100%/45% active/inactive opacity as the left palette. In short windows, scroll over the right palette to reach all buttons. Lit buttons indicate enabled values; a dash indicates mixed hitsound values in a selection. Hover a button for its shortcut or the targeted slider edge. Hover Distance Snap to reveal **Configure DS…** on its left, using the same hover-to-flyout interaction as FSlider.

Y toggles distance snapping; holding Alt temporarily inverts that toggle. The upper-right Snap slider always controls beat subdivision, including while Alt is held. Grid Snap applies horizontal grid rounding after distance snapping; holding Shift temporarily inverts Grid Snap unless Alt is held.

Distance snapping places fruits and slider draft points relative to the previous source object, and moves a selected group by a shared offset based on its earliest object. Beat snapping still determines time. All DS calculations use `DPB / beatLength` as the 1.0x reference speed (green timing speed fixed at 1.0x), ignoring inherited slider speed. Horizontal DS is `abs(deltaX) / (deltaTime × referenceSpeed)`. Snapping and readouts sample this speed at the departure endpoint's time; slider references use their final endpoint after all spans. Cross-timing gaps retain that departure speed. BPM and the difficulty's DPB still determine the reference speed. Banana showers do not supply horizontal references. Overlapping or simultaneous references have no positive spacing interval. Distance snapping chooses the closest valid X among both sides of every configured multiplier and an implicit **0 DS** (the reference endpoint X). Zero DS is always available when snapping is enabled and does not consume a configuration slot. An empty configuration uses only zero DS.

**Configure DS…** opens a two-column modal. The left side has an upper 1.0x DPB section and a lower four-colour Stand → Walk → Dash → HDash distance reference and an initially empty collection of up to eight non-negative DS multipliers. **Add**, beside HDash, creates an arrow at 0 DS. A left-click on empty space in the arrow row creates an arrow at the clicked distance, rounded to 0.1 steps or 0.01 with Shift. Both respect the eight-preset limit; clicking an existing arrow starts its drag. Drag an arrow to adjust its value in 0.1 steps, or 0.01 while Shift is held. Right-click an arrow to remove it. Esc or lost capture cancels the active drag. Each arrow shows its DS value above it; nearby labels stack to stay readable. The values below the reference are read-only and displayed from smallest to largest in outlined badges, wrapping when needed. Each badge border uses the colour of its current Stand, Walk, Dash or HDash interval. All configured multipliers participate simultaneously, along with implicit 0 DS. **Apply** stores the sorted list and base DPB on the current difficulty as one undoable change; saving the project retains it in the difficulty file. New and previously unconfigured maps start empty. Switching difficulty restores its own list. **Cancel** discards the draft. The stored DistanceSpacing value is retained for project and osu! file compatibility but does not provide a hidden snap target. Custom DS lists are not exported to osu! files.

The upper-left section sets the 1.0x Distance Per Beat (DPB) from 32 px up to the distance at the end of the four-colour reference bar. New difficulties default to 192 px with SliderMultiplier 1.92; imported difficulties initially show `100 ×` their stored [Difficulty] SliderMultiplier. Changing DPB affects the editor's distance snapping and readouts while retaining the stored SliderMultiplier and existing slider playback. The DPB override is saved in `.catchproj` and is not exported to `.osu`. Drag the Stand/Walk/Dash/HDash bar to choose a whole-pixel value. The bar uses the same four equal Stand/Walk/Dash/HDash regions as the preset reference below; its 1.0x marker and drag positions use the same movement distance limits at the selected beat subdivision. The upper limit follows the selected reference Snap and timing, so all four movement regions remain reachable. A separate Grid Snap switch row and the 4/8/16/32 px level row below the numeric field control only DPB drag rounding; they start from the editor grid settings on first opening and then remain independent for the editor session. The level row is dimmed and unavailable while this Grid Snap switch is off. With Grid Snap enabled here, dragging moves in the selected step; the numeric field accepts any value in range, including decimals. The base distance is independent of BPM, while its speed in milliseconds follows the active red timing point. Existing preset multipliers rescale with draft DPB changes so their pixel distances stay fixed; the preview uses the draft DPB immediately. Apply stores the base distance and rescaled presets together as one undoable difficulty edit; Cancel leaves both unchanged.

The right side is a fixed four-beat canvas: Snap 1/4 shows 16 intervals and 17 horizontal lines, including the white closing line at the end of the fourth beat, which also accepts fruit placement. Time increases upward. Left-click to place fruits with beat and distance snapping relative to the preceding preview fruit; right-click a fruit to remove it. A hollow marker shows the next placement. Each consecutive pair shows its actual DS in a translucent black label centred on the connection midpoint, including zero. **Show DS**, beside Reset, toggles all preview DS labels while retaining fruits, connections and skin-coloured hyperdash starts. It defaults to off and retains its choice while the editor remains open. Its border matches the DS interval colour used by the preset badges; simultaneous fruits show a dash because their DS is undefined. A DS label is red when its value is no longer among the available presets (including implicit zero). A fruit that starts a hyperdash uses the skin HyperDashFruit colour. Classification uses the complete preview sequence and includes the prospective placement, updating when fruits are added or removed. This canvas does not pan or scroll, and it does not create sliders. Preview fruits are temporary and never become beatmap content, even when applying the DS configuration. The preview Snap slider starts at the editor's current divisor and offers the same fixed choices: 1, 2, 3, 4, 5, 6, 7, 8, 9, 12 and 16. It updates the reference and grid without changing editor Snap or existing preview fruit beat positions. Esc or lost capture cancels an active Snap drag. BPM is taken from the playhead at dialog entry and has no separate control. **Reset**, beside the preview Snap slider, clears all placed preview fruits without changing presets or Snap. Reopening resets the preview.

Each reference interval occupies one quarter of the track. Transitions use the entry BPM and preview Snap, document CS and base slider velocity, assuming a fresh departure from the preceding note centre without prior movement carry-over. Vertical ticks beneath segment boundaries show transition DS. Shorter ticks from the Stand, Walk and Dash segment centres show the midpoint of their physically available horizontal-distance intervals, capped at 512. HDash has no midpoint tick. The active arrow and its value are highlighted. Values beyond the reference range appear at its right edge. A segment with no interval inside that range shows a dash. The reference uses standard Catch movement timing, including the hyperdash quarter-frame allowance; actual movement classification retains full-sequence context.

**Prev / Next** shows horizontal DS with two decimal places in the floating movement panel. DS divides horizontal distance by the time interval multiplied by `DPB / beatLength` at the departure object's time; inherited green-point speed is ignored. Clicking a fruit, slider head/tail, or FSlider droplet targets that specific converted object and its adjacent Fruit/Droplet neighbours. Selecting a slider as a whole uses its head for Prev and final tail for Next; placement previews use the snapped candidate. With no selection or placement preview, the readout is hidden. Readouts update during dragging and do not depend on zoom or window size. Missing or zero-duration intervals show a dash.

With a single fruit or slider child selected and a valid preceding neighbour, click the movement or DS area of the floating panel to edit **Prev DS**. A range slider appears on the left and a numeric input on the right; Next remains informational. Numeric entry previews changes immediately and accepts up to two decimal places. The slider previews in 0.1 steps, or 0.01 while holding Shift, and its range extends from zero to the furthest position on the original side within X=0..512. Initial and slider-updated numbers display two decimals. Enter or clicking outside commits the whole adjustment as one undo step; Esc or interrupted pointer capture cancels it. Invalid input retains the last valid preview and must be corrected or cancelled. The selected object's time stays fixed and its X stays on the original side of the reference, including after previewing zero. An out-of-field result is rejected rather than clamped or flipped. Horizontally aligned objects cannot establish a new direction through a nonzero DS input. Lock Notes disables editing.

The bottom row displays a compact readout such as **X: 185** for the selected fruit or slider child, or the placement preview. Clicking anywhere on this row opens the numeric input for a selected object, including the first object without a preceding neighbour. Confirming or cancelling restores the readout. Row clicks do not reach the canvas or seek time, including when editing is disabled. X uses no slider and clamps numeric input to `0..512`. It displays rounded whole numbers, accepts integer input only, previews immediately while preserving time, and uses the same confirmation, cancellation, undo, and Lock Notes behavior as DS editing. Slider children reuse or insert a first-span anchor; imported sliders become editable FSliders in the same transaction.

Clicking an FSlider droplet or tiny droplet first selects its parent slider. Clicking that child again selects the specific event and shows a bright outer ring above curves and movement lines. In Select mode, a click closer to the droplet than nearby controls selects the droplet; an already selected droplet remains draggable after its curve gains an anchor. Horizontal dragging changes only that event's X and recalculates the curve while preserving the time and positions of every other converted event. The curve between the adjacent event samples becomes linear, replacing any intervening anchors and handles while retaining the curve outside that interval. TinyDroplet sampling follows the slider's compensation policy. Unreachable positions clamp to the last valid X. One drag is one undoable edit. Repeated spans share their curve geometry, so a drag that would move another event is rejected. Legacy Slider children cannot be selected individually; convert the parent to an FSlider first, then use the converted positions as the drag baseline. The ring follows live edits and is visible with Movement Analysis on or off.

For selected slider children, DS editing inserts or reuses an anchor at the child's first-span time and reshapes the curve locally. A reference in the same slider is anchored too so its position remains fixed. Editing an imported Legacy Slider head or tail can convert it to an FSlider within the same transaction. Its droplets and tiny droplets require prior conversion. Repeated spans share the edited first-span geometry. Conversion must retain the selected child's time and achieve the requested position; unsupported or unreachable changes show an error and roll back.

A compact movement panel uses a consistent font size, aligned left/right readouts, and a dark translucent background. It floats over the bottom centre of the editing playfield without resizing the canvas. Its movement and DS area opens Prev DS editing when a valid single object is selected. Fruit/slider placement and single-object selection show the incoming Stand, Walk, Dash, or HDash connection, using the Appearance indicator colours, with the outgoing state as a secondary label. The pointer measures horizontal distance on a linear scale; the right edge represents 1.5 times the larger of the current Dash threshold and Stand range and larger distances saturate there. Stand checks each connection independently: the target must be within the catching half-width of the departure centre, including the boundary, using current document CS. It takes precedence over movement requirements; it does not infer a shared standing position for a pattern. Outside that range, HDash follows the complete converted Fruit/Droplet sequence and current document CS. Walk estimates travel from the departure centre to the target catching edge, capped at the HDash boundary; it is not a guarantee for arbitrary catcher positions. TinyDroplets and Bananas do not supply connections. Missing or simultaneous connections show a dash. Playback speed, zoom, and Distance Spacing do not change classification.

**Slider Path** toggles slider path visibility. Its label stays fixed and the button highlights while enabled, matching the Movement Analysis toggle. The View menu exposes the same checked toggle.

**Movement Analysis**, beside **Slider Path** in the canvas toolbar, toggles 4-DIP coloured connections above curves and behind objects on the editing canvas. The button highlights when enabled; the same toggle is also available under **View → Movement Analysis**. It is off by default and is a session display setting. Each consecutive Fruit/Droplet pair uses the same Stand, Walk, Dash, and HDash classification, using the Appearance indicator colours as the floating panel. Connections follow placement previews and content edits, retain full-sequence movement context across viewport edges, and skip simultaneous pairs and TinyDroplets. Banana shower, kiai, and break intervals suppress entire connections and their DS labels whenever they overlap the pair, including links between fruits on opposite sides of an interval. Each Stand connection assumes its own departure-centre position; a sequence of Stand connections does not imply one shared standing position. Toggling the mode does not edit content or enter undo history. DS labels beside the connections use the same base-DPB formula as the floating panel. Each label has one fixed position to the right of the full connection midpoint, independent of viewport clipping. Intervals of 37.5 ms or less (a 1/8 beat at 200 BPM) omit DS labels. Longer intervals show labels wherever the fixed position fits inside the viewport without overlapping banana showers, the floating panel, or other labels. Fruit/droplet sprite bounds do not suppress labels because their transparent padding and glow overstate the occupied area. Labels do not move to alternate positions.

In Select mode, New Combo toggles the selected parents. The sound buttons toggle additions independently and support mixed multi-selection. Clicking a slider fruit targets that head, repeat or tail's hitsound; selecting the whole slider through its timeline body targets every edge. Droplet/tiny selections target the parent, whose edge sounds are editable; these children retain their existing tick/silent playback rules. Banana showers use their fixed banana sound and disable the three additions. In Fruit placement mode, or with no selection, buttons set pending flags for new objects. New Combo resets after placement; pending additions remain until changed or switching difficulty.

Lock Notes prevents moving, reshaping or deleting existing objects, including timeline reverse edits. Selection, playback, New Combo and sound editing remain available. New objects can still be placed; undo/redo remains available. Grid Snap, Distance Snap and Lock Notes are session switches; changing them alone does not dirty the difficulty.

## Fruits and beats

The toolbar above the object timeline groups Zoom, curve visibility and beat Snap, with Snap at the right. Beat snapping offers 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, and 16 subdivisions using the active red point's BPM and offset. Double-clicking a fruit shows its exact time and beat position, and temporarily selects a matching Snap subdivision while that fruit remains selected. Nearby fruits within half a beat can establish a finer shared subdivision. Positions more than 1 ms from every available subdivision show as off-grid without changing Snap. Changing Snap manually keeps the chosen value. Horizontal dragging preserves the fruit's original time. In Select mode, a single click on empty canvas seeks to the snapped time. Object selection, control-point interaction, and box drags preserve the playhead, including object selection in the timeline. Clicks on the upper object timeline ruler preserve the playhead. Changing snap settings does not move existing objects.

**View → Grid Level** opens a right-side submenu on hover or click, with the current level checked. It selects Tiny (4), Small (8), Medium (16), or Large (32) in osu! playfield pixels. **View → Grid Snap** enables horizontal snapping. **T** toggles the grid and **G** cycles its four sizes. The grid affects horizontal placement, control-point movement and group movement; groups keep a common offset. Time snapping remains independent, and Bézier handles retain continuous movement.

Absolute timestamps use `mm:ss:fff` (minutes, seconds, milliseconds), truncating the displayed fractional millisecond without changing stored precision. Selecting an object preserves its exact stored timestamp.

## FSliders

Hover over **FSlider** to reveal two vertically stacked buttons on its right: **osu legacy mode** and **pen tool mode**. The default is osu legacy mode. The active mode is highlighted, and either can be selected with any tool active. The mode is a session setting: both tools edit the same FSlider objects, and changing modes does not change geometry or create undo history. New and existing sliders may be edited with either tool. Imported Legacy Sliders still require conversion to an editable FSlider.

In **pen tool mode**, press B with no track selected to start drawing. Click to add curved anchors; Ctrl+click adds a straight segment. Hold and drag upward to pull direction handles. Click the last anchor again to begin a new curve section. Right-click a placed draft point to remove it, or right-click elsewhere to finish at that position. One track may mix straight and Bezier segments. Enter also finishes; Esc cancels the draft. While editing an existing track, click the **FSlider** tool button to start another.

Select an FSlider and press B, or double-click its track, to edit anchors. Clicking an anchor on an already selected complete track also enters editing. Drag anchors and handles directly on the canvas. Interior anchor dragging is free by default. Enable **View → Snap interior anchors** to snap interior anchor times to the selected beat subdivision. Head and tail anchors follow beat Snap. Invalid snapped endpoint moves keep their previous time rather than clamping between grid lines. Handles remain free, and the option does not change placement or whole-object snapping. Times remain increasing, and control-point X stays within the playfield.

With a single slider selected, left-dragging the body away from anchors moves the whole slider, and left-dragging an anchor moves that anchor. Ctrl+click at a new position inside the slider's time range inserts a curved anchor; Ctrl+click on an existing anchor makes it straight. Right-click a straight anchor to restore a curved anchor, then right-click the curved anchor to delete it. These rules apply in both editing modes, including points exposed in Select mode. In legacy mode, an interior straight anchor is a segment boundary; restoring it to curved merges it back into the control polygon. Right-click the slider body away from anchors to delete the parent. Ctrl+L also toggles the selected point, and Ctrl+I inserts on the curve under the pointer. Ordinary insertion may change shape; the shape-preserving split action retains it. Batch deletion may include endpoints. Fewer than two remaining anchors deletes the complete track.

Span count applies to the entire FSlider; later traversals reuse the first span's nodes in alternating directions. Hold the left mouse button stationary on a Legacy Slider to open its conversion actions beside the pointer. A small progress ring appears after 300 ms and fills over the next 700 ms. Moving at least 2 DIP or releasing before completion cancels the hold; ordinary selection and dragging remain available. The buttons stay open until an action, another click, a key, or focus loss dismisses them. Conversion preserves start time, total duration, and span count. It first fits a small set of straight/Bezier anchors within 0.25 playfield units, then relaxes TinyDroplet alignment or uses a linear approximation if needed to complete the conversion. Exact nested-object positions and sequences are not required to match. Invalid or unrepresentable input retains its original object with a reason.

The first workspace import from Songs, an external folder, `.osu`, or `.osz` asks whether to convert sliders in all newly imported difficulties if they contain Legacy Sliders. Keeping Legacy preserves that representation. Reopening an existing project does not prompt again. Importing one difficulty into a project prompts only for that difficulty.

**Edit → Convert all sliders to FSliders** processes the current difficulty and is disabled when it has no Legacy Sliders. Batch conversion runs in the background with a cancellable prompt and blocks content editing. Cancellation applies no partial results. Conversions per difficulty can be undone together; valid sliders that previously failed strict alignment now convert approximately. Invalid input and reasons appear in paginated results. Saving persists conversion in the workspace without modifying original Songs/external files; Export is still required to write `.osu`.

The generator handles FSlider TinyDroplet alignment. The Tiny alignment toggle only affects older project data without a saved per-track policy.

### osu legacy mode

See [Slider interaction reference](SLIDER_INTERACTION.md) for the source comparison and coordinate constraints.

Left-click a start point, move the pointer to preview the endpoint, and left-click to add controls. Right-clicking away from placed points or pressing Enter completes the slider; Esc cancels the complete draft. Right-click a placed point to remove it. Ctrl+click adds a straight segment. Click the last placed point again to begin a new segment; this does not depend on the system double-click interval. Each draft segment defaults to a line with two points, a circular arc with three, and a Bezier with four or more; counts include the segment endpoints. White controls shape the curve and do not necessarily lie on it. Red segment boundaries lie on the path and allow corners.

A single selected slider exposes controls in Select mode as well as the Slider tool. For a completed slider, drag a control, Ctrl-click an existing control to turn it into a straight segment boundary, or box-select controls. Ctrl-click between the slider's first and last times to insert a control at the pointer's position; the time interval determines its place in the control polygon. Insertion can change shape. Right-click a straight point to return it to a curved control; right-click a curved control to delete it. Delete removes selected controls directly. Double-click an interior point to toggle its segment boundary. Removing a boundary merges its adjacent control polygons; removing endpoints changes the time range. Fewer than two remaining controls deletes the slider. Each edit is undoable. Control times must remain ordered. A circular preview or drag that would reverse time or leave the playfield falls back to Bezier; moving back during the same gesture can restore the arc. Explicit circular-arc commands still reject invalid geometry.

Use Ctrl+I for insertion and Ctrl+L to toggle the selected point between straight and curved. A circular arc requires exactly three points. Existing Bezier segments retain their type when their point count decreases; changing tools never implicitly turns a three-point Bezier into an arc. A line receiving its first internal control becomes an arc using the current map AR. Ordinary control dragging uses the interior-anchor snap setting; endpoint moves use the global snap setting.

Circular arcs retain a reference ratio derived from the map AR at creation (`440 / preemptMs` in playfield units per millisecond). Changing map AR or viewport zoom stretches their appearance without changing time–X coordinates. The reference excludes window width and DPI. Editing an existing arc preserves this reference; explicitly choosing a new circular arc uses the current map AR.

### Editing shared curves with the pen

A cubic Bezier exposes exactly the same controls in either tool. Arcs and higher-degree Beziers remain exact when selecting or switching modes. Pen mode displays endpoint handles from a bounded cubic approximation; provisional handles have a minimum 18-DIP display length so they remain clickable. Their stored offsets remain in map coordinates. The first actual handle movement converts only its affected segment, potentially adding anchors. The conversion and gesture share one undo step. Pen corner conversion/deletion and ordinary pen insertion may likewise require local conversion. Undo restores the exact original controls and AR reference. Shape-preserving splitting retains exact custom segment geometry.

### Reverses and direction

Both modes use **Ctrl+= / Ctrl+−** to change reverses. Dragging a base-path endpoint instead edits the path and therefore changes the duration of every traversal.

**Reverse path direction** (Ctrl+G) reverses the first span's horizontal trajectory while preserving its time range and repeat count. It is distinct from adding a reverse. All repeated spans derive from the same controls; changing the base path updates every traversal.

## Banana showers

With N, left-click to set the start, then right-click at a later time to finish. Esc or switching tools cancels. A banana shower appears as a time rectangle spanning the playfield. Drag its body to move it or its top/bottom handles to change start/end times. RNG generates individual banana X positions.

## Files and playback

| Input | Action |
| --- | --- |
| Ctrl+O | Open `.osz` / `.osu` / `.catchproj` |
| Ctrl+S | Save current difficulty; workspace-only projects offer an optional Songs export after saving |
| Ctrl+E | Export `.osu` |
| Space | Play / pause |
| Click, drag, or scroll the bottom timeline | Seek while preserving play/pause state |
| Home | Return to the start |

The File menu can replace MP3 / OGG / WAV audio. Manual seeking and editing remain available without playable audio. Save the editor project to retain editable data; further changes after `.osu` export still require a project save.

Text inputs show a blinking caret at the end of the text and highlight the full selection after Ctrl+A (Command+A on macOS). Typing replaces the selection; library fields also support pasting text. Long focused text scrolls horizontally to keep its end visible.

## Display settings

The language dropdown in **Settings → Appearance** lists the supported languages. Selecting a language applies and saves it immediately. Menu shortcut hints align to the right edge of each row. Existing beatmap titles and object names retain their values. The main canvas can hide curves and nodes, while the right-hand preview has a separate debug-curve toggle.

The Skin selector to the left of **← Library** lists skins from the configured osu!stable `Skins` folder and offers `.osk` import. Imported archives and extracted Catch assets are kept under `workspace/Skins`; imported entries use gold text and an Imported label. Skin selection persists independently of beatmap edits. Library Settings accepts a user-owned default skin `.osk` file; each missing or unreadable custom image falls back to the default skin independently, then to geometric rendering. Long lists provide previous/next pages. Missing skins or textures fall back to basic shapes; see [Skins](../assets/skins/README.md). Drawing and hit-test sizes are described in [Catch Rendering and Conversion](CATCH_RENDERING.md).

## Multiple difficulties

**File → New project** creates a project with one blank difficulty. Opening `.osz` loads all Catch (Mode=2) difficulties into one project and skips other modes. A damaged Catch file does not replace the current project. Opening a single `.osu` or older `.catchproj` creates a single-difficulty project.

A separate row below the main toolbar displays Chrome-style difficulty tabs with the official Catch icon, Version, live No Mod stars, and an unsaved dot. Icon color follows stars. Active tabs have rounded top corners and spread outward at the bottom to join the content below. Tabs use actual text widths rather than filling the row. Tabs first use full difficulty names. When space is insufficient, up to eight tabs share the available width by shortening the longest names; more than eight tabs use compact names and a horizontally draggable strip. Arrow buttons and the wheel also scroll overflowing tabs. Stored names remain complete. Hovering a truncated tab shows its full name in a pointer-following tooltip that wraps and stays within the window. Click to switch; use arrows or the tab-row wheel when tabs overflow. Ctrl+Tab / Ctrl+Shift+Tab cycle and reveal the active tab. The **+** button opens the add/import menu. A new blank difficulty inherits the active difficulty's audio, timing, settings, and resource context but clears objects. Importing an `.osu` adds one file. Difficulties may reference different audio.

Switching commits valid pending edits first; unfinished banana drafts or invalid input prevent switching. It pauses playback and retains each difficulty's playhead, time-viewport start, and undo/redo history. Selection and the active tool reset. Title/status dirty indicators cover the whole project, including hidden difficulties. One save writes every difficulty and updates baselines without clearing undo history. Unsaved confirmation on new/open/close applies to the whole project.

`.osu` export applies to the active difficulty; suggested filenames include its name. Workspace project saving is described in [Workspace](WORKSPACE.md), and the compatible `.catchproj` format in [Project Model](PROJECT_MODEL.md). Resource paths remain references rather than embedded project-file contents.

See [Catch Star Rating](CATCH_DIFFICULTY.md) for calculation, cache invalidation, and export/website-version limits.

## Library and workspace

**Library** opens a separate page for workspace/stable Songs settings, bilingual metadata search, difficulty browsing, and project opening. Editor difficulty tabs retain their layout. Missing source maps or song audio show an error bar. Missing optional video, storyboard, background, or sample files do not. Export offers a standalone `.osu` save dialog, associated-difficulty overwrite, or a new difficulty in Songs. After export links a difficulty to Songs, Save also updates its linked `.osu`. See [Workspace](WORKSPACE.md).

Below the Catch Preview title, one line shows `AR … · CS … · NM`. It omits fall time, generation status, and skin name. Preview scrolling and object drawing still follow AR/CS.

A new project's blank difficulty starts unmodified, so directly opening or importing an external beatmap does not trigger an unsaved prompt. Content edits, audio binding, and added/imported difficulties do prompt. Undoing to the initial blank state clears the dirty marker.

The bottom status bar shows current action feedback, such as save results or operation limits, with conversion errors taking priority. It is not a log viewer. Platform details, internal zoom percentages, and duplicate dirty indicators are omitted.

For FSliders, 0 reverses plays the path once, 1 returns once, and higher counts continue alternating. Use **Ctrl+= / Ctrl+−** to add/remove a reverse. With a completed FSlider selected, move the pointer to empty canvas at a time after its final end and press **Ctrl+J**. A new anchor is placed at the pointer position using the placement snap setting, with a straight segment from the base path endpoint. Existing segments remain unchanged. Extending a repeated slider lengthens its base path for every span; it does not append after the repeats. Each operation is undoable.

In the object timeline, a slider tail displays a horizontal resize cursor. Drag it right to add reverses or left to remove them, down to one traversal. Each step equals one unchanged base-span duration. This works for FSliders and imported Legacy Sliders; release commits one undo step and Esc cancels.

## Operation errors

Recoverable file-operation errors appear inside the editor window on both the canvas and library pages. The message identifies a rejected beatmap file when parsing fails. Scroll long messages with the mouse wheel; dismiss with OK, Enter, or Esc. While the message is open, editing and library input are blocked and the current document is retained. Windows startup and rendering failures use a foreground system dialog because the editor canvas may be unavailable.

## Slider fruit streams

Select one or more sliders and press **Ctrl+Shift+F**, or use **Edit → Slider to stream**. Long-press an FSlider to reveal **Convert to stream**; imported Legacy Sliders offer **Convert to FSlider** above **Convert to stream**. Every stream-conversion entry opens a confirmation dialog with the same snap slider and subdivisions as the main toolbar: **1/1–1/9, 1/12 and 1/16**. Enter confirms; Esc cancels; arrow keys change the choice.

A confirmed stream remains one editable slider parent with its anchors, handles and repeats. Dragging, reshaping, cloning, saving and undo retain its stream snap. Existing streams offer **Change snapping** above **Convert back to slider** in their long-press menu. The Edit menu and Ctrl+Shift+F open Change snapping for a stream selection. Changing snap requires confirmation; converting back restores ordinary slider output while retaining geometry and supports undo. Preview and testplay display independent fruits, and `.osu` export writes hit circles. Sampling starts at the slider head, uses its starting BPM across all spans, and includes the tail only when it falls on that subdivision. New Combo applies to the first fruit; object-level sound/sample settings apply to each fruit.

The keyboard aliases above follow the [legacy shortcut reference](https://osu.ppy.sh/wiki/en/Client/Keyboard_shortcuts) where supported. Existing Ctrl+L point conversion, Ctrl+I point insertion, Ctrl+J extension, Ctrl+E export and Ctrl+wheel zoom remain editor-specific bindings; V and End provide last-note navigation. Timing creation and geometric rotation dialogs are not available.
