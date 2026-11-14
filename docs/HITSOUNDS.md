# Preview hitsounds

The editor schedules hitsounds at converted Catch object timestamps. Windows
mixes them into the music's WASAPI output stream; macOS mixes preloaded PCM through a persistent
AVAudioSourceNode, aligned with the music transport. Music playback remains
the source of the playhead position. Pausing, seeking, changing maps, or closing the
window stops active hitsound voices. Seeking does not play the skipped interval.

| Catch object | Sample |
| --- | --- |
| Standalone fruit | Normal plus whistle, finish, and clap flags |
| Slider head, repeat, tail | The corresponding edge flags and sample banks |
| Droplet (slider tick) | `slidertick` |
| Tiny droplet / slider body | Silent; no looping slide or whistle sound |
| Banana | Packaged Catch banana sample |
| Banana shower end | Spinner normal sample plus whistle, finish, and clap flags |

The Hitsounding Panel exposes each spinner / banana-shower end as an editable sound event.
Fixed per-banana sounds are hidden from the editable lanes.
Placement, selection, Q cycling and the Sampleset/Additions selectors edit its preserved
spinner sample fields. The end sample plays once in preview and Testplay, independently
of catching individual bananas. Patterned showers share their authored sample settings
across segment ends. Export retains these settings in the spinner line.

Bananas at the same timestamp share one sample in preview and Testplay, so stacked
banana showers do not amplify the sound. Testplay plays it when any banana at that
timestamp is caught; each banana still has its own visual and judgement.

The right toolbar edits Whistle, Finish and Clap flags for fruits and slider edges through undoable transactions. Whole-slider selection applies an addition to all edges; clicking a slider fruit scopes it to that edge. ObjectFlags updates the preserved stable columns while retaining sample banks, volume, custom filenames and unrelated flags. Editing and export controls are described in [Editing controls](EDITOR_UI.md#distance-spacing-and-object-flags).

## Sample selection

The **Sampleset** and **Additions** dropdowns above the four editing tools offer
Auto, Normal, Soft and Drum. Sampleset selects the base sound bank; Additions selects
the bank for Whistle, Finish and Clap. Auto on Sampleset inherits timing, while Auto
on Additions follows the base bank. These selectors do not change sample volume.
Selected objects show their authored settings; differing values display Auto without
rewriting them. Choosing a value applies it in one undo step. A selected slider fruit
targets its edge; a whole-slider selection updates all edges and the object bank.
Existing custom indices, volumes, filenames and hitsound flags are retained, including
copied hitsound overrides. With no selection, or with the Fruit tool active, the
dropdowns set defaults for newly placed fruits and sliders. They are disabled during
an unfinished object and while the Banana placement tool is active. A selected banana
shower edits the spinner end sample; its Whistle, Finish and Clap controls are also editable.

The menus close on selection, an outside click or Escape. The shared editor implements
the same controls on Windows and macOS. The underlying fields follow the
[osu! sample format](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_(file_format)#custom-hit-samples).

**Settings → Audio → Use skin's sound samples** is enabled by default. Disabling it
skips both the selected and configured default skin's audio and uses packaged samples
for skin fallback. Beatmap-provided samples and skin visuals remain unchanged. The
preference is saved immediately; changing it clears scheduled hitsounds and preloads
the project's samples again for preview, testplay and sample audition on both platforms.

`HitsoundResolver` reads the object's preserved `.osu` line, including FSliders converted
from imported sliders. Timing points provide the sample bank, custom index, and volume;
nonzero circle sample values override them. Fruit, slider-edge, and droplet sample lookup includes the legacy 5 ms tolerance around timing boundaries. Each droplet uses the sample bank, index, and volume at its own time, so a timing point within a slider can change its tick sample. This tolerance selects samples only and does not shift object playback times. Legacy slider hitSample fields supply banks;
their index and volume follow timing. Normal, soft, and drum banks are supported. Addition
bank zero follows the normal bank. Slider edge settings select samples independently for
the head, repeats, and tail. An explicit custom filename replaces the normal layer
and retains enabled additions, matching lazer's legacy parser.

Resources are resolved relative to the source map directory, or the audio directory for
a new document without a source map. Lookup is case-insensitive and limited to indexed
files inside that directory, excluding symbolic links. For an enabled custom sample index,
lookup checks `.wav`, `.ogg`, then `.mp3`; index 1 has no numeric suffix, while higher
indices use names such as `soft-hitclap2.wav`. Index zero selects the current skin's unindexed samples. Missing beatmap samples fall back to the current skin, then the configured default skin, then packaged osu! classic recordings,
including the normal layer on fruits without additional flags. Normal, soft, and drum
each include hitnormal, hitwhistle, hitfinish, hitclap, and slidertick. Banana uses its
separate Catch sample. Source revisions, checksums and asset licenses are in
[Default samples](../assets/audio/osu/README.md).

A synthesized emergency tone remains available for corrupt/oversized input or missing
application assets. Imported `.osk` files include supported WAV, OGG and MP3 hitnormal, hitwhistle, hitfinish, hitclap and slidertick samples for all three banks. Skin switches invalidate resolved events and preload the new sample bank across difficulties. Older imported caches are re-extracted from their stored archives when selected. Within a skin, WAV takes precedence over OGG and MP3; a selected skin always takes precedence over the fallback skin regardless of file format.

Opening a project preloads the samples referenced by every difficulty, including notes far
beyond the current playhead. The editor supplies isolated document snapshots. macOS builds
the PCM bank on a background loader and waits for it before starting playback; Windows
preloads during project opening. Identical file paths share decoded memory. A project bank
has a 256 MiB limit and source/decoded samples have a 16 MiB limit. Samples beyond the bank
limit are skipped and logged rather than evicting earlier samples and decoding them again
during playback. Replacing a project releases its previous bank. Newly edited events are
prepared ahead of playback; Mac playback misses never synchronously load a file.

Windows retains a bounded 2,048-event window of timestamped samples and mixes them at
the corresponding music frames before the final PCM conversion and output gain. Mono
hitsounds are copied to each music channel; differing sample rates use linear interpolation.
The host primes 250 ms of events before play/resume/seek and extends this horizon from
transport polls. Both music and hitsounds pass through the same device buffer, avoiding
an additional per-hit output buffer. UI stalls beyond the horizon can still omit attacks.
Pause, seek, and content changes clear the event window. Pausing rebuilds music output
at its consumed frame, so resume uses the same position for audio and the playhead.

Testplay plays only caught objects. Windows measures submitted PCM ahead of the
device clock and advances catch judgement by that lead plus 25 ms of output time.
Each caught sample is placed at its original map timestamp in the music stream.
A late catch starts its full sample at the next writable frame. The 10 ms WASAPI
latency is a request; the actual buffer lead varies by device. Stop and seek clear
testplay and preview events. macOS uses its existing persistent live mixer.

macOS uses one persistent
`AVAudioEngine` / `AVAudioSourceNode` mixer with 128 voices and a bounded 2,047-command queue.
Native WAV/MP3 decoding and OGG conversion happen during preparation. The real-time callback
only mixes immutable PCM into the output: no file I/O, allocations, locks, or Objective-C
calls. Under voice pressure the oldest attack is replaced; the newest command is dropped
if the queue is full. Pause/seek cancellation invalidates queued and active voices by
changing their generation. The engine is stopped before its sample memory is released.

The Mac host queues the next
100 ms of events with original beatmap timestamps. `MacAudio` maps its music device clock
to the monotonic host clock used by the source node's render timestamps. Music starts/resumes
with a 150 ms scheduling lead. This lead delays startup, not hitsounds relative to music.
Before starting or resuming, the music player stops its previous output session, restores
the paused map position, and prepares again before sampling a new device-clock origin.
A plain pause followed by `playAtTime` can retain stale output timing, so resume must use
this same reset path even when the playhead did not move. The hitsound PCM bank and mixer
remain loaded throughout. Seek establishes a new origin; pause, content changes, and clock jumps larger than 250 ms
cancel queued sounds before rescheduling.

Do not put per-note `AVAudioPlayer` creation, `prepareToPlay`, seek, or `playAtTime` calls
back on the UI thread. Even prepared players can block when scheduling or resetting many
overlapping samples. Preloading encoded bytes alone is insufficient: decode to PCM before
playback and keep the output device running. Timing tests check actual source-node render
timestamps as well as the music/host-clock mapping. These checks do not measure acoustic
latency from speakers or headphones.

## Hitsounding workspace

Choose **Hitsounding Panel** from the same workspace selector as Mapping and Timing.
Its dock layout and display mode are remembered independently. The horizontal ruler
uses the current Snap division and the shared osu-style tick colours and lengths.
Alt+wheel zooms around the pointer's time without changing playback position;
the visible span is bounded to 250 ms–10 s, or the song duration when shorter.
Middle-drag pans time over any lane. Ctrl+wheel changes Snap, and Shift+wheel uses
Mapping's coarse navigation. In Default mode, ordinary wheel moves one current Snap
division per notch while paused. From an off-grid timestamp,
the first notch lands on the nearest grid point in the chosen direction. Drag the highlighted yellow line to change its viewport
position without seeking. Both dragging and zoom keep it within 5–95% of the timeline.

The seven lanes are Notes, Normal, Whistle, Finish, Clap, SliderTick and SB Hitsound.
Notes shares Mapping's object-timeline renderer, including combo colours, numbers,
slider bodies, repeats and banana intervals, and adds converted slider-tick markers.
The sound lanes use the same effective banks, indices and volumes as preview audio.
Tiny droplets are silent. SB Hitsound shows unconditional storyboard samples;
Pass/Fail-dependent samples are omitted.

The top toolbar offers **Note based / Precise HS**. Note based uses compact tags centred on event times. Tag width fits the bank/index text with horizontal padding
and shrinks symmetrically to fit neighbouring events at dense zoom levels. Text keeps
a fixed size and is hidden when the tag cannot contain it. Simultaneous samples share
columns within their timestamp. Precise HS uses left-aligned horizontal labels and
peak-normalized waveform envelopes along the bottom, including at narrow zoom levels;
Object sounds end visually at the next occurrence in the same sound lane when their
sample extends past it; intervening notes and sounds in other lanes do not truncate them;
storyboard samples use the next unconditional sample as their visual boundary.
Decoding is asynchronous and its cache is bounded and retired with the difficulty.
Normal, Whistle, Finish, Clap and SliderTick tags use their resolved sample bank for colour: Soft
green, Normal amber, and Drum red. Borders, waveforms and playback glows share that
colour; these five lane labels have neutral markers. Timing lines retain red/green
colouring, while SB Hitsound retains purple.
Note-based bank tags show S, N or D above the sample index; default samples omit the
index and centre the letter. The toolbar groups Note based/Precise HS,
and the HS text toggle with vertical separators. HS text controls sound-tag labels
in both display modes and is remembered with the Hitsounding layout; it leaves
Notes numbering, timing labels and waveforms visible. Precise HS retains the bank and non-default index beside the waveform.
Four timing tracks above Notes show BPM, Hitsound Set (bank and optional index, for
example S:2), volume and Kiai. Each track independently merges consecutive equal
values for display, while preserving the original timing points and their stems.
Labels stay at the visible left edge; narrow intervals reveal their text on hover.
Kiai shows only On or Off: On uses the overview timeline's orange and Off uses
blue-grey. Timing edits and drag previews refresh these intervals. Red timing
points can be selected but cannot be dragged in Hitsounding.

Clicking a Notes object or an existing sound tag selects its event.
Selecting a sound highlights only its sound tag, leaving the Notes graphics
unhighlighted, and does not seek. Clicking empty grid space clears selection and
preserves the playing position.
Left-click the upper time ruler to seek directly to the corresponding time, while
retaining the playing or paused state. Dragging the play line still adjusts its viewport position.
Ctrl-click toggles individual sound tags, Notes events, storyboard sounds, and red or green timing blocks or handles without replacing the existing selection.
Clicking the lane-label column does not seek. Dragging empty grid space draws a selection
rectangle across Normal, Whistle, Finish, Clap and SB Hitsound tags. Its starting
time follows the scrolling timeline during playback while the opposite edge stays
under the pointer. Selection refreshes each frame, including sounds that have scrolled
offscreen. Ctrl+C copies selected sounds, Ctrl+V
pastes their relative times at the playhead onto existing fruit events (within 1 ms),
Ctrl+X cuts deletable selected sounds, and Delete removes selected additions or SB sounds. Normal remains undeletable. Each batch is one
undo step. With sounds selected, right-clicking a selected sound immediately deletes the selected additions and storyboard sounds. Right-clicking empty grid space clears selection without editing content. Right-clicking an unselected sound deletes that sound. Normal remains undeletable. When the red/green timing points governing the source and target have different sample indices, a dialog offers the
copied sampleset, the target sampleset, or cancellation. Bank differences and per-event
index overrides do not trigger this dialog. Matching timing indices transfer the source
bank directly and preserve an inherited sample index so later timing edits still apply.
Explicit nonzero object indices remain explicit. A zero object index, including saved
event overrides, inherits timing for display, playback and export. Adjustments affect target event
overrides, including their shared addition bank/index, without rewriting timing intervals.
In Select mode, drag an existing Whistle, Finish or Clap tag to move it or its selected
addition group. In Draw mode, pressing a tag in a multi-selection also starts group
movement, taking priority over placement and marquee selection. The original tags remain visible and half-opacity ghosts show the
destination with relative timestamps and lanes preserved. Release applies one undoable
move only when every addition has a destination fruit event; missing destinations leave
the complete group unchanged. Escape cancels the preview. Sampleset mismatches use the
same confirmation as paste. Normal and SliderTick remain attached to their notes.
Select is the default input mode. Clicking an empty addition slot does not place a
sound; dragging empty space selects sounds with a marquee. Draw mode previews the
addition under the pointer: click to place one sound, or drag to marquee-select
sounds without placing any. A placement is one undo step. Both modes
retain existing-sound selection, clipboard operations and right-click
deletion. Placement and existing-sound selection take priority over dragging the play
line where they overlap. Right-click removes additions, never Normal.
Sampleset and Additions at the upper left display the authored banks of the selected
events, using Auto for inherited or mixed values as in Mapping. Changing either
selector applies that bank to every selected event in one undo step; the other bank
remains unchanged. With no selected events, the selectors retain their independent
placement defaults; Hitsounding painting uses its own brush instead.

The left tool column offers **Select (1)**, **Draw (2)**, then **Soft (A)**,
**Normal (S)** and **Drum (D)** brushes. Mode and brush are independent selections. In Select mode, brush buttons and Q/A/S/D are enabled only while notes or sound events are selected; otherwise the buttons are dimmed. Draw mode always enables the brushes.
**Q** cycles Soft → Normal → Drum, using the earliest selected event's current bank
when a selection exists. In either mode, Q/A/S/D and the brush buttons
apply the bank to the selection in one undo step. Selected Normal tags change their
normal bank; selected addition tags change their shared addition bank. Selected
Notes parents change both banks on their events. Other sample fields are retained.
Without a selection, Draw changes only its placement brush. Placing an addition
uses that bank while preserving Normal. W/E/R toggle Whistle/Finish/Clap on the selected
event. Text inputs and modified shortcuts retain priority. Hover tips explain each
tool's shortcut and interaction behavior.

Each track has a **Lock** button beside its label. Locked tracks show a pale red background
behind their notes, cannot be selected, and reject direct editing and paste. A selection
rectangle can start in a locked track and cross other tracks; only unlocked sound
tracks contribute selected tags. Notes starts
locked; the other tracks start unlocked. Lock buttons are teal when unlocked and pale red when locked. Locks do not mute playback or change history.
Unlock Notes to edit existing notes in either mode. In Draw, an empty-space hover
shows a snapped placement preview and a click creates a circle at Mapping
X=256 (osu! Y=192), drag empty space to marquee-select parents, and Ctrl-click to
add or remove a parent from the selection. Drag an existing note body to move the
selection in time. Drag a slider's head or tail to adjust that endpoint while
retaining the other; Ctrl+plus/minus changes its reverse count as in Mapping.
Dragging retains the committed note graphics and shows half-opacity destination ghosts
for the selected parents. Release creates one undo step and Escape cancels the preview.
Right-click a note to delete
it, or the selected group if that note is selected, with undo support.

Notes selection uses a separate clipboard payload from sound tags. Ctrl+C/X/V
copies, cuts or pastes complete selected notes. A destination interval with no
notes receives new copies with relative timing preserved. If existing notes
intersect the interval, every parent must match the source's relative start/end,
slider span count and nested event rhythm; a complete match transfers sounds while
preserving destination geometry. Partial overlap or a rhythm mismatch rejects the
whole paste with an error. Sound-tag-only copy/paste retains its existing behavior.
Notes mutations require its track to remain unlocked.

The workspace combines drawing, clicking to select, Ctrl-click selection and empty-space marquee selection. The three additions share one bank
and index; a vertical connector links active additions belonging to the same event.
Audible events flash with a white edge and a coloured outer glow for 280 ms as playback crosses their timestamps. Both Notes and sound tags show this feedback.

SB Hitsound supports selection, marquee selection, cut/copy/paste, deletion and dragging.
Pastes preserve relative times, filenames, layers and volumes without requiring a note
at the destination. Dragging a selected SB sound moves the selected SB group with one
shared snapped offset and half-opacity ghosts, without changing content before release.
Edits participate in undo/redo. Moving or deleting a shared `.osb` sample edits that shared
file, so it affects every difficulty using it; pasted copies belong to the current map.
External changes to a shared file are checked before edits and undo/redo.

**Timing Helper** follows the playhead when nothing is selected and the event
time when an object is selected. Clicking a timing interval or its stem selects that
specific timing point. A fixed-height information card shows the context, timestamp,
effective BPM and active red/green timestamps in every selection state. The parameter
area starts at the same vertical position, and the nearby list stays anchored below it.
At an exact timestamp with existing timing points, the helper
shows each point in a separate section divided by horizontal rules. Offset, bank, sample index, volume and the Kiai toggle edit the existing point directly; BPM and meter controls appear only for red points. Numeric inputs take effect on Enter or focus loss. Toggling Kiai preserves the point's other effect flags.
Sample indices use Default, Custom 1 and Custom choices, with the custom index input on the Custom row. Existing-point volume uses one row for its label, slider, numeric input and percent suffix; Hit, Finish, Whistle and Clap buttons preview the current sample settings.
Clicking the volume track changes the value by five percentage points toward the pointer. Dragging and numeric input retain one-percent precision. Volume updates while dragging, with one undo step for the complete gesture. The helper does not display or edit SV; sound edits preserve the green line's beat length.
Only timestamps without a point show effective timing information and **Apply as new
inherited timing point**, which inserts a green line while preserving effective SV.
At these timestamps, **Apply to affecting timing line** writes the draft bank, index and volume to the latest active timing point without moving it or adding another point. Values remain drafts until either Apply button is clicked. Existing-point edits support undo and mark the map as modified; saving remains explicit.
A separator and **Nearby Timing Points** title introduce up to seven nearby timing points in chronological order, showing timestamp, timing/sample information and volume. The current point is highlighted, retaining red for red points; others fade with distance. The wheel only scrolls the visible rows, leaving the playhead and selection unchanged. Clicking a row selects that point in the helper and recenters the list without moving the playhead or canvas. The initial view prefers three points on each side, filling from the other side at map boundaries; it has no separate scrollbar.
The parameter scrollbar reflects the visible proportion of its content. Drag its thumb
or click its track to scroll; the nearby list stays anchored. The scrollbar disappears
when all parameters fit.
The Hitsounding dock menu provides Config, Timing Helper, Song Setup and Preview independently
of Mapping.
Restoring Hitsounding as the active panel on editor startup loads its sound library immediately, without requiring pointer input.

**Map Samplesets** selects the map's indexed sample groups and S/D/N bank. Preview
buttons audition the assigned sample or its skin/default fallback. The Default group
uses skin/osu! samples and is read-only; numbered groups display actual indexed resource
filenames. Fallback filenames are grey, while map resources use the accent colour. Drop a library
file onto a slot to replace that bank/index's audio. This updates the shared map
resource file, including other difficulties that use it, and is recorded in undo
history. Replacement also refreshes preview audio; conflicting external file changes
are rejected during undo/redo.

**Hitsound Library** stores independent copies under `workspace/HitsoundLibrary`.
Double-click directory rows to browse nested folders, or use the parent and Home buttons.
Navigation stays inside this root and excludes symbolic links/junctions. Search filters
files and folders in the current directory; imports are saved into that directory.
Drop folders or WAV, OGG or MP3 files into the Hitsounding workspace to import them, or drag
an assigned map sample into the library pane. Folder imports preserve nested directories
and copy supported audio files. Right-click the library to open the current folder
in the system file manager. The navigation bar shows the path relative to
`HitsoundLibrary`, omitting the start when space is limited and leaving it empty at
the root. External changes refresh the listing, waveforms and audition samples.
The footer counts folders and audio files in the current directory; overflowing
lists have a draggable scrollbar. Drops are accepted throughout the library pane.

The context menu provides new folder, rename, copy, cut, paste and recycle operations.
The corresponding shortcuts are Ctrl+Shift+N, F2, Ctrl+C, Ctrl+X, Ctrl+V and Delete.
Click or drag a selection rectangle to select files and folders; Ctrl adds/toggles,
Alt subtracts, and Shift selects a range. Drag selected items onto a folder to move
them, or hold Ctrl to copy. Hover over the parent or Home button for one second
during a drag to navigate while retaining the selected source items. Existing files
are preserved with numbered destination names. Library file operations are independent
of beatmap undo history.

Library audition plays one file at a time. Starting another file stops the previous
one; the active row shows a progress fill, outline and Stop button. Navigation and
file changes stop audition. Audition uses independent playback from map preview.

In Precise HS, clicking a sound block selects it and reveals an audition button
inside its lower-left waveform area when the visible block is wide enough.
Play/Stop and the progress fill use the same exclusive audition
channel as Hitsound Library, so starting a sound in either view stops the previous
audition. Selecting a block alone does not start playback or move the playhead.
Imports preserve existing different
files with a numbered filename and reuse identical copies. Library files can be
dragged to map slots; their names need not match osu! sample filenames. Samples are
limited to 16 MiB each and one drop accepts up to 128 files.

**Hitsounding Config** contains Map Samplesets above Hitsound Library in one dock
panel. Resize their internal divider to allocate space; the two sections keep their
order. Hovering or dragging the divider uses a vertical resize cursor.
The SampleSet dropdown offers Default and the custom indices defined by the
map’s red and green timing points; more than eight choices scroll using the wheel, scrollbar dragging, or scrollbar track clicks. The selector
does not create or edit sample indices. Audio rows provide audition controls and decoded waveform previews. Search
filters the current folder by name. Long lists scroll within their own section.
View and library navigation do not edit beatmap objects.

Each sound track has a mute button, highlighted green while audible and grey while muted. Muting greys its entire row and suppresses that
track only during Hitsounding playback; Mapping and Testplay ignore these session
settings. Note-based sound labels use a fixed font size when **HS text** is enabled
and hide when the block is too narrow. Equal-width blocks with the same number of
index digits use a shared width threshold, independent of the particular digits
or sample bank. Other blocks entering the viewport do not affect this decision.

Drag green timing handles horizontally to move inherited points to the nearest
Snap. Marquee-select handles to move several green points together, preserving
their relative offsets while snapping the grabbed point. A selection containing
red points cannot be dragged. Timing intervals update during the preview;
release commits one undoable edit, and Escape cancels it. Right-click empty space
in the handle strip to create an inherited point at the nearest Snap using the
same parameter inheritance as Timing Panel. Right-click a timing block or handle
to edit it in Timing Helper, opening the panel when needed. Green points also
offer Delete. The Delete key and context-menu Delete remove all selected green
points in one undoable edit. Right-clicking a selected block or handle preserves
the group; right-clicking an unselected point selects that point instead.
Ctrl-select or box-select red and green timing points, then press **Ctrl+C** to copy
the selection. **Ctrl+V** in another difficulty pastes at the original timestamps,
replacing all destination timing points at those timestamps. Other timestamps are
unchanged, pasted points remain selected, and the paste is one undo step.
Press **5** in Hitsounding to add an inherited point at the playhead. This binding is configurable in Settings.
Ctrl-select exactly two adjacent green points to reveal **Volume Ramp Generation** in Timing Helper.
No other green point may lie between them. The endpoints must have different volumes
and matching SV, meter, sample bank, sample index and effects. Choose the beat interval from the dropdown
and click **Generate** on the same row; available intervals follow the Snap division choices.
The section has separators above and below it. When it is absent, the separator
between the context card and timing parameters remains. Multi-selection hides Offset.
Generation follows musical beat intervals across BPM changes and interpolates volume
linearly in time between the unchanged endpoints. Volumes are rounded to integers;
an intermediate line is omitted when its rounded volume matches the preceding line. Existing interior points receive
the interpolated volume; their other properties are preserved. Missing green points
inherit the active timing properties. Generation selects both endpoints and every
generated green point together. The complete operation is one undo step.
Timing Helper's sample bank choices are Soft, Normal and Drum.
The left-hand sample-bank tools highlight the resolved bank of selected sounds;
mixed banks have no highlighted choice. With no selection, they show the drawing bank.
While playing, timeline wheel navigation advances by full beats, matching Mapping;
paused navigation follows the selected Snap subdivision.
Left/Right and Shift+Left/Right use Mapping's keyboard navigation: one/four beats
while playing and one/four Snap steps while paused in Default mode. Lazer mode uses
its BPM-dependent playback seek distance and coarse paused grid. These bindings are
configurable in the Hitsounding section of Key Bindings. Home jumps to song start;
Z jumps to the first object and then song start; V/End jumps to the last object end
and then song end. X starts playback at song start, and Space/C toggles playback.
Up/Down visits timing points with the active editing profile's direction. Bookmark,
lazer parent/sample-boundary, speed and Snap shortcuts also follow Mapping.
J/K moves the active unlocked Notes, addition or SB selection earlier/later by one
Snap subdivision; every addition destination must match a fruit event.

Navigation remains available while dragging or selecting: ghosts update beneath a
stationary pointer, with content committed only on release. Moving into either
24-pixel lane edge scrolls progressively faster, up to 600 pixels per second,
matching Mapping's horizontal object timeline. Marquees keep their time anchor and
include objects scrolled out of view. Escape cancels the edit and stops scrolling.
See [Mapping and Hitsounding command coverage](HITSOUND_MAPPING_PARITY.md) for the
remaining navigation, selection and editing differences.
After copying sounds or Notes while paused, selecting a different target without seeking
makes its first timestamp the paste destination. Otherwise, paste starts at the playhead. Matching Notes allow
one millisecond of rounding difference and do not treat a preceding slider ending
at the destination as part of the target rhythm.

## Maintenance

Keep sample selection in Core and transport boundary handling in the shared editor.
Platform players own decoding, mixing, native handles, and disposal. Hitsounds must not
modify beatmap content, undo history, or the music clock. Keep Catch rules separate from
osu!standard slider-body audio rules.

`HitsoundTests` in the shared App suite covers bank/index/volume inheritance, explicit
filenames, path isolation, slider edges, silent tiny droplets, bananas, simultaneous
objects, pause/resume, seek, and replay. Mac native tests exercise custom WAV/OGG samples
and stopping voices with their output muted. The device-free Windows audio suite
(`--hitsound-check`) compares mixed PCM, gain, clipping, custom WAV decoding, and stop
behavior, and runs in Windows CI. `HitsoundLatencyTests` exercises Mac scheduling and reuse. `HitsoundPerformance` measures
dense scheduling batches, checks preload across difficulties, and tests native PCM overlap,
gain, clipping, future deadlines, and cancellation without audible output. Run the affected suites and both platform
builds as described in [Testing](TESTING.md). Native device tests keep application output
muted without changing system volume.

Behavior references: [osu! file format](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_(file_format)),
and osu!lazer revision `f599aa874bc09078993bee720422213a8d9f0601`:
[JuiceStream](https://github.com/ppy/osu/blob/f599aa874bc09078993bee720422213a8d9f0601/osu.Game.Rulesets.Catch/Objects/JuiceStream.cs),
[legacy sample parser](https://github.com/ppy/osu/blob/f599aa874bc09078993bee720422213a8d9f0601/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs),
and [Banana](https://github.com/ppy/osu/blob/f599aa874bc09078993bee720422213a8d9f0601/osu.Game.Rulesets.Catch/Objects/Banana.cs).
These references describe the rules; the preview scheduler, resolver, players, and fallback
sample generator are implemented locally.

Apple scheduling references: [play(atTime:)](https://developer.apple.com/documentation/avfaudio/avaudioplayer/play(attime:))
and [deviceCurrentTime](https://developer.apple.com/documentation/avfaudio/avaudioplayer/devicecurrenttime).

Apple mixer reference: [AVAudioSourceNode](https://developer.apple.com/documentation/avfaudio/avaudiosourcenode).

## Playback speed

Song tempo is adjustable to 10%, 25%, 50%, 75%, 100%, and 150% with pitch preserved. Windows stretches music before mixing hitsounds: event map offsets are divided by tempo to locate output frames, while each sample advances at its normal sample rate. Rebuilding Windows output discards prior hitsound attacks and recalculates queued future events from their map timestamps using the new output origin and tempo. macOS applies a music-only AVAudioUnitTimePitch and divides event offsets by tempo when scheduling the independent hitsound engine. Speed changes retain the map playhead; macOS cancels future hitsounds and reschedules against the new start time.

## Volume

Library settings store All, Song and Hitsound percentages independently of beatmap sample volume. Effective song gain is All × Song; effective sample gain is All × Hitsound × the resolved beatmap sample volume. Windows applies song gain before preview mixing and hitsound gain to scheduled and live voices before clipping. macOS updates its music node and an atomic native hitsound mixer gain, including queued and active voices. Changes do not restart playback or change timing. Automated device output remains muted regardless of these preferences.

The Timing Helper header lists **Affecting Timing Lines** using the same timestamp,
timing information and volume columns as Nearby Timing Points. Click either active
red or green row to select it without moving the playhead or canvas. The header’s
**Jump** button seeks to the displayed point when requested and is disabled when
the playhead already matches its displayed millisecond. Missing active lines retain
a placeholder so the header height stays constant.

**Add new inherited timing point here** is available without changing draft values.
While paused at an existing timing timestamp (matched at the displayed millisecond precision), the helper displays that point’s full live editing
controls while retaining the Current timestamp heading until a point is selected.

During playback, Current timestamp retains its regular form when crossing timing
points. Starting playback or dragging the transport timeline clears the timing-point
selection and resumes following the current time.

Within one mapset, difficulty switches retain the active editor panel and the hitsound
clipboard. Copy in one difficulty and paste at the playhead in another; paste uses
the destination difficulty's undo history. Opening another project clears the clipboard.
