# Catch conversion references

The conversion implements sliders for the editor's time–X curves, including repeat spans,
imported v14 L/B/P/C slider paths with repeats, and banana showers. It uses the
following osu!lazer sources at commit
`48c4800e3ae4ee752452cdff83bd3787ccf3105f` in [ppy/osu](https://github.com/ppy/osu/tree/48c4800e3ae4ee752452cdff83bd3787ccf3105f).
Adapted portions retain the upstream MIT licence in `LICENCE.osu.txt`.

| Source file | Applied behaviour |
| --- | --- |
| `osu.Game/Rulesets/Objects/SliderEventGenerator.cs` | Head/tick/legacy-last-tick/tail ordering, 10 ms tick exclusion and 36 ms legacy tail leniency |
| `osu.Game.Rulesets.Catch/Objects/JuiceStream.cs` | Scoring distance, independent SliderTickRate, integer event-time differences and recursive halving for TinyDroplet spacing |
| `osu.Game/Rulesets/Objects/Legacy/LegacyRulesetExtensions.cs` | Float quantisation of inherited beat length and SV limits |
| `osu.Game/Utils/LegacyRandom.cs` | Seeded xorshift sequence and truncation of ranged draws |
| `osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmapProcessor.cs` | Seed 1337, complete-parent RNG traversal, droplet rotation draws, TinyDroplet X offsets and clamping |
| `osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmap.cs` | Stable time ordering after flattening parent objects |
| `osu.Game.Rulesets.Catch/Objects/JuiceStreamPath.cs` | Time–X velocity bound and Pythagorean construction of a linear slider path within geometric Y bounds |
| `osu.Game/Rulesets/Objects/SliderPath.cs` | Arc-length lookup of the resulting linear path |
| `osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs` | v14 duplicate-point segmentation, collinear perfect curves, float-to-integer coordinates |
| `osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs` | Red/green timing precedence, inherited SV and NaN tick metadata |
| `osu.Game.Rulesets.Catch/Objects/BananaShower.cs` | Integer shower endpoints, float halving/accumulation and inclusive banana creation |
| `osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmapConverter.cs` | Imported path/repeat conversion and Catch-specific tick rules |

Bezier, perfect-circle and Catmull approximation also adapt
`osu.Framework/Utils/PathApproximator.cs` and
`osu.Framework/Utils/CircularArcProperties.cs` from
[ppy/osu-framework commit e01524d1492885d8b00ac88b38e7963d76d7d454](https://github.com/ppy/osu-framework/tree/e01524d1492885d8b00ac88b38e7963d76d7d454),
the `2026.807.0` dependency tag used by this pinned lazer commit. The framework
MIT notice is retained separately in `LICENCE.osu-framework.txt`.

`CatchStreamConverter` retains the source Bezier handles. Its derived path
is constrained only by actual event positions, without intermediate curve samples. It
queries the resulting path for each event rather than assigning the target X
directly to generated objects. Internal fruit/tick alignment must remain within
0.0001 playfield units; final object X values use float precision.

Tiny compensation uses the actual seeded offset and the path-progress time of
each event, including legacy timing discrepancies. The desired pre-offset X is
limited to 0..512. Single-span FSliders that require compensation fail when the target is unreachable. Repeated FSliders treat this saved preference as best effort: conflicting random offsets fall back to the normal uncompensated stream while preserving fruit/droplet targets.
Legacy compatibility settings can instead retain uncompensated positions. The result reports both whether compensation was applied
and whether every tiny target met the internal tolerance.

Each authoring track stores one traversal and an explicit span count. Individual
segments can be linear or Bezier; a nullable outgoing kind inherits the track's
default. The generated polyline uses double coordinates. Geometric
Y folds at playfield boundaries do not add slider repeats or Catch ticks.
Imported paths use float vector approximation and declared-length
shortening/extension, preserving the no-extension rule for equal final points.
Repeat spans retain the original path while reversing progress; repeat fruit
and legacy final-tick timing come from the event generator.

Legacy Sliders retain their original representation until conversion is requested.
`ImportedSliderEditing` then projects their approximated path's cumulative distance
to first-span time–X, with 0.001-unit simplification tolerance and field-boundary
knots. It retains the parent ID, source order, original line and span count, so a
turnaround remains one repeat event rather than adjacent tail/head objects. It
compares generated event kinds, identities and times before replacing the source;
this is not lossless recovery of an author's
control handles. Zero-length path duplicates have no elapsed time and collapse in
time–X; repeat events remain in the span count. Converted FSliders require tiny
alignment. Shared repeat progress with conflicting tiny targets leaves the Legacy
Slider unchanged.

Timing queries preserve red-point BPM/offset/meter and green-point SV, clamped
to the stable Catch range 0.1–10. Green
points do not restart the beat grid; a slider locks its beat length and SV at
its start. At equal times green difficulty values override red ones, with the
first red point and last green point selected according to the pinned decoder.
The inherited NaN tick flag is retained in the model and timing query; the
referenced Catch converter does not forward that osu!-specific GenerateTicks
flag into JuiceStream, so Catch events still follow its independent TickDistance.

The file reader/writer performs v14 import/export separately. Native source
paths, curve-generated double paths and file-quantised paths are distinct
representations; round-trip validation must compare the post-encoding result.

RNG input contains every represented fruit, FSlider, Legacy Slider and
banana shower, ordered by source start time and retained source order. Each
banana consumes its position draw and three additional legacy appearance draws.
Objects created in the editor use their deterministic collection order when no
import order exists. The viewport does not participate in conversion. When an
object cannot be generated, the result is
marked incomplete and RNG only describes the successfully generated subset.

Limits are explicit: generated SV at most 10, generated path length at most 100000 units, at most 50000
nested objects, 30000 authoring samples, 65536 generated path points, 10000
imported control points, 9000 spans and 200000 imported path samples. Imported
editing is bounded to 30000 anchors and 20 million simplification checks. Grid
enumeration is bounded to 10000 lines. Inputs
outside these bounds fail with diagnostics rather than silently trimming data.
