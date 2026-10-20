# Editing Controls

## osu! editor links

**Settings → osu! Links** contains one fallback application selector. It becomes
available when the system routes `osu://` to FruitsAtelier. Choose from the
applications registered to handle osu! links. The list shows application names
and icons; selection is saved immediately.

When FruitsAtelier is the system handler and its fallback is missing or invalid,
a required application list appears automatically. It remains open until a
registered external application is selected. The system association is checked at
startup, on activation and periodically while running. Changing the default
handler away from FruitsAtelier removes the requirement. Windows protocol
registration advertises FruitsAtelier as an available application without
changing the user's selected default.

Editor links address the active difficulty, without identifying or opening a
beatmap. For example, `osu://edit/00:13:247%20(2,3)` pauses playback and selects
the next objects numbered 2 and 3 from 13,247 ms onward. Numbers are combo-local
and matched in chronological order, including repeated numbers across combos.
With a selection suffix, the playback position moves to the first object at or
after the timestamp; without one it moves directly to the timestamp. Times are
clamped to the current timeline. Streams and segmented banana showers resolve
their event numbers to selectable source objects. Navigation does not edit the
beatmap or add undo history. The time-jump dialog also accepts a complete editor URL.

Outside editing, including the library, settings, startup and testplay, editor
links only display a notification. They are discarded rather than deferred until
a difficulty opens. Active text input, gestures and modal dialogs must finish
before an external editor link can navigate.

Other `osu://` commands are passed unchanged to the configured application,
directly rather than through the system protocol association. Windows retains
the registered command arguments and resolves the selected ProgId on each use;
macOS resolves its bundle identifier. Their behaviour depends on that client's
support. Missing forwarding targets and self-forwarding
are rejected. Link preferences are stored in `FruitsAtelier/osu-links.json` under
the system application-data directory.

## Collab Mapping

Open **Layout → Collab Mapping** to add the dockable panel, then select its tab.
The panel follows the active difficulty. Each difficulty independently chooses its
references and when to synchronize. It lists source difficulties and their ranges,
and all target difficulties that directly reference the current difficulty.
Selecting a target opens that difficulty. The panel supports multiple sources and
multiple targets, including separate non-overlapping ranges from the same source.
All referenced difficulties must belong to the same project.

Use **Add reference**, select the source, and edit **Start** and **End**. Inputs
display osu! timestamps such as `01:24:771`; typed or pasted integer milliseconds
such as `84771` are also accepted. Clipboard timestamps with an osu! object-list
suffix are accepted. Ctrl/Cmd+A, C, X and V use the normal field selection and
clipboard controls. Fractional milliseconds are rejected. Enter commits a field;
Escape discards its draft. References are saved with the target difficulty.

Ranges include Start and exclude End. Overlaps and self-references are rejected;
adjacent ranges are allowed. Objects belong to the range
containing their start time, and complete sliders, streams and banana showers are
retained, including tails beyond End. Removing a reference detaches it and leaves
the merged content in place.

**Sync references** reads the currently loaded source documents, including unsaved
edits, and replaces target objects whose starts lie in the configured ranges.
Source additions, modifications and deletions are applied together. Objects
starting outside those ranges remain. The source's timing points are copied within
each range, with boundary points establishing inherited BPM, sample bank, index,
volume and effects and restoring target state after End. Boundary red points
establish a beat-grid origin at that boundary. Reference relationships do not
trigger automatic synchronization or recursively synchronize their sources.

Object identities are mapped independently for each reference and remain stable
on repeated synchronization. Object sample fields and event overrides are
retained; boundary inheritance differences are materialized when required to
preserve audible events. The merged difficulty's SliderTickRate and tick rules
govern derived output. Changing tick density remaps existing event overrides to
their matching edges or times; new ticks inherit source sample timing. Random
positions follow the merged map's RNG traversal.

Sources in other resource folders copy their used samples into the target resource
folder. Custom files and numbered sample groups receive separate names so existing
target samples remain intact. Resource copies are part of the same undo step.
Default skin samples require no file copies. A target receiving actual sample
files needs an audio/source resource folder.

Validation runs on detached source/target snapshots in a worker. A changed source,
target or compensation setting invalidates its result. Each
successful synchronization is one undo step in the target difficulty. Saving or
switching difficulties waits until validation completes.

## Remembered window and canvas preferences

Closing the editor saves its normal window size and maximized state for the next
launch. Restored sizes fit the current monitor's work area. Windows and macOS keep
separate window sizes; fullscreen retains the preceding windowed size and state.
Startup setup and acknowledgement windows do not replace these preferences.

The Catch canvas **Slider Path** and **Selection Rect** switches are saved globally
as soon as they change, including Slider Path changes through the View menu.
They survive project changes and restarts without changing beatmap content or undo history.

Each difficulty remembers its beat-snap divisor and Anchor Snap setting alongside
GS (Grid Snap), its grid level, DS (Distance Snap), and Object Snap. Switching
difficulties or reopening the project restores their individual preferences.
Temporary beat grids shown by inspecting a note do not replace the saved divisor.

## osu! canvas

Tab switches the Compose canvas between Catch's time–X view and an
osu!standard 512×384 geometric view. Text fields, dialogs, Ctrl+Tab difficulty
navigation and Testplay retain their own key handling. Switching views does not
convert objects or create a content edit. Catch preview continues to show the
exported fruits in either view.

The osu! canvas draws circles, approach circles, Legacy Slider paths, repeats,
slider balls and spinners. Circle size, approach rate, combo ordering and stacking
use standard geometry. Past circles turn white and fade over 700 ms. Seeking and
pausing use the same map-time presentation. Skin elements are used when available.
The coordinate readout below the Distance Snapping control shows the pointer's X and Y
in the 512×384 geometric space, including after zooming or panning. Coordinates
outside the playfield retain their signed values; leaving the canvas shows dashes.
Selecting objects does not replace the pointer coordinates in this view.
Selected circles have the same orange ring as their timeline markers. Selected
sliders show this ring at both geometric endpoints, including even-repeat sliders.
Circle and slider placement previews use their resulting combo colour and number.
Their placement time follows the snapped playhead; pointer Y controls only the
geometric position, including the placement marker in the upper timeline.
Combo colour also takes precedence over skin slider-track overrides in this view.
Alt+wheel over the upper object timeline changes its time scale independently of
osu! canvas zoom.

Slider bodies retain a white border and a brighter centre with darker edges.
Static `sliderb.png` and numbered slider-ball animations use the owning skin's
resources and tint settings. Retained skin archives upgrade older extraction
caches when the supported resource set changes.
Spinners use the owning skin's old or modern composition and native texture
dimensions, with the fixed centre's tint following the spinner's elapsed time.

Catch edits FSlider shapes; osu! edits Legacy Slider shapes. The other
representation remains visible and selectable for hitsound, sample-bank and NC
changes. Shape edits require the explicit conversion action.
Conversion is undoable, validates playback and preserves the original object on
failure. Global droplet settings can rebuild Legacy paths to match their exported
fruit targets; this can change their two-dimensional shape.

With Movement Analysis enabled, osu! placement connects the pending note to its
previous and next objects. Leaving placement restores the committed connections.
Ctrl+Shift+F in the osu! canvas opens a subdivision picker and converts selected
sliders into individual notes along their two-dimensional paths, including reverse
spans. Catch retains its Stream/Stack dock. Both conversions are undoable.

Ctrl+Shift+R opens **Rotate by**, with an angle slider and numeric entry,
signed angles from −180° to +180°, clockwise/anti-clockwise direction, and
playfield/selection centre origins. Negative angles reverse the chosen direction.
Selection centre is the default. Origin and direction choices are global preferences
saved immediately across maps, canvases and restarts, including when the rotation
is cancelled. Each newly opened dialog starts at 0°.
Click the angle slider to the right of its thumb to add 45°, or to the left to
subtract 45°, clamped to −180° and +180°. Drag the thumb for continuous adjustment.
For a selected slider, Selection centre uses the current slider's beginning point;
standalone notes use their selection bounds' centre.
Changes preview directly on the canvas from the original selection. OK commits
one undo step; Cancel, Escape or interrupted input restores the original objects.
Drag the title area to move the window within the editor. Window movement does
not change the preview. Angle input, slider dragging and clicks stop at the
selection's feasible Catch rotation boundary; the angle and preview stay in sync.
Ctrl+comma and Ctrl+period rotate the selection by −90°/+90° in osu! and
−5°/+5° in Catch. osu! uses Selection centre, including the current slider's
beginning point in both canvases. Standalone notes use their selection bounds' centre.
Catch angles use the current time-to-X
display ratio; its playfield origin is the centre of the visible time range.
Catch rotates anchors, handles and explicit controls together. Circular arcs
whose stored aspect ratio differs from the rotation plane use the existing bounded
Bezier fitting before rotation. The operation rejects reversed time, coincident
exported note times and out-of-range endpoints atomically. Catch rotation requires
single-span curves without Stack or droplet adjustments. Native osu! rotation uses
stable integer coordinates while retaining slider length, timing and repeat count.
Each Catch curve keeps its exact start and end timestamps. After rotation, a
uniform scale fits its duration back into the original interval while preserving
the new leaning angle. A selection of standalone notes similarly retains its
first and last timestamps; intermediate times remain strictly ordered.

In osu! view the Fruit and Banana tools place circles and spinners. The Slider
tool places Legacy controls and preserves their original two-dimensional path
through global droplet-setting changes and project reopening. Click creates a grey square anchor, double-click
creates a red segment boundary, and right-click finishes at the pointer. Thin
white lines connect the controls. The draft uses the final slider body, skin,
combo colour and end circles. A new slider with exactly three controls (head,
one interior anchor and tail) uses a perfect circular arc; collinear controls
form a straight path. Other new sliders use Bezier geometry. Adding a fourth
control or a red boundary to a circle switches it to Bezier. Removing a control
from an existing Bezier retains Bezier geometry. Selected controls can be dragged; Ctrl+click
turns an interior control into a red boundary and right-click removes it.
The Context Menu key opens path type, repeats and length actions.
Perfect circle requires three controls. Enter, right-click or a number-key tool
switch completes a valid draft; Escape cancels it. An insufficient draft is
discarded when switching tools.
With beat snapping enabled, draft and control-edit lengths round to the nearest
current Snap subdivision using the slider head's BPM and SV, with a minimum of
one subdivision. The visible slider end follows that length; its final control
point can extend beyond or fall before it. Disabling beat snapping allows free
lengths. The upper timeline shows the draft's current interval before placement.
The geometric grid is shown only inside the white 512×384 playfield at the selected
4/8/16/32 px level. Zoom and pan keep it clipped to that playfield. Grid Snap uses that
lattice on both axes; holding Shift temporarily inverts snapping. Both remain
available independently of Catch editing mode and its speed presets.
Alt+wheel zooms and middle drag pans the geometric view.
The upper-right **Distance Snapping:** row places its slider beside the label
and shows its own 0.1–3.2 multiplier; Shift uses 0.01
steps instead of 0.1. Changing it does not change Catch DS.

Both canvases draw into the side areas beneath the tool palettes. Logical
playfield coordinates and zoom stay unchanged; tool buttons remain above the
content and receive clicks first. Side docks, the toolbar and transport retain
their own drawing regions.

## Interface size

**Settings → Appearance → UI scale** adjusts the whole interface from 50% to
200% in 25% steps. The default 150% retains the reference size described below.
Apply activates and saves the choice without restarting; closing Settings discards
unapplied changes. Drawing and pointer coordinates use the same scale on both
platforms. Compact startup dialogs retain their fitted layout size.

Outside text fields, dialogs and active gestures, Ctrl+= / Ctrl+- adjusts UI scale
by one step with any selection. Ctrl+Shift+= and Ctrl+keypad plus/minus are aliases.
Shortcut changes save immediately and do not enter content history.

**Settings → Appearance → Selection highlight** sets the colour of selected-object
rings on the Catch canvas, Catch timeline selection rings and selection boxes.
The osu! canvas and its timeline use orange selection rings.
The colour picker supports HEX input; Apply saves the global preference, Cancel
discards the draft, and Reset restores turquoise. Skin hyperdash colours stay independent.

On Windows, the editor scales uniformly with its client pixel dimensions, using
2520 × 1575 at 175% as the reference: the original 1440 × 900 logical startup
window. This preserves its text, buttons and layout proportions. Equal client dimensions give
equal text, button and icon sizes regardless of monitor or Windows display scale.
Different aspect ratios fit the reference into the available width and height;
the remaining space extends the editing area. Compact startup dialogs fit their
own layout dimensions. Native title bars and file dialogs follow Windows display
settings; macOS uses its native display scaling.

## Same-time object placement

Settings → Experimental → **Enable same-time object placements** disables
the automatic replacement of fruits, FSliders and banana showers starting within
2 ms of a newly placed fruit in the Catch canvas. In the osu! canvas, placing a
circle, native slider or spinner replaces same-time circles, native sliders and
spinners; the same toggle preserves them. Sliders that are read-only in the active
canvas remain unchanged. The toggle defaults to off and is saved as an application
preference. Enabling it allows fruits at the same time with
independent X coordinates; previews retain the existing objects too. Changing the
preference leaves existing content and undo history unchanged. Placement remains
one undo step, and turning it off restores replacement for subsequent placements.
Completing a Catch FSlider also retains a fruit at its exact head when enabled.

## Experimental lazer editing

**Settings → Experimental → lazer editing** enables the alternative Compose
interaction mode after Apply. Closing Settings discards an unapplied mode change.
The mode is a global editor preference; switching it preserves beatmap content,
undo history, testplay key assignments, canvas scaling, scroll preferences and the
saved Mapping playback-line height.

The lazer canvas shows an automatic catcher at a fixed judgement line. The line
stays 10% above the bottom of the plot; catcher artwork below the canvas is clipped.
White corner brackets mark the normal gameplay visibility range above the catcher:
their bottom follows the catch line, and their top projects the visible time from
Catch Preview's 4:3 Fit reference into the canvas time scale. The Zoom slider and Alt+wheel over the canvas
scale both axes together with easing, keeping the catch line fixed. The lock defaults
to linked axes capped at 100%. Unlocking shows independent X/Y sliders up to 200%;
locking again aligns Y to X and caps both at 100%. These lazer zoom settings are
session-only and separate from saved Mapping preferences. Middle-button
dragging pans horizontally to inspect the enlarged screen without seeking. Caught objects
disappear from the canvas when their timestamp is reached; they do not
accumulate on this canvas. Catch Preview and testplay retain their plate stacks.
Missed overlap objects continue below the line while fading over 250 ms.
Speed presets display perfect-walk and perfect-dash guides from the last prior
eligible object at 0.5 and 1 playfield units per millisecond, together with a
vertical stand guide. Placement and selection show these guides while Distance
Snap is enabled.

Distance Snap → Configure offers **Distance Snap presets** and **Global speed presets**
buttons on the same row, sized to their full labels. Hovering either button shows
its units and storage scope above it. Mapping defaults to DS and lazer defaults to Speed;
each editing mode remembers its choice across maps and restarts. DS presets stay
with the map, while Speed presets are global. Apply saves the choice and edited
presets; Cancel discards pending changes in both tabs. The fixed 0.5 and 1.0
speed entries use bold text and cannot be moved or deleted. Speed mode lets
you add up to eight custom speeds above zero through 3.0. Values are logical
playfield pixels per millisecond (0.5 = 500 pixels/second), independent of SV.
The existing preset arrows and Add control edit these speeds; right-click a
custom marker to remove it. Apply saves these presets globally across maps and
restarts. The upper SV/DPB controls still edit the current map's base velocity.
Guide colours follow the current map's Stand/Walk/Dash/HDash analysis and configured
movement colours, with short gradients at category transitions.
Movement Analysis connection labels follow the selected preset system: logical
pixels per millisecond for Speed and DS ratios for DS. Speed is independent of base SV.
Slider Inspector shows one metric for this choice: Peak horizontal speed in px/ms
for Speed mode, or Max DS as a ratio for DS mode.
Placement connections and labels use the Break ranges that would result from
placing the candidate note, including shrinking, splitting or removing a Break.
Hovering does not change the map; cancellation restores the committed ranges.
Speed configuration opens with a 1/4 preview subdivision at the current BPM.
**Show DS guides** appears only for Global speed presets, defaults to on and is saved
globally; turning it off hides canvas guide lines while retaining distance snapping.
**Collinear snap** is available in both preset systems and adds alignment between
the preceding and following objects to the placement and dragging candidates.

The upper object timeline has a separate lazer zoom: Alt+wheel animates its
visible time range over 200 ms. It starts at 6000 ms and ranges from 500 to
10000 ms, bounded by song length. Mapping's saved timeline scale is preserved.

**Settings → Appearance → Hit lighting** applies to the canvas, Catch Preview
and testplay. **Follow mode** enables it in lazer editing and disables it in
Mapping; **On** and **Off** override that choice. Apply saves the preference;
closing Settings discards unapplied changes.
Keyboard and mouse differences are listed in the
[shortcut manual](KEY_BINDINGS.md#experimental-lazer-editing).

## Acknowledgements

Normal Windows and macOS launches require confirmation of the current
acknowledgements before starting the library or opening a supplied beatmap.
The English-only notice fills its own compact, borderless startup window,
bounded to 820 × 740 logical pixels and 85% of the monitor work area after UI
scaling. Drag its header to move it. It has a scrollable text box, a read-and-agree checkbox,
and Cancel / Confirm buttons. Confirm is disabled until checked; cancelling,
Escape or closing the window exits. Mouse wheel, scrollbar dragging, arrow keys,
Page Up/Down and Home/End scroll the text; Tab navigates the text box, checkbox
and enabled buttons, with Space or Enter activating the focused checkbox.

The notice revision is saved atomically in `FruitsAtelier/acknowledgements.json`
under the system application-data directory. It is independent of first-time
setup and application versions. Missing, unreadable, damaged or different-revision
records require confirmation; a failed save keeps the modal open. Increment
`AcknowledgementsPreference.CurrentVersion` when revising the notice to require
all users to confirm the updated text. Existing users retain their setup preferences.
New users proceed from confirmation to first-time setup, then enter the main
editor; existing users enter the main editor directly after confirmation.
On Windows, startup transitions change window bounds and decorations together
and synchronously present the final-size content before returning to input.
See [the user manual](USER_MANUAL.md#acknowledgements-and-community-use) for the
notice's scope and community-use requirement.

Run [Run-Acknowledgements-Debug.cmd](../Run-Acknowledgements-Debug.cmd) on Windows
to build and open the notice on every launch. Each launch builds to a separate
directory under `artifacts/acknowledgements-debug`, allowing an earlier preview
to remain open. The `--acknowledgements-preview`
option uses a new isolated confirmation file under `artifacts/tests/acknowledgements-preview`
each time and preserves the normal confirmation record. On macOS, use
`./Run-Editor-Mac.command --acknowledgements-preview`.

## First-time setup

Startup opens a five-step setup guide when the saved setup version is missing or
older than the current version. This includes existing users upgrading to the
release that introduces setup. Completing the guide saves its version, so later
normal launches open the editor directly.

The guide uses a compact window, targeting 880 × 620 logical pixels and bounded
to 85% of the screen work area after UI scaling. Its filled arrow-shaped tabs
cover folders, language and romanised artist/title display; skin and skin sounds;
volume; Slider Droplets; and testplay. Artist/title display appears below Language
on the first page. Inactive tabs alternate two similar colours and join into a
continuous strip. The pages show setting labels and controls without explanatory
paragraphs. Short windows scroll the content
while keeping navigation visible. The guide has no system title bar or outer
border; drag the progress strip, or the top of the completion page, to move it.
The main editor opens after Start with its normal window decorations.

Back and Next retain choices in an isolated draft. Only Finish saves the full
configuration and selected language. Invalid workspace settings keep the current
step open. Exit, Escape and closing the window discard unfinished choices,
restore the original configuration, and leave setup required on the next launch.

An unfinished first setup starts in English. Master, music and hitsound volume
load their saved values; each missing volume parameter defaults to 50%.
Selecting a language previews it until Finish. The **Audio Test**
uses the bundled Campus After Class recording with Play, Pause and Stop icons and never starts
automatically. **Hitsound Test** labels the four sample buttons: **Hit**,
**Whistle**, **Finish**, and **Clap**, with these names retained in every language.
They audition normal, whistle, finish and clap
through the same sample resolver as Timing, respecting skin sounds and volume.
Leaving the volume step stops its independent music transport. The skin step
previews a catcher, fruit and slider and links to the osu! default skin download
page; selecting a default `.osk` updates the preview.

Finish saves completion and shows a welcome page in the same compact window,
without progress tabs, with a proportionate FA logo. The community welcome and
Join Discord button share a row; Start and Exit appear below with extra spacing.
Start opens the library. Settings
remain editable through the ordinary Settings window.

For development, `Run-Setup-Debug.cmd` or the `--first-run-setup` executable flag
opens the guide on each run, including after completion, using current settings.
Replay changes also remain a draft until Finish.

## AiMod

Open **Edit → AiMod** or press **Ctrl+Shift+A** to inspect the current difficulty.
The report separates information, warnings and errors into Compose, Design,
Timing, Meta and Mapset categories. Drag column boundaries to resize them, or drag
headers to reorder columns. Long messages wrap; the horizontal scrollbar and
Shift+wheel reveal columns outside a narrow panel.

Click rows to select them, use Ctrl-click or Shift-click for multiple rows, and
press **Ctrl+C** or **Copy** to copy complete values as tab-separated text with
headers. **Ctrl+A** selects all rows in the current category. Click a timestamp,
double-click a row or press Enter to locate it; object findings also select their
editable parents. **Refresh** (or F5 while the report has focus) reruns checks.
The time column is leftmost by default. Drag the vertical scrollbar or use the
mouse wheel to scroll the report continuously.

AiMod opens directly as a side panel. Drag its tab to either dock or reorder it
with other tabs. Dock placement persists with the workspace layout. Ctrl+Shift+A
focuses and refreshes the panel. Editing makes the report a stale snapshot; its
footer asks for a refresh. A restored AiMod panel automatically checks once the
map is loaded. Switching difficulties replaces the report with a check of the
new difficulty; painting and resizing do not rerun analysis.

Analysis uses exported Catch objects, including generated Stream/Stack objects,
and does not modify content or undo history. Resource checks run in the background.
See [AiMod checks](AIMOD.md) for the rule scope and stable compatibility details.

See the [shortcut manual](KEY_BINDINGS.md) for the complete keyboard reference and the [compatibility review](KEY_BINDINGS_REVIEW.md) for known differences and gaps.

Grid Snap, Distance Snap, Movement Analysis and Grid Level are saved immediately
as local preferences for each project difficulty. Switching difficulties or reopening
the editor restores these values. They do not change map content or undo history.
Shift/Alt temporary snap overrides are not saved. Difficulties without saved
preferences start with all three switches off and Grid Level Tiny (4 px).

## Droplet randomization

**Droplet Settings** is an independent dock tab opened from Layout or Edit.
Its current-difficulty section contains the default droplet mode
(Derandomize or Randomize), Strength (0–100 playfield pixels), a signed 32-bit
Seed, and the NM/HR compensation target. Valid edits apply immediately with undo;
invalid numeric input leaves the last valid value intact. Strength has a numeric
field and slider, plus Reset strength to restore 20.

The separate **Global Derandomize** section controls derandomization for new projects
and Legacy-to-FSlider conversion. These preferences save immediately and do not
change the current difficulty or its undo history. New projects default to
derandomization. Valid current-difficulty edits also save the last used mode, strength,
seed and NM/HR target as global defaults for new projects. Existing projects retain
their own saved values. In **Slider Inspector**, **Droplet mode** selects
Follow map, Derandomize or Randomize for the selected ordinary FSliders without
switching tabs. Explicit modes take precedence over the difficulty default.
Multiple differing modes display Mixed; choosing a mode applies it to all eligible
selected sliders in one undo step. The chosen mode is also remembered globally for
newly drawn FSliders in both drawing modes, including after restarting; it initially
uses Follow map. Existing sliders keep their own modes. Legacy Sliders and managed Stream/Stack objects
are excluded. The effective mode is displayed with the Inspector metrics.

In imported maps and older projects without new-slider randomization enabled,
new FSliders default to Derandomize when the remembered mode is Follow map and
the difficulty's derandomization switch is off. Explicit Randomize choices in
the Inspector and the difficulty's new-slider randomization policy are retained.

**Reset manual droplet adjustments** shows the selected sliders' correction count
and is enabled only when corrections exist. It clears only those corrections,
without changing modes. Mode switches preserve corrections; they apply only while
randomization is effective. The reset label identifies wholly inactive corrections.
Existing project sliders retain their previous output until edited: old enabled
effects follow the map default, and old disabled effects remain derandomized.

Strength 20 and Seed 1337 reproduce osu!'s native NM randomization when there are
no manual corrections. Custom seeds use the same legacy RNG; Strength scales its
offsets. The sequence continues across droplets, TinyDroplets and bananas,
including sliders with their effect disabled. Identical sliders use different
parts of the sequence. Adding or removing earlier RNG-consuming objects changes
later offsets; ordinary NM fruits do not. Unchanged content and Seed reproduce
the same result. HR gameplay applies osu!'s usual additional position rules.

Randomization affects TinyDroplets. Drag a TinyDroplet in Select mode or edit its
X coordinate to save a correction without changing the base curve. Unlock droplet
selection using the Lock Notes flyout when needed. Disabling randomization retains
the corrections, and reenabling restores them. Repeat geometry and slider speed
limits can constrain the result; unreachable manual moves stop at a valid position
or reject a numeric change. See [Droplet randomization](PROJECT_MODEL.md#droplet-randomization)
for target generation and export behavior.

## FSlider preview

When drawing a new FSlider, the canvas updates only that slider's provisional
preview. Finish the curve to update full-map gameplay data in the background;
the status bar indicates this work while editing remains available. Testplay
requested during this update starts after the current result is ready. Cancelling
an unfinished curve restores the previous preview and leaves no undo entry.

## Timing editing

**Mapping Panel** at the left of the workspace toolbar opens a dropdown with **Mapping Panel** and
**Timing Panel**. **F3** selects Timing; **F1** returns to Mapping. Timing replaces
the note canvas with a horizontal audio waveform and a centered playback cursor.
Ctrl+wheel changes Snap within its doubling/halving family (for example 1/3, 1/6,
1/12, and experimentally 1/24 and 1/48; the 1/4 family reaches 1/32), stopping at either end. Alt+wheel zooms its time scale; click to seek or click
a red line to edit it.
Only red timing points and their BPM labels appear on the waveform. Nearby BPM
labels use up to four rows. Every label remains visible, allowing overlap when all
rows are occupied; red lines remain clickable. Audio is decoded
in the background and its peak envelope is cached at multiple resolutions.
The time ruler sits just below the waveform envelope. Snap subdivisions extend
through a rectangular waveform band, using the editor's beat-grid colours. The
band follows the waveform amplitude range symmetrically around its center, with
vertical subdivisions only. The waveform is drawn beneath translucent grid and
marker lines. BPM and playback markers extend just beyond the band. The ruler retains its beat and subdivision tick lengths. The filled envelope uses fixed audio-time sampling
windows while scrolling and reserves vertical space around its peaks.
The single-column Timing panel places tap controls above
BPM and edits the active red section's BPM and offset and the difficulty's Slider
Tick Rate. This panel displays and commits BPM to at most two decimal places and
offset to whole milliseconds, rounding to the nearest value. Tap timing uses the
same precision when applied. **Move notes with offset / BPM changes** keeps objects at their beat
positions within the edited section. BPM buttons step by 1, Ctrl by 0.25 and Shift
by 5. **Move greens / bookmarks with offset** independently translates
inherited points and bookmarks by the offset difference within the original red
section, including its start and excluding the next red point. BPM changes do not
move these markers; the audio preview point stays fixed. The option starts off.
Offset buttons step by 2 ms, Ctrl by 1 ms and Shift by 10 ms.

Below Slider Tick Rate, the single **Slider Velocity** row has an **Adjust**
button that opens **Configure DS…**. Its upper section edits the difficulty's base SV and DPB
through one numeric field. Select the visible **SV** or **DPB** buttons beside the field to switch units; DPB equals
100 × SV. Switching units does not edit content. SV accepts 0.4–3.6, equivalent
to DPB 40–360 px, independently of reference Snap and BPM.

Apply validates a changed base SV on a detached worker before committing SV,
DPB and rescaled DS presets in one undo step. Cancel discards the entire draft,
including pending validation. The previous confirmed value remains active until
validation succeeds. Inherited SV compensation, including red-point resets,
must stay within 0.1–10 and preserve exported NM/HR object kinds, times and
positions within 0.001 ms/px. Invalid compensation leaves the difficulty unchanged
and displays its error in the dialog. Loading a project resolves conflicting
legacy DPB values using the effective SV. Timing Setup validates compensated
export before committing when a base-SV override is active.

In Timing mode, object hitsounds are suppressed and the sound-flag palette is
disabled. During playback, **Metronome Clicks** schedules one tick per beat, with a distinct
measure accent. Holding Ctrl follows lazer: Snap divisors divisible by three use
three ticks per beat, other even divisors use two, and other divisors use one.
The music clock controls tick times and visual indicators. Pause,
seek, timing changes and leaving Timing cancel queued ticks. These controls are
editor state and do not alter beatmap data. **Tap Here / T** collects up to 32 taps.
The tenth tap automatically fits the red section's BPM and integer offset by least
squares against tap times and beat indices; each subsequent tap refines both values
through undo history. The latest 32 taps retain their original beat indices so the
fitted section start stays relative to the beginning of the measurement. BPM uses at most two
decimal places. **Apply timing** can also apply a shorter measurement with at least two taps.
**Reset taps** clears the measurement. Tap uses map time, including playback speed.

**Timing Setup / F6** opens an application-modal **Timing and Control Points**
window. It displays and commits edited BPM values to at most two decimal places
and offsets to integer milliseconds. **Use Current Time** takes the whole-millisecond
part of the playback position. The draft is committed by OK as one undo step; Cancel, Escape or the close
button discards it. Timing, Audio and Style pages edit the selected rows, while
All, Timing Points and Inherited Points filter the list. Ctrl-click toggles rows;
Shift-click selects a range. Ctrl+A selects visible rows. Mixed numeric values are
blank until explicitly replaced. Offset arrows add or subtract from each selected
row independently, including mixed values. **Shift selected points (ms)** accepts a
signed amount; **Apply** translates the selected rows while retaining their spacing
(subject to integer-millisecond truncation for inherited points). Click the Volume
column header to toggle ascending/descending display order; ties retain time and
source order. Click Offset to restore chronological display. Selection follows row
identity, and range selection/navigation follow the displayed order. Sorting does
not change timing source order or content history. Arrow, Page Up/Down and
Home/End keys navigate
the list. Drag the list scrollbar or click its track to navigate longer lists without
changing the selected rows. Tab moves between numeric fields; Enter commits a field before accepting
the dialog. Ctrl+Z/Y operate on the draft while no numeric field has focus.

Red points expose offset, BPM and meter. Green points expose offset, samples,
volume and Kiai. The inheritance checkbox changes point type; it protects the
first red point. Audio supports Normal/Soft/Drum banks, default or numbered custom
samples, a volume slider, and four sample audition buttons. Default and Custom 1
lock the sample-index input; selecting Custom enables it. Each audition button
plays only its named sound from the selected bank, index and volume, including
while music is paused. Default
samples show only the bank abbreviation in the list. Imported velocity and
unrelated effect bits are preserved, while green-point BPM cells remain empty. Kiai updates the editor's existing Kiai indication and exports.

Ctrl+P adds a red point at the playhead; Ctrl+Shift+P adds a green point, opening
the draft window on its Audio tab for green points. Shift+arrows extends or shrinks
the timing-row selection from its moving end. New red and green points truncate the playhead to integer
milliseconds. Pasted or edited green offsets also truncate fractional parts.
Ctrl+I deletes the current section outside the window, or the
selected rows inside it. Ordinary deletion protects the first red point. The
window supports Ctrl+C/X/V with `.osu` timing-row text; text fields retain normal
text clipboard behavior. Ctrl+Shift+I inserts a slider control point on the curve
under the pointer in Compose.

The dialog's apply options independently control object time scaling, object
resnapping, slider-length resnapping and bookmark/preview-point adjustment. The
original red section determines which object is transformed, so moving a timing
boundary does not reassign objects before scaling them. FSlider scaling transforms
node times, handle time components and exact-curve reference scale while retaining
X. Object resnap translates a complete parent from its start. Length resnap changes
an imported slider's path length or scales an FSlider's complete duration, including
repeats. Imported sliders retain source samples and flags. Invalid edits roll back
the transaction. The chosen beat divisor is independent of Slider Tick Rate and is adjusted with
the draggable snap bar. Timing Panel uses blue textured controls and arrows;
setup uses flat arrows. Numeric values between arrows are centered, including
while editing.

The Timing menu has a **Time Signature** submenu with 3/4 and 4/4 presets, section/all-object resnap,
all-object time translation, slider-length recalculation, clearing all sections,
and setting the song preview point. **Reset Current Section** clears the active
red point for retiming; applying taps or entering BPM/offset restores a section.
An untouched cleared section is absent when saved. Clearing all timing uses the
editor's 120 BPM / 0 ms fallback until new timing is supplied. All content operations
participate in undo/redo; clearing and resetting have a confirmation panel.

## Workspace

The **View** menu groups grid and snapping, view navigation, display, movement analysis, and timing controls, with separators between groups.

The time–X canvas occupies the main area, read-only AR/CS/DPB are at the upper right, and time navigation is at the bottom. Select objects directly on the canvas. Playfield X spans `0..512`; time increases upward. Startup opens the Library without loading a demo beatmap. Open a beatmap set to enter the editor. The window title identifies the active difficulty as `Artist - Title (Mapper) [Diffname]`; the menu row does not repeat the project title. The compact **← Library** button at the top right returns to the library. Esc first dismisses an active menu, field, dialog, or gesture; otherwise it requests a return to the library. Unsaved changes prompt for Save, Discard, or Cancel before closing the editor; Cancel or a failed save keeps the editor open. See [Workspace](WORKSPACE.md) for navigation and position memory.

The Mapping header shows read-only beatmap AR, CS, and Distance Per Beat (DPB) in pixels. Catch Preview starts collapsed; the small button at the center of the canvas’s right edge opens it at the upper right. Drag the sidebar’s canvas-facing boundary to resize it, and use the edge button to collapse its group. See [panel docking](#display-settings) for moving and grouping sidebars. NM, Easy and Hard Rock select preview-only difficulty and position rules; see [Catch rendering](CATCH_RENDERING.md). The preview uses its effective AR for falling speed. The main canvas uses the beatmap's AR timing ratio; its **Zoom** slider changes the displayed width of X=0..512 and scales object sizes and time spacing together. Zooming out shows more notes vertically without changing their coordinates, map AR, or CS. The playfield stays horizontally centered, from a minimum **192 DIP** wide to the full available width with CS0 edge padding while locked. Percentages are relative to that available width; resizing preserves the zoom percentage except when the minimum width requires clamping. **Zoom initially defaults to 60%.** The lock beside its percentage keeps X and Y scaling uniform. Click to unlock: the Zoom area displays two independent rows, X above Y, each reaching 200%. Both sliders stay directly in the toolbar. Alt+wheel scales X and Y together around the pointer's time, including while unlocked; the manual toolbar sliders adjust only their corresponding axis. Middle-button dragging also pans horizontally when the enlarged playfield exceeds the viewport, with bounds including edge padding. Locking again aligns Y to X, caps the uniform scale at 100%, and recentres the playfield. Object sprite sizes, selection rings and their hit regions stop growing at 100%, while coordinate spacing continues to scale up to 200%. Scale values and the lock state persist across restarts; horizontal pan is session state.

Canvas zoom, upper object-timeline scale and Timing waveform scale are remembered independently across restarts and difficulties. **View → Reset view** uses the remembered canvas zoom and follows the playhead. Each wheel notch moves one full beat (1/1) during playback or one current Snap subdivision while paused: up to the preceding grid line and down to the following one, independent of zoom. Canvas scrolling shifts the viewport and playhead by the same relative amount, even while paused; each clamps at its bounds. Off-grid positions move to the adjacent grid line in the scroll direction, and BPM changes use the grid on the corresponding side of the timing boundary. High-resolution wheel input accumulates until it reaches one notch. Drag with the middle button to pan, and Alt+scroll to scale around the pointer's time. When paused, slider zoom preserves the viewport's center time; during playback, it preserves the play line.

`Catch Preview` offers 4:3, 16:9 and Fit display modes. Fit uses the entire available sidebar height, revealing more future notes as the window grows vertically. Drag the sidebar divider to adjust width. Objects retain their proportions, and an automatic catcher follows playback and seeking. Mode and Resolution controls stay above the picture; 4:3 and 16:9 pictures are centred in the remaining area. Caught fruit remains on the plate. Completing a combo group scatters the stack; seeking restores the plate effects.

The arrow to the left of the four tool buttons opens a whole-map **Movement strain** sidebar. Its default left dock is 280 DIP wide at normal window sizes. Time increases upward in its vertical curve, matching the canvas; an amber marker shows the shared playback position. Click or drag in the graph to seek continuously. The left sidebar and Catch Preview can be open together. A very narrow window may compress the sidebars to preserve at least 192 DIP for the central playfield. Sidebar visibility is session display state and does not edit the beatmap.

During playback and seeking, the play line stays at its configured height, initially 25% above the bottom of the drawing area. While paused, drag its leftmost yellow handle vertically to set that height between 5% and 95% of the drawing area. The current time stays unchanged and canvas content scrolls with the line. Playback disables handle dragging and uses the chosen height. The height is saved as a global view preference when the drag finishes and restored after restarting the editor. It is retained across resizing and difficulty switches without editing map content or undo history. Esc or lost capture cancels a drag. Left-button marquee selection on the canvas and object timeline keeps playback scrolling and accepts wheel navigation while held. Space or C can start or pause playback without releasing the selection. The start stays anchored to its original map time while the other end follows the pointer, so the box grows during scrolling even with a stationary pointer. Within 24 DIP of the canvas's top/bottom edge or the object timeline's left/right edge, a dragged selection automatically scrolls toward that edge, gradually increasing to 1200 DIP per second on the canvas or 600 DIP per second on the object timeline. Moving back inside, releasing the button, cancelling, or reaching the map boundary stops automatic scrolling. Objects inside the time range remain selected after they move outside the viewport. Paused middle-button panning is free; canvas wheel navigation preserves the current playhead-to-viewport offset, while playback and other seeking resume following.

The upper-right corner of the Mapping canvas shows the pointer's playfield X
coordinate. Selecting an object shows its X coordinate and **Prev / Next** DS
ratios instead. Multiple selections use chronological object order: X and Prev
come from the first selected object, while Next comes from the last selected
object's end. Missing neighbours show a dash. These readouts use map coordinates
and base SV, independently of canvas zoom.

### Playback pause snapping

In Compose, with beat snapping enabled, pausing through Space, C or the Pause button aligns
the confirmed audio position to the nearest current Snap grid line. An exact
midpoint selects the later line. The audio seek, playhead and viewport use the
same target, bounded by the audio duration. Note placement continues to use the
mouse's snapped time, as shown by the placement preview.

The alignment runs once after the pause is confirmed. A later seek, document
change, loading or audio failure cancels it. Stop returns to zero.
Timing mode preserves the exact confirmed audio position without snapping.
Testplay, automatic pauses for dialogs and disabled beat snapping retain their existing
pause behavior.

## Song Setup

Tags use a wrapped editable area. The dialog expands within the window for longer tag lists; the stored string remains a single metadata value. Clicking a wrapped row positions the caret on that row, and selection and clipboard operations span the complete value.

Editor text fields share one caret and selection model, including Song Setup,
Library search, settings paths, export names, time jump and numeric inputs.
Clicking places a blinking caret without selecting text; dragging selects a range,
and double-clicking selects the entire field. Ctrl+A (Command+A on macOS) also
selects all. Left/Right, Home and End move the caret; Shift extends the selection.
Backspace and Delete remove the selected range or one adjacent character.
Copy, cut and paste operate on the selected range and caret position. Keyboard
focus changes can request select-all explicitly, such as Tab between Song Setup
fields.

**Song Setup** is a dock tab, initially after Notes on the right. Use **Layout**
to show or close it, or press **F4** to select it. It can move between either side
and share a tab group with other panels. General, Difficulty, Colors and Design
use four horizontal category buttons; narrow panels abbreviate their labels.
Each category scrolls vertically. Valid edits immediately update the map and mark
the project dirty, preserving field focus. There are no Apply/Cancel buttons.
Use normal project saving and undo. Difficulty or document changes reload the
form from current content. Opening this panel does not pause playback.

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

Design selects countdown speed (off, normal, half or double) from four choices and
stores a non-negative countdown offset in beats. Switches control widescreen
storyboard support, letterboxing in breaks and the flashing-light warning. These
options are preserved in project saves and
`.osu` exports; the editor does not render countdown or storyboard effects.
Audio, Advanced and preferred-skin controls are not part of Song Setup.

## Settings

**Settings → Experimental** can enable 1/24, 1/32 and 1/48 snap divisions.
It also extends upper object-timeline zoom from 1.5 to 16 DIP/ms. Use its +/−
buttons or Alt+wheel to separate densely spaced circles. Disabling the preference
returns timeline zoom to its standard range.
The preference defaults to Off and takes effect after Apply. It extends beat snap,
Timing and Distance Snap controls, note beat-position detection, and Stream/Stack
conversion and Banana Shower length/gap lists. Disabling it returns active editor
snap controls to at most 1/16;
saved Stream/Stack content retains its division.

On Windows, **General → Display mode** uses a dropdown with a check beside the
current draft choice, offering **Vertical sync (default)** and
**Low latency**. The highlighted hint recommends trying this option for audio
delay or sound/picture mismatch. **Apply** saves the preference and switches
presentation immediately, including paused editing and Library; no diagnostic
launcher or restart is needed. Closing without applying discards the draft.
Testplay retains its low-latency presentation. Diagnostic display launchers
override this preference for their run. macOS does not expose this Windows setting.

**General → Fullscreen**, below Display mode on Windows, toggles fullscreen on
Windows and macOS. Apply saves and activates the choice; closing Settings discards
an unapplied choice. **Alt+Enter** switches immediately and saves the preference,
including in Library, Settings and testplay. Holding the keys switches only once.
The shortcut updates the fullscreen choice in an open Settings draft while keeping
other pending changes. Windows uses borderless fullscreen on the current monitor;
leaving restores the previous window bounds and maximized state. macOS uses native
fullscreen. New installations start windowed, and subsequent launches restore the
saved fullscreen preference. Fullscreen does not change beatmap content or history.

Settings Workspace, Audio and embedded Updates show their controls without the
workspace-path, skin-sound, automatic-save or restart explanatory paragraphs.

**Droplet Settings → Global Derandomize** contains two independently saved preferences.
First-time setup also offers both preferences as part of its draft, saved on Finish.
General retains a link and a notice directing randomization and derandomization to
Droplet Settings. The link preserves unapplied General preferences and returns there
when the dialog closes. With no open project, it shows only global preferences.
**Legacy → FSlider** sets the initial conversion choice in maps
without a saved choice. **New projects** controls projects created
from scratch or from audio. Both start On. With the new-project option Off,
new FSliders using Follow map randomize with Strength 20 and Seed 1337; with it
On, they derandomize. The map default survives reopening and is inherited by
additional blank difficulties. Imported difficulties retain their own settings.
Global defaults save immediately, including when the current difficulty is locked;
Cancel in Song Setup discards map drafts while retaining these global preferences.
Changing them does not modify existing maps or a saved Legacy conversion choice.

Settings pages share a full-width content column, 24 DIP page headings, 16 DIP section headings, 13 DIP labels, values and actions, and 12 DIP hints. Standard controls are 32 DIP high, with 12 DIP horizontal text padding, 4 DIP corner radii, and matching surface fills and borders. Field labels sit above inputs; related controls align within their rows. General uses 16 DIP gaps between related options and wider spacing around section boundaries; its content scrolls when the window is too short, with the page title and Apply fixed. Combo count and Dim Background in Testplay use compact buttons; the Background dim row matches their width. Navigation retains its larger click targets. The same logical dimensions apply on both desktop platforms and follow their interface scaling. Field and group labels are bold; control values keep a regular weight in both active and inactive states. Secondary text uses the muted colour. In General, Romanised artist / title and Language use fixed labels on the left with aligned, separate value buttons on the right.

The top-bar **Settings** button is available in both Library and Editor. Settings uses a left category sidebar and a right panel for Workspace, Appearance, Testplay, and Updates (when supported by the host). Switching categories retains pending path and key changes. **Apply** is enabled only while unapplied changes exist. Ordinary preferences can be applied during library scanning or searching; changes to library paths wait for those tasks to finish. It saves them, stays in the current settings category, and becomes disabled again; the top-right return button or Esc closes settings without applying those drafts. Esc first dismisses active text or key capture. Update preferences save immediately. Opening settings pauses playback and retains the editor document, undo history, selection, and viewport. **General → Romanised artist / title** defaults to On and controls Library cards, Library details, and the editor window title. Off prefers the Unicode metadata; either mode falls back to the other spelling when its preferred field is empty. Apply persists the preference without changing beatmap data, filenames, or search matching.

**Appearance → Movement indicator colours** provides separate Stand, Walk, Dash, and HDash swatches. Click a swatch to drag within a saturation/value palette and hue bar, or enter an exact six-digit HEX value, as in Song Setup → Colors. The selected colour appears beside the HEX field. Done keeps the choice in the settings draft; Cancel or Esc restores the colour from before the picker opened. These preferences colour Movement Analysis connections, the floating movement indicator, and Distance Snap reference regions; they do not change skin rendering or beatmap content. **Reset colours** restores the original silver, green, amber, and rose drafts. **Apply** saves the colours across launches; leaving Settings before applying discards draft changes.

## Testplay

**Settings > Testplay > Testplay speed** sets an independent starting speed from
10% to 150% in 5% steps (default 100%). Apply saves it across launches. F5 uses this
speed for music and gameplay; returning to the editor restores its previous speed.
Autoplay speed shortcuts affect the current testplay session, including retries,
without changing either the saved testplay preference or the editor speed.

Beatmap backgrounds fill the Catch preview and testplay viewport
without changing their aspect ratio. **Settings > Testplay > Background dim** and
the pause menu adjust the persistent 0–100% dim preference (default 90%). In the pause
menu, click or drag the fill bar behind the centered label to set the percentage;
the side buttons adjust it by 5%. Dragging updates the background immediately and
saves the preference when the drag ends. Settings uses the same fill bar, with changes
kept in the draft until Apply. The editing canvas has a fully opaque backing.

**View > Dim Background** and the matching **Settings > Testplay** switch
force an opaque black background in Catch preview and testplay, including intro
and break periods. The stored dim percentage is retained and used again when the
switch is off. View changes save immediately; Settings changes require Apply.

**Space** or clicking **Skip** skips an intro to three seconds before the first
note. Skip is unavailable after that point. **Esc / Ctrl+P** opens the pause menu;
**Up/Down** selects Continue, Retry or Back and **Enter** activates the selection.
Mouse buttons use the same actions. Opening the menu fades it in over 300 ms while
music and judgement are already paused. Continue fades the pause menu out over 600 ms,
then resumes music and judgement together. Esc cancels the fade back to the menu. Retry
restarts at the session's original lead-in position, resetting judgement and combo
while retaining autoplay. All retry shortcuts and menu actions then use the same
600 ms reaction transition as Continue, with music and judgement held at the start
until the transition completes. Back and F1 return to the selected editor position.
Hover smoothly enlarges buttons; keyboard selection shows two skin arrows.
The skin cursor and its trail appear only while paused. Running testplay hides
both the skin cursor and system pointer. Intro and break-end warnings use four
flashing arrows with the same seven-flash sequence: 100 ms visible, 100 ms fully
hidden between flashes. A resume transition also shows these arrows.

Pause loops and button feedback use the selected skin when skin sounds are enabled,
otherwise packaged osu! resources. Missing skin samples fall back to the default
skin and packaged resources. Explicit silent samples remain silent.

Breaks of at least 650 ms lighten the background by 30 percentage points, clamped
at zero dim. The background returns to the configured dim starting just after
325 ms before the break ends. Dim transitions last 800 ms with OutQuint easing.
The intro before the first note minus 2000 ms also uses the lighter background.
Breaks and background filenames are cached with the document conversion snapshot;
rendering does not parse Events or read image headers each frame.

See [skin rendering](../src/FruitsAtelier.App/Skinning/REFERENCE.md) for supported
Skip and pause assets, animation, scaling and fallback.

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

The upper-left corner groups the current map time and live 300/100/50/Miss counts above the star rating (when available), total drain time (minutes:seconds followed by total seconds in parentheses, for example `3:05 (185s)`), maximum combo and maximum droplets. Maximum combo counts Fruits and Droplets; maximum droplets counts TinyDroplets (50 judgements). Counts cover catches in the current session, with Miss counting missed Fruits and Droplets. Map totals cover the entire playable difficulty, including when starting partway through it. Drain time runs from the first object start to the maximum object end, including slider repeats and banana-shower duration, minus the break intervals within that range. Unmapped intros and outros are excluded. Unsupported storyboard load, timing-error and maximum-score statistics are omitted. This information panel uses the osu! English labels in every interface language. The shortcut hints appear below these statistics, including **Tab** (autoplay), **F3** (autoplay speed), **Ctrl+P** (pause/resume), **Ctrl+B** (add a bookmark), **F1** (exit to the testplay start), and **F2** (exit at the current position). During autoplay, F3 switches between 1.0x and 1.5x; from any other speed, the first press selects 1.0x. Holding F3 changes speed only once. Pausing freezes gameplay and music; resuming continues the same session.

Pressing **F3** in manual mode shows a brief reminder to press **Tab** before changing speed.

Press **Tab** during testplay to toggle autoplay. Pressing a bound left, right, or dash key
also returns to manual control. A centered fading banner announces either change.
**Ctrl+R** retries immediately from this session's start, including its configured lead-in, retaining autoplay and speed. It works while running or paused and triggers once per press. During autoplay, **Ctrl+Up/Down** adjusts speed by 25 percentage points; **Ctrl+Shift+Up/Down** uses 5 percentage points. Both work while paused, clamp to 10%–150%, and leave pause-menu selection unchanged. Manual play shows the existing autoplay-required notice instead of changing speed.

Each new testplay starts in manual mode. Losing window focus releases held keys while
testplay and its music continue.

The catcher body appears in front of caught plate objects in both preview and testplay.

The right-side preview and testplay animate fruit rotation and banana rotation/size;
the main editing canvas remains static. Fruit bases use beatmap combo colours when
present, otherwise skin colours; overlays stay white.

Caught fruit stacks on the catcher using the preview effects and releases at combo ends.
The last remaining note (after miss and plate animations) or the end of the music returns to the editor. **Esc** also exits. Playback stops and the playhead
returns to the position where testplay began. Gameplay does not change hit objects or selection; bookmark shortcuts update metadata through undo history. The playfield fits the full window while preserving its
aspect ratio; it reserves no space for navigation controls. Release Esc before
pressing it again to navigate from the editor to Library.

In **Library → Settings**, click the left, right or dash binding and press a supported key. See the [user manual](USER_MANUAL.md#testplay) for supported keys and reserved shortcuts. Esc cancels capture; choosing an already assigned
key swaps the two bindings. **Apply** saves the bindings across restarts.

## Object timeline and playback speed

The upper timeline also shows translucent Fruit and slider placement markers,
the candidate slider length in both Pen and Legacy modes, and the candidate
Banana shower interval. These previews use the canvas's snapped placement times,
disappear when the pointer leaves the canvas, and do not participate in selection
or change the document.

During object movement and slider or Banana endpoint dragging, playback keeps the
canvas following the play line, including when resumed with the pointer held.
Holding an object enters dragging as soon as playback or wheel navigation moves
its time beneath the pointer, even without mouse movement. Paused dragging keeps
the viewport stable until wheel navigation. Release commits one
edit; Esc or lost capture restores the gesture's starting content.

In the upper object timeline, clicking a slider head, tail, or reverse marker selects that edge for Whistle, Finish, and Clap edits. Only the selected edge marker is highlighted. Clicking the slider body selects the whole slider for sound edits. Head/body movement and tail repeat resizing retain their existing drag behavior.

The bottom overview shows red and green timing points above a white center line, continuous yellow kiai intervals and white break intervals centered on that line, and blue bookmarks extending down from it. The center line is behind the break and kiai intervals, which are behind timing points and bookmarks. These timeline marks use 80% opacity and colors tuned against osu!legacy. Hovering over the overview reveals a fixed bookmark toolbar above its left edge with Add, Remove, Previous, Next, and Reset actions. The toolbar stays visible while moving from the overview to its controls, and its tooltip appears above it. The toolbar uses an ImageGen-created background texture. The time display and separate Play, Pause, Stop, and Testplay controls sit to the left of the overview; Stop pauses audio and seeks to the start. Ctrl+B adds a bookmark at the playhead; Ctrl+Shift+B removes the nearest bookmark within two seconds. Ctrl+Left/Right seeks to the previous/next bookmark; Ctrl+Shift+Left/Right moves selected objects one X unit. Ctrl-click adds or removes a bookmark at the clicked time; clicking within five pixels of an existing bookmark removes it. Shift-drag across the overview adds a break interval, and right-click inside a break removes it. These edits are undoable and persist in the `.osu` `[Editor] Bookmarks` and `[Events]` sections. Esc cancels an in-progress break drag.

Both timelines draw nonnegative `[General] PreviewTime` as a full-height yellow line and bookmarks as blue lower lines. Preview lines draw before timing points, the playhead and bookmarks, so overlapping markers remain visible. Red and green timing points meet the overview center line and also appear on the upper object timeline. Short overview spans retain a minimum two-DIP width. Break intervals are clipped to each visible range and shaded across the full height or width of the object timeline and canvas left time axis. The Break label appears only on the object timeline; the canvas left axis shows red timing labels, blue bookmark labels, and ordinary time labels in the default muted color. Red timing and blue bookmark lines span the canvas width like the playhead line. Markers sharing a pixel row are grouped for display, with spaced labels and hover details showing counts and time ranges; stored timestamps remain unchanged. The kiai fill uses a lighter orange. The Timing menu sets the preview point at the rounded playhead time through undo history. Insert Break Time sits between Movement Analysis and Snap; it inserts an undoable interval between the surrounding source objects, starting 200 ms after the previous object ends and ending when the next object's AR approach begins, if at least 400 ms remains and no break overlaps it. Double-click an upper timeline object to select it and seek to its start without editing content. During a slider endpoint or timeline-tail drag, wheel navigation, Ctrl+wheel Snap, Alt+wheel zoom and Space/C playback remain available; release commits one edit and Esc restores its starting content. The Snap slider remains on one row. The time display uses a fixed position for the duration so changing digits do not move it. The bookmark toolbar is left aligned and vertically centered in the strip above the overview; its buttons are inset from the panel edge and remain visible while the pointer is held over the overview.

Drag the divider below the upper object timeline or above the bottom overview to
adjust their heights. Each ranges from 50% to 100% of its original height: 32–64
DIP for the upper timeline and 20–40 DIP for the overview. The timestamp and
playback controls retain their sizes; the transport row always reserves at least
the controls' 32-DIP height. Upper timeline markers and their hit regions shrink
together. Releasing saves the heights as view
preferences; Esc or lost pointer capture restores the previous height. Resizing
does not edit beatmap content or create undo entries.

During a kiai interval, a small badge appears at the upper-left of the editing plot. It brightens at the interval start and on every full beat from the active red timing point, then fades through the beat. Green timing points that preserve the kiai state do not restart the pulse. Its pulse follows map time, including seeking and timing edits.

Adding, pasting, moving, extending or removing notes around an existing break recalculates the affected interval when the edit commits. Breaks leave the next object's AR preempt time clear and resume at least 200 ms after the preceding object ends, including all slider repeats and banana-shower duration. An occupied break is shortened, split, or removed; generated pieces shorter than 650 ms are removed. Removing the note that split a break merges its remaining pieces. Deleting the last objects in a bounded section also creates a break for a newly vacated gap with at least 650 ms after the preceding object's recovery and before the next object's approach, even when no existing break remains. Overlapping occupied intervals prevent a break. After object-time or duration edits, breaks before the first object or after the maximum object end are removed, including manually resized intervals. Approach-rate edits reconcile breaks against the new approach time. Extensions use the maximum preceding object end so overlapping long objects remain clear. Unaffected valid break boundaries and other Events data are preserved. The note change and break adjustment share one undo step, and saving or exporting retains the updated intervals.

In the object timeline and canvas left time axis, each break has a grey core and lighter white and green transition regions extending to the adjacent source objects, without changing the stored break timestamps. Hovering a core edge in the object timeline shows a horizontal resize cursor. Dragging that edge snaps to the current beat subdivision when Snap is on and previews the new range; releasing commits one undoable edit, releasing with less than 400 ms remaining removes the break, and Esc cancels the preview.

Fruit and slider shapes on the upper object timeline use standard osu! circle and slider-endpoint skin resources, tinted with the beatmap or skin combo colour. Numbers use the skin's HitCircle font and overlay-order setting; missing resources have geometric/text fallbacks. Banana showers remain gold. Each object's body, endpoints, reverse arrows and number occupy one chronological layer: earlier starts cover later starts, with earlier ends and source order breaking ties. Selected objects retain this order and show orange rings with blue outer rings. Clicking and deleting use the same front-to-back order. Slider tracks honour SliderTrackOverride and have no separate perimeter; their white edges come from the endpoint artwork. Circle textures account for legacy transparent padding, and HitCircle font glyphs use the legacy 0.8 scale. Timing point markers use the same time-to-X coordinate as note centers. Insert Break Time uses the same text size as Movement Analysis; the Mapping header places AR, CS, and DPB in fixed columns on one baseline.

In testplay, Ctrl+B adds a bookmark at the live testplay time and Ctrl+Shift+B removes the nearest bookmark within two seconds. These commands enter undo history and consume the B key before gameplay input.

Newly opened maps start at 0 ms from their first frame. While audio loads, the total duration displays a placeholder and the overview waits for the final range before becoming interactive. If audio is unavailable, the overview uses the map duration. Switching back to an open difficulty retains its playhead and resumes the audio at that position when loading completes.

Library and Editor use the same 40-DIP header, logo geometry, 28-DIP button height, and navigation button positions. Difficulty tabs remain below the Editor header.

The row beneath Zoom spans both the left tools and canvas columns. It is a horizontal object timeline centered on the current playhead, with centered object numbers that restart at 1 on New Combo. Circles show source objects in time order; capsules show complete slider and banana-shower durations. The ruler and canvas use the current beat subdivision with osu! beat-snap colors: white for full beats, red for halves, purple for thirds/sixths, blue for quarters, yellow for fifths/sevenths/eighths/ninths, and grey for finer subdivisions. Classification uses the reduced fraction, so a half-beat stays red on a 1/12 grid. Full beats, halves and thirds have thicker lines; measure starts are strongest and follow the active red timing point’s meter and offset, including meter changes. See the [osu! beat snap divisor reference](https://osu.ppy.sh/wiki/en/Client/Beatmap_editor/Beat_snap_divisor). Slider repeat boundaries show a circle with a right-facing reverse arrow; the head shows its combo number and the final tail stays empty. The arrow uses reversearrow.png from the current skin (preferring @2x), with a geometric fallback when unavailable. Click an object or duration body to select its parent without moving the playhead; Ctrl-click toggles selection; Shift-drag bypasses beat snapping. Drag an object head or duration body to move the selected parents in time, preserving their X positions and relative timing. Beat snapping applies to the earliest selected start; release commits one undo step and Esc cancels. Click empty space in the object timeline to clear selection without moving the playhead. Drag empty space in the note row to box-select parent objects; Ctrl/Shift adds to the selection. On both the canvas and object timeline, right-click a note to delete it, or delete the selected group if that note is already selected. Esc cancels a box selection and deletion is undoable. Scroll up steps to earlier times and scroll down to later times, using full beats during playback and the current Snap subdivision while paused; Alt+scroll or the +/− buttons changes this timeline's scale independently of canvas Zoom. Finish an active drawing draft before selecting or dragging in this row.

A single click on empty canvas space clears selection without changing playback time, both paused and playing.

The timestamp block sits left of Play, Pause, Stop and Testplay on the same transport row, followed by the overview timeline. Click the current timestamp to open Jump to time with its value selected. Copy/Paste buttons and Ctrl+C/Ctrl+V (Cmd on macOS) use the system clipboard; Ctrl+A selects the full input. Enter or Jump seeks without beat snapping, and Escape cancels. Inputs accept `mm:ss:ms`, milliseconds, or an osu! timestamp reference such as `03:03:311 (2,3) -`. Invalid input keeps the dialog open; times beyond the track clamp to its end. The dialog blocks background editing and does not change beatmap content.

The bottom status bar places a static **Playback speed** label and six always-visible buttons immediately left of **Volume**. The buttons select **10%, 25%, 50%, 75%, 100%, and 150%** playback speed with one click; the current preset is highlighted. Only song tempo changes, with pitch preserved. Hitsounds keep their original pitch and real-time duration, with trigger times mapped to the music clock. Changing speed preserves the map position and play/pause state. This setting does not edit or export beatmap timing.

In the editor, the status-bar **Volume** button or **View → Volume** opens three vertical bars, ordered Master, Music and Effect from left to right, from 0% to 100%. Alt+Left/Right selects a channel; Alt+Up/Down opens the controls and changes the selected channel by five percentage points. The shortcuts also show the controls during testplay and take precedence over catcher movement. The controls fade in over 120 ms, wait 800 ms after interaction, then fade out over 150 ms. Hovering, dragging or holding an adjustment key keeps them visible; Esc dismisses them in the editor. Changes apply immediately and persist after mouse release or a keyboard adjustment. Master multiplies both channels; Music and Effect independently control music and preview/testplay samples. Opening the controls preserves active drawing drafts. Muting does not pause playback or change the beatmap.

During active testplay, plain Up/Down opens the volume controls and adjusts the
selected channel by five percentage points only when the key is not bound to
catcher movement or dash. The pause menu retains Up/Down navigation. Catcher
movement keys do not extend the volume controls' inactivity timeout.

## Tools and selection

Canvas box selection includes an object when its centre lies inside or on the
selection rectangle. Slider and banana-shower parents are selected when an
eligible child object's centre is enclosed. Selection is independent of sprite
size and skin artwork.

The left palette has equally sized Select, Fruit, FSlider, and Banana buttons with transparent outer margins. The four-button group is vertically centred beside the canvas plot. The active icon is fully opaque; the other three use 45% opacity. Labels share one font size. Clicking FSlider starts placement; B also enters control editing for a selected slider. Finishing placement keeps the current tool active.

Snap offers 1/1, 1/2, 1/3, 1/4, 1/5, 1/6, 1/7, 1/8, 1/9, 1/10, 1/11, 1/12 and 1/16; the default is 1/4.
Enable **Settings → Experimental** to add 1/24, 1/32 and 1/48.

### Slider Inspector sections

Slider Inspector groups its actions into compact **Timing & snapping**,
**Conversion**, **Droplets** and **Structure** tabs. Click a section title to
collapse or expand its controls. The editor retains these choices during the
session, including when selection changes. Collapsing a section does not edit
the document or create an undo step. Merge shortcuts expand Structure so the
requested controls remain visible.

### Independent slider endpoint snapping

The global beat-snap control includes 1/10 and 1/11. For one selected slider,
**Slider Inspector → Independent endpoint snapping** enables a slider-local
start and end grid. Each endpoint offers **Auto (nearest grid)** or a fixed
1/1–1/12, 1/16, 1/24 or 1/32 division, including when experimental divisions
are disabled. Auto chooses the nearest position across those grids. Choosing
a fixed division snaps that endpoint immediately and retains the other endpoint.
The setting is saved with the slider and can be undone.
Inspector also reports each endpoint's actual reduced fraction within its beat.
Selected sliders show these start/end positions beside their canvas endpoints,
with NC and snap labels on separate lines. Coincident osu! slider endpoints use
separate lines for their start and end labels. Positions refresh after edits,
undo/redo and timing changes; a position over 2 ms from every supported grid is
shown as **Off grid**. Whole beats are displayed as 0/1.

Endpoint dragging uses the slider's own grid independently of global beat snap.
Dragging the whole slider uses its start grid while preserving its duration.
Dragging its upper-timeline tail changes duration while retaining the repeat count;
the end grid refers to the final traversal's end. Ordinary sliders retain their
existing global-snap and repeat-tail controls. FSliders are edited in Catch and
Legacy sliders in the osu! canvas. Disabling independent snapping returns the
slider to the global grid without moving its endpoints.

Fruit and FSlider placement display a 60%-opaque fruit under the pointer, with its time snapped to the current beat subdivision. Hover previews recalculate incoming and outgoing hyperdash markers, including the unconfirmed slider endpoint in both editing modes. This temporary calculation affects canvas markers only; it does not add playback sounds, change saved content, or enter undo history. Fruit left-click places immediately, replacing all existing parent objects whose start times are within ±2 ms of the new fruit, regardless of horizontal position. Slider tails and nested fruits do not independently trigger replacement. Replacement and placement form one undo step; hover hyperdash previews use the same replacement rule. In Fruit or FSlider mode, right-click empty canvas toggles **New combo** for the next object. While drawing an FSlider, right-click retains its point-removal and curve-completion actions. In Fruit mode during playback, right-click arms/toggles New combo even over a note; when paused, right-click on a note deletes it. The combo flag survives project saving, `.osu` export, copying, and undo/redo. It resets after placement or changing difficulty.

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
| Ctrl+L | Confirm restoring the current difficulty to its previous saved version |
| Ctrl+Shift+I | Insert a control point on the curve under the pointer |
| Ctrl+D | Clone selected parents one measure after the last selected start |
| Ctrl+A | Select all objects |
| Ctrl+H | Flip selected parents horizontally around X=256 |
| Ctrl+Left / Right | Seek to the previous / next bookmark |
| Ctrl+Shift+Left / Right | Move selected parents by one X unit |
| J / K | Move selected parents backward / forward by one beat subdivision |
| Ctrl+1 / 2 / 3 / 4 | Set grid size to 4 / 8 / 16 / 32 |
| Shift+1…9 / Ctrl+M | Set beat subdivision directly / cycle 1/3, 1/4, 1/6, 1/8 |
| C / Space / X | Pause or resume / pause or resume / play from song start |
| Z / V (also End) | Jump to the first object's start / last object's end; repeat to reach song start / end. Z/V also work during box selection and object dragging, following the same selection and time adjustment rules as scrolling. |
| Left / Right (Shift for 4×) | Seek backward / forward by one beat subdivision |
| Up / Down | Seek next / previous timing point |
| Ctrl+Up / Down | Increase / decrease playback speed by 25 percentage points, within 10%–150% |
| Ctrl+Shift+Up / Down | Increase / decrease playback speed by 5 percentage points |
| Ctrl+Shift+F | Catch: Stream/Stack dock; osu!: convert slider to notes with X/Y |
| Ctrl+Shift+R | Open Rotate by |
| Ctrl+comma / Ctrl+period | Rotate selection −90°/+90° in osu!, −5°/+5° in Catch |
| Ctrl+= / Ctrl+− | Increase / decrease UI scale |
| Ctrl+J | Extend the selected FSlider to the pointer |
| Esc | Cancel an active drag, box selection, draft, or text input |

Mac accepts both Command and Ctrl shortcuts; Delete and Backspace both delete objects.

A slider's Fruit, Droplet, and TinyDroplet share parent selection. Selecting several children counts as one slider. Even with curves hidden, actual objects can select their complete track.

In Select mode, a single selected slider or at least two consecutive selected
parents displays a teal bounding box with a light background and left/right resize
handles. Consecutive means no omitted parent in start-time/source order; a selected
or intervening banana shower blocks the box. Bounds enclose the displayed Fruit,
Droplet and TinyDroplet circles including their radii, excluding curve controls.
Time bounds cover complete parent durations, including slider repeats. The resize
handles are small squares with larger pointer hit areas.
Drag a side handle to scale X positions and slider geometry around the opposite
side, keeping times, repeats and sprite sizes unchanged. Grid Snap applies to the
moving edge; individual events do not distance-snap. Handles stop before crossing
the opposite side and at the playfield boundaries; stored stack width and adjustment
limits also cap expansion. A zero-width pattern can move
but cannot resize. Drag inside the box to translate all selected parents using the
ordinary group-movement snap rules. Translation stops when the leftmost or rightmost
generated Fruit or Droplet reaches X=0 or X=512; curve handles may remain outside
the playfield. With droplet selection unlocked, droplets and
tiny droplets in a single selected slider take priority over box movement and use
individual-event reshaping. A nearby curve control takes priority when it is at
least as close to the pointer, unless that droplet is already selected. Visible
slider controls and box resize handles retain priority over box movement.
Other points inside the box move the selected parents.
Ctrl selection, clicks on event details and long-press actions remain available.
For one selected slider, holding anywhere inside its box opens its action group;
moving the pointer cancels the hold and continues the normal drag.
Each drag is one undo step; Esc or lost capture restores its starting content.
Legacy Sliders convert to FSliders on the first movement. Horizontal resizing
converts exact circular arcs to bounded Bezier approximations in the same transaction.

Undo and redo reveal an affected position only when none of the affected positions is visible on the canvas. Deletion uses the removed position; restored notes and changed slider nodes use their resulting positions. Visible changes retain the viewport.

Copy and cut write a legacy reference such as `02:27:094 (1,2,3) - ` to the system clipboard. Inside the editor, a separate snapshot retains complete objects for pattern pasting into the same difficulty session. Other difficulties and reopened projects cannot receive that pattern. Paste aligns the earliest start to the playhead, preserves other objects' relative times and positions, and assigns new IDs. Each batch move, delete, cut, or paste is one undo step.

## Distance spacing and object flags

The right toolbar contains New Combo (Q), Whistle (W), Finish (E), Clap (R), Grid Snap (T), Distance Snap (Y), and Lock Notes (L). The seven square buttons use generated icons with the same charcoal fill, mint outline and 100%/45% active/inactive opacity as the left palette. In short windows, scroll over the right palette to reach all buttons. Lit buttons indicate enabled values; a dash indicates mixed hitsound values in a selection. Hover a button for its shortcut or the targeted slider edge. Hover Distance Snap to reveal **Configure DS…** on its left, using the same hover-to-flyout interaction as FSlider.

Y toggles distance snapping; holding Alt temporarily inverts that toggle. The upper-right Snap slider always controls beat subdivision, including while Alt is held. For fruit placement and whole-object movement, Grid Snap applies horizontal grid rounding after distance snapping; holding Shift temporarily inverts Grid Snap unless Alt is held.

Distance snapping places fruits and the first slider draft point relative to the previous source object, and moves a selected group by a shared offset based on its earliest object. Beat snapping still determines time. All DS calculations use `DPB / beatLength` as the 1.0x reference speed (green timing speed fixed at 1.0x), ignoring inherited slider speed. Horizontal DS is `abs(deltaX) / (deltaTime × referenceSpeed)`. Snapping and readouts sample this speed at the departure endpoint's time; slider references use their final endpoint after all spans. Cross-timing gaps retain that departure speed. BPM and the difficulty's DPB still determine the reference speed. Banana showers do not supply horizontal references. Overlapping or simultaneous references have no positive spacing interval. Distance snapping chooses the closest valid X among both sides of every configured multiplier and an implicit **0 DS** (the reference endpoint X). Zero DS is always available when snapping is enabled and does not consume a configuration slot. An empty configuration uses only zero DS.

Drawing and reshaping a slider uses beat and horizontal grid snapping independently of DS. Slider endpoints, control points and Bezier handles are not constrained by DS presets or by the spacing of generated Fruit/Droplet events. Their X coordinates may extend beyond the 0–512 playfield while dragging or editing numeric values. The first draft point still distance-snaps relative to the previous source object. A completed slider remains selected in Slider mode, so its tail can be dragged immediately.

When a Select-mode drag is outside the bounding box and visible slider controls, horizontal dragging of a slider head or tail keeps its time and strictly snaps relative to the preceding large event in the same slider, including zero DS, even on a curved slider. Droplet dragging also follows these rules inside the box. Endpoint candidates account for the preceding droplets that move with the reshaped curve. A selected TinyDroplet instead snaps its displayed X relative to the preceding event in the same slider, including other tiny droplets. Its compensation policy still applies. Following events impose no DS preset or maximum-distance requirement on a selected-object drag. With no preceding reference, dragging follows horizontal Grid Snap. Candidate positions are tried in pointer-distance order; unrepresentable candidates are skipped, and if none is available, the last accepted position remains unchanged. DS takes precedence over horizontal Grid Snap when a preceding reference exists.

Changing DS settings, BPM, or tick rate does not reshape existing sliders.

**Configure DS…** opens a two-column modal. The left side has an upper 1.0x DPB section and a lower four-colour Stand → Walk → Dash → HDash distance reference and an initially empty collection of up to eight non-negative DS multipliers. **Add**, beside HDash, creates an arrow at 0 DS. A left-click on empty space in the arrow row creates an arrow at the clicked distance, rounded to 0.1 steps or 0.01 with Shift. Both respect the eight-preset limit; clicking an existing arrow starts its drag. Drag an arrow to adjust its value in 0.1 steps, or 0.01 while Shift is held. Right-click an arrow or its DS value badge to remove that preset. Esc or lost capture cancels the active drag. Each arrow shows its DS value above it; nearby labels stack to stay readable. The values below the reference are read-only and displayed from smallest to largest in outlined badges, wrapping when needed. Each badge border uses the colour of its current Stand, Walk, Dash or HDash interval. All configured multipliers participate simultaneously, along with implicit 0 DS. **Apply** stores the sorted list and base DPB on the current difficulty as one undoable change; saving the project retains it in the difficulty file. New and previously unconfigured maps start empty. Switching difficulty restores its own list. **Cancel** discards the draft. The stored DistanceSpacing value is retained for project and osu! file compatibility but does not provide a hidden snap target. Custom DS lists are not exported to osu! files.

**Collinear snap**, in its own row below the presets and a horizontal divider in Configure DS, is on by default and is saved with the current difficulty through Apply/Cancel and undo. While DS is active, it adds a candidate at the current time on the straight line from the preceding object's final tail to the following object's head. The nearest candidate wins alongside the DS presets and implicit zero; missing neighbours or overlapping intervals add no candidate. Placement and whole-object dragging use this candidate, excluding dragged objects from the neighbours. With Grid Snap enabled, the chosen X rounds to the nearest horizontal grid position; with Grid Snap disabled, it retains the exact collinear coordinate. Hover over the row for a detailed explanation of neighbour selection, DS candidates and Grid Snap. The preview canvas uses the draft option immediately. This option does not constrain a slider's internal curve or add a shortcut.

**Object snap**, below the Configure DS preview, is disabled by default and operates independently
of Distance Snap. Fruit placement and the first point of either slider tool align X
to a nearby fruit, including slider heads, tails, repeats and Stream/Stack fruits,
while retaining the placement's Beat Snap time. Droplets, tiny droplets and bananas
are not targets. **Earlier time** searches two beats before the placement time by
default; **Later time** searches one beat after it. Each range accepts 0–4 beats;
zero disables that direction's reach beyond the placement time. The window follows
BPM changes, counting partial beats across timing boundaries. One unit is one
full beat (a 1/1 Snap interval), independent of meter and the selected snap divisor.
Objects need not be visible to be candidates. A target must be within six screen DIP horizontally
of the raw pointer. The nearest X wins, then the nearest time, then the conversion's
stable event order. An object snap retains the target's exact authored X and takes
priority over Grid Snap and Distance Snap. A mint guide highlights the target when
visible and shows horizontal alignment. Subsequent slider control points retain
their existing placement rules.

Whole-object canvas dragging uses the first and last selected
fruit in chronological order as probes; a single fruit has one probe. For a slider,
the last probe is its final repeat endpoint. Selected source parents are excluded
from targets, including all their nested objects. Each probe searches around its
moved time using the configured earlier/later beat ranges. The smallest horizontal
correction wins, with time distance breaking ties. The whole selection receives
one X offset, preserving spacing, curve geometry and the drag's Beat Snap time
offset. Candidates that would move any selected playable object outside X=0–512
are skipped. While DS is active, object alignments, DS presets (including zero)
and the collinear position compete by horizontal distance from the raw pointer.
Object alignments use the complete configured time window without a six-DIP
capture limit; this keeps the candidate order stable during a horizontal sweep.
Equal corrections prefer an exact object alignment; remaining object ties use
time distance and stable event order. DS and collinear positions follow Grid Snap
before the distance comparison; object alignments retain their exact X and show
the alignment guide.
With DS inactive, object alignment retains the six-DIP capture limit.
Timeline dragging, scaling and individual
slider control/event edits retain their existing rules. Apply saves the switch and
ranges as per-difficulty editing preferences; Cancel discards their drafts. These preferences survive restart
without changing beatmap content, undo history or exported files. The DS preview
continues to demonstrate distance and collinear snapping.

The upper-left section sets base SV or its equivalent 1.0x Distance Per Beat (DPB), with SV limits 0.4–3.6 (DPB 40–360 px). New difficulties default to 192 px with SliderMultiplier 1.92; imported difficulties initially show `100 ×` their stored [Difficulty] SliderMultiplier. Changing the base value updates DPB and effective SliderMultiplier together while preserving existing slider playback through validated inherited SV compensation. The DPB override is saved in `.catchproj`; `.osu` export uses the effective SliderMultiplier and compensated timing. Drag the Stand/Walk/Dash/HDash bar to choose a whole-pixel value. The bar uses the same four equal Stand/Walk/Dash/HDash regions as the preset reference below; its 1.0x marker and drag positions use the same movement distance limits at the selected beat subdivision. Both dragging and numeric entry enforce the SV limits, independently of the selected reference Snap and timing. A separate Grid Snap switch row and the 4/8/16/32 px level row below the numeric field control only DPB drag rounding; they start from the editor grid settings on first opening and then remain independent for the editor session. The level row is dimmed and unavailable while this Grid Snap switch is off. With Grid Snap enabled here, dragging moves in the selected step; the numeric field accepts any value in range, including decimals. The base distance is independent of BPM, while its speed in milliseconds follows the active red timing point. Existing preset multipliers rescale with draft DPB changes so their pixel distances stay fixed; the preview uses the draft DPB immediately. Apply stores the base distance and rescaled presets together as one undoable difficulty edit; Cancel leaves both unchanged.

The right side is a fixed four-beat canvas: Snap 1/4 shows 16 intervals and 17 horizontal lines, including the white closing line at the end of the fourth beat, which also accepts fruit placement. Time increases upward. Left-click to place fruits with beat and distance snapping relative to the preceding preview fruit; right-click a fruit to remove it. A hollow marker shows the next placement. Each consecutive pair shows its actual DS in a translucent black label centred on the connection midpoint, including zero. **Show DS**, beside Reset, toggles all preview DS labels while retaining fruits, connections and skin-coloured hyperdash starts. It defaults to off and retains its choice while the editor remains open. Its border matches the DS interval colour used by the preset badges; simultaneous fruits show a dash because their DS is undefined. A DS label is red when its value is no longer among the available presets (including implicit zero). A fruit that starts a hyperdash uses the skin HyperDashFruit colour. Classification uses the complete preview sequence and includes the prospective placement, updating when fruits are added or removed. This canvas does not pan or scroll, and it does not create sliders. Preview fruits are temporary and never become beatmap content, even when applying the DS configuration. The preview Snap slider starts at the editor's current divisor and offers the same fixed choices: 1, 2, 3, 4, 5, 6, 7, 8, 9, 12 and 16. It updates the reference and grid without changing editor Snap or existing preview fruit beat positions. Esc or lost capture cancels an active Snap drag. BPM is taken from the playhead at dialog entry and has no separate control. **Reset**, beside the preview Snap slider, clears all placed preview fruits without changing presets or Snap. Reopening resets the preview.

Each reference interval occupies one quarter of the track. Transitions use the entry BPM and preview Snap, document CS and base slider velocity, assuming a fresh departure from the preceding note centre without prior movement carry-over. Vertical ticks beneath segment boundaries show transition DS. Shorter ticks from the Stand, Walk and Dash segment centres show the midpoint of their physically available horizontal-distance intervals, capped at 512. HDash has no midpoint tick. The active arrow and its value are highlighted. Values beyond the reference range appear at its right edge. A segment with no interval inside that range shows a dash. The reference uses standard Catch movement timing, including the hyperdash quarter-frame allowance; actual movement classification retains full-sequence context.

**Prev / Next** shows horizontal DS with two decimal places in the floating movement panel. DS divides horizontal distance by the time interval multiplied by `DPB / beatLength` at the departure object's time; inherited green-point speed is ignored. Selecting a fruit or an individual slider child targets that converted object and its adjacent Fruit/Droplet neighbours. Slider children use the two-stage selection described below. Selecting a slider as a whole uses its head for Prev and final tail for Next; placement previews use the snapped candidate. DS previews and selected-object DS readouts use authored time and X precision; movement and hyperdash previews retain exported gameplay rounding. With no selection or placement preview, the readout is hidden. Readouts update during dragging and do not depend on zoom or window size. Missing or zero-duration intervals show a dash.

With a single fruit or slider child selected and a valid preceding neighbour, click the movement or DS area of the floating panel to edit **Prev DS**. A range slider appears on the left and a numeric input on the right; Next remains informational. Numeric entry previews changes immediately and accepts up to two decimal places. The slider previews in 0.1 steps, or 0.01 while holding Shift, and its range extends from zero to the furthest position on the original side within X=0..512. Initial and slider-updated numbers display two decimals. Enter or clicking outside commits the whole adjustment as one undo step; Esc or interrupted pointer capture cancels it. Invalid input retains the last valid preview and must be corrected or cancelled. The selected object's time stays fixed and its X stays on the original side of the reference, including after previewing zero. An out-of-field result is rejected rather than clamped or flipped. Horizontally aligned objects cannot establish a new direction through a nonzero DS input. Lock Notes disables editing.

The bottom row displays a compact readout such as **X: 185** for the selected fruit or slider child, or the placement preview. Clicking anywhere on this row opens the numeric input for a selected object, including the first object without a preceding neighbour. Confirming or cancelling restores the readout. Row clicks do not reach the canvas or seek time, including when editing is disabled. X uses no slider and clamps numeric input to `0..512`. It displays rounded whole numbers, accepts integer input only, previews immediately while preserving time, and uses the same confirmation, cancellation, undo, and Lock Notes behavior as DS editing. Slider children reuse or insert a first-span anchor; imported sliders become editable FSliders in the same transaction.

**View → Include tiny droplets** controls whether Movement Analysis connections and DS labels include tiny droplets. It starts off and only changes the analysis display; gameplay hyperdash indicators, snapping, selection locks and beatmap content remain unchanged.

Slider SV and maximum DS are available in Slider Inspector. The movement panel
shows the previous/next object indicators without a separate slider speed badge.

For a clicked or selected tiny droplet, Prev and Next display movement classifications and DS ratios to its immediate neighbours, including other tiny droplets. A missing neighbour or zero time interval displays a dash. These tiny-droplet distance values are read-only.

With droplet selection unlocked, clicking a droplet or tiny droplet displays its X coordinate, including when the click selects its parent slider. Clicking that child again selects the specific event and shows a bright outer ring above curves and movement lines. The coordinate readout alone does not change selection, dragging or the parent geometry. Lock droplet selection blocks droplet selection and coordinate inspection. In Select mode, a click closer to the droplet than nearby controls selects the droplet; an already selected droplet remains draggable after its curve gains an anchor. Horizontal dragging changes the selected event's X and recalculates the curve. Droplets of the same kind that share that path sample on repeated traversals move together; other events remain fixed, and all event times are preserved. The curve between the adjacent event samples becomes linear, replacing any intervening anchors and handles while retaining the curve outside that interval. TinyDroplet sampling follows the slider's compensation policy. Unreachable positions clamp to the last valid X. Content dragging starts after at least 6 DIP of pointer movement on either axis.
Playback alone does not start a drag; an already started drag follows playback.
One drag is one undoable edit. Dragging a selected Legacy Slider droplet or tiny droplet converts its parent to an FSlider automatically within the same undo step. The ring follows live edits and is visible with Movement Analysis on or off.

For selected slider children, DS editing inserts or reuses an anchor at the child's first-span time and reshapes the curve locally. A reference in the same slider is anchored too so its position remains fixed. Editing an imported Legacy Slider child converts its parent to an FSlider within the same transaction. Repeated spans share the edited first-span geometry. Conversion must retain the selected child's time and achieve the requested position; unsupported or unreachable changes show an error and roll back.

A compact movement panel uses a consistent font size, aligned left/right readouts, and a dark translucent background. Its default position is the bottom centre of the editing playfield. Drag the panel to move it within the canvas; right-click it and choose **Reset position** to restore the default. Releasing a click without dragging opens its numeric controls; pressing or dragging the panel keeps the readouts visible. Active numeric controls retain their editing gestures. Moving the panel does not resize the canvas or change beatmap content. Its movement and DS area opens Prev DS editing when a valid single object is selected. Multiple selected parents show read-only incoming distance at the earliest selected start and outgoing distance at the latest selected end, excluding selected parents from neighbour lookup. Prev and Next DS values highlight when their absolute difference is at most 0.02, even when the time intervals differ. Fruit/slider placement previews and their panel hide outside the canvas; the panel remains usable while directly editing its controls. Fruit/slider placement and single-object selection show the incoming Stand, Walk, Dash, or HDash connection, using the Mapping Canvas indicator colours, with the outgoing state as a secondary label. The pointer measures horizontal distance on a linear scale; the right edge represents 1.5 times the larger of the current Dash threshold and Stand range and larger distances saturate there. Stand checks each connection independently: the target must be within the catching half-width of the departure centre, including the boundary, using current document CS. It takes precedence over movement requirements; it does not infer a shared standing position for a pattern. Outside that range, HDash follows the complete converted Fruit/Droplet sequence and current document CS. Walk estimates travel from the departure centre to the target catching edge, capped at the HDash boundary; it is not a guarantee for arbitrary catcher positions. TinyDroplets and Bananas do not supply connections. Missing or simultaneous connections show a dash. Playback speed, zoom, and Distance Spacing do not change classification.

**Slider Path** toggles slider path visibility. Its label stays fixed and the button highlights while enabled, matching the Movement Analysis toggle. The View menu exposes the same checked toggle.

**Selection Rect**, immediately right of **Slider Path**, toggles the selected-object bounding box and its move/scale interaction regions. It defaults to on and highlights while enabled. The choice lasts for the editor session and does not change selection, map content or undo history. The shorter Zoom slider leaves room for this button.

Slider Path, Selection Rect and Movement Analysis use equal-width buttons. Long localized labels use a smaller font down to 10 DIP, then wrap onto two lines when needed in the compact toolbar. Wrapping prefers spaces and localized word-break hints. Text fitting measures the enabled button's font so toggling a button retains its layout; results are refreshed when the language or compact layout changes.

**Movement Analysis**, beside **Selection Rect** in the canvas toolbar, toggles 4-DIP coloured connections above curves and behind objects on the editing canvas. The button highlights when enabled; the same toggle is also available under **View → Movement Analysis**. It is off by default and is a session display setting. Each consecutive Fruit/Droplet pair uses the same Stand, Walk, Dash, and HDash classification, using the Mapping Canvas indicator colours as the floating panel. Connections follow placement previews and content edits, retain full-sequence movement context across viewport edges, and skip simultaneous pairs and TinyDroplets. Banana shower and break intervals suppress entire connections and their DS labels whenever they overlap the pair, including links between fruits on opposite sides of an interval. Kiai intervals retain the connections and labels. Each Stand connection assumes its own departure-centre position; a sequence of Stand connections does not imply one shared standing position. Toggling the mode does not edit content or enter undo history. DS labels beside the connections use the same base-DPB formula as the floating panel. Each label has one fixed position to the right of the full connection midpoint, independent of viewport clipping. Intervals of 37.5 ms or less (a 1/8 beat at 200 BPM) omit DS labels. Longer intervals show labels wherever the fixed position fits inside the viewport without overlapping banana showers, the floating panel, or other labels. Fruit/droplet sprite bounds do not suppress labels because their transparent padding and glow overstate the occupied area. Labels do not move to alternate positions.

In Select mode, New Combo toggles the selected parents, or the particular Stream/Stack fruit selected in the upper timeline. On the editing canvas, a fruit with this flag has an NC label beside it in every UI language. The label sits to the right when there is room and otherwise to the left; slider heads with the flag are labelled once. A pending New Combo fruit placement shows the same NC label beside its preview. The sound buttons toggle additions independently and support mixed multi-selection. Clicking a slider fruit targets that head, repeat or tail's hitsound; selecting the whole slider through its timeline body targets every edge. Droplet/tiny selections target the parent, whose edge sounds are editable; these children retain their existing tick/silent playback rules. Banana showers use their fixed banana sound and disable the three additions. In Fruit placement mode, or with no selection, buttons set pending flags for new objects. New Combo resets after placement; pending additions remain until changed or switching difficulty.

Lock Notes prevents moving, reshaping or deleting existing objects, including timeline reverse edits. Selection, playback, New Combo and sound editing remain available. New objects can still be placed; undo/redo remains available. Hover Lock Notes to reveal **Lock Droplet Selection**. This independent toggle is enabled by default and excludes droplets and tiny droplets from canvas hit testing and box selection, and clears an active droplet child selection. Clicking a locked droplet with the Pen slider tool selects its parent without starting a new anchor. Slider paths, controls, fruits and parent selection in the object timeline remain available. Lock Droplet Selection is saved automatically as a user preference and restored after restarting. The flyout also offers **Lock Sliders to Their Canvas**. When enabled, FSliders can be selected only in Catch and Legacy Sliders only in osu!, including click, box and object-timeline selection. This global preference is disabled by default and restored after restarting. Changing these controls alone does not dirty the difficulty.

The four left tool buttons show multiline operation hints. FSlider's hint follows the current editing mode. FSlider and Distance Snap display their hints above their hover buttons so the controls remain accessible.

## Fruits and beats

The toolbar above the object timeline groups Zoom, curve visibility and beat Snap, with Snap at the right. Beat snapping offers 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, and 16 subdivisions using the active red point's BPM and offset. Holding the left mouse button stationary on a fruit for 300 ms identifies its nearest beat position, including slider heads, tails and repeats. If the enabled current Snap already matches within 2 ms, it stays unchanged. Otherwise the status shows the original time and nearest beat position and temporarily selects the nearest Snap subdivision while the parent remains selected. Continue dragging without releasing to use that subdivision. Releasing early, moving at least 2 DIP before activation, a key press or focus loss cancels recognition. Recognition uses the active red point and prefers the smaller divisor when grids are equally close. Automatic Snap uses at least 1/4 for whole/half beats and 1/6 for third beats; the displayed beat fraction remains reduced (for example, 2/3 with Snap 1/6). Finer subdivisions and matching manually chosen Snap values remain unchanged. A nearest-grid offset of at most 2 ms matches; larger offsets show as off-grid and restore any temporary Snap. Inspection preserves the original timestamp; vertical dragging snaps to the selected subdivision. Changing Snap manually keeps the chosen value. Horizontal dragging preserves the fruit's original time. In Select mode, a single click on empty canvas clears selection without seeking. Object selection, control-point interaction, and box drags preserve the playhead, including object selection in the timeline. Clicks on the upper object timeline ruler preserve the playhead. Changing snap settings does not move existing objects.

**View → Grid Level** opens a right-side submenu on hover or click, with the current level checked. It selects Tiny (4), Small (8), Medium (16), or Large (32) in osu! playfield pixels. **View → Grid Snap** enables horizontal snapping. **T** toggles the grid and **G** cycles its four sizes. The grid affects horizontal placement, control-point movement and group movement; groups keep a common offset. Time snapping remains independent, and Bézier handles retain continuous movement.

Absolute timestamps use `mm:ss:fff` (minutes, seconds, milliseconds), truncating the displayed fractional millisecond without changing stored precision. Selecting an object preserves its exact stored timestamp.

## FSliders

Hover over **FSlider** to reveal two vertically stacked buttons on its right: **osu legacy mode** and **pen tool mode**. The default is osu legacy mode. The active mode is highlighted, and either can be selected with any tool active. The mode is a session setting: both tools edit the same FSlider objects, and changing modes does not change geometry or create undo history. New and existing sliders may be edited with either tool. Imported Legacy Sliders still require conversion to an editable FSlider.

In **pen tool mode**, press B or 3 to clear selection and start drawing. Click to add curved anchors; Ctrl+click adds a straight segment. The first anchor and its preview follow Distance Snap within the playfield; later anchors follow the pointer without Distance Snap. Hold and drag to pull direction handles. The placed head fruit remains visible from the first anchor. Moving the pointer previews the next segment and its generated objects. Anchors and handles can extend beyond X=0–512 where the resulting path remains renderable. Click the last anchor again to begin a new curve section. Right-click a placed draft point to remove it, or right-click elsewhere to finish at that position. One track may mix straight and Bezier segments. Enter also finishes; Esc cancels the draft. Finishing keeps the final draft objects and Movement Analysis connections visible while background validation completes, and keeps the FSlider placement tool active for another slider. Press 1 to select and edit existing objects.

Select an FSlider and click its controls, or double-click its track, to edit anchors in **Select**. Clicking an interior control on an already selected complete track also enters editing; visible slider fruits use the selection rules below. Drag anchors and handles directly on the canvas. Interior anchor dragging is free by default. Enable **View → Snap interior anchors** to snap interior anchor times to the selected beat subdivision. Head and tail anchors follow beat Snap. Invalid snapped endpoint moves keep their previous time rather than clamping between grid lines. Handles remain free, and the option does not change placement or whole-object snapping. Anchor times remain increasing; anchors and handles may extend beyond the playfield. Moving a pen draft anchor preserves its handle vectors, and the whole draft remains one undo step. Curve handles may extend before the start or after the end; the curve must define an unambiguous forward branch inside the slider time interval. Generated events still undergo normal conversion validation.

In Select mode, the first click selects the whole slider; the second click on a head, tail or repeat selects that fruit with an individual outer ring. Dragging that selected fruit inside the selection box edits the fruit instead of moving the whole slider. Horizontal dragging changes its X while preserving its time. Dragging a selected FSlider fruit at a base-path endpoint vertically edits that red anchor in time and X, with endpoint beat snapping and a single undo step. This works in both editing modes, including paths with only two red anchors; the opposite endpoint stays fixed. Other slider events keep their time during dragging; repeated traversals share the same endpoint geometry. Legacy Sliders remain read-only in the Catch canvas until explicitly converted to FSliders. Clicking outside a selected child returns to whole-slider selection; a subsequent outside click clears it. Dragging the body selects and moves the whole slider. In anchor edit mode, dragging an anchor changes that anchor. Ctrl+click at a new position inside the slider's time range inserts a curved anchor; Ctrl+click on an existing anchor makes it straight. Right-click a straight anchor to restore a curved anchor, then right-click the curved anchor to delete it. These rules apply in both editing modes, including points exposed in Select mode. In legacy mode, an interior straight anchor is a segment boundary; restoring it to curved merges it back into the control polygon. Right-click the slider body away from anchors to delete the parent. Ctrl+L also toggles the selected point, and Ctrl+Shift+I inserts on the curve under the pointer. Ordinary insertion may change shape; the shape-preserving split action retains it. Batch deletion may include endpoints. Fewer than two remaining anchors deletes the complete track.

Selecting or double-clicking a Legacy Slider in Catch preserves its representation. Editing requires the explicit Convert to FSlider action; the conversion and subsequent edits have separate undo steps. Legacy geometry can instead be edited directly in the osu! canvas. B / 3 selects the current canvas's slider tool.

Span count applies to the entire FSlider; later traversals reuse the first span's nodes in alternating directions. Hold the left mouse button stationary on a Legacy Slider to open its conversion actions beside the pointer. Holding a slider fruit first identifies its beat at 300 ms. Continuing to hold stationary opens conversion actions at 1000 ms; a small progress ring fills during the final 700 ms. Moving at least 2 DIP or releasing before completion cancels the hold; ordinary selection and dragging remain available. The buttons stay open until an action, another click, a key, or focus loss dismisses them. Conversion preserves start time, total duration, and span count. It first fits a small set of straight/Bezier anchors within 0.25 playfield units, then relaxes TinyDroplet alignment or uses a linear approximation if needed to complete the conversion. Exact nested-object positions and sequences are not required to match. Invalid or unrepresentable input retains its original object with a reason.

The first workspace import from Songs, an external folder, `.osu`, or `.osz` asks whether to convert sliders in all newly imported difficulties if they contain Legacy Sliders. The prompt offers **Derandomize droplets**. With it off, conversion builds an editable path through the actual legacy fruit and droplet positions, including randomized TinyDroplets. Generated geometry compensates the RNG so objects stay on that path. Conversion preserves their times and positions; incompatible shared repeat targets remain Legacy with a reason. The selected policy is saved per difficulty after a successful conversion and reused for later conversions. Keeping Legacy preserves that representation. Reopening an existing project does not prompt again. Importing one difficulty into a project prompts only for that difficulty.

**Edit → Convert all sliders to FSliders** opens the droplet choice for the current difficulty and is disabled when it has no Legacy Sliders. Explicit single-slider conversion proceeds without a prompt, using the saved difficulty policy or the global conversion default in Droplet Settings. Batch conversion runs in the background with a cancellable prompt and blocks content editing. Cancellation applies no partial results. Conversions per difficulty can be undone together; valid sliders that previously failed strict alignment now convert approximately. Invalid input and reasons appear in paginated results. Saving persists conversion in the workspace without modifying original Songs/external files; Export is still required to write `.osu`.

With Distance Snap disabled, dragging a selected slider fruit, droplet or tiny droplet follows horizontal Grid Snap, including the existing Shift override. Unreachable positions stop at an accepted grid point, or retain the original X when no grid point is reachable. With Distance Snap enabled, the strict event snapping rules above apply; rejected candidates do not leave partial geometry.

The generator handles FSlider TinyDroplet alignment. The Tiny alignment toggle only affects older project data without a saved per-track policy.

### osu legacy mode

See [Slider interaction reference](SLIDER_INTERACTION.md) for the source comparison and coordinate constraints.

Left-click a start point, move the pointer to preview the endpoint, and left-click to add controls. Right-clicking away from placed points or pressing Enter completes the slider; Esc cancels the complete draft. Right-click a placed point to remove it. Ctrl+click adds a straight segment. Click the last placed point again to begin a new segment; this does not depend on the system double-click interval. Each draft segment defaults to a line with two points, a circular arc with three, and a Bezier with four or more; counts include the segment endpoints. White controls shape the curve and do not necessarily lie on it. Red segment boundaries lie on the path and allow corners.

A single selected slider exposes controls in Select mode as well as the Slider tool. Anchors and handles draw above Movement Analysis connections, objects and distance labels in both editing modes. For a completed slider, drag a control, Ctrl-click an existing control to turn it into a straight segment boundary, or box-select controls. Ctrl-click between the slider's first and last times to insert a control at the pointer's position; the time interval determines its place in the control polygon. Insertion can change shape. Right-click a straight point to return it to a curved control; right-click a curved control to delete it. Delete removes selected controls directly. Double-click an interior point to toggle its segment boundary. Removing a boundary merges its adjacent control polygons; removing endpoints changes the time range. Fewer than two remaining controls deletes the slider. Each edit is undoable. Segment boundary times remain ordered. Bezier controls may extend beyond the endpoint times, provided the curve does not reverse time inside the segment interval. A circular preview or drag that would reverse time or leave the playfield falls back to Bezier; moving back during the same gesture can restore the arc. Explicit circular-arc commands still reject invalid geometry.

Use Ctrl+Shift+I for insertion and Ctrl+L to toggle the selected point between straight and curved. A circular arc requires exactly three points. Existing Bezier segments retain their type when their point count decreases; changing tools never implicitly turns a three-point Bezier into an arc. A line receiving its first internal control becomes an arc using the current map AR. Ordinary control dragging uses the interior-anchor snap setting; endpoint moves use the global snap setting.

Circular arcs retain a reference ratio derived from the map AR at creation (`440 / preemptMs` in playfield units per millisecond). Changing map AR or viewport zoom stretches their appearance without changing time–X coordinates. The reference excludes window width and DPI. Editing an existing arc preserves this reference; explicitly choosing a new circular arc uses the current map AR.

### Editing shared curves with the pen

A cubic Bezier exposes exactly the same controls in either tool. Arcs and higher-degree Beziers remain exact when selecting or switching modes. Pen mode displays endpoint handles from a bounded cubic approximation; provisional handles have a minimum 18-DIP display length so they remain clickable. Their stored offsets remain in map coordinates. The first actual handle movement converts only its affected segment, potentially adding anchors. Circular arcs use compact tangent-matched cubic segments, subdividing only when required for trajectory accuracy. The conversion and gesture share one undo step. Pen corner conversion/deletion and ordinary pen insertion may likewise require local conversion. Undo restores the exact original controls and AR reference. Shape-preserving splitting retains exact custom segment geometry.

### Clearing internal nodes

Choose **Edit → Clear all internal slider nodes**, next to
**Convert all sliders to FSliders**. The command processes every FSlider in the active
difficulty, including unselected and offscreen sliders, and replaces each path with a
straight segment, removing interior anchors, exact controls and endpoint handles.
It preserves the first and last anchors (including their coordinates and times),
repeat count, stream snap and object metadata. One undo restores the whole batch.
No selection is required. The command is disabled for locked notes, unfinished
gestures, or a difficulty without internal FSlider controls. Convert imported Legacy Sliders to FSliders first.

For selected sliders, use **Clear internal anchors** in Slider Inspector.
This applies the same endpoint-only path to the selected sliders
in one undo step, converting selected Legacy Sliders first. Holding a selected
slider retains the complete selection for the batch operation.

### Reverses and direction

In both modes, drag the slider tail in the object timeline to change reverses. Dragging a base-path endpoint instead edits the path and therefore changes the duration of every traversal.

**Ctrl+G / Edit → Reverse selection** reflects selected parent intervals within the complete selection time range and reverses each slider’s first-span path, retaining its repeat count. A single FSlider keeps its original interval. Imported sliders become editable FSliders in the same undo step. Banana showers exchange their relative start/end interval positions; standalone fruit X coordinates stay unchanged. Lock Notes blocks the operation. **Edit → Reverse path direction** applies only to the active FSlider and preserves its time range. It is distinct from adding a reverse. All repeated spans derive from the same controls; changing the base path updates every traversal.

## Banana showers

With N, left-click to set the start, then right-click at a later time to finish. Esc or switching tools cancels. A yellow snapped-time line marks banana placement. A banana shower appears as a time rectangle spanning the playfield. Drag its body to move it or its top/bottom handles to change start/end times. The upper object timeline also allows dragging its tail to change the end time; Shift bypasses beat snapping, Esc cancels, and release commits one undo step. RNG generates individual banana X positions.

## Files and playback

| Input | Action |
| --- | --- |
| Ctrl+O | Choose a difficulty in the current project |
| Ctrl+Shift+O | Open `.osz` / `.osu` / `.catchproj` |
| Ctrl+S | Save current difficulty; workspace-only projects offer an optional Songs export after saving |
| Ctrl+L | Confirm restoring the current difficulty to the preceding saved version; requires an earlier workspace save |
| Ctrl+Alt+E | Export `.osu` |
| Space | Play / pause |
| Click, drag, or scroll the bottom timeline | Seek while preserving play/pause state |
| Home | Return to the start |

Dragging the bottom timeline pauses audio while the pointer holds the time, then
resumes from the selected position on release if playback was active before the drag.
Dragging while paused keeps playback paused.

The File menu can replace MP3 / OGG / WAV audio. Manual seeking and editing remain available without playable audio. Save the editor project to retain editable data; further changes after `.osu` export still require a project save.

Text inputs show a blinking caret at the end of the text and highlight the full selection after Ctrl+A (Command+A on macOS). Typing replaces the selection; library fields also support pasting text. Long focused text scrolls horizontally to keep its end visible.

## Display settings

The workspace toolbar has three sections separated by vertical rules: the
Mapping Panel / Timing Panel dropdown at the left; Zoom, canvas buttons and Snap
in the middle; and AR, CS and DPB at the right. Its geometry is independent of
side-panel placement. In Timing, the middle section contains the waveform label
and Snap. The canvas and side docks sit below this row, with the transport timeline
at the bottom.

Mapping Panel provides two side dock areas. The default groups contain Movement
Strain on the left and Preview, Notes and Song Setup on the right. Stream/Stack is
available from Layout and is hidden by default.
Drag a named tab to either side to move it; dropping into an occupied area combines
the panels into one tab group. Click a tab to select its content, or drag within
the tab strip to reorder it. Empty sides remain drop targets at the window edge.
Only the side areas accept drops. Escape, lost capture and drops on the canvas
leave the layout unchanged. The side arrow collapses or expands the whole group;
both dock groups can be resized at their canvas-facing borders with the same minimum
and maximum widths, independently of their tabs. Each side can reach 45% of the
window width. Widening one side beyond the combined dock budget narrows the other
side down to its minimum; the canvas retains its minimum usable width. Reordering tabs within a group
previews the resulting order without a dock overlay. Preview options stack vertically
in narrow docks.

The fixed Mapping Panel / Timing Panel selector switches workspaces. Each workspace independently retains panel placement, tab order,
active tabs, collapsed groups, widths, preview options and the movement readout's
position. Layout changes are saved in user preferences after completed actions
and restored when the editor restarts, independently of project saves. Timing Panel keeps its fixed waveform and timing-controls layout and
does not expose dock dragging. Layout changes do not change beatmap content or undo.

The **Layout** menu next to **View** lists the side-dock arrangement, a layout reset,
and available panels. A check marks each existing panel, including collapsed groups;
clicking it removes that panel, and clicking an unchecked panel restores it on its
default side. Right-click a dock tab for **Close tab** and a **Panels** checklist. Checked panels
use the same teal highlight as Layout; clicking one closes it, including panels on
the opposite side. Clicking an unchecked panel adds it to the right-clicked group.
Each panel can exist only once. Reset restores the default panels on their default sides,
expanded with their default widths. Layout controls are locked in Timing Panel.

**Stream/Stack** follows the current selection. Existing Streams, Stacks and Banana
Stacks load their parameters automatically. Plain sliders and banana showers show
an explicit conversion entry first; standalone fruits are not handled. The panel
reuses the conversion dialog's parameter controls without its right-hand preview.
Valid parameter changes immediately update the selected patterns, mark the map
dirty and support undo. Graph and snap drags preview while moving and commit when
released. Break into Fruits and Break into Bananas are explicit action buttons;
clicking one splits the pattern immediately in one undo step. Existing patterns
also offer a button to restore ordinary slider or banana shower output. Short
docks scroll with the mouse wheel. Ctrl+Shift+F opens and focuses this dock, adding it when closed. An empty selection shows selection guidance. Panel buttons retain their background and border when idle.

Dragging an interior Stack fruit on the canvas edits its individual horizontal
offset. Its time, neighbouring fruits and center path remain unchanged, including
other traversals of a repeated path. The edit supports undo and project persistence.

**Notes** is a multiline project-wide text editor, shared across difficulties. It
supports text selection, copy/paste and independent text undo/redo while focused.
Notes changes mark the project dirty and are saved with the project, including
workspace manifests and compatibility project files. Closing or moving its tab
does not delete the notes. Notes are not exported to `.osu` files.

Copy selected map objects with Ctrl+C and paste into Notes with Ctrl+V. Notes keeps
exactly the external plain-text reference format, for example `00:12:345 (1,2) - `.
The timestamp and object numbers appear as an underlined link. Click to seek and
select matching objects in the current difficulty; drag to select and copy the
text instead. References are reconstructed from saved text, including wrapped
lines. If no matching objects remain, navigation still seeks to the timestamp.
Notes editing has its own undo/redo; clicking references does not edit the map.

The language dropdown in **Settings → General** lists the supported languages. Selecting a non-English language opens an AI-assisted machine translation notice before applying and saving it. Menu shortcut hints align to the right edge of each row. Existing beatmap titles and object names retain their values. The main canvas can hide curves and nodes. Catch Preview displays gameplay objects.

The Skin selector to the left of **← Library** lists skins from the configured osu!stable `Skins` folder and offers `.osk` import. Imported archives and extracted Catch assets are kept under `workspace/Skins`; imported entries use gold text and an Imported label. Skin selection persists independently of beatmap edits. Library Settings accepts a user-owned default skin `.osk` file; each missing or unreadable custom image falls back to the default skin independently, then to geometric rendering. Long lists provide previous/next pages. Missing skins or textures fall back to basic shapes; see [Skins](../assets/skins/README.md). Drawing and hit-test sizes are described in [Catch Rendering and Conversion](CATCH_RENDERING.md).

## Multiple difficulties

**File → New project** creates a project with one blank difficulty. Opening `.osz` loads all Catch (Mode=2) difficulties into one project and skips other modes. A damaged Catch file does not replace the current project. Opening a single `.osu` or older `.catchproj` creates a single-difficulty project.

A separate row below the main toolbar displays Chrome-style difficulty tabs with the official Catch icon, Version, live No Mod stars, and an unsaved dot. Icon color follows stars. Active tabs have rounded top corners and spread outward at the bottom to join the content below. Tabs use actual text widths rather than filling the row. Tabs first use full difficulty names. When space is insufficient, up to eight tabs share the available width by shortening the longest names; more than eight tabs use compact names and a horizontally scrollable strip. Arrow buttons and the wheel also scroll overflowing tabs. Stored names remain complete. Hovering a truncated tab shows its full name in a pointer-following tooltip that wraps and stays within the window. Click to switch; use arrows or the tab-row wheel when tabs overflow. Ctrl+Tab / Ctrl+Shift+Tab cycle and reveal the active tab. The **+** button opens the add/import menu. A new blank difficulty inherits the active difficulty's audio, timing, settings, and resource context but clears objects. Importing an `.osu` adds one file. Difficulties may reference different audio.

Difficulty tabs initially sort by completed No Mod SR from low to high.
Equal ratings retain their display order; failed ratings appear last. Drag a tab
horizontally to reorder it, with live feedback; dragging near either edge scrolls
an overflowing strip. Escape or lost pointer capture restores the previous order.
After a manual reorder, rating changes from edits and undo/redo preserve that order.
Right-click a tab and choose **Pin difficulty** or **Unpin difficulty**. Pinned tabs
show a pushpin icon to the left of the Catch icon and always precede ordinary tabs.
Each group can be reordered independently. Tab order and pins last for the open
project session. Opening a
project restores automatic sorting. These display preferences preserve the active
difficulty, project storage order, content and undo history. Ctrl+Tab and
Ctrl+Shift+Tab follow the displayed order.

Switching commits valid pending edits first; unfinished banana drafts or invalid input prevent switching. It pauses playback and retains each difficulty's playhead, time-viewport start, and undo/redo history. Selection and the active tool reset. Title/status dirty indicators cover the whole project, including hidden difficulties. One save writes every difficulty and updates baselines without clearing undo history. Ctrl+L and **Edit → Revert to previous save** restore only the active difficulty from the most recent prior save snapshot. A first save has no prior version. Synchronization and restore working copies are excluded. Confirmation retains an undo step and recovery copy of the current edits; saving the restored content remains a separate action. Unsaved confirmation on new/open/close applies to the whole project.

`.osu` export applies to the active difficulty; suggested filenames include its name. Workspace project saving is described in [Workspace](WORKSPACE.md), and the compatible `.catchproj` format in [Project Model](PROJECT_MODEL.md). Resource paths remain references rather than embedded project-file contents.

See [Catch Star Rating](CATCH_DIFFICULTY.md) for calculation, cache invalidation, and export/website-version limits.

## Library and workspace

**Library** opens a separate page for workspace/stable Songs settings, bilingual metadata search, difficulty browsing, and project opening. Editor difficulty tabs retain their layout. Missing source maps show status on their difficulty tabs without a persistent error bar. Missing required resources are checked before export. Export offers a standalone `.osu` save dialog, associated-difficulty overwrite, or a new difficulty in Songs. After export links a difficulty to Songs, Save also updates its linked `.osu`. See [Workspace](WORKSPACE.md).

Below the Catch Preview title, one line shows `AR … · CS … · NM`. It omits fall time, generation status, and skin name. Preview scrolling and object drawing still follow AR/CS.

A new project's blank difficulty starts unmodified, so directly opening or importing an external beatmap does not trigger an unsaved prompt. Content edits, audio binding, and added/imported difficulties do prompt. Undoing to the initial blank state clears the dirty marker.

The bottom status bar shows current action feedback, such as save results or operation limits. During background synchronization checks or application of changes, synchronization status takes priority; otherwise conversion errors take priority. It is not a log viewer. Platform details, internal zoom percentages, and duplicate dirty indicators are omitted.

For FSliders, 0 reverses plays the path once, 1 returns once, and higher counts continue alternating. Drag the slider tail in the object timeline to add or remove reverses. With a completed FSlider selected, move the pointer to empty canvas at a time after its final end and press **Ctrl+J**. A new anchor is placed at the pointer position using the placement snap setting, with a straight segment from the base path endpoint. Existing segments remain unchanged. Extending a repeated slider lengthens its base path for every span; it does not append after the repeats. Each operation is undoable.

In the object timeline, a slider tail displays a horizontal resize cursor. Drag it right to add reverses or left to remove them, down to one traversal. Each step equals one unchanged base-span duration. This works for FSliders and imported Legacy Sliders; release commits one undo step and Esc cancels.

Measure starts use longer white ticks in the upper object timeline and brighter
white grid lines on the main canvas to distinguish them from ordinary beats.

Objects with exactly the same start time stack upward in source order in the
object timeline. Up to 16 objects are displayed, with compact spacing inside the
existing timeline height. Slider bodies and all their markers share the offset.
Clicking and dragging follow the displayed layers; box selection includes objects
beyond the display limit. Stacking changes neither timestamps nor map content.

## Operation errors

Recoverable file-operation errors appear inside the editor window on both the canvas and library pages. The message identifies a rejected beatmap file when parsing fails. Scroll long messages with the mouse wheel; dismiss with OK, Enter, or Esc. While the message is open, editing and library input are blocked and the current document is retained. Windows startup and rendering failures use a foreground system dialog because the editor canvas may be unavailable.

## Slider fruit streams

The conversion dialog includes **Break into Fruits**, off by
default. Enabling it replaces the slider parent with independent fruits at the
chosen snap, each editable separately. Existing streams offer **Break into Fruits**
directly below **Edit Stream/Stack** in Slider Inspector. Breaking preserves the
stream's fruit positions, times, combo flags and sample settings, and forms one
undo step.

Select at least two consecutive circles or sliders and long-press one of them,
then choose **Merge into slider** from the action list, or press **Ctrl+Shift+M**. This opens and focuses Slider Inspector, adding the tab when needed; choose the path type and apply the merge within the panel. Consecutive means no
circle or slider is omitted between the earliest and latest selected starts in
time/source order. A selected banana shower blocks merging. The dialog offers
straight segments or a curved path; when any selected slider has a curved path,
only the curved choice appears. Existing slider paths are retained, with repeats
unfolded into consecutive traversals; circles become path anchors. Overlapping
selected objects or incompatible joining endpoints fail with a reason, retaining
the original objects. Conversion failures also leave the selection unchanged.

Merging replaces the selected objects with one FSlider and supports session undo.
Its generated catch events can differ from the original objects. The dialog warns
that saving retains only the merged slider: reopening cannot recover the original
objects because project files do not store undo history.

Select one or more sliders and press **Ctrl+Shift+F**, or use **Edit → Convert to Stream/Stack**, to open the Stream/Stack dock. Long-press an object in a selection or press **Shift+F** to show the compact action list first; choosing a conversion action opens its dock. Imported Legacy Sliders also offer **Convert to FSlider**. Existing streams and stacks retain their saved mode. Changes in the dock apply immediately and support undo.

The dialog separates its title, tabs, Snap and conversion switches with padding.
The Snap value has a gap from the slider thumb. Stream and Stack show their
controls and preview without the introductory or graph-instruction paragraphs.

With **Break into Fruits** off, a confirmed stream remains one editable slider parent with its anchors, handles and repeats. The first click on a stream fruit selects its parent. Once the parent is selected, dragging a fruit edits its X position and marks that event with a bright outer ring. Middle-fruit edits preserve neighbouring samples and event times; repeated traversals sharing the same path sample move together. Head and tail fruits also support dragging in time. A repeated tail resizes its span duration and edits the endpoint of its final traversal. Drag the empty area inside the selection box to move the whole stream. Visible anchors and handles take priority over overlapping stream fruits, so dragging a control reshapes the parent curve. Hiding Slider Path lets the overlapping fruit be edited directly. Dragging, reshaping, cloning, saving and undo retain the stream snap. Existing streams offer **Edit Stream/Stack** above **Convert back to slider** in Slider Inspector. The Stream/Stack dock changes their subdivision immediately with undo support; converting back restores ordinary slider output while retaining geometry and supports undo. Preview and testplay display independent fruits, and `.osu` export writes hit circles. Sampling starts at the slider head, uses its starting BPM across all spans, and includes the tail only when it falls on that subdivision. Parent-level New Combo applies to the first fruit; internal fruit NC is edited in the upper timeline. Object-level sound/sample settings apply to each fruit.

The keyboard aliases above follow the [legacy shortcut reference](https://osu.ppy.sh/wiki/en/Client/Keyboard_shortcuts) where supported. Ctrl+L restores the current difficulty's previous saved version after confirmation. Ctrl+Shift+I point insertion, Ctrl+J extension, Ctrl+Alt+E export and Alt+wheel canvas zoom remain editor-specific bindings; V and End provide last-note navigation. Geometric rotation dialogs are not available.

Testplay lead-in is configured in Settings > Testplay, from 0 to 5 seconds in 0.5-second steps (default 1). Settings also offers a persistent Combo-count visibility toggle, without a keyboard shortcut. Holding the configured Dash key adds a bright white catcher layer while preserving the existing trails and Hyperdash tint. Testplay starts at the selected position minus the lead-in. When that position reaches zero, opening preparation follows the [user manual](USER_MANUAL.md#testplay). Audio starts at the negative map position: Windows supplies silent PCM before the music, and macOS schedules the music node for zero while its output clock advances. The session uses the same interpolated audio clock throughout preparation and music. Pause, speed changes, retry and intro skips preserve negative transport positions. Hold the `~` / backtick key for 300 ms to retry once from the session start. A black overlay gradually dims the whole window while the pause loop plays during the hold; completing it plays the pause-menu Retry click and clears the overlay at the session start. Releasing early cancels the retry and removes the overlay. It also works while paused. Esc opens the pause menu; F1 returns to the selected position.

Number keys 1–4 select Select, Fruit, FSlider and Banana Shower. During an FSlider draft they finish valid geometry, or cancel an insufficient draft, before switching tools; pressing 3 prepares another slider. Shift+1–9 changes Snap during drawing without moving placed points. F4 opens Song Setup. Left/Right seeks one full beat during playback. While paused, it moves to the preceding/following Snap grid line, including timing boundaries, so off-grid positions align in the chosen direction. Shift+Left/Right seeks four full beats during playback or four grid lines while paused, and Shift+1–9 changes Snap; other Shift variants do not invoke unmodified transport or nudge commands. Timing blocks horizontal object nudges and accepts Ctrl+Alt+E outside fields and dialogs. F6 row deletion requires Delete or Ctrl+I without Shift or Alt. The Settings language dropdown consumes keyboard input until Enter applies or Esc closes it.

Ctrl+wheel doubles or halves the Snap divisor within supported choices on the canvas and timelines: 1→2→4→8→16 or 3→6→12. Enabling the experimental divisions extends these families to 1→2→4→8→16→32 and 3→6→12→24→48. It stops at either end; 5, 7 and 9 stay unchanged because their doubles are unsupported. The Snap slider retains every subdivision. Shift+wheel seeks four times the normal wheel distance. Alt+wheel zooms the canvas or the upper object timeline under the pointer. Alt+wheel over the bottom timeline adjusts the currently selected Master, Music or Effect channel and displays the existing volume overlay. Ctrl+Alt+wheel cycles Select, Fruit, FSlider and Banana Shower over the canvas or upper timeline: wheel down advances and wheel up reverses the cycle. Unsupported wheel modifier combinations do not seek. Ctrl+M enters its quick cycle at 1/3 when the current divisor is outside the four choices.
While dragging one or more objects, ordinary wheel navigation, Ctrl+wheel Snap adjustment, Alt+wheel zoom, and Space/C playback control remain available. Wheel navigation cancels a pending slider long press without ending the drag.

## Settings

Categories are ordered General, Workspace, Appearance, Audio, Testplay, Mapping Canvas, and
Application updates (where supported). Appearance groups the active skin selector
with the default skin archive. Mapping Canvas groups background dim, reverse
canvas scrolling and movement indicator colours. General includes metadata display and language.
Active skin selection takes effect immediately and is saved automatically.
Application updates ends with a divider and a community section. A single sentence
identifies Fruits Atelier as a catch editor project by Yumeno Himiko and links the
name to the osu! profile. Suggestions and feedback can be sent through osu! or the
Join Discord button. Project Website, Github Page, and Join Discord share a row in
that order; Join Discord uses the first-time setup invite.
Mapping Canvas includes a Reverse canvas scrolling toggle. It
defaults to off and reverses ordinary and Shift+wheel time navigation over the
canvas, including wheel navigation during marquee selection. Other panels and
wheel shortcuts retain their direction. Apply saves this preference.

Settings opens a centered modal overlay above the current editor or library, with
its background dimmed and blocked from pointer and keyboard input. Categories retain
unapplied drafts while switching between them. Apply saves changes and keeps the
overlay open; Escape or the close button closes it and discards
unapplied drafts. Escape first dismisses an active field, key capture, language menu,
or colour picker. Language changes take effect immediately.

## Stream and Stack conversion

Select sliders and choose **Convert to Stream/Stack** from Edit, Slider Inspector,
or the compact object action list, or press **Ctrl+Shift+F**. The Stream/Stack dock
opens and receives focus. Existing streams and stacks retain their mode and snap;
ordinary sliders convert to Stream. A mixed Stream/Stack selection follows its
first target. Repeating the shortcut keeps the existing dock and parameters.

The dock previews the result on the main canvas. Switching Stream/Stack and changing
parameters apply through undoable edits; graph and snap drags commit on release.
**Break into Fruits** is an explicit action. Short panels scroll vertically.

The Stack tab retains the source curve and samples
independent fruits with the selected stream subdivision, using the head BPM across
all spans. The first-side switch chooses left or right.

The envelope graph edits horizontal distance from the centre curve over 0–100% of
the complete duration. Drag a point to change time and distance; endpoints stay at
0% and 100%. Click empty graph space to add a point, and right-click an interior
point to remove it. The graph uses a fixed 0–32 px range divided into 32 horizontal bands. Mouse
dragging snaps distance to whole pixels; numeric inputs allow fractions. Smooth
interpolation joins points without overshooting their distance values. The right
preview updates immediately; generation alternates sides and clamps final X to
0–512. Actual DS follows the generated positions and time intervals.

Valid changes apply to all selected sliders and support undo. Reopen the dock to edit a saved stack. Conversion back to a slider clears
the envelope and retains the centre geometry. Project files retain the editable
parent and envelope; osu export writes independent hit circles.

The upper object timeline displays each Stream or Stack fruit at its sampled time
with its combo number. Clicking a fruit selects its editable parent. Conversion
back restores the ordinary slider display; Break into Fruits retains the fruit
markers as independently selectable objects.

Click a particular Stream or Stack fruit in the upper timeline and press **Q** or
use **New Combo** to toggle NC at that fruit. Its highlighted ring identifies the
NC target. Moving or deleting it acts on the entire slider pattern. Canvas and
whole-parent selections use the head's NC flag. Internal NC starts reset subsequent
timeline numbers and colours, persist in projects and export on the corresponding
hit circles. Break into Fruits retains them.

Stack geometry and generated fruits are previewed on the main canvas. The distance
curve and numeric fields remain in the dock. Parameter changes preserve the
editable parent and apply through document undo history.

Select an envelope point to edit **Time (%)** and **Width (px)** numerically.
Enter or Tab accepts the value; Escape cancels the text edit. Interior times must
remain between neighbouring points; endpoint times stay at 0% and 100%. Width
accepts 0–32 px. Manual fruit markers
also accept width edits; moving their percentage turns the marker into an envelope
point at that time, replacing its individual-fruit offset.

New stacks default to 1/16 subdivision and reach their width over the first 2% of
the duration, returning to zero over the final 2%. Existing stacks retain their
subdivision. Endpoint widths can be edited in the graph or numeric fields.

Completed dock edits use document undo history. Ctrl+Z undoes a committed edit;
Ctrl+Y or Ctrl+Shift+Z redoes it.
Right-click a manual fruit marker on the left graph to remove its offset and
recompute that fruit from the envelope. This removal is also undoable.

## Audio delay diagnostics

Windows release builds provide recording and output test controls in Settings >
Audio. Enable recording, select event-driven or polling shared-mode WASAPI with a
requested 10 or 50 ms buffer, then click Apply. When diagnostic options change,
the app saves pending project edits and restarts automatically, restoring the
project, active difficulty, paused playhead and Audio page scroll position. Other
preferences retain their usual Apply behavior. A divider separates diagnostics
from the skin sample option. The current-run status stays
separate from saved settings. Optional frame timing logs and a correlation overlay
follow the General display mode. The overlay sits near the bottom edge, below the transport timeline, with its
text kept inside the window. The Audio page scrolls while its navigation and
Apply button stay fixed.

Users can press Ctrl+Shift+F8 during playback to mark a delay without pausing,
mark a recently noticed delay from Settings, open the current capture directory and
export a ZIP while the app remains open. Export runs in the background and reports
completion or failure on the page. Diagnostic preferences do not change beatmap
content. The Mac Audio page identifies the WASAPI controls as Windows-only.
See [Audio capture instructions](AUDIO-DIAGNOSTICS.txt) for test comparisons,
collection limits and the distinction between software timing and acoustic delay.

## Hitsound Copier (Beta)

The Timing menu separates snap/metronome controls, current timing-section commands,
setup tools, whole-map commands, and the preview point with dividers.

Open **Timing → Hitsound Copier (Beta)** for a floating dialog. The tabs are external
copy, same-set copy, and Clear from left to right. Choose Clear,
an external osu!standard or Catch `.osu`, or another difficulty in the current set. Clear restores
normal default samples and removes Whistle, Finish, Clap and custom object samples.
Copy requires matching red timing times, BPM and meter (floating-point rounding is tolerated). Events match the nearest playable
time; unmatched events on either side are silently skipped. Target SV is preserved. Source sample bank, index and volume changes appear as
green timing points in FA and persist with the project. Copy overwrites the sample
fields of existing timing points, including Kiai markers, while keeping target SV
and Kiai state.

External copying can include only the used hitsound files. Same-set copying shares
existing resources. Conflicting numbered sample groups are renumbered; explicit
files receive a distinct relative name. Existing files are preserved. Clearing can
delete unshared referenced hitsound files while preserving music, storyboard samples
and other difficulties' references. Unreadable sibling maps prevent file deletion.
Deleted files are backed up under `.hitsound-copier-backups` with a path manifest.

Check lists the matched event count and planned file changes; scroll the file
list to inspect all paths. Apply overwrites the current difficulty in one undo step
or creates a named difficulty from the current chart. A new difficulty preserves
the original, including its files. Overwrite undo/redo restores associated file
changes, retains files now shared by another difficulty, and rejects files modified
externally after preview. An outdated content preview must be rebuilt before Apply. Legacy slider events
cannot use explicit filenames or incompatible extra tick banks; preview explains
these format constraints without applying a partial result.

Select **All target Diffs** to copy to every difficulty in the current project, excluding the selected same-set source. Overwrite adds an undo step to each target; undo and redo work per Diff and retain audio files still used by other Diffs. Create new Diff creates a uniquely named copy of each target. Check validates every target before applying any changes and lists the aggregate matched-event count and shared file changes. Matches allow a time difference of up to 2ms; the nearest source wins, with the earlier event winning a tie.

## Clipboard screenshots

F12 copies the current window content, including open dialogs and testplay, as an image on the system clipboard. Capture runs independently of content editing and undo history. Windows reads the completed render target before presentation; macOS renders the editor at the window's display scale and supplies native PNG clipboard data. The shortcut fires once per press and rearms on release or focus cancellation.

## Mapping Canvas background

The main mapping canvas displays the current difficulty's background across its
drawing area, upper object timeline and left tool palette. The image fits this
combined area independently of zoom and scrolling. **Settings → Mapping
Canvas** provides a separate Background Dim preference (90% by default), reverse
canvas scrolling, movement indicator colours and X-axis grid appearance. Preview and testplay background
dim remain independent.

Mapping canvas dim blends the image toward the canvas's original dark blue
background colour (#171A20).

Drop a PNG, JPEG or BMP image into the editor to set the current difficulty's
background. An existing background requires replacement confirmation. Images
outside the map directory are copied there; conflicting filenames receive a numeric
suffix. Previous image files remain on disk. Changing the background is undoable
and its Events reference is included in project saves and osu! exports.

The **X-axis grid** section lets you pick separate colours for ordinary vertical
lines (including Grid Snap subdivisions) and the X=256 centre line, using the
colour palette or HEX input.
Their shared opacity ranges from 0% to 100%; the percentage menu selects multiples
of ten and the minus/plus buttons adjust by five. **Reset grid** restores the
default grid colour `#8F8F8F`, centre-line colour `#FFFFFF` and 40% opacity. These preferences affect all vertical X-axis
lines. Horizontal time lines keep their existing appearance. Changes remain
drafts until Apply and do not change beatmap content. Short settings windows scroll to reach the grid controls.

The canvas has a **Background Dim** slider in its lower-left corner. It is hidden
until the pointer approaches, then fades in over 150 ms. After the pointer leaves,
it stays visible for one second and fades out over 150 ms. Dragging keeps it visible
and adjusts the mapping background immediately; release saves the preference.
The control remains fixed in screen coordinates when zooming or scrolling. It does
not change beatmap content or preview/testplay dim, and modal dialogs hide it.

## Banana Shower Edit Window

Select a banana shower (spinner) and press **Ctrl+F**, or use the Edit menu.
**Ctrl+Shift+F** opens the Stream/Stack dock for a selection containing only banana
showers; its controls apply valid changes immediately with undo support. Set each short shower's length and the empty gap after it as separate
beat fractions. Numerators are editable integers; denominator lists use the
editor's supported snap divisions. For example, length **1/16** and gap **3/16**
start one short shower every **4/16** beats. A zero gap creates adjacent showers.
Segments shorter than **2 ms**, including a clipped tail, are omitted from preview,
count and export. Confirmation is unavailable if a selected shower would produce
no valid segments.

The right preview shows the first selected shower using the complete candidate
beatmap's exported Normal-mode banana times and positions, including preceding
objects' RNG consumption. Each segment shows both boundary lines. Length and Gap
share one row, with the total beats per group and group count below (including a
clipped final group). **Perfect fit** appears only when N complete showers plus
N−1 gaps exactly fill the selected range, without clipping the last shower or
leaving a trailing gap. Scroll over the preview to inspect later segments. Preview
calculation runs in the background; confirmation becomes available when it
finishes. Enter commits a numeric field; Confirm applies the settings to all
selected banana showers in one undo step. Cancel or Escape discards the draft.

The canvas and object timeline display the segments as one selectable group.
Enable **Split into separate bananas** before confirming to turn the short showers
into independently selectable and editable objects instead. Splitting is one undo
step and preserves the exported banana sequence. With the option off, move, copy
or delete the group as a whole. Drag its original head or tail to
regenerate the segments within the new range; the final short shower is clipped
at the tail. Reopen the window to change the fractions. See the
[data model](PROJECT_MODEL.md#banana-shower-patterns) for persistence and export.

The right previews in Stream, Stack and Banana Shower Edit support **Alt + mouse
wheel** to zoom the time axis around the pointer. Ordinary wheel input scrolls.
Zoom is local to the dialog and does not change object timing or create undo history.

## Slider Inspector

Open Slider Inspector from Layout. Long-press an object in a selection or press
Shift+F to open its compact action list; choosing an editing function opens the
corresponding dock. Ctrl+Shift+M focuses the merge controls in Slider Inspector. It follows the current selection and shows type, start/end times,
duration, spans, control-point count, exported SV and exported path length.
Multiple sliders show value ranges. Managed streams and stacks have no exported
slider SV. These properties are read-only.

Selecting one Legacy Slider exposes **Legacy SV (×)** in Slider Inspector. This
edits its independent authored multiplier (0.1–10); it retains path length and
changes duration without inserting authoring green timing points. Imported
sliders capture their original head SV. New Legacy sliders use **SV** below **DS** in the upper-right corner of the osu!
canvas (initially 1×). The compact sliders share this corner with the X/Y readout.
Catch canvas shows X and the selected object’s previous/next distance ratios in
the same corner. The assist buttons sit below these readouts and scroll when the canvas is too
short to show every button. Catch uses uniformly scaled square buttons and icons,
while osu! keeps a compact palette. Catch previous/next distances share one row
below the X coordinate. The slider covers
0.1–3.2× in 0.1× steps; Shift uses 0.01×. Click its numeric value to enter any SV
from 0.1–10×, confirm with Enter or by clicking outside, and cancel with Esc.
Values above 3.2× keep the slider at its right endpoint. The default saves globally
on release or numeric confirmation and survives restarts without changing existing
sliders or content history. Moving a Legacy slider across green timing boundaries
retains its own SV. Ordinary control dragging retains SV and snaps length; holding
Shift during the drag changes length and authored SV together to preserve duration,
within the legal SV range. The whole gesture is one undo step. In Drawn view,
these gestures edit the retained original controls and regenerate the processed result.

Distance Snap places both circles and Legacy slider heads at the configured spacing
from the preceding object's endpoint; the placement circle shows the same snapped position.
Whole-object dragging uses the preceding unselected object's endpoint, retains times
and translates the selection together. Slider head controls also use DS; other
controls retain their path-editing gestures. Object Snap in the osu! canvas uses
active visible circles and geometric slider endpoints within 6 screen pixels,
independently of the Catch beat-window settings. Moving parents are excluded.
Nearby object targets compete with DS by correction distance. Dragging retains
its reference snapshot until release or cancellation.
The osu! canvas toolbar includes geometric Zoom, Movement Analysis, Insert Break Time
and Snap. Movement Analysis colours connections between successive objects using
the Catch movement classification and labels each connection with its DS ratio.
Connections and the floating panel's corresponding previous/next values are hidden
when the gap from the preceding object's end to the next object's head exceeds
two beats, including placement previews. Beat lengths follow BPM changes across
the gap; exactly two beats remains visible.
The osu! DS ratio uses two-dimensional endpoint-to-head distance and the preceding
object's SV, matching placement spacing independently of canvas zoom. Switching
analysis does not change beatmap content.

Hand-drawn Legacy Sliders also use Droplet Settings for Catch preview, Testplay
and export. Their original two-dimensional controls and visible authored SV are retained
separately from processed geometry and SV. Postprocessing may increase its own
processed SV to preserve duration, without adding editor timing points. Failed
processing retains the previous state. Both representations survive project saves.
Coincident FSlider/Legacy heads share the Legacy slider's actual SV; incompatible
requirements are rejected instead of changing the Legacy slider's SV.

With Legacy Sliders selected in the osu! canvas, **Legacy display** switches
between **Drawn slider** and **Processed slider**. Drawn is the default and uses
the retained original path when available; Processed uses the actual exported
derandomized or randomized path. Without a retained processing baseline, both
views show the same geometry. Switching affects only the osu! canvas, preserving
content, history, Catch preview, Testplay and export. Selected sliders expose the
controls of the displayed path. Editing Drawn controls retains the original path
as the authoring source and regenerates droplet effects in the same undo step.

**Max DS** always includes tiny droplets. **Peak
horizontal speed** is expressed relative to the map's base SV/DPB, shown directly
below it: 0.50× at a base of 1.92× SV / 192 DPB means half that base horizontal
speed. It uses adjacent exported catch events, including tiny droplets, and
integrates the base speed across BPM changes. No raw displacement/time formula is displayed.

The action area contains conversion, clearing internal anchors, stream breaking
and reversion, merging, and the droplet mode and manual-adjustment controls.
Stream/Stack opens its dock panel. Droplet mode selection and correction reset
apply directly in Slider Inspector; see [Droplet randomization](#droplet-randomization).

Inspector properties use two columns, switching to one in narrow docks. A mixed
selection shows its count in a single header row. Operation buttons occupy full
rows with a persistent background and border, matching Settings. Start/end times
use osu! editor format (`03:37:063`). Content fits the dock width and scrolls
vertically when needed. Merge path choices stack vertically in narrow docks.
Dock tab names retain their natural width. When tabs overflow, left/right arrows
scroll the tab strip; selecting a panel through Layout reveals its active tab.
