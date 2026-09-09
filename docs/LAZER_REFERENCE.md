# osu!lazer Implementation References

The reference revision is [ppy/osu commit 48c4800e3ae4ee752452cdff83bd3787ccf3105f](https://github.com/ppy/osu/commit/48c4800e3ae4ee752452cdff83bd3787ccf3105f).

This index identifies source entry points consulted for editing and clock design. Adapted algorithms and licenses are recorded in [Third-party notices](../THIRD_PARTY_NOTICES.md).

## File responsibilities

This project implements its own stable `.osu` reader/writer; see [stable File Contract](STABLE_FORMAT.md). This reference covers editing, curves, Catch conversion, and audio. Editor projects additionally preserve anchors and Bezier handles.

## Beat snapping and fruit placement

| Source | Responsibility | Use in this project |
| --- | --- | --- |
| [BindableBeatDivisor](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Screens/Edit/BindableBeatDivisor.cs) | Defines divisor presets including 4 and 6. | The slider offers 1/4, 1/5, 1/6, 1/7, 1/8, 1/9, 1/12, and 1/16, ending at 1/16. |
| [BeatDivisorPresetCollection](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Screens/Edit/Compose/Components/BeatDivisorPresetCollection.cs) | Regular presets include 1, 2, 4, 8, 16; triplet presets include 1, 3, 6, 12. | Reference for grid grouping rather than a mandatory UI copy. |
| [EditorClock.SeekSnapped](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Screens/Edit/EditorClock.cs) | Snaps using current timing offset and beat length/divisor, accounting for the next timing boundary. | Reference for timing boundaries; an independent snapping service serves grid and object editing. |
| [FruitPlacementBlueprint](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Edit/Blueprints/FruitPlacementBlueprint.cs) | Uses composer snapping results and handles lateral position on placement. | Reference for fruit placement, implemented with this project's coordinates and commands. |

`SeekSnapped()` provides clock snapping; this project implements object snapping and grid behavior in the `Timing` module.

## Bezier curves and Catch streams

| Source | Responsibility | Use in this project |
| --- | --- | --- |
| [PathControlPointVisualiser](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Osu/Edit/Blueprints/Sliders/Components/PathControlPointVisualiser.cs) | Provides osu! ruleset control-point selection, dragging, deletion, and BEZIER path types. | Reference for curve-editing transactions; not directly usable as a time–X handle UI. |
| [SliderPath](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Rulesets/Objects/SliderPath.cs) | Stores controls, calculated paths, cumulative length, and position by progress. | Reference for generated two-dimensional slider sampling; u, time, and arc length remain distinct. |
| [JuiceStreamPath](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/JuiceStreamPath.cs) | Time–X polyline with `ComputeRequiredVelocity()`, `ConvertToSliderPath()`, and `ConvertFromSliderPath()`. | Reference for inverse geometry and vertical folding; Bezier curves are evaluated at gameplay event times. |
| [Catch EditablePath](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Edit/Blueprints/Components/EditablePath.cs) | Adjusts SV, generates sliders, and snaps endpoints after path changes. | Reference for update order; clamped SV may violate the target path, so this project reports failure and error. |
| [JuiceStream](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/JuiceStream.cs) | Generates Fruit, Droplet, and TinyDroplet from slider events with timing/SV. | Reference for actual stream conversion; uniformly sampled curve points do not replace generated objects. |

`JuiceStreamPath` uses polylines; this project additionally preserves Bezier handles.

## Banana showers

| Source | Responsibility | Use in this project |
| --- | --- | --- |
| [BananaShower](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/BananaShower.cs) | The parent stores start time and duration; nested hit objects derive individual bananas. | The Banana tool sets start with left-click and end with right-click. Properties edit the time range; complete beatmap RNG still generates individual X positions. |

## Music, editor clocks, and timeline dragging

| Source | Responsibility | Use in this project |
| --- | --- | --- |
| [PlaybackControl](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Screens/Edit/Components/PlaybackControl.cs) | Playback controls call editor-clock Start/Stop. | The playback button and Space use the same transport. |
| [EditorClock](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Screens/Edit/EditorClock.cs) | Associates a Track and supplies Start/Stop/Seek, length, state, and end handling. | Separate audio backend and editor clock with a unified time source. |
| [MarkerPart](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Screens/Edit/Components/Timelines/Summary/Parts/MarkerPart.cs) | Maps timeline click/drag/release to Seek and throttles dragging during playback. | Update the pointer immediately, coalesce audio requests, and commit the final target on release. |
| [FramedBeatmapClock](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Beatmaps/FramedBeatmapClock.cs) | Maps track time, smoothed frame time, offsets, and seeks. | Reference for clock responsibilities without copying platform/backend offset constants. |

These classes depend on osu!framework. See [Architecture](ARCHITECTURE.md) for this project's audio implementation.

## Dependencies and licenses

At this revision, [osu.Game.csproj](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/osu.Game.csproj) targets net8.0 but also depends on Realm, osu!framework, resources, and other components. This project does not reference that project.

See the repository [LICENCE](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/LICENCE). Actual reuse must retain required copyright and license notices and record source files and commits. Check native audio dependencies, resources, and other repositories independently; the main repository license does not establish the licenses of all dependencies.

Adapted Catch conversion, hyperdash detection, and skin sizing rules and their source files are recorded in [Third-party notices](../THIRD_PARTY_NOTICES.md).
