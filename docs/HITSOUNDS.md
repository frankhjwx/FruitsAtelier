# Preview hitsounds

The editor plays hitsounds as the music clock crosses converted Catch objects. Windows
uses a separate WASAPI mixer; macOS mixes preloaded PCM through a persistent
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

## Sample selection

`HitsoundResolver` reads the object's preserved `.osu` line, including FSliders converted
from imported sliders. Timing points provide the sample bank, custom index, and volume;
nonzero circle sample values override them. Legacy slider hitSample fields supply banks;
their index and volume follow timing. Normal, soft, and drum banks are supported. Addition
bank zero follows the normal bank. Slider edge settings select samples independently for
the head, repeats, and tail. An explicit custom filename replaces the normal layer
and retains enabled additions, matching lazer's legacy parser.

Resources are resolved relative to the source map directory, or the audio directory for
a new document without a source map. Lookup is case-insensitive and limited to indexed
files inside that directory, excluding symbolic links. For an enabled custom sample index,
lookup checks `.wav`, `.ogg`, then `.mp3`; index 1 has no numeric suffix, while higher
indices use names such as `soft-hitclap2.wav`. Index zero selects the packaged osu!
classic default samples. Missing beatmap samples also fall back to these recordings,
including the normal layer on fruits without additional flags. Normal, soft, and drum
each include hitnormal, hitwhistle, hitfinish, hitclap, and slidertick. Banana uses its
separate Catch sample. Source revisions, checksums and asset licenses are in
[Default samples](../assets/audio/osu/README.md).

A synthesized emergency tone remains available for corrupt/oversized input or missing
application assets. Imported skin audio is not currently used.

Opening a project preloads the samples referenced by every difficulty, including notes far
beyond the current playhead. The editor supplies isolated document snapshots. macOS builds
the PCM bank on a background loader and waits for it before starting playback; Windows
preloads during project opening. Identical file paths share decoded memory. A project bank
has a 256 MiB limit and source/decoded samples have a 16 MiB limit. Samples beyond the bank
limit are skipped and logged rather than evicting earlier samples and decoding them again
during playback. Replacing a project releases its previous bank. Newly edited events are
prepared ahead of playback; Mac playback misses never synchronously load a file.

Windows uses its persistent WASAPI mixer with 32 voices. macOS uses one persistent
`AVAudioEngine` / `AVAudioSourceNode` mixer with 128 voices and a bounded 2,047-command queue.
Native WAV/MP3 decoding and OGG conversion happen during preparation. The real-time callback
only mixes immutable PCM into the output: no file I/O, allocations, locks, or Objective-C
calls. Under voice pressure the oldest attack is replaced; the newest command is dropped
if the queue is full. Pause/seek cancellation invalidates queued and active voices by
changing their generation. The engine is stopped before its sample memory is released.

Windows dispatches elapsed events from transport polls. The Mac host queues the next
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
