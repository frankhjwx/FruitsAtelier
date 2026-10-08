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
  events when no downstream FSlider uses droplet randomization. Droplet FX uses
  the same NM random draws as legacy conversion; standalone fruits do not advance
  its sequence. Replacing a slider or
  banana shower requires the full RNG-aware path.
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

`CatchConversionCache` reuses parent results only when input geometry, timing at the parent head,
conversion settings and incoming RNG state permit it. Authoring and read-back
conversion caches remain separate. Read-back slider and banana identities are
mapped to their source parents so reparsing does not discard every cache entry.
Parsed slider lines are reused by exact text and format, with independent mutable
copies and pruning of unused entries.
Authored curve targets can be reused when incoming RNG changes. The conversion
cache owns a detached track snapshot and exact-time position memo per parent,
invalidates it on track or conversion-setting changes, and prunes removed parents.
Each memo retains at most 16,384 positions. Control-curve time queries prepare the
segment's points and circular arc once for the complete binary search.
SV-adjusted read-back owns separate conversion and parsed-line caches from the
canonical export. Baseline matching compares authored content without cloning,
ignoring only the override value and editing lock. Continuous SV input retains
one detached validation snapshot until its owning history or content changes.

The write cache retains one emitted timing result, keyed by generated slider
start times and SV values, the complete timing input and imported slider head times.
Geometry or repeat edits that leave these inputs unchanged can reuse it. Changed
heads, generated SV, timing or imported head times rebuild it. Failed timing
validation must not populate this cache.
Timing emission sorts generated heads once and searches existing timing boundaries
by time; it does not repeatedly sort all following sliders for each head.
Callers that need both authoring events and exported events can use
`OsuWriteCache.Convert` before serialization to reuse the same source conversion.
Each conversion batches timing queries through one lookup, including downstream
parents invalidated by a changed incoming RNG state. Emitted SV queries scan the
current mutable timing points without rebuilding the full timing lookup per head.

Placement previews share their hyperdash calculation with the movement readout.
The ordered playable merge input is retained only for one exported snapshot.
An unstarted slider uses the same standalone ghost calculation as a fruit without
applying fruit replacement rules. Unmappable export sequences retain full validation.

An active FSlider draft converts only its own track, merging provisional visual
events with the unchanged committed preview. Pen hover likewise uses a single-track
candidate without export/read-back. Timeline parent ordering and combo numbers are
retained while the draft tail changes. Hitsounds retain the pre-draft conversion.
Completing the curve commits one undo transaction, then queues full-map conversion
and export/read-back on a detached worker. The UI transfers exclusive ownership of
its warmed write cache to that worker. Only one job runs at a time; newer edits are
captured after the preceding job finishes, and results publish only when history,
content, compensation and language still match. A new draft delays publication.
Testplay requests wait for validated output. Save and export use their own authored
snapshots, never the provisional preview. Cancellation restores the pre-draft
preview; switching projects or difficulties retires the pending result.

Base-SV confirmation reuses the editor write cache's canonical export before SV
override. Cached minimum/maximum inherited beat-length magnitudes provide a
constant-time range rejection, including compensated red resets. Local validation
reuses read-back slider geometry and recomputes nested events only when velocity
changes. Identical event kinds/counts preserve NM random offsets and downstream
RNG consumption; the merged NM and recalculated HR sequences must still match.
SV input updates a displayed draft and resets a one-second idle deadline; it does
not change document content or invalidate conversion during continuous input.
Validation starts on a detached worker only after that deadline. Cold profiles
are prepared lazily there, so ordinary editing does not build SV event indexes.
New input remains available during validation, supersedes the worker's result and
restarts the deadline. Only the current, matching snapshot may commit, in one undo
step. Full read-back validation remains a fallback and correctness oracle.
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
An inactive difficulty rating worker owns one write cache for its source conversion
and export/read-back, reusing the source conversion within that calculation. The
cache is released when the worker completes.

Synchronization context rebasing prepares one metadata delta for the entire undo/redo history.
Content-only saves retain each snapshot's existing sections and timing without
parsing or rebuilding them. Changed metadata still rebases across history while
preserving unrelated historical fields. Explicit resource checks use the same
snapshot-owned background worker as periodic checks; filesystem and storyboard
work must not run during save completion on the UI thread.
Stale synchronization results wait for a stable snapshot before retrying. Retries
reuse per-difficulty export caches passed from the completed worker; these caches
are released when the synchronization chain ends and never shared with UI conversion.

The Timing waveform fills one reusable grid for its two drawing passes and retains
sorted red-line values for the current timing snapshot. Stream previews initialize
Stack adjustments only when that mode is selected. With a native testplay driver,
transport polling leaves simulation updates to that driver and copies presentation
state during rendering. The fallback driver still advances with transport updates.

## Correctness boundaries

Optimizations must preserve source order for tied timestamps, exported time and
coordinate quantization, nested event identities, downstream random events, NM/HR
results, and undo/redo. Removing an RNG-consuming parent can invalidate later
parents even if their geometry is unchanged. Stream fruits and ordinary nested
slider fruits have different parent ordering rules.
When both event timestamp and source order tie, emission must retain the
converter's stable parent traversal order (parent start, source order, then input
order across fruits, tracks, imported sliders and banana showers). A stream fruit
uses its own timestamp as the primary event sort key.

For each cache, identify its owner, invalidation inputs and retention policy.
Compare optimized output against the existing full calculation after geometry,
timing, repeat, source-order and parent-removal changes, including undo and failed
edits. Keep the uncached calculation available as an independent correctness oracle.
Draft visuals may be provisional while the curve is being authored. Required
export validation and playable NM/HR results must complete before testplay consumes
them; benchmark completed output against the uncached calculation.

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
