# stable .osu File Contract

The project implements its own `.osu` reader/writer for the beatmap format used by osu!stable. Output uses `osu file format v14` and Catch `Mode: 2`. Fields follow the [official osu! format documentation](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29).

## Input and output

- Accept only v14 / Mode=2; the reader rejects other format versions and unsupported object types.
- Preserve General, Editor, Metadata, Difficulty, Events, TimingPoints, Colours, HitObjects, and audio/sample references.
- Preserve raw section text and unedited object lines; unsupported object types are errors.
- Save authored anchors, Bezier handles, and editing constraints in the editor project, rather than custom `.osu` object fields.

## Object and timing rules

| Item | Representation |
| --- | --- |
| Standalone fruit | Hit circle preserving X, time, type flags, and samples |
| Slider / fruit stream | Slider with curve type/points, span count, path length, and edge samples; the stream is derived |
| Banana shower | Spinner start/end times; individual banana X positions are not serialized |
| Time | Object times use integer milliseconds; projects retain doubles and report export rounding error |
| Coordinates | Object and path coordinates are written as integers; generation accounts for quantization error |
| Timing | Time and beatLength permit format-supported decimals; preserve uninherited/inherited semantics and same-time ordering |
| Slider count | File `slides` maps to model `SpanCount`, the number of traversals |
| Beat subdivision | Editor BeatDivisor and gameplay SliderTickRate are independent |

These rules follow the [official format document](https://github.com/ppy/osu-wiki/blob/master/wiki/Client/File_formats/osu_(file_format)/en.md).

## Writer behavior

The generator first produces a two-dimensional slider that satisfies its targets; the writer then serializes it. Serialization uses invariant culture with consistent newline and UTF-8 policies. Integer object times and coordinates round midpoints away from zero. Original unedited integer values remain unchanged.

Output defaults to a new file. It validates all objects before writing a temporary file and safely replacing the destination. Failure preserves the original file. Hosts copy associated resources and manage relative paths for exports across directories; missing resources and same-name content conflicts are errors.

Unedited sliders are not resampled. SV changes are checked against parameters affecting simultaneous and later objects; required restoration points are written and verified. `.osu` export and project saving maintain separate success states and dirty-state handling.

When an edited slider requires a different SV, the writer replaces the old green point at its start time to avoid stacking different velocities at the same time, preserving red points and effective sample fields. Catch interprets inherited SV within stable's 0.1–10 range; FSliders requiring SV above 10 fail generation and export. A restoration point is written only if an unedited slider before the next original timing point or edited slider still depends on the old velocity. Otherwise it is omitted. Restoration uses a safe integer time after the head; incompatible velocities for nearby objects cause an explicit failure. Duration regressions calculate `length × spans × beatLength / (100 × SliderMultiplier × SV)` directly from exported fields.

See [Building and Testing](TESTING.md) for tests and the [format module reference](../src/FruitsAtelier.Core/Formats/REFERENCE.md) for APIs and sources.
