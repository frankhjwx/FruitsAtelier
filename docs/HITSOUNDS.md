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

The right toolbar edits Whistle, Finish and Clap flags for fruits and slider edges through undoable transactions. Whole-slider selection applies an addition to all edges; clicking a slider fruit scopes it to that edge. ObjectFlags updates the preserved stable columns while retaining sample banks, volume, custom filenames and unrelated flags. Editing and export controls are described in [Editing controls](EDITOR_UI.md#distance-spacing-and-object-flags).

## Sample selection

`HitsoundResolver` reads the object's preserved `.osu` line, including FSliders converted
from imported sliders. Timing points provide the sample bank, custom index, and volume;
nonzero circle sample values override them. Fruit and slider-edge sample lookup includes the legacy 5 ms tolerance around timing boundaries. Droplets inherit the slider body's sample bank, index and volume at its start plus 6 ms; later timing points do not change those tick samples. These tolerances select samples only and do not shift object playback times. Legacy slider hitSample fields supply banks;
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

Testplay plays only caught objects. Windows prepares a separate persistent WASAPI
output with 10 ms requested latency for live judgements, before starting the song.
Live voices enter its next audio callback instead of the music's 80 ms read-ahead
buffer. The device determines the effective latency. Stop and seek clear live voices
as well as scheduled preview events. macOS uses its existing persistent live mixer.

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

Song tempo is adjustable to 25%, 50%, 75%, and 100% with pitch preserved. Windows stretches music before mixing hitsounds: event map offsets are divided by tempo to locate output frames, while each sample advances at its normal sample rate. macOS applies a music-only AVAudioUnitTimePitch and divides event offsets by tempo when scheduling the independent hitsound engine. Speed changes retain the map playhead; macOS cancels future hitsounds and reschedules against the new start time.

## Volume

Library settings store All, Song and Hitsound percentages independently of beatmap sample volume. Effective song gain is All × Song; effective sample gain is All × Hitsound × the resolved beatmap sample volume. Windows applies song gain before preview mixing and hitsound gain to scheduled and live voices before clipping. macOS updates its music node and an atomic native hitsound mixer gain, including queued and active voices. Changes do not restart playback or change timing. Automated device output remains muted regardless of these preferences.
