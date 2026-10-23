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

Object snap retains one time-sorted fruit target array per editor, keyed by the
current authoring conversion identity. A new conversion replaces the array, so
content edits, undo and document replacement refresh its positions. Pointer
placement reuses the owned timing lookup to calculate the musical search window,
then binary-searches its first fruit and visits only that interval. Viewport and
preference changes reuse target data; subsequent slider draft points do not query it.
Whole-object dragging retains the gesture's fruit array and its first/last selected
fruit coordinates. Standalone fruit origins use authoring coordinates so applying
the offset does not introduce converted float rounding. Each pointer update searches just those two time windows,
excluding the selected source parents, without rebuilding conversion or flattening
slider paths. Release, cancellation and failed preparation discard the gesture
snapshot. Alignment is bounded by the complete selection's playable X extents.
The gesture also retains its DS reference array. DS presets, collinear positions
and object alignments compete against the raw horizontal offset before applying
the winning offset; pointer updates do not rebuild DS references from moved content.

## Full-map work and cache ownership

Discussion annotations reuse the timeline's committed parent durations and combo
event numbers. Associations rebuild only when the committed conversion or online
snapshot changes. Viewport movement queries time-sorted regions with prefix end
times; quote wrapping and discussion text layout are cached independently of
hover and playback. Text selection uses logical offsets in cached runs so visual
wrapping does not change copied text. Project replacement cancels pending loads, and difficulty
cache retirement releases annotation associations and quote layouts. Network and
cache I/O run off the UI thread. Pending requests do not drive rendering; task
completion and ready-avatar queues request frames, while relative posting ages
refresh once per minute when the panel is visible.

Replacing a difficulty releases its conversion, export/read-back and presentation
caches, including when returning to the library. The next frame releases its
renderer geometry and background resources before drawing the new content.
Project replacement also clears the project-local clipboard. Retired rating,
resource and conversion workers cannot publish into the new session.
After project/difficulty preparation or retirement, a single idle collection
compacts large-object storage when committed managed memory exceeds 256 MiB.
It waits for retired workers and difficulty ratings, and is deferred during
playback and input gestures. Ordinary edits do not request this collection.

Imported control points use copy-on-write buffers: exact integer coordinates occupy
two 32-bit fields; coordinates requiring double precision retain it. History and
conversion snapshots share immutable storage, and every mutation detaches first.
Equality can reuse buffer identity. Derived geometry uses a weak memo keyed by all
geometry inputs, so inactive imports do not pin their sampled paths. Active
conversion results retain the geometry they use.
The standard canvas shares those immutable path samples; it creates renderer
stroke arrays only for visible slider bodies and retains them until invalidation.

Catch path drawing caches the original source path for ordinary imported Legacy
Sliders, keyed by the committed conversion snapshot. Drawing and hit testing share
its cumulative distances; processed event positions remain separate. Source paths
are sampled lazily and retired with the difficulty caches.

Catch path drawing retains cumulative distances per conversion and binary-searches
the visible span interval. Screen polylines are reused while the viewport is
unchanged; subpixel screen samples do not change authoring or exported geometry.
Dense standard slider bodies use a bounded Direct2D raster cache with a 32 MiB
budget, retaining the border and gradient. Pan, zoom and fade reuse cached pixels.
Bitmap effect inputs compensate their 96 DPI before path-space transforms, keeping
body positions and radii aligned with anchors when window DPI changes.
Visible dense bodies share that budget; their per-texture resolution is bounded by
the simultaneous body count so cached bodies do not repeatedly evict one another.
During playback, new bodies use small preview textures while one background CPU
worker builds their detailed pixels. Only the rendering thread uploads native
bitmaps. Ready textures replace the previous image through a short premultiplied
cross-fade, preserving one application of object opacity. Budget reductions retain
the displayed texture until its replacement is ready. Both images count toward
the 32 MiB cache limit. Paused windows repaint until pending replacements finish.
Stopping playback permits synchronous detail generation.
Huge path bounds use a padded viewport window instead of allocating an unbounded
texture. Ordinary paths retain vector rendering.

Hitsound snapshots parse each parent's sample and edge fields once. Nested events
must not copy or split its slider geometry text while resolving their samples.

Legacy droplet effects run on a detached candidate at the content commit boundary.
The active transaction retains its baseline until validation completes. Rendering
and playback reuse the prior committed conversion; save/export and content edits
wait for validation. Only the matching history transaction, content, compensation
and language may publish the prepared geometry, timing and warmed export cache.
Failure or cancellation restores the baseline. Worker caches are never shared
with the UI. Baseline fitting data preserves idempotent strength/seed changes and
is retimed when authored timing changes.
Compensated-import curve recovery also runs at this boundary. It fits batches of
candidate lines, Beziers and arcs with a bounded fitting budget, then shares one
write cache across candidate export validations. Samples are evaluated without
allocating control arrays per point. Final conversion validates NM and HR playback
after any rejected parents return to Legacy. Neither fitting nor its export checks
run during pointer movement, hover or painting.
Automatic Legacy droplet refresh reports progress in the status bar without
opening the slider conversion dialog. The pending transaction still gates further
content edits and save/export until validation completes; Escape cancels it.

Placement movement overlays project break reconciliation from cached parent
intervals and the candidate interval. They share the transaction's break algorithm
without cloning a document or exporting the map. The interval cache follows the
conversion snapshot; projected breaks follow the placement snapshot, committed
break array and AR. Leaving placement restores committed break filtering.

Lazer distance guide colours retain a sorted playable fruit/droplet array and
movement prefix contexts, keyed by playable snapshot identity, CS and excluded
selected parents. Guide sampling binary-searches the preceding event and evaluates
one candidate using its cached context. Viewport movement and colour preferences
reuse these contexts; changing content, CS or the excluded parents rebuilds them.

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
Authoring and read-back use the same effective base SV, with separate conversion
and parsed-line caches. Baseline matching compares authored content without cloning,
including the override value and ignoring only the editing lock. Continuous SV input retains
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
Placement ordering maps read-back parents through the export's source identities;
grouped banana events use their containing exported segment's start time.

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

Native osu slider drafts retain one local candidate and sampled path shared by
the geometric canvas and upper timeline. Controls, pointer, path type, timing
lookup identity, beat-snap state, divisor and effective base SV invalidate it.
Length snapping and duration queries reuse the editor's timing lookup. Pointer
movement does not export or convert the committed map for this draft geometry.

The object timeline retains Stream/Stack fruit times, stacking, combo numbers and
event lookup entries for the current conversion snapshot. The colour presentation
owns one combo-entry snapshot shared by numbering and preview combo ends. Content
and NC edits refresh these snapshots; viewport changes reuse them. Drawing queries
a snapshot-owned maximum-end range index, retaining overlapping long
objects while skipping offscreen markers. Draft tail changes update their indexed end;
viewport and selection changes reuse it. Banana pattern segments are ordered and
non-overlapping, so drawing binary-searches the first visible segment and stops
after the visible interval. Drawing filters
the cached chronological markers into a reusable visible buffer, preserving paint
and hit order without allocating or sorting a new visible array each frame.
Exact control-curve and internal NC snapshot comparisons use indexed lists to
avoid allocating enumerators per track during conversion, dirty-state and rating
checks. The transport overview retains sorted timing points with the Kiai
presentation snapshot; content and timing edits rebuild it.

Base-SV input uses the editor's current exported snapshot for a constant-time
range rejection from inherited beat-length bounds, including compensated red
resets. Confirmation converts the confirmed and candidate snapshots on a detached
worker with its own write cache. NM and HR object identities, kinds, times and
positions must match before the new value commits.
SV/DPB input in Configure DS updates a local draft without changing content or
invalidating conversion. Apply queues detached validation; draft controls stay
inactive until it completes, while Cancel discards the pending result. Only the
current, matching snapshot may commit SV, DPB and DS presets in one undo step.
FSlider fitting and export read-back use the candidate's effective base.
Each write cache retains one current baseline and one validated override result,
keyed by document content, effective base, Tiny compensation and language. The baseline ignores
only the editing-lock flag. Content edits invalidate pending
results; a successful result commits after active history transactions end and
supplies the next render's export cache. Workers do not share mutable conversion
caches with the UI.

Coordinate projection reuses the current viewport transform until its origin,
dimensions, time range or time scale changes; drawing each object must not allocate
another transform.

Windows drawing uses one mutable solid brush, at most 128 cached text formats and
256 cached text layouts. Layouts are keyed by text, size, clipping dimensions and
weight, so dense NC labels and timeline numbers reuse shaping while moving.
Position, colour and opacity do not invalidate a layout. Eviction and difficulty
retirement release layouts; continuous animation cannot grow the cache unbounded.
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

Ordinary difficulty saves clone only the requested document on the UI thread.
Their worker owns synchronization, export validation and journal publication, and
prepares the saved baseline before handing it back. Completion transfers that
detached baseline without cloning other difficulties or rebuilding conversion.
Recovery history copies persisted difficulty files and records the current
difficulty separately; full project reconstruction happens when a version is read.
History compression remains deferred to the idle worker. See
[workspace publication](WORKSPACE.md#file-structure) for recovery boundaries.

Creating a history archive only queues workspace retention. Saving and synchronization
publish their recovery snapshots without enumerating other projects or enforcing
history limits. Retention runs in the idle Library worker and checks for resumed
editing between files and complete snapshot removals. Cancellation retains the
pending request; reference validation and recovery checks still precede deletion.

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

## Dock inspectors and live editing

Collaboration selection locks retain one set of selectable parent IDs per editor,
keyed by the conversion snapshot and editable-range list. The set reads the object
timeline's existing parent intervals; pointer hit tests reuse it without rebuilding
slider geometry. Lock overlays iterate only the configured ranges. Transaction
validation checks changes against locked content at commit, including pasted and
retimed parents and timing inherited into locked gaps.

Rotation previews retain one history transaction and restore only the selected
objects from its baseline for each new angle. Repeated values reuse the preview.
Catch validation uses a preview-owned conversion cache; canvas rendering uses the
content-drag conversion path, deferring full-map export/read-back until confirmation
or cancellation. Ratings and file operations wait for the preview to finish.
When an angle exceeds Catch constraints, a bounded angle search finds a valid
boundary. The dialog retains that boundary for subsequent input with the same
direction and origin; changing either or reopening the dialog resets the bounds.

Slider Inspector caches rows by committed conversion snapshot, playable-export
identity and selected parent IDs. It reads existing exported events and timing;
repainting and horizontal scrolling do not export or convert. Tiny droplets are
always included in its peak DS and horizontal-speed calculations.

Song Setup, Stream/Stack and Droplet Settings commit valid changes at input
boundaries through editing transactions. Unchanged values do not create edits.
Graph and snap drags retain their candidate preview until pointer release. Local
edit snapshots preserve focus when committed conversion catches up; external
changes and undo reload the panel. Randomization uses the editor's committed
conversion pipeline rather than starting a separate export for each repaint.

DockHorizontal owns per-panel content width, viewport clipping and scrollbar
input. Panels with a minimum content width can reuse it without tying their
content geometry to the resizable dock width.
