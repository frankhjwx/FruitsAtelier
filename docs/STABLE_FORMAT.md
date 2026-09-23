# stable .osu File Contract

The project implements its own `.osu` reader/writer for the beatmap format used by osu!stable. Output uses `osu file format v14` and Catch `Mode: 2`. Fields follow the [official osu! format documentation](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29).

## Input and output

- Accept v12, v13 and v14 / Mode=2; export v14. The reader rejects other format versions and unsupported object types.
- Preserve General, Editor, Metadata, Difficulty, Events, TimingPoints, Colours, HitObjects, and audio/sample references.
- Import/export `[Editor] DistanceSpacing` as the per-difficulty spacing multiplier.
- Display `[Difficulty] SliderMultiplier` as DPB = 100 × SliderMultiplier in the editor; export converts it back to the standard field.
- Read and edit `[Editor] Bookmarks` and `[Events]` break periods as difficulty-local timeline content. Unrelated event lines retain their source text and order.
- Preserve raw section text and unedited object lines; unsupported object types are errors.
- Save authored anchors, Bezier handles, and editing constraints in the editor project, rather than custom `.osu` object fields.

## Object and timing rules

Versions 12–14 share the Catch timing, slider tick and legacy coordinate rules used here. Import preserves raw sections and object/sample fields; saving an editor project retains that content, and `.osu` export emits a v14 header. Older timing-offset and tick-generation rules (before v5 and v8 respectively) are outside the supported input range. These boundaries were checked against the pinned [LegacyBeatmapDecoder](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs) and [CatchBeatmapConverter](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmapConverter.cs).

| Item | Representation |
| --- | --- |
| Standalone fruit | Hit circle preserving X, time, type flags, and samples |
| Ordinary slider | Slider with curve type/points, span count, path length, and edge samples; the stream is derived |
| Slider-managed fruit stream | Hit circles sampled at the saved StreamSnapDivisor; no slider or SV override |
| Banana shower | Spinner start/end times; individual banana X positions are not serialized |
| Time | Object times use integer milliseconds; projects retain doubles and report export rounding error |
| Coordinates | Object and path coordinates are written as integers; generation accounts for quantization error |
| Timing | Time and beatLength permit format-supported decimals; preserve uninherited/inherited semantics and same-time ordering |
| Slider count | File `slides` maps to model `SpanCount`, the number of traversals |
| Beat subdivision | Editor BeatDivisor and gameplay SliderTickRate are independent |

These rules follow the [official format document](https://github.com/ppy/osu-wiki/blob/master/wiki/Client/File_formats/osu_(file_format)/en.md).

## Writer behavior

The generator first produces a two-dimensional slider that satisfies its targets; the writer then serializes it. Serialization uses invariant culture with consistent newline and UTF-8 policies. Integer object times and coordinates round midpoints away from zero. Original unedited integer values remain unchanged.

Output defaults to a new file. It validates all objects before writing a temporary file and safely replacing the destination. Failure preserves the original file. Hosts copy available associated resources and manage relative paths for exports across directories; missing song audio and same-name content conflicts are errors. Optional video, storyboard, background, and custom sample files may be absent; their original references are preserved.

Unedited sliders are not resampled. SV changes are checked against parameters affecting simultaneous and later objects; required restoration points are written and verified. `.osu` export and project saving maintain separate success states and dirty-state handling.

When an edited slider requires a different SV, the writer replaces the old green point at its start time to avoid stacking different velocities at the same time, preserving red points and effective sample fields. Catch interprets inherited SV within stable's 0.1–10 range; FSliders requiring SV above 10 fail generation and export. A restoration point is written if an unedited slider before the next independent timing boundary still depends on the old velocity, or if a nearby original SV change must be deferred. Otherwise it is omitted.

The writer holds the required SV through the head's next-millisecond lookup window, including fractional timing points that can truncate into that window. Original sample, volume, and effect events retain their timestamps; only conflicting SV values are replaced temporarily, then restored at a safe integer time using the state after the deferred boundary. Overlapping equal-SV head windows share a safe restoration time. Original timing points at that restoration time remain authoritative. Export verifies unchanged sliders retain their exact and next-millisecond timing states, including truncated timing offsets. Conflicting slider heads or BPM changes that cannot be represented safely still cause a localized failure identifying both times. Duration regressions calculate `length × spans × beatLength / (100 × SliderMultiplier × SV)` directly from exported fields.

See [Building and Testing](TESTING.md) for tests and the [format module reference](../src/FruitsAtelier.Core/Formats/REFERENCE.md) for APIs and sources.

FSlider generation evaluates the authoring curve at actual fruit, droplet, and tiny-droplet events. Only these event targets constrain path construction and required SV; intermediate anchors and Bezier curvature do not impose additional speed constraints. Tiny alignment retains its existing random-offset compensation and repeated-path compatibility rules. Connections between event targets may differ from the authoring curve without changing event positions or duration.
