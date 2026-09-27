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
