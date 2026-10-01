# Interactive editing performance

Read this document before changing placement, hover previews, dragging, selection,
conversion, export read-back, or metadata refreshed by an edit. Functional correctness
on a small map does not establish that an interaction is responsive.

## High-frequency work

Trace the complete call chain from pointer input through the next render. Hover,
pointer movement, painting, inspector values, timeline layout and distance labels
must not introduce an uncached full-map export or conversion. A helper that returns
a time or position can still flatten a complete imported slider path.

- Reuse the editor's current conversion and playable export when content is unchanged.
- Use local candidate data for previews. Standalone fruits consume no normal-mode
  RNG; a fruit-only preview can merge the quantized candidate with existing playable
  events. Replacing a slider or banana shower requires the full RNG-aware path.
- Batch timing queries through one `TimingMap.Lookup`. Calling `TimingMap.At` in a
  per-object loop constructs and sorts a new lookup for every object.
- Fill beat grids into each view's reusable buffer with `TimingMap.Lookup.FillGrid`.
  The lookup owns timing values; the buffer is refilled for the current visible range
  and snap divisor, without retaining grid data across timing changes.
- Reuse `TimingMap.Lookup.Snap` for pointer placement. The editor checks the owned
  timing snapshot against current timing values so in-place edits and undo invalidate
  it even before the next render.
- Obtain slider end times from existing converted slider durations. Do not rebuild
  `ImportedSliderGeometry` just to display a duration already computed elsewhere.
- Refresh content-dependent data at its owning edit boundary. Viewport, selection,
  language and hover changes must not create content history or invalidate geometry.

## Full-map work and cache ownership

`OsuBeatmapWriter.Serialize` validates, converts, emits text, parses it again,
reconverts the quantized map and computes playable NM/HR events and diagnostics.
It is not a cheap accessor. Explicit export needs this validation. The committed
editor snapshot also uses exported events for gameplay accuracy, but must reuse
unchanged work through its caller-owned `OsuWriteCache`.

`CatchConversionCache` reuses parent results only when input geometry, timing,
conversion settings and incoming RNG state permit it. Authoring and read-back
conversion caches remain separate. Read-back slider and banana identities are
mapped to their source parents so reparsing does not discard every cache entry.
Parsed slider lines are reused by exact text and format, with independent mutable
copies and pruning of unused entries.

The write cache retains one emitted timing result, keyed by converted slider
instances, the complete timing input and imported slider head times. Fruit-only
changes can reuse it; changed geometry, repeats, timing or imported head times must
rebuild it. Failed timing validation must not populate this cache.
Callers that need both authoring events and exported events can use
`OsuWriteCache.Convert` before serialization to reuse the same source conversion.
Each conversion batches timing queries through one lookup, including downstream
parents invalidated by a changed incoming RNG state. Emitted SV queries scan the
current mutable timing points without rebuilding the full timing lookup per head.

Placement previews share their hyperdash calculation with the movement readout.
The ordered playable merge input is retained only for one exported snapshot.
An unstarted slider uses the same standalone ghost calculation as a fruit without
applying fruit replacement rules. Unmappable export sequences retain full validation.

Base-SV confirmation reuses the editor write cache's canonical export before SV
override. Cached minimum/maximum inherited beat-length magnitudes provide a
constant-time range rejection, including compensated red resets. Local validation
reuses read-back slider geometry and recomputes nested events only when velocity
changes. Identical event kinds/counts preserve NM random offsets and downstream
RNG consumption; the merged NM and recalculated HR sequences must still match.
Cold profiles are prepared lazily on a worker, so ordinary editing does not build
SV event indexes. Warm UI checks have a 4 ms cooperative budget and an event-count
cap; cold, large, exhausted or uncertain checks use the detached background
snapshot and retain full read-back validation as a fallback and correctness oracle.
Both paths keep generated geometry and do not rerun FSlider fitting for an SV step.
Each write cache retains one canonical baseline and one validated override result,
keyed by document content, Tiny compensation and language. The baseline ignores
only the override value and editing-lock flag. Content edits invalidate pending
results; a successful result commits after active history transactions end and
supplies the next render's export cache. Workers do not share mutable conversion
caches with the UI.

Windows drawing uses one mutable solid brush and at most 128 cached text formats.
Animated text sizes keep exact dimensions; evicted formats are disposed, and all
remaining formats and the brush are released with the canvas.

Break reconciliation belongs to the same undo transaction as the edit. Its
history-owned slider length cache reuses geometry, while timing, SV and repeats
are applied from the current snapshot. Undo retains IDs, so ID alone is never a
sufficient geometry cache key.

The active difficulty rating worker can consume the immutable playable events
already produced for the matching editor snapshot. Starting another uncached
export in a worker still creates CPU and GC pressure that can stall UI frames.
Do not share mutable conversion caches across workers.

## Correctness boundaries

Optimizations must preserve source order for tied timestamps, exported time and
coordinate quantization, nested event identities, downstream random events, NM/HR
results, and undo/redo. Removing an RNG-consuming parent can invalidate later
parents even if their geometry is unchanged. Stream fruits and ordinary nested
slider fruits have different parent ordering rules.

For each cache, identify its owner, invalidation inputs and retention policy.
Compare optimized output against the existing full calculation after geometry,
timing, repeat, source-order and parent-removal changes, including undo and failed
edits. Keep the uncached calculation available as an independent correctness oracle.
Do not silently defer required content validation or substitute unquantized events
for exported gameplay data to make a benchmark pass.

## Verification and review

Use the procedures in [Building and Testing](TESTING.md#editing-performance-benchmark).
Measure hover, pointer dispatch, committed edits and rendering separately on a
representative map with imported sliders, banana showers and timing changes.
Include cold setup and warm repeated interactions; report allocations as well as
elapsed time, because background allocation can trigger process-wide GC pauses.
A 60 Hz frame has about 16.7 ms; 120 Hz has about 8.3 ms. A long commit may span
multiple frames even if ordinary playback remains smooth.

Inspect `editor.log` for `InputDispatch`, `ConversionRebuild`, `ExportReadback`,
`ViewRender`, `InputToSubmit` and GC counters. High input queue time may be a
consequence of earlier synchronous work rather than slow input handling itself.

Before handing off an editing change:

1. Check the diff and call sites for new `Serialize`, `Convert`, `Read`, geometry
   construction and per-object timing lookups in interactive paths.
2. Run output-equivalence and undo regressions for the changed cache boundaries.
3. Compare the same interaction and map before and after the change. Distinguish
   CPU measurements from native rendering and physical display latency.
4. Keep benchmark outputs, profiles and screenshots in ignored `artifacts/`.
   Record measured results and remaining limitations in the task, not as permanent
   performance guarantees in this document.
