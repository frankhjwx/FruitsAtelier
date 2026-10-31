# AiMod

AiMod inspects the current difficulty's exported Catch representation. Generated
objects are mapped back to their editable parents for navigation. A report is a
snapshot: Refresh updates it, and checking or navigating never creates an edit.
Clicking a timestamp or pressing Enter on a selected finding seeks to its reported
time and selects its objects that still exist. Findings retained after object deletion
remain navigable until Refresh updates the report.

## Checks

| Category | Checks |
| --- | --- |
| Compose | Less than 10 ms between the previous object's end and the next object's start, using integer milliseconds and including slider repeats; unsnapped starts and ends; uncommon snap divisors. |
| Design | Missing background and referenced resources; background/video dimensions, skin image dimensions and short storyboard lifetimes. |
| Timing | Map/audio length, quiet sections, duplicate points, inherited points before timing, first-point Kiai, Kiai snapping and intervals, missing preview point. |
| Meta | Drain length, stack leniency and non-ASCII romanised artist/title. |
| Mapset | Shared metadata, audio settings and uninherited timing differences between loaded difficulties. |

Resource existence and PNG/JPEG/GIF header checks run on a document snapshot in a
worker. Shared `.osb` files, file-local variables, animation frames, loops and compound
commands participate in resource and storyboard checks. Video dimensions and media
readability use the packaged FFmpeg metadata probe, including FLV, without starting
playback or depending on the video/storyboard display preference. The probe reads
only the referenced local file and is cancelled when the report is refreshed or
retired. Audio metadata checks use TagLibSharp 2.3.0. Audio length uses the
loaded playback duration shared with the timeline, rather than a metadata bitrate
estimate. The editor refreshes the report when that duration becomes available;
audio-length checks wait while playback audio is unavailable. Unreadable videos and
unreadable or unsupported audio produce diagnostics. The editor consumes
only completed results; rendering performs no file I/O.
Mapset checks use loaded difficulties, not an online submission service.

## Stable compatibility

Catch does not apply osu!standard's geometry/distance checks. The distance-snap
toggle therefore does not add Catch findings. Snapping uses integer millisecond
grid positions, common divisors 6 and 8, then uncommon divisors 5, 7, 9, 12 and 16.
Starts tolerate 1 ms and ends tolerate 2 ms on the common grids. Kiai transitions
require an exact integer-grid match. Stable's short-Kiai message measures the gap
from the previous Kiai end, rather than the duration of the new interval.

The screenshot fixture stores observed Twin Bloom messages and timestamps without
redistributing the beatmap or its media. The opt-in regression requires the user's
matching `.osu` file and resources; it checks totals, the complete Timing sequence
and the Compose rows visible in the supplied screenshots. Unseen Compose rows
have not been individually compared against stable.

This is a Catch report, not a validator for the other three game modes or all
ranking criteria. Video warnings require both dimensions to exceed stable's thresholds, with
1280×720 tested before 1024×768. Skin checks cover hitcircle, hitcircleoverlay,
approachcircle and reversearrow PNGs (128×128, or 256×256 for `@2x`). More than
five storyboard objects with lifetimes under 100 ms suggest an epilepsy warning;
sample events are excluded. The wiki's inactive bitrate checks are not emitted.

## References

- [AiMod wiki](https://osu.ppy.sh/wiki/en/Client/Beatmap_editor/AiMod).
- [osu! editor checks at bdfabf93c77483bb471dfde8c900fa9176a9d531](https://github.com/ppy/osu/tree/bdfabf93c77483bb471dfde8c900fa9176a9d531/osu.Game/Rulesets/Edit/Checks).

The checks are independently implemented. The public implementation provides
comparison context; stable screenshots define the compatibility regression.
