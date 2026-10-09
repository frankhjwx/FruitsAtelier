# stable .osu File Contract

The project implements its own `.osu` reader/writer for the beatmap format used by osu!stable. Output uses `osu file format v14` and Catch `Mode: 2`. Fields follow the [official osu! format documentation](https://osu.ppy.sh/wiki/en/Client/File_formats/osu_%28file_format%29).

## Input and output

- Accept v12, v13, v14 and stable-compatible lazer v128 / Mode=2; export v14. The reader rejects other format versions and unsupported object types.
- Preserve General, Editor, Metadata, Difficulty, Events, TimingPoints, Colours, HitObjects, and audio/sample references.
- Import/export `[Editor] DistanceSpacing` as the per-difficulty spacing multiplier.
- Synchronization accepts osu!'s `Editor/TimelineZoom` without a conflict; it is
  an osu! editor preference and does not control FA's viewport.
- New projects start with `[Difficulty] SliderMultiplier` 1.92 and editor DPB 192 px. Imported maps initially derive DPB as 100 × their stored SliderMultiplier. Subsequent DPB edits belong to the `.catchproj` editor configuration; `.osu` export retains SliderMultiplier and slider playback unchanged.
- A confirmed Timing-panel SV override replaces exported SliderMultiplier and compensates inherited SV at red resets and green points. Export keeps the generated object lines and validates NM/HR playback before returning text. The authoring multiplier and DPB remain unchanged; see [Timing editing](EDITOR_UI.md#timing-editing).
- Base-SV compensation starts at existing timing points. If a slider starts before all timing points, export writes its implicit BPM followed by the compensated green at that slider's head; it does not add an unused green at 0 ms.
- Read and edit `[Editor] Bookmarks` and `[Events]` break periods as difficulty-local timeline content. Unrelated event lines retain their source text and order.
- Project files preserve raw source sections. `.osu` exports normalize section spacing and setting layout while retaining comments, unknown content, and unedited object lines; unsupported object types are errors.
- Synchronization compares osu! save representations: truncated object start/end
  milliseconds, 15-significant-digit slider lengths and timing values, and implicit
  first-object/post-spinner combo boundaries. Events comparisons ignore comments,
  blank lines and break placement within the section, while retaining break
  intervals and storyboard command order/indentation. Break starts at an object's
  200 ms recovery boundary use truncated integer milliseconds. Synchronization
  also recognizes the former FA ceil-rounded start at that same fractional object
  boundary; other break-time edits remain reviewable. Comparison does not rewrite
  authoring values or retained section text; actual edits remain reviewable.
- Timing synchronization compares changes against the captured emitted timing
  baseline and reviews only changed timestamp groups. Unchanged generated SV
  points remain derived. Applying external timing transfers edited fields and
  added/deleted groups to the saved authoring timing, preserving unrelated
  authoring values and same-time ordering. Retained local timing choices remain
  reviewable after saving and restarting.
- Uniform timing shifts are summarized as an offset with the affected point count,
  with unmatched changes shown below. FA/osu! confirmation remains required.
  Applying a pure shift moves authored timing; generated SV stays derived.
- Accepting external slider sound, sample or combo edits retains the exact FSlider
  anchors and handles when its single exported slider has unambiguous matching
  geometry, start time, repeats and playback velocity. Changed paths or ambiguous
  matches are imported as external sliders. This also applies to the entire osu!
  version choice.
- Save authored anchors, Bezier handles, and editing constraints in the editor project, rather than custom `.osu` object fields.

## Object and timing rules

Lazer v128 imports use the same supported object and timing fields. They require integer-valued coordinates after float parsing and ordinary L/B/C/P slider paths. Fractional coordinates, explicit mixed path segments and B-spline degree syntax are rejected; these need a stable-compatible export before import. This prevents interpreting lazer's fractional coordinates with stable's integer truncation. Project saves preserve accepted raw fields, and export still writes v14. See the pinned [ConvertHitObjectParser](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Rulesets/Objects/Legacy/ConvertHitObjectParser.cs) for the coordinate and path syntax boundaries.

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

The generator first produces a two-dimensional slider that satisfies its targets; the writer then serializes it. Serialization uses invariant culture with consistent newline and UTF-8 policies. Integer object times truncate toward zero, matching stable snapping as modeled by MapsetVerifier. Coordinates round midpoints away from zero. Generated SV points use the same truncated time as their slider heads. Beat grids calculate offsets from whole subdivision counts before dividing, retaining exact whole-millisecond grid points. Original unedited integer values remain unchanged.

Read-back maps events by source and nested event identity when quantization changes their order. An integer slider head can precede an unchanged fractional fruit at the same authored time. Playable NM/HR output retains the actual exported order; this ordering change alone does not invalidate a base-SV override. Compensation failures report the affected object time and count or time/position changes where available, with guidance for changing SV or reporting an editing failure.

Metadata exports Title, TitleUnicode, Artist, ArtistUnicode, Creator, Version,
Source, Tags, BeatmapID, and BeatmapSetID in that order, followed by any additional
fields or comments. Metadata fields are contiguous. The header and named sections
are separated by exactly one blank line. Settings use stable's `key: value` style
in General and Editor, `key:value` in Metadata and Difficulty, and `key : value`
in Colours. Other section bodies retain their lines and internal spacing. Timing
data starts directly below its section header, with any retained comments before
the data.

Standard sections emit in General, Editor, Metadata, Difficulty, Events,
TimingPoints, Colours, and HitObjects order. General, Editor, and Difficulty
settings follow the official field order; combo colours sort by their numeric
index before slider colours. Duplicate settings retain their source precedence,
and unknown fields and comments follow known settings. Unknown sections retain
their slots and relative source order. Timing points always sort chronologically,
including exports without generated sliders; same-time points retain source order.

Output defaults to a new file. It validates all objects before writing a temporary file and safely replacing the destination. Failure preserves the original file. Hosts copy available associated resources and manage relative paths for exports across directories; missing song audio and same-name content conflicts are errors. Optional video, storyboard, background, and custom sample files may be absent; their original references are preserved.

Unedited sliders are not resampled. SV changes are checked against parameters affecting simultaneous and later objects; required restoration points are written and verified. `.osu` export and project saving maintain separate success states and dirty-state handling.

When an edited slider requires a different SV, the writer replaces the old green point at its start time to avoid stacking different velocities at the same time, preserving red points and effective sample fields. Catch interprets inherited SV within stable's 0.1–10 range; FSliders requiring SV above 10 fail generation and export. A restoration point is written if an unedited slider before the next independent timing boundary still depends on the old velocity, or if a nearby original SV change must be deferred. Otherwise it is omitted.

The writer holds the required SV through the head's next-millisecond lookup window, including fractional timing points that can truncate into that window. Original sample, volume, and effect events retain their timestamps; only conflicting SV values are replaced temporarily, then restored at a safe integer time using the state after the deferred boundary. Overlapping equal-SV head windows share a safe restoration time. Original timing points at that restoration time remain authoritative. Export verifies unchanged sliders retain their exact and next-millisecond timing states, including truncated timing offsets. Conflicting slider heads or BPM changes that cannot be represented safely still cause a localized failure identifying both times. Duration regressions calculate `length × spans × beatLength / (100 × SliderMultiplier × SV)` directly from exported fields.

See [Building and Testing](TESTING.md) for tests and the [format module reference](../src/FruitsAtelier.Core/Formats/REFERENCE.md) for APIs and sources.

FSlider generation evaluates the authoring curve at actual fruit, droplet, and tiny-droplet events. Only these event targets constrain path construction and required SV; intermediate anchors and Bezier curvature do not impose additional speed constraints. Tiny alignment retains its existing random-offset compensation and repeated-path compatibility rules. Connections between event targets may differ from the authoring curve without changing event positions or duration.

FSlider tiny intervals use the exported integer head time when counting nested
events. Their times are then mapped back to the precise authored origin for curve
sampling and editing. This keeps tiny counts and NM/HR random consumption consistent
with export at integer interval thresholds while retaining saved anchor times and
span durations. Imported Legacy Sliders continue to use their retained source times.
