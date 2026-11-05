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
an unfinished object and for banana showers, which use the fixed Catch banana sound.

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
ordinary wheel moves one current Snap division per notch. From an off-grid timestamp,
the first notch lands on the nearest grid point in the chosen direction. Drag the highlighted yellow line to change its viewport
position without seeking. Both dragging and zoom keep it within 5–95% of the timeline.

The seven lanes are Notes, Normal, Whistle, Finish, Clap, SliderTick and SB Hitsound.
Notes shares Mapping's object-timeline renderer, including combo colours, numbers,
slider bodies, repeats and banana intervals, and adds converted slider-tick markers.
The sound lanes use the same effective banks, indices and volumes as preview audio.
Tiny droplets are silent. SB Hitsound shows unconditional storyboard samples;
Pass/Fail-dependent samples are omitted.

The top toolbar offers **Select / Draw** and **Note based / Precise HS**. Note based uses compact tags centred on event times. Tag width fits the bank/index text with horizontal padding
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
index and centre the letter. The toolbar groups Select/Draw, Note based/Precise HS,
and the HS text toggle with vertical separators. HS text controls sound-tag labels
in both display modes and is remembered with the Hitsounding layout; it leaves
Notes numbering, timing labels and waveforms visible. Precise HS retains the bank and non-default index beside the waveform. Timing tags show bank and optional index (for example S:2) above volume; both lines
are bold, with the bank line three points larger. A short coloured stem marks their timestamp.
Timing tags fill the interval up to the next timing point. Their left-aligned text stays
at the visible left edge when the interval begins outside the viewport.

In **Select**, only clicking a Notes object or an existing sound tag selects its event.
Selecting a sound does not seek. Clicking empty timeline space clears selection and seeks
only while paused; during playback, empty grid and ruler clicks preserve the playing position;
clicking the lane-label column does not seek. Dragging empty grid space draws a selection
rectangle across Normal, Whistle, Finish, Clap and SB Hitsound tags. Its starting
time follows the scrolling timeline during playback while the opposite edge stays
under the pointer. Selection refreshes each frame, including sounds that have scrolled
offscreen. Ctrl+C copies selected sounds, Ctrl+V
pastes their relative times at the playhead onto existing fruit events (within 1 ms),
Ctrl+X cuts deletable selected sounds, and Delete removes selected additions or SB sounds. Normal remains undeletable. Each batch is one
undo step. When source and target banks or sample indices differ, a dialog offers the
copied sampleset, the target sampleset, or cancellation. Adjustments affect target event
overrides, including their shared addition bank/index, without rewriting timing intervals.
In **Select**, drag an existing Whistle, Finish or Clap tag onto another fruit event to
move that addition, with a half-opacity preview and the same sampleset confirmation.
Normal and SliderTick cannot be moved. A move is one undo step.
In **Draw**, click or drag across existing fruit event
times in Whistle, Finish or Clap to add that addition; the half-opacity ghost previews
the target. A stroke is one undo step. Right-click removes additions, never Normal.
W/E/R toggle the selected event's Whistle/Finish/Clap. Sampleset and Additions selectors
edit the selected event or set banks for drawing. The three additions share one bank
and index; a vertical connector links active additions belonging to the same event.
Audible events flash with a white edge and a coloured outer glow for 280 ms as playback crosses their timestamps. Both Notes and sound tags show this feedback.

SB Hitsound supports selection, marquee selection, cut/copy/paste, deletion and dragging.
Pastes preserve relative times, filenames, layers and volumes without requiring a note
at the destination. Dragging snaps to the beat grid and previews without changing content.
Edits participate in undo/redo. Moving or deleting a shared `.osb` sample edits that shared
file, so it affects every difficulty using it; pasted copies belong to the current map.
External changes to a shared file are checked before edits and undo/redo.

**Timing Helper** follows the playhead when nothing is selected and the event
time when an object is selected. Clicking a timing interval or its stem selects that
specific timing point. At an exact timestamp with existing timing points, the helper
shows each point in a separate section divided by horizontal rules. Offset, BPM/SV,
meter, bank, sample index, volume and effects can be applied to the existing point.
Only timestamps without a point show effective timing information and **Apply as new
inherited timing point**, which inserts a green line while preserving effective SV.
Changes remain drafts until applied and support undo.
The Hitsounding dock menu provides Config, Timing Helper, Song Setup and Preview independently
of Mapping.

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
map’s red and green timing points; more than eight choices scroll. The selector
does not create or edit sample indices. Audio rows provide audition controls and decoded waveform previews. Search
filters the current folder by name. Long lists scroll within their own section.
View and library navigation do not edit beatmap objects.

Each sound track has a mute button. Muting greys its entire row and suppresses that
track only during Hitsounding playback; Mapping and Testplay ignore these session
settings. Note-based sound labels use a fixed font size when **HS text** is enabled
and hide when the block is too narrow. Equal-width blocks with the same number of
index digits use a shared width threshold, independent of the particular digits
or sample bank. Other blocks entering the viewport do not affect this decision.

Drag green timing handles horizontally to move inherited points to the nearest
Snap. Marquee-select handles to move several green points together, preserving
their relative offsets while snapping the grabbed point. Red points cannot be
dragged or included in this selection. Timing intervals update during the preview;
release commits one undoable edit, and Escape cancels it. Right-click empty space
in the handle strip to create an inherited point at the nearest Snap using the
same parameter inheritance as Timing Panel. Right-click a timing block or handle
to edit it in Timing Helper, opening the panel when needed. Green points also
offer Delete. The Delete key and context-menu Delete remove all selected green
points in one undoable edit. Right-clicking a selected block or handle preserves
the group; right-clicking an unselected point selects that point instead.
Timing Helper's sample bank choices are Soft, Normal and Drum.

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
