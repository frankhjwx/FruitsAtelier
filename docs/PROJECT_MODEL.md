# Project and Data Model

Default saves use [workspace project directories](WORKSPACE.md): a `project.catchdiff` manifest and separate difficulty files. The `.catchproj` schema 1/2 descriptions below cover the retained compatibility format and document encoding.

The authoring model persists as UTF-8 JSON. Documents containing exact control curves use schema 3 (single difficulty) or schema 4 (multi-difficulty `.catchproj`); ordinary pen-only documents continue to use schema 1/2. Documents containing slider fruit streams use schema 5 (single difficulty) or 6 (multi-difficulty). All six schemas are readable. Older applications reject the newer schemas rather than silently discarding curve geometry. The project implements stable v12–v14 / Mode=2 `.osu` parsing and v14 writing. Authored content, imported context, and derived output remain separate.

## Authoritative and derived data

| Category | Contents | Rule |
| --- | --- | --- |
| Authored data | Standalone fruits, tracks, anchors, segment types, handles, span counts, stream snap, timing/difficulty | Authoritative content for editing and undo |
| Imported context | Raw sections and object lines, source order, complete timing, sliders/bananas, resource references | Saved with the project; unedited objects retain their original representation on export |
| Derived results | Slider geometry, SV, F/D/T/bananas, RNG, errors, hyperdash | Recomputable; never overwrite authoring intent |
| Session state | Object/anchor selection, tool, language, snapping, viewport, transport time, skin, layers, Tiny compensation toggle, internal batch clipboard | Not beatmap content |

## Projects and difficulties

`BeatmapProject` stores a project Name and 1–256 `ProjectDifficulty` entries. Each difficulty contains its own Guid, Version display name, and complete `MapDocument`. Schema 2 uses a `Project` outer container and atomically saves all difficulties together. Schema 1's `Document` is automatically wrapped as a single-difficulty project. Each difficulty's resource paths are written and resolved relative to the project directory, retaining the older path safety checks. Project JSON is limited to 32 MiB.

The editor maintains an independent `EditorHistory` per difficulty and accesses `Document` through the current difficulty. Undo affects only that difficulty; saving updates every history baseline. Adding a difficulty changes project structure rather than a difficulty's object undo stack and keeps the project dirty until saved. The active difficulty, tab scroll position, playhead, and viewport are session state and are not persisted. Opening selects the first difficulty; switching does not create content history. The project container does not force imported `.osu` difficulties to share audio or timing, avoiding overwriting source content.

Song Setup stores metadata, HP/OD, combo colors and Design options in the existing
raw sections; AR/CS remain the authoritative model properties. No project schema
change is needed. Shared metadata edits propagate only the changed keys to the
other difficulty histories. Their saved baselines remain intact for dirty checks,
and their local undo snapshots retain the shared values. The initiating undo
transaction restores each difficulty's own previous shared values.

## Concrete model

Raw section lines retain independent mutable storage in document snapshots. Unchanged clones share a non-persisted equality identity, so repeated dirty, conversion and difficulty checks do not scan entire storyboards. Every line mutation invalidates that identity; different identities fall back to content comparison. Project JSON still stores section lines as string arrays.

| Type | Fields and semantics |
| --- | --- |
| MapDocument | Name, DurationMs, difficulty, TimingPoints, Fruits, Tracks, ImportedSliders, BananaShowers, SourcePath, AudioPath, OriginalSections |
| Fruit | Stable Guid Id, TimeMs, X, SourceOrder, OriginalLine (including the authored New combo flag) |
| CurveTrack | Stable Guid Id, Name, Kind (default Linear / Bezier), Nodes, SourceOrder, SpanCount, OriginalLine, CompensateTinyDroplets |
| Anchor | Stable Guid Id, TimeMs, X, HandleIn, HandleOut, nullable OutgoingKind and OutgoingCurve |
| ControlCurve | Exact Bezier or CircularArc kind, fixed ReferenceScale, interior CurveControl list |
| CurveControl | Stable Guid Id and MapPoint Offset relative to the segment start anchor |
| MapPoint | Double TimeMs and X; handles store offsets relative to anchors |
| TimingPoint | TimeMs, BeatLengthMs, Meter, Uninherited, sample/volume/effect fields, SourceOrder, OriginalLine |
| ImportedSlider | Id, TimeMs, X / Y, PathType, ControlPoints, SpanCount, PixelLength, SourceOrder, OriginalLine |
| BananaShower | Id, TimeMs, EndTimeMs, SourceOrder, OriginalLine |

Difficulty includes ApproachRate, CircleSize, SliderMultiplier, and SliderTickRate. `DistancePerBeatOverride` is a nullable editor-only distance-snap baseline in `.catchproj`; when absent, effective DPB is `100 × SliderMultiplier`. New maps therefore start at DPB 192 px with SliderMultiplier 1.92, while imported maps initially use their original multiplier. Changing DPB sets the override and rescales DS preset multipliers by old DPB / new DPB to preserve their pixel distances, without changing slider conversion or `.osu` export. Older projects without the override continue to derive DPB from their stored SliderMultiplier. DistanceSpacing is a per-difficulty editor multiplier (0.1–6, default 1), included in cloning, dirty comparison, undo and project persistence. `DistanceSnapRatios` stores up to eight finite non-negative multipliers for the difficulty. It defaults to an empty list in older projects and participates in cloning, dirty comparison, undo and project persistence. Zero DS is always implicit; an explicitly added zero-valued preset is also stored so it remains available for dragging. An empty preset list snaps only to zero and does not use the legacy DistanceSpacing value. Neither DPB overrides nor custom DS lists are exported to `.osu`; see [Editing Controls](EDITOR_UI.md#distance-spacing-and-object-flags). Demo defaults are duration 30000 ms, beat length 500 ms, offset=0, AR=8, CS=5, SliderMultiplier=1.92 (DPB 192), and SliderTickRate=1. MapDocument's BeatLengthMs / TimingOffsetMs are fallbacks when no red timing point exists; real beatmaps retain all timing points.

Import initially derives `.osu` `DurationMs` from the last object. Once associated audio is decoded, a longer audio duration becomes available for placement, numeric editing, and dragging. Loading and navigation do not modify the document. The first object edit beyond the original range extends `DurationMs` within the same user transaction.

Authoritative time is double-precision milliseconds; beat snapping does not round early. TimingMap queries the active red point's BPM / offset / Meter and inherited SV. Grid and snapping use local beat spacing and red-point boundaries; green points do not reset phase. Each slider locks its start timing, so timing changes along its path do not alter velocity. Changing snapping does not alter existing objects or SliderTickRate. Anchor dragging has an independent, default-off session checkbox and uses the selected beat divisor only when enabled.

Gameplay-derived timing uses the exported osu v14 read-back event stream. The writer rounds authored object times and coordinates where required by the file format, reads the output, and maps converted events back to their editor `(SourceId, EventIndex)`. Canvas objects, hyperdash, movement analysis, preview, TestPlay and ratings consume this playable stream. Authoring anchors, beat snapping and editable curve geometry retain their double-precision values. An incomplete draft uses the direct conversion until it can be exported.

EditorHistory uses deep copies for transactions, undo, and dirty comparisons, preserving object IDs, segment types, span counts, Tiny overrides, raw lines, and timing. Saving updates the baseline without clearing undo/redo. ContentEquals invalidates the view's conversion cache. A New Combo toggle retains the playable object stream and refreshes combo grouping and labels because the flag does not change catch event times or positions. Batch Legacy conversion uses an independent background snapshot and validates its source snapshot before applying results. Cancelling an active drag or draft restores the complete transaction; later field edits locate objects by ID to avoid writing into stale snapshots.

Object selection stores complete parent IDs only. Multiple children of the same slider are deduplicated by SourceId. Anchor selection in B mode is restricted to the active track; Fruit mode places objects immediately; Select mode selects complete objects and can enter control editing for a selected slider. Box selection separately snapshots the starting selection. Esc or lost capture restores it without creating content history.

The internal clipboard stores deep snapshots of selected complete parent objects. Paste applies `new time = playhead + original time − earliest selected start`, preserving relative times, X, geometry, relative handles, SpanCount, and sample fields, while assigning new parent/node IDs. It extends document duration as needed and rolls back the entire batch if any object is out of bounds. Copy creates no undo entry; each batch cut, delete, or paste is one transaction. Clipboard snapshots and unselected objects stay unchanged; the system clipboard is not used.

Interface language and resources are not serialized in `.catchproj`. Built-in default names come from resources when creating objects. Existing Name values, imported metadata, and raw lines remain user data; switching languages does not rename them. Language changes invalidate conversion diagnostic caches without changing geometry or document history.

## FSliders and Legacy Sliders

`MapDocument.DerandomizeDroplets` stores the per-difficulty Legacy conversion choice. A null value uses the General settings default until conversion. When disabled, conversion samples the original full-map Catch output once and builds a linear target path through every actual event position, including randomized TinyDroplets. Tracks use `CompensateTinyDroplets=true` to keep the generated objects on that path. Acceptance checks every event identity, kind, time, position, and target alignment. Conflicting folded repeat targets or unreachable geometry leave the source Legacy Slider intact.

Randomized conversion includes nested event positions as exact fitting boundaries and compares TinyDroplet X with the original full-map conversion. If the fitted curve changes a position, conversion uses a linear track through the original path and TinyDroplet positions; dense paths can use just the mandatory event positions.

Tracks lie in the `(timeMs, X)` plane, with segment type `Nodes[i].OutgoingKind ?? track.Kind`. Null inherits the track default; a track may mix linear and cubic Bezier segments. Endpoint times increase, and Bezier control-point times are nondecreasing. Time evaluation solves `time(u)` rather than substituting a linear time fraction for u. Anchors are at least 0.001 ms apart, and control-point X stays in 0..512.

Handles store relative offsets and move with their anchor. The Slider tool (B) has a session-only editing-mode choice. Pen tool mode adds handle-free points on click and direction handles by holding and dragging upward; osu legacy mode edits a control-polygon projection of the same track. Converting control points between curved and straight uses handles and adjacent segment types. Right-click insertion creates a point without handles; neighboring handles may still curve adjacent segments, so ordinary insertion need not preserve shape. The split action preserves the segment's shape and subsequent types according to its type, without replacing existing anchors/handles with sampled output. Geometric slider Y is not editing time.

An anchor's optional `OutgoingCurve` takes precedence over its outgoing pen handles and `OutgoingKind` for evaluation. It stores exact circular arcs or arbitrary-degree Beziers (up to 64 interior controls). Relative control offsets follow whole-object translation and copy/paste, which assigns fresh control identities. Endpoint times and Bezier control times are ordered; circular-arc validation checks coordinate extrema and time derivative signs, not just rendered samples. Collinear arcs evaluate as lines.

`SliderControlEditing.Vertices` is a non-persisted projection. Cubic pen handles have deterministic temporary vertex identities; projecting or switching modes does not mutate content. Exact segments, identities and AR references participate in clone, undo, equality and conversion-cache invalidation. Save files never store viewport scale or editing mode.

For arcs, `ReferenceScale = 440 / CatchScrollTiming.PreemptMs(creationAR)`. It converts relative milliseconds to playfield units for circle construction, independently of window width and DPI. Subsequent AR edits only affect display. Bezier evaluation remains in time–X coordinates.

`ControlCurveEditing` splits exact segments without changing their shape and converts a single segment to cubic pen form only for a content edit that needs it. Degree elevation is exact for quadratic/cubic input. Higher-degree input uses Bernstein-coefficient error bounds; circular arcs use a fourth-derivative Hermite remainder bound plus any control clamping error. Adaptive subdivision bounds parameter-matched X error to 0.001 playfield units and time error to 0.01 ms, with at most 4096 cubics and the existing minimum anchor spacing. This is not a universal bound on X at equal times near a horizontal time tangent. Conversion failure leaves the gesture's source intact. Resulting cubic controls remain time ordered and within the playfield.

Batch anchor deletion allows endpoints and preserves surviving IDs, times, and order. Before merging adjacent segments, it clears hidden handles from old linear segments that would become active, then chooses the segment type from remaining usable handles. New endpoints lose unused outward handles. When fewer than two anchors remain, App deletes the parent slider; undo restores all data.

A Legacy Slider retains imported `.osu` L/B/P/C geometry, declared length, span count, and original line. **Convert to FSlider** in properties or the context menu fits the first span's time–X track from path arc length and start velocity, preferring straight segments and using cubic Bezier segments for smooth portions. It does not recover the original author's handles. Endpoints, reversals, plateau boundaries, and significant corners are prioritized without beat snapping. Maximum lateral fitting error is 0.25 playfield units: each original polyline interval checks the cubic-versus-line difference at endpoints and derivative roots, subdividing when necessary. Handle times divide intervals into thirds; lateral controls stay within endpoint bounds and preserve direction to avoid introducing reversals.

Derandomizing conversion requires a generated slider with the same start time, total duration, and span count. Exact object counts, sequences, and legacy random tiny offsets are not acceptance conditions for that mode. The preferred fit retains strict TinyDroplet alignment when possible; otherwise the same fitted track opts out of compensation. If generation still fails, a bounded linear approximation follows the imported path at the original times, with lateral speed limited to the source velocity. This fallback may exceed the preferred 0.25-unit fitting tolerance. Tiny offsets and derived star ratings can change.

Batch conversion validates in the complete beatmap context, retrying after alignment or shape relaxation so downstream RNG is recalculated. All replacements per difficulty form one undo transaction. Cancellation or a changed project applies no partial results. Invalid geometry or durations below the editable model's 0.001 ms minimum remain unconverted with a reason.

FSliders retain the original parent Id, SourceOrder, OriginalLine, and SpanCount. Nodes define the first span; repeats share them with reversed evaluation. `SpanCount=1` is a single traversal. New FSliders and imports that satisfy strict alignment use `CompensateTinyDroplets=true`. Approximate imported tracks save `false`, retaining legacy random tiny offsets instead of requiring every TinyDroplet to lie on the edited target. `null` preserves the old-project session-toggle behavior. Banana showers store editable start/end times. Their X=0–512 canvas rectangle and top/bottom handles edit that range; individual banana positions are not saved.

## Derived conversion

```text
Complete MapDocument
  → mixed-segment time–X evaluation / unedited imported L/B/P/C approximation
  → start timing and SV / first-span path and SpanCount traversals
  → head / tick / repeat / legacy-last-tick / tail, tiny, and banana events
  → RNG in complete parent-object order
  → arc-length positions + random offsets
  → stable time sorting of F/D/T / bananas
  → errors, failure diagnostics, and hyperdash
```

`ConvertedCatchObject` contains SourceId, EventIndex, Kind, TimeMs, X, TargetX, PathX, RandomOffset, and IsStandalone. `X` is the actual position; `PathX` precedes random offsets. New curves' `TargetX` is the authored target; imported objects have no additional alignment target. `(SourceId, EventIndex)` identifies the source and is shared by hyperdash rendering.

`GeneratedSlider` stores its source, IsImported, SpanCount, start time, total duration, velocity, SV, TickDistance, single-span length and geometry, compensation applied/succeeded flags, and maximum tick/tiny errors. `CatchConversionResult` provides Sliders, Objects, Diagnostics, Success, and maximum error.

Each FSlider generates one `.osu` slider preserving SpanCount. Actual position follows first-span arc length and traversal direction. Internal F/D/T alignment tolerance is 0.0001 playfield units; final positions use float arithmetic. Folding geometric Y at boundaries does not add repeats or ticks. Legacy Sliders use original-length clipping/extension, repeated spans, and reversed evaluation.

Ordinary Droplets have no lateral RNG offset. TinyDroplet alignment solves the pre-offset X from actual RNG and event path progress. FSlider alignment is constrained by `0..512`, shared repeat geometry, and horizontal velocity. Automatic SV may increase within stable's 0.1–10 range. Unreachable mandatory targets fail the track rather than returning an off-track result. Legacy Sliders retain osu's original TinyDroplet RNG offsets.

RNG uses seed 1337. Parents are stably ordered by start time and SourceOrder; new objects without imported order use deterministic collection order. All nested RNG for one stream is processed before the next parent, and time sorting happens only afterward. Droplets consume rotation randomness; TinyDroplets use lateral offsets. Each banana consumes position randomness and three appearance draws, with float time accumulation retained. Viewport culling does not change input.

Failed objects produce no result, set overall Success=false, and leave RNG corresponding only to successfully generated objects. Path length, event count, imported controls, repeats, sampling, and grid sizes have explicit limits. Values, Catch handling of inherited NaN, and source references are in the [conversion module documentation](../src/FruitsAtelier.Core/Conversion/UPSTREAM.md).

Hyperdash uses all Fruit / Droplet results, skipping TinyDroplets and Bananas, and preserves direction and remaining movement. Markers belong to the departure object and recalculate when CS changes.

## Persistence and export

`.catchproj` saves nodes, handles, optional exact OutgoingCurve controls and reference ratios, OutgoingKind, default track Kind, SpanCount, OriginalLine, CompensateTinyDroplets, difficulty, complete timing, and resource references. It excludes undo history, derived objects, and GPU caches. Older projects default to OutgoingKind=null, SpanCount=1, and Tiny override=null. Reading validates schema, IDs, model boundaries, and curve constraints, rejecting unsupported fields and versions. Inherited NaN uses named JSON floating-point representation. Saving replaces a same-directory temporary file and stores resource paths relative to the project directory without copying audio.

`.osu` accepts v12–v14 / Mode=2 and emits v14 / Mode=2; see [stable File Contract](STABLE_FORMAT.md). Legacy Sliders retain original lines. FSliders encode integer time and path coordinates, preserve SpanCount, and insert/restore inherited SV as needed. Legacy-converted FSliders reuse original type/hitsound/sample fields. Span-count edits preserve surviving edge samples; new edges receive defaults and a diagnostic. Incompatible same-time SV conflicts fail explicitly. Export read-back compares the full object sequence, times, and X; quantization can introduce errors or sequence changes.

Project saving and `.osu` export are independent; exporting cannot replace saving authoring intent. Video and storyboard playback are unsupported. Preserving raw section text alone does not mean its referenced assets have been packaged; workspace archive retention is described separately in [Workspace](WORKSPACE.md).

`CurveTrack.StreamSnapDivisor` is null for ordinary sliders and 1–16 for slider-managed fruit streams. It participates in cloning, history, equality and conversion-cache invalidation. Stream sampling uses the head BPM and follows every repeated traversal without consuming slider RNG. Derived fruits retain the parent ID with distinct event indices and standalone gameplay semantics. They do not produce generated slider geometry or SV overrides. `.osu` export expands them into ordered hit circles; only project files retain the editable parent.
