# Project and Data Model

Default saves use [workspace project directories](WORKSPACE.md): a `project.catchdiff` manifest and separate difficulty files. The `.catchproj` schema 1/2 descriptions below cover the retained compatibility format and document encoding.

The authoring model persists as UTF-8 JSON. Documents containing exact control curves use schema 3 (single difficulty) or schema 4 (multi-difficulty `.catchproj`); ordinary pen-only documents continue to use schema 1/2. Documents containing slider fruit streams use schema 5 (single difficulty) or 6 (multi-difficulty). Stack envelopes use schema 7 (single difficulty) or 8 (multi-difficulty). Droplet randomization effects or non-default randomization parameters use schema 9 (single difficulty) or 10 (multi-difficulty). Map droplet derandomization and HR compensation use schema 11 (single difficulty) or 12 (multi-difficulty). Event hitsound overrides with timing inheritance use schema 15 (single difficulty) or 16 (multi-difficulty). Readers also accept the earlier schema 13/14 overrides. Per-fruit stream combos use schema 17/18. Documents retaining v7 slider tick rules use schema 19/20. Banana shower patterns use schema 21/22. Project-level notes use multi-difficulty schema 24; empty notes retain the document-derived schema. Per-slider droplet modes use schema 25 (single difficulty) or 26 (multi-difficulty). All preceding schemas remain readable. Older applications reject the newer schemas rather than silently discarding authoring data. The project implements stable v7–v14 and stable-compatible lazer v128 / Mode=0 or Mode=2 `.osu` parsing, retaining v7 on export for v7 maps and using v14 otherwise. Authored content, imported context, and derived output remain separate.

Non-default osu canvas coordinates, independent osu distance spacing, Legacy native
geometry preservation, or randomization baselines use schema 27 (single difficulty)
or 28 (multi-difficulty).
Independent Legacy slider SV uses schema 29 (single difficulty) or 30 (multiple
difficulties). Each Legacy slider stores its authored `SliderVelocityMultiplier`
and an optional `ProcessedSliderVelocityMultiplier` for materialized droplet
compensation. Imported sliders capture the effective SV at their head; opening an
older project in the editor captures missing values before establishing history.
Moving or reshaping a slider retains its authored SV. Green timing changes do not
replace this per-object value. The Inspector edits authored SV; export metrics
report the processed value. Export alone synthesizes required green points.

When a project or difficulty enters the editor, inherited timing is normalized to
sound, volume and effect transitions with a neutral SV. Imported sliders retain
their captured SV, and native curves retain their starting SV seed before timing
is normalized. Red points retain their BPM and meter. The low-level osu reader
still preserves source timing for lossless format operations.

Synchronization compares this normalized timing, excluding generated SV-only
points. Slider paths are compared through all converted Catch events, including
droplets and tiny droplets: kinds and counts must match, times allow at most
0.000001 ms serialization error, and horizontal positions allow 1 pixel. Object
sample and combo attributes remain independently significant. Equivalent sliders
retain native authoring and their association identity. Timing review shows aligned
attribute columns per timestamp; differing cells are red until resolved, then green.

Older schemas remain readable. A fruit's geometric Y defaults to 192; old saved
fruits recover Y from their retained source object line when present. Catch editing
preserves this coordinate. `OsuDistanceSpacing` is independent of Catch spacing,
defaults to 1 and accepts 0.1–3.2. It participates in persistence and undo and is
not exported as the Catch `Editor/DistanceSpacing` value.

Imported slider control points retain the existing JSON array representation.
In memory, integer coordinates use exact 32-bit pairs; other values preserve
double precision. Independent mutable collections may share immutable backing
storage until an edit detaches it. Derived geometry caches are not serialized.

Legacy sliders can retain detached `RandomizationSource` geometry and a fitted
`RandomizationTrack`. Settings changes derive their path from this baseline so
repeated strength or seed changes do not compound previous effects. Materialized
geometry and emitted timing are validated through NM and HR export/read-back
before replacement. A failure retains the original objects and reports its reason.
Confirmed compensated imports retain original Legacy geometry for parents whose
playback cannot be recovered as a baseline. Other parents retain their recovered
baselines, and source confirmation completes for the difficulty.
Legacy sliders authored or manually edited in the osu! canvas persist `PreserveNativeGeometry`.
Their original two-dimensional controls and authored SV remain separate from the
materialized droplet path and processed SV, including after reopening. Failed
processing retains the prior committed state. Older objects default to false.

Slider-local endpoint snapping uses schema 31 (single difficulty) or 32 (multiple
difficulties). `CurveTrack.EndpointSnap` and `ImportedSlider.EndpointSnap` are
optional immutable start/end divisor settings. A missing setting follows the
global grid; a missing divisor selects the nearest supported grid. Cloning,
content equality, history and slider representation conversion retain the settings.
They are project authoring metadata; `.osu` output contains the resulting timing
and slider length.

Collaboration references use schema 33 (single difficulty) or 34 (multiple
difficulties). `MapDocument.CollaborationReferences` stores reference IDs, source
difficulty IDs and whole-millisecond intervals. Intervals include Start and exclude
End, except the final section in time also includes End. An unassigned source uses
`Guid.Empty`; documents containing unassigned sections use schema 35 (single) or
36 (multiple), while fully assigned references retain schema 33/34. All sections
participate in overlap validation; only assigned sections synchronize. An empty collection is
omitted and keeps the document-derived older schema. References participate in
cloning, dirty comparison and each difficulty's undo history. Reverse references
are derived from the loaded project's difficulty IDs rather than stored twice.
See [Collab Mapping](EDITOR_UI.md#collab-mapping) for synchronization rules.

`CollaborationEditableRanges` stores a difficulty's optional interval lock as
ordered, non-overlapping editable ranges with an explicit inclusive-end flag.
Null disables the lock. Schema 39 (single difficulty) and 40 (multiple difficulties)
preserve this state through save, clone and undo; unlocked documents retain their
content-derived schema. Pattern snapshots omit interval locks. Locks do not affect
exported gameplay, and their overlays require no object conversion during painting.

## Authoritative and derived data

| Category | Contents | Rule |
| --- | --- | --- |
| Authored data | Standalone fruits, tracks, anchors, segment types, handles, span counts, stream snap, timing/difficulty | Authoritative content for editing and undo |
| Imported context | Raw sections and object lines, source order, complete timing, sliders/bananas, resource references | Saved with the project; unedited objects retain their original representation on export |
| Derived results | Slider geometry, SV, F/D/T/bananas, RNG, errors, hyperdash | Recomputable; never overwrite authoring intent |
| Session state | Object/anchor selection, tool, language, snapping, viewport, transport time, skin, layers, Tiny compensation toggle, internal batch clipboard | Not beatmap content |

## Projects and difficulties

New blank and audio-based projects initialize Catch Mode 2, matching Title and
TitleUnicode, the first difficulty's Version, BeatmapID 0 and BeatmapSetID -1.
General defaults include no audio lead-in or preview point, normal samples, a
normal countdown with zero offset, stack leniency 0.7, and disabled widescreen,
letterbox and epilepsy-warning flags. HP/OD start at 5, and an explicit red point
at 0 ms starts at 120 BPM. AR/CS, slider velocity, Tick Rate and DistanceSpacing
retain their model defaults. Empty artist, creator, source and tags remain optional
for blank projects; audio setup supplies its required metadata before publication.
These values agree with the first export, avoiding synchronization conflicts from
generated defaults. Older raw-section-free authoring snapshots receive Catch mode
and their document title when synchronization reconstructs their context. Snapshots
without timing points use their stored BPM and offset to reconstruct the initial
red point, matching export.

`ProjectDifficulty.FaOnly` is an optional difficulty-local workspace policy, also retained by multi-difficulty project snapshots. It defaults to false and is omitted when false, preserving the representation of ordinary projects. It controls Songs synchronization and generation rather than `.osu` metadata or curve authoring. Older readers reject a project containing the new field rather than silently enabling synchronization. See [Workspace behavior](WORKSPACE.md) for creation, settings and external-file handling.

`BeatmapProject` stores a project Name, optional project-wide Notes, and 1–256 `ProjectDifficulty` entries. Each difficulty contains its own Guid, Version display name, and complete `MapDocument`. Schema 2 uses a `Project` outer container and atomically saves all difficulties together. Schema 1's `Document` is automatically wrapped as a single-difficulty project. Each difficulty's resource paths are written and resolved relative to the project directory, retaining the older path safety checks. Project JSON is limited to 256 MiB of UTF-8 data per file, including workspace difficulty files and legacy single- or multi-difficulty projects.

Large imported paths use compact JSON above 65,536 total control points. This changes whitespace only; source text, control points and project schemas remain unchanged.

The editor maintains an independent `EditorHistory` per difficulty and accesses `Document` through the current difficulty. Undo affects only that difficulty; saving updates every history baseline. Adding a difficulty changes project structure rather than a difficulty's object undo stack and keeps the project dirty until saved. The active difficulty, tab scroll position, playhead, and viewport are session state and are not persisted. Opening selects the first difficulty; switching does not create content history. The project container does not force imported `.osu` difficulties to share audio or timing, avoiding overwriting source content.

Song Setup stores metadata, HP/OD, combo colors and Design options in the existing
raw sections; AR/CS remain the authoritative model properties. No project schema
change is needed. Shared metadata edits propagate only the changed keys to the
other difficulty histories. Their saved baselines remain intact for dirty checks,
and their local undo snapshots retain the shared values. The initiating undo
transaction restores each difficulty's own previous shared values.

## Event hitsounds

`MapDocument.HitsoundOverrides` stores effective samples by parent ID and event
index, including sample banks, additions, sample index, volume and explicit file.
Slider fruit entries also retain their edge index for existing sound flag controls.
The collection participates in cloning, dirty comparison, undo and project saves.
Empty collections are omitted and retain the earlier schema selection.

Hitsound Copier matches the nearest exported playable event within 2ms after checking matching
red timing points. Source SV participates in event decoding. Copying leaves target
geometry, red timing and SV intact. Source sample bank, index and volume changes
are stored as visible authoring timing points; new green points use the target SV
and effects. Unmatched audible events retain their initial sound through sample timing points. Export writes circle/stream object
samples, slider edge banks and additions, and timing sample states while retaining
the emitted SV and effects. Unmatched target events keep their sound at copy time and subsequently follow their timing inheritance.
Copied samples retain which fields inherit timing. Playback resolves those fields
from current timing at each event; export writes zero for inherited object/edge
fields. Explicit object-specific samples remain intact. Projects store this
inheritance in schemas 15/16; earlier copied samples default to timing inheritance.
Explicit filenames on legacy slider events and incompatible per-tick extra banks
are rejected during preview because the legacy representation cannot encode them.

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
| BananaShower | Id, TimeMs, EndTimeMs, SourceOrder, OriginalLine, optional immutable Pattern |

Difficulty includes ApproachRate, CircleSize, SliderMultiplier, and SliderTickRate. `DistancePerBeatOverride` retains the distance-snap baseline in `.catchproj`. Project loading resolves legacy conflicts to `100 × EffectiveSliderMultiplier`. The DS panel presents SV and DPB as two units of the same value. Applying a base change updates both and rescales DS preset multipliers by old DPB / new DPB to preserve their pixel distances, after validating slider playback and inherited SV compensation. New maps start at DPB 192 px with SliderMultiplier 1.92; imported maps use their original multiplier. DistanceSpacing is a per-difficulty editor multiplier (0.1–6, default 1), included in cloning, dirty comparison, undo and project persistence. `DistanceSnapRatios` stores up to eight finite non-negative multipliers for the difficulty. It defaults to an empty list in older projects and participates in cloning, dirty comparison, undo and project persistence. Zero DS is always implicit; an explicitly added zero-valued preset is also stored so it remains available for dragging. An empty preset list snaps only to zero and does not use the legacy DistanceSpacing value. Neither DPB overrides nor custom DS lists are exported to `.osu`; see [Editing Controls](EDITOR_UI.md#distance-spacing-and-object-flags). Demo defaults are duration 30000 ms, beat length 500 ms, offset=0, AR=8, CS=5, SliderMultiplier=1.92 (DPB 192), and SliderTickRate=1. MapDocument's BeatLengthMs / TimingOffsetMs are fallbacks when no red timing point exists; real beatmaps retain all timing points.

Import initially derives `.osu` `DurationMs` from the last object. Once associated audio is decoded, a longer audio duration becomes available for placement, numeric editing, and dragging. Loading and navigation do not modify the document. The first object edit beyond the original range extends `DurationMs` within the same user transaction.

Authoritative time is double-precision milliseconds; beat snapping does not round early. TimingMap queries the active red point's BPM / offset / Meter and inherited SV. Grid and snapping use local beat spacing and red-point boundaries; green points do not reset phase. Each slider locks its start timing, so timing changes along its path do not alter velocity. Changing snapping does not alter existing objects or SliderTickRate. Anchor dragging has an independent, default-off session checkbox and uses the selected beat divisor only when enabled.

Gameplay-derived timing uses the exported osu read-back event stream. The writer rounds authored object times and coordinates where required by the file format, reads the output, and maps converted events back to their editor `(SourceId, EventIndex)`. Canvas objects, hyperdash, movement analysis, preview, TestPlay and ratings consume this playable stream. Authoring anchors, beat snapping and editable curve geometry retain their double-precision values. An incomplete draft uses the direct conversion until it can be exported.

EditorHistory uses deep copies for transactions, undo, and dirty comparisons, preserving object IDs, segment types, span counts, Tiny overrides, raw lines, and timing. Saving updates the baseline without clearing undo/redo. ContentEquals invalidates the view's conversion cache. A New Combo toggle retains the playable object stream and refreshes combo grouping and labels because the flag does not change catch event times or positions. Batch Legacy conversion uses an independent background snapshot and validates its source snapshot before applying results. Cancelling an active drag or draft restores the complete transaction; later field edits locate objects by ID to avoid writing into stale snapshots.

Object selection stores complete parent IDs only. Multiple children of the same slider are deduplicated by SourceId. Anchor selection in Select is restricted to the active track; Fruit mode places objects immediately; Select mode selects complete objects and can enter control editing for a selected slider. Undo/redo preserves the current tool and selection of unchanged parents, comparing authored object content by ID across history snapshots. Changed or removed parents lose their selection, and restored parents are not automatically selected. An unavailable history step leaves selection unchanged. Box selection separately snapshots the starting selection. Esc or lost capture restores it without creating content history.

The internal clipboard stores deep snapshots of selected complete parent objects. Paste applies `new time = playhead + original time − earliest selected start`, preserving relative times, X, geometry, relative handles, SpanCount, and sample fields, while assigning new parent/node IDs. It extends document duration as needed and rolls back the entire batch if any object is out of bounds. Copy creates no undo entry; each batch cut, delete, or paste is one transaction. Clipboard snapshots and unselected objects stay unchanged; the system clipboard carries only the timestamp reference, not native object data.

The clipboard belongs to the open project session and can paste across its
difficulties. It retains the source difficulty ID and timing context for Legacy
sliders without retaining that difficulty's history. Cross-difficulty paste
expresses Legacy SV in the target's stored base and BPM so path duration stays
unchanged. Target settings, including SliderTickRate, govern derived events.
Slider-bearing cross-difficulty batches validate export through the editor-owned
write cache at the paste transaction boundary; incompatible SV rolls back the
batch. Document replacement clears the clipboard.

Interface language and resources are not serialized in `.catchproj`. Built-in default names come from resources when creating objects. Existing Name values, imported metadata, and raw lines remain user data; switching languages does not rename them. Language changes invalidate conversion diagnostic caches without changing geometry or document history.

`SliderMultiplierOverride` is nullable and stores the confirmed map base SV;
`EffectiveSliderMultiplier` supplies it to editing, conversion and export,
falling back to the stored `SliderMultiplier`. The DS panel accepts SV 0.4–3.6
and updates DPB to 100 × SV in the same transaction. `OverrideSliderMultiplier`
is retained for project compatibility and enabled when the panel changes the
base value. These fields participate in cloning, dirty comparison, persistence
and undo. Stored timing points retain their source basis. Timing lookups and
exported timing compensate them into the current base, including red resets.
Exact inherited SV compensation is required at retained Legacy Slider heads.
Other timing states are bounded to 0.1–10 before FSliders solve their generated
SV, so unused extreme green points do not block saving. Timing sample and effect
fields remain at their original times.
FSlider generation uses that same base and inherited SV range 0.1–10; export
writes its generated SV directly. Timing insertion retains the stored basis so
compensation is applied once. Base changes validate the candidate conversion
and NM/HR read-back against the current playable objects before committing.
See [Timing editing](EDITOR_UI.md#timing-editing).

## FSliders and Legacy Sliders

New import provenance uses schema 37 (single difficulty) / 38 (multi-difficulty).
`SliderImportState` is None for existing projects, Pending during a new import,
Native for ordinary sources, or Compensated after source confirmation. Missing
state preserves old project behavior and never triggers import preparation.
Opening a saved workspace or project does not apply global import preferences.
Pending imports capture their global mode before preparation and can resume after
reopening. The state participates in cloning, equality, validation and persistence.

Legacy-to-FSlider editor actions resolve the current map/slider droplet mode and
reuse `RandomizationTrack` when present. Ordinary sources fit the original geometric
path; confirmed compensated sources recover a baseline from full-map Catch events.
Recovery retains the original Legacy geometry on import, checks generated event
kinds/times and positions within one playfield pixel, and can simplify a curve only
under the same playback check. Shared repeat targets use bounded playback fitting;
unreachable recovery is reported without publishing a partial import. Subsequent
refreshes and Ctrl+D reuse the retained baseline rather than fitting the processed
geometry again. FSlider-to-Legacy conversion also retains its track and mode.
`MapDocument.DerandomizeDroplets` remains readable for compatibility; editor
conversion no longer resolves an independent policy from that legacy field.
The global `LibrarySettings.DerandomizeDroplets` preference initializes ordinary
imports, independently of the new-project default.

Tracks lie in the `(timeMs, X)` plane, with segment type `Nodes[i].OutgoingKind ?? track.Kind`. Null inherits the track default; a track may mix linear and cubic Bezier segments. Endpoint times increase. Bezier controls may extend beyond either endpoint in time; Bernstein subdivision validates that any time reversal lies outside the authored interval. Time evaluation solves `time(u)` rather than substituting a linear time fraction for u. Anchors are at least 0.001 ms apart, and anchor and control-point X may extend outside 0..512. Gameplay event targets are clamped to the playfield; saved authoring geometry retains its coordinates.

Handles store relative offsets and move with their anchor. The Slider tool (B) has a session-only editing-mode choice. Pen tool mode adds handle-free points on click and direction handles by holding and dragging; osu legacy mode edits a control-polygon projection of the same track. Converting control points between curved and straight uses handles and adjacent segment types. Right-click insertion creates a point without handles; neighboring handles may still curve adjacent segments, so ordinary insertion need not preserve shape. The split action preserves the segment's shape and subsequent types according to its type, without replacing existing anchors/handles with sampled output. Geometric slider Y is not editing time.

An anchor's optional `OutgoingCurve` takes precedence over its outgoing pen handles and `OutgoingKind` for evaluation. It stores exact circular arcs or arbitrary-degree Beziers (up to 64 interior controls). Relative control offsets follow whole-object translation and copy/paste, which assigns fresh control identities. Endpoint times are ordered; Bezier control times are validated by the resulting curve rather than control-polygon order; circular-arc validation checks coordinate extrema and time derivative signs, not just rendered samples. Collinear arcs evaluate as lines.

`SliderControlEditing.Vertices` is a non-persisted projection. Cubic pen handles have deterministic temporary vertex identities; projecting or switching modes does not mutate content. Exact segments, identities and AR references participate in clone, undo, equality and conversion-cache invalidation. Save files never store viewport scale or editing mode.

For arcs, `ReferenceScale = 440 / CatchScrollTiming.PreemptMs(creationAR)`. It converts relative milliseconds to playfield units for circle construction, independently of window width and DPI. Subsequent AR edits only affect display. Bezier evaluation remains in time–X coordinates.

`ControlCurveEditing` splits exact segments without changing their shape and converts a single segment to cubic pen form only for a content edit that needs it. Degree elevation is exact for quadratic/cubic input. Higher-degree input uses Bernstein-coefficient bounds on parameter-matched X error (0.001 playfield units) and time error (0.01 ms). Circular arcs use tangent-matched cubics with spans of at most 90°. An implicit-circle Bernstein bound limits their X error at equal times to 0.001 playfield units, including vertical endpoint tangents; radial time-coordinate error is at most 0.01 ms. Adaptive subdivision adds anchors only where these bounds require it, with at most 4096 cubics and the existing minimum anchor spacing. Conversion failure leaves the gesture's source intact. Approximated cubic time controls remain ordered; exact quadratic/cubic conversion preserves valid endpoint overhangs.

Batch anchor deletion allows endpoints and preserves surviving IDs, times, and order. Before merging adjacent segments, it clears hidden handles from old linear segments that would become active, then chooses the segment type from remaining usable handles. New endpoints lose unused outward handles. When fewer than two anchors remain, App deletes the parent slider; undo restores all data.

A Legacy Slider retains imported `.osu` L/B/P/C geometry, declared length, span count, and original line. **Convert to FSlider** in properties or the context menu fits the first span's time–X track from path arc length and start velocity, preferring straight segments and using cubic Bezier segments for smooth portions. It does not recover the original author's handles. Endpoints, reversals, plateau boundaries, and significant corners are prioritized without beat snapping. Maximum lateral fitting error is 0.25 playfield units: each original polyline interval checks the cubic-versus-line difference at endpoints and derivative roots, subdividing when necessary. Handle times divide intervals into thirds; lateral controls stay within endpoint bounds and preserve direction to avoid introducing reversals.

Derandomizing conversion requires a generated slider with the same start time, total duration, and span count. Exact object counts, sequences, and legacy random tiny offsets are not acceptance conditions for that mode. The preferred fit aligns TinyDroplets exactly when possible. Conflicting repeat targets or compensation exceeding the SV limit instead use a bounded fit that minimizes the maximum playable TinyDroplet error at the highest SV allowed by the SV=10 and path-length limits, keeping fruit and droplet targets fixed. This partial compensation is silent and retains the saved compensation preference. If generation still fails, the same fitted track can opt out of compensation, followed by a bounded linear approximation at the original times with lateral speed limited to the source velocity. This shape fallback may exceed the preferred 0.25-unit fitting tolerance. Tiny offsets and derived star ratings can change.

Batch conversion validates in the complete beatmap context, retrying after alignment or shape relaxation so downstream RNG is recalculated. All replacements per difficulty form one undo transaction. Cancellation or a changed project applies no partial results. Invalid geometry or durations below the editable model's 0.001 ms minimum remain unconverted with a reason.

FSliders retain the original parent Id, SourceOrder, OriginalLine, and SpanCount. Nodes define the first span; repeats share them with reversed evaluation. `SpanCount=1` is a single traversal. `CompensateTinyDroplets=true` requests exact alignment where possible and partial compensation where repeat or speed constraints prevent it. A saved `false` explicitly disables compensation; existing tracks with that value are not automatically changed. `null` preserves the old-project session-toggle behavior. Banana showers store editable start/end times. Their X=0–512 canvas rectangle and top/bottom handles edit that range; individual banana positions are not saved.

## Derived conversion

`CurveTrack.StreamNewComboOffsets` stores additional Stream/Stack NC starts as
millisecond offsets from the parent head. The head uses `OriginalLine`'s NC flag.
Offsets participate in deep cloning, content equality, undo and export cache
invalidation; nonempty collections require project schemas 17/18, while older
projects default to no internal NC starts. Whole-parent movement preserves the
offsets, and timing transforms scale them with the parent duration. Only fruits
sampled at a saved offset receive NC, so a subdivision change
can temporarily omit a marker and restore it when that time is sampled again.
Ordinary slider output uses only the head flag; retained offsets apply again when
the parent is converted to Stream/Stack. Breaking transfers the flags to the fruits.

### Droplet randomization

`MapDocument.RandomizeDropletStrength` is the TinyDroplet lateral randomization
amplitude in playfield pixels (0–100, default 20). Earlier project files with valid
strengths above 100 are capped at 100 when loaded. `RandomizeDropletSeed` is a signed
32-bit integer, default 1337. Each ordinary FSlider can retain a
`DropletRandomization` with its own `Enabled` flag and sorted normalized-time
`Adjustments`. `CurveTrack.DropletMode` is FollowMap, Derandomize or Randomize. Newly drawn
FSliders use the remembered Inspector mode, initially FollowMap, resolved against
`DerandomizeFSliderDroplets` on the current difficulty. When both that switch and
`RandomizeNewSliders` are off, drawing resolves the FollowMap preference to an
explicit Derandomize mode so imported maps and older projects retain their drawing
default. Explicit modes override that default. A missing mode retains the
older resolution: the map derandomization switch overrides the stored effect's
Enabled flag. This preserves old project output without rewriting content on load.
The Inspector presents old enabled effects as FollowMap and disabled effects as
Derandomize. The nullable mode is cloned, compared, validated and included in
conversion-cache identity. Non-null modes require schema 25/26; earlier readers
reject them. `RandomizeNewSliders` retains the difficulty's opt-in to randomization
for newly drawn FSliders. New projects capture the independent General derandomization
preference, which defaults to on. Legacy conversion uses the current difficulty
and retained slider baseline, as described above.
These fields participate in deep cloning, content equality, history, persistence and cache
invalidation. Slider-managed fruit streams retain dormant effects but do not apply
them; imported Legacy Sliders retain their existing conversion rules.

Randomization uses the osu! legacy RNG initialized with Seed, in complete parent
conversion order before final time sorting. Droplets and TinyDroplets consume one
draw each; bananas consume four. Disabled effects still consume these draws.
Standalone fruits and slider-managed fruit streams consume no NM randomness.
Earlier changes to RNG-consuming events shift subsequent targets. Cached parents
restore the normal RNG state and advance the effect RNG by their event kinds.
The target first clamps the base curve at the event's path progress, adds the
legacy integer offset scaled by Strength / 20, clamps, adds a matching manual
adjustment, and clamps again. Fruits and ordinary Droplets retain their base
targets. Strength zero removes the random contribution; saved manual adjustments
still apply while the resolved mode is Randomize.

Strength 20 and Seed 1337 without manual adjustments use the normal uncompensated
slider path and native osu! offsets, including repeat traversal and edge clamping.
Other enabled effects request Tiny compensation against their targets, preserving the
base anchors and handles. Shared repeat geometry, playfield edges and speed limits
can require partial compensation. The normal legacy RNG still determines actual
osu offsets and is consumed in complete parent order. Generated geometry encodes
the effect for `.osu` export; only project files retain editable effect parameters.

Dragging a randomized TinyDroplet or editing its X stores an offset at its
normalized event time. A candidate must reach the requested X and preserve its
sibling event positions, otherwise the editor rejects it. Timing/subdivision edits
retain unmatched adjustments without applying them. Selecting Derandomize retains
corrections and restores compensation to the base curve; selecting Randomize
again restores matching corrections. Copy/paste retains the effect and its
normalized adjustments.

### Conversion flow

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

Ordinary Droplets have no lateral RNG offset. TinyDroplet alignment solves the pre-offset X from actual RNG and event path progress. FSlider alignment is constrained by `0..512`, shared repeat geometry, and horizontal velocity. Automatic SV may increase within stable's 0.1–10 range. Unreachable fruit or droplet targets fail the track; TinyDroplets allow residual error. Legacy Sliders retain osu's original TinyDroplet RNG offsets.

Partial compensation evaluates fixed fruit and droplet targets at their shared
first-span path time, avoiding contradictory constraints from repeat-time
round-off. TinyDroplet targets retain their actual event times.

RNG uses seed 1337. Parents are stably ordered by start time and SourceOrder; new objects without imported order use deterministic collection order. All nested RNG for one stream is processed before the next parent, and time sorting happens only afterward. Droplets consume rotation randomness; TinyDroplets use lateral offsets. Each banana consumes position randomness and three appearance draws, with float time accumulation retained. Banana generation uses the timestamps emitted by `.osu` export: changed or newly authored endpoints round to integer milliseconds, while unchanged imported endpoints retain their original values. This keeps banana counts and downstream slider compensation consistent with export. Viewport culling does not change input.

Failed objects produce no result, set overall Success=false, and leave RNG corresponding only to successfully generated objects. Path length, event count, imported controls, repeats, sampling, and grid sizes have explicit limits. Values, Catch handling of inherited NaN, and source references are in the [conversion module documentation](../src/FruitsAtelier.Core/Conversion/UPSTREAM.md).

Hyperdash uses all Fruit / Droplet results, skipping TinyDroplets and Bananas, and preserves direction and remaining movement. Markers belong to the departure object and recalculate when CS changes.

`DerandomizeFSliderDroplets` is the per-difficulty compensation gate. On suppresses
FSlider FX targets without deleting switches or correction data, and forces tiny
compensation even on tracks with an explicit compensation override. With
`DerandomizeDropletsForHardRock` also On, compensation uses the HR RNG entering
each exported slider. The shared HR state includes fruit stack offsets and
rotation/banana draws. Stream fruits join the chronological exported parent
sequence. Cache reuse additionally checks the incoming HR state, and both flags
participate in content equality, cloning and undo. Conversion objects retain
normal-mode X; tiny compensation errors measure the selected mode. HR preview
and exported read-back apply the ordinary osu HR rules to the generated geometry.

## Persistence and export

`.catchproj` saves nodes, handles, optional exact OutgoingCurve controls and reference ratios, OutgoingKind, default track Kind, SpanCount, OriginalLine, CompensateTinyDroplets, difficulty, complete timing, and resource references. It excludes undo history, derived objects, and GPU caches. Older projects default to OutgoingKind=null, SpanCount=1, and Tiny override=null. Reading validates schema, IDs, model boundaries, and curve constraints, rejecting unsupported fields and versions. Inherited NaN uses named JSON floating-point representation. Saving replaces a same-directory temporary file and stores resource paths relative to the project directory without copying audio.

`.osu` accepts v7–v14 and stable-compatible lazer v128 / Mode=0 or Mode=2 and emits v7 for maps retaining legacy slider ticks or v14 otherwise, always with Mode=2; see [stable File Contract](STABLE_FORMAT.md). Legacy Sliders retain original lines. FSliders encode integer time and path coordinates, preserve SpanCount, and insert/restore inherited SV as needed. Legacy-converted FSliders reuse original type/hitsound/sample fields. Span-count edits preserve surviving edge samples; new edges receive defaults and a diagnostic. Incompatible same-time SV conflicts fail explicitly. Export read-back compares the full object sequence, times, and X; quantization can introduce errors or sequence changes.

Project saving and `.osu` export are independent; exporting cannot replace saving authoring intent. Video and storyboard playback are unsupported. Preserving raw section text alone does not mean its referenced assets have been packaged; workspace archive retention is described separately in [Workspace](WORKSPACE.md).

`CurveTrack.StreamSnapDivisor` is null for ordinary sliders and 1–16 for slider-managed fruit streams. It participates in cloning, history, equality and conversion-cache invalidation. Stream sampling uses the head BPM and follows every repeated traversal without consuming slider RNG. Derived fruits retain the parent ID with distinct event indices and standalone gameplay semantics. They do not produce generated slider geometry or SV overrides. `.osu` export expands them into ordered hit circles; only project files retain the editable parent.

`DistanceSnapCollinear` is an editor-only per-difficulty flag, defaulting to true for new and older projects without a saved value. An explicitly saved false value remains disabled. It participates in cloning, content comparison, undo and project persistence, and is not exported to `.osu`. It adds a time-interpolated collinear candidate between adjacent parent endpoints while distance snapping is active.

`CurveTrack.Stack` adds a horizontal-distance envelope to a slider-managed fruit
stream. Points store normalized time over the complete repeated duration and
non-negative distance from the centre curve; `StartLeft` selects the first side.
Adjacent points use smoothstep interpolation. Fruits alternate sides before final
X is clamped to 0–512. The envelope participates in deep cloning, content equality,
undo and conversion-cache invalidation. Ordinary sliders and streams omit it.

`StackEnvelope.FruitAdjustments` stores sorted normalized-time keys and signed
horizontal offsets. Generation first clamps the envelope result, adds the matching
fruit adjustment and clamps again. Adjustments do not change event times or
neighbours, and unmatched keys remain saved when subdivision changes. Horizontal
mirroring reverses their signs together with the envelope's starting side.

Projects using the map derandomization gate or HR mode use single-difficulty
schema 11 or multi-difficulty schema 12. Readers still accept the earlier schemas;
absent flags default to false to preserve existing randomization behavior. New
blank/audio projects initialize the gate from the General new-project preference.
The nullable `DerandomizeDroplets` field is retained for older project compatibility;
new editor conversions use the effective droplet mode.

## Banana shower patterns

A banana shower may store `BananaShowerPattern` with independent integer length
and gap numerators and snap divisors. The length is positive; the gap may be zero.
Fractions represent beats and integrate across red timing changes. Derived short
showers start at the parent head, alternate length and empty gap, and clip the
last occupied segment to the parent tail. No extra segment starts at the tail.
Segments shorter than 2 ms, before or after export quantization, are omitted.
The authored range remains authoritative even when it ends inside a gap.

The parent keeps one identity for selection, transforms, clipboard and undo.
Short showers have deterministic derived identities for conversion caching and
participate individually in chronological RNG traversal. Playable bananas map
back to unique event indices on the parent. Project JSON stores the pattern and
range; `.osu` export writes ordinary individual spinners. Reimporting `.osu`
therefore does not recover the authoring group.

Project notes are UTF-8 text stored in the workspace manifest and project container.
Missing notes default to an empty string. The editor supports up to 65,536 UTF-16
code units. Notes have a separate saved baseline and text undo history; they do not
modify map documents, invalidate conversion, or enter exported beatmap data.
