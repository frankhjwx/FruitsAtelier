# Preview hitsounds

The editor plays hitsounds as the music clock crosses converted Catch objects. Windows
uses a separate WASAPI mixer; macOS reuses prepared AVAudioPlayer voices and schedules
them on the music player's audio-device clock. Music playback remains
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

Both backends retain a bounded sample cache and allow up to 32 simultaneous voices.
At the Mac voice limit, a new attack may reuse the oldest tail of the same sample.
Source files and managed decoded buffers are limited to 16 MiB per sample; each cache
is limited to 64 MiB. The Mac native voice pool also has a 64 MiB backing-data budget.
macOS passes WAV/MP3 data to AVAudioPlayer and decodes OGG into
PCM WAV. Samples in the upcoming second are warmed before starting playback and
periodically during playback. On Mac this preparation also creates native players; completed
and paused voices retain their resources for reuse. Uncached samples decode on demand.

Windows dispatches elapsed events from transport polls. The Mac host queues the next
100 ms of events with their original beatmap timestamps. `MacAudio` maps those timestamps
to the shared `deviceCurrentTime` clock and both music and hitsounds use `playAtTime`.
Music starts/resumes with a 150 ms scheduling lead, giving the initial sounds time to be
submitted before the common start deadline. This lead delays transport startup, not the
hitsounds relative to the music. Seek establishes a new device-clock origin. Pause,
content changes, and clock jumps larger than 250 ms cancel queued voices before rescheduling.

Keep player allocation and preparation ahead of hit deadlines. Caching only encoded bytes
still leaves native initialization and output startup on every hit; emitting elapsed events
from a UI timer also introduces frame-dependent lateness. Timing regression tests compare
music and hitsound playback clocks while the managed thread waits, check native voice reuse,
and verify cancellation of future sounds. Device-clock tests do not measure acoustic output
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
behavior, and runs in Windows CI. `HitsoundLatencyTests` exercises Mac scheduling and reuse. Run the affected suites and both platform
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
