# Building and Testing

`Core.Tests --timing-alignment-check` covers read-only detection, red-line chains,
parent movement with retained durations, idempotence and undo. `App.Tests
--timing-alignment-check` checks workspace opening, initial difficulty selection,
localized counts, modal input, keep/align choices, undo/redo, project replacement
and one prompt per difficulty per open. Windows `--render-check
--timing-alignment-render-check` renders and operates the dialog in English and
Chinese at two window sizes and three DPI scales; captures go to
`artifacts/tests/timing-alignment-native`.

`Formats.Tests --export-snap-check` checks one-millisecond circle correction after
red-line resets, source preservation, cached export equivalence, occupied heads,
exact Stream/Stack grid times, and unused timing pruning with slider-body and
five-millisecond tail sample preservation.

## Embedded browser checks

`App.Tests --discussions-check` covers the Discussion tab's Read only / osu! Web
context-menu modes, preference persistence, legacy tab migration, canvas annotation
visibility with retained note-interval bands, and the website's initial Discussion URL,
scoped Canvas annotations, timestamp selection, toolbar command order, input
isolation, address copying, Settings coverage, and green Praise annotations.
Browser lifecycle regressions cover dock collapse, dragging to the left dock,
Timing, Library and full-screen errors, including removal of stale annotation hit targets.
`App.Tests --docking-check` covers the default tab and retained layout.

On Windows, `FruitsAtelier.App --render-check --browser-render-check` renders the
shared toolbar, synchronized copy-feedback fade and Settings in English and Chinese
at compact and wide sizes. Images go to `artifacts/tests/browser/native`.
`FruitsAtelier.App --browser-check <artifact-directory> write` followed by `read`
checks synthetic persistent cookies and localStorage across separate processes.
On the same isolated fixture directory, `clear` followed by `empty` verifies that
the browser clearing API removes those cookies and localStorage across processes.
The `embed` mode checks native child placement, text editing and retained drafts
across hide/show, and owner repaint notifications after hiding, moving and resizing.
These checks use hidden windows without taskbar entries and
isolated fixture profiles. Screenshots are generated from test controls; they do
not capture other desktop windows. They do not log in or submit website content.
The optional `web` mode loads a fixed public beatmap in a separate anonymous profile.

Windows requires an installed WebView2 Runtime for browser checks. macOS requires
native WebKit build validation on macOS, plus manual session persistence and native
view/input checks; a C# compile on Windows does not validate the Objective-C bridge.

## SDKs and builds

`App.Tests --discussions-check` verifies anonymous snapshot transport, shared and
per-difficulty categories, complete replies and review embeds, team badges,
timestamp and wrapped HTTP/HTTPS link navigation, tab overflow, retained scroll, annotation bounds and slider
duration, full long annotations at zero, negative view navigation with nonnegative
editing, relative posting ages and footer alignment, type/status icons, independent
category sorting, cache recovery, offline refresh retention, stalled headers/body
reads, unchanged content and conversion, and retirement of stale requests.
It also covers Markdown emphasis, links, literal code, quotes, extensionless image
transport and caching, pending-image idle frames, invalid-image fallback, shared
post/reply image requests, image navigation and retired image requests.
Interaction checks include object selection from numbered timestamps, destination
difficulty selection, image preview zoom/pan and input isolation, profile links,
hand/text cursor feedback, whole-link hover underlines across wrapped segments,
permalink clipboard hints and expiring confirmation, and body
text selection/copy across formatted text, Unicode, replies and canvas annotations.
On Windows, `FruitsAtelier.App.exe --render-check --discussions-render-check`
captures compact and wide discussions, replies, type icons, offline states and
canvas quotes in English and
Chinese at 980×620 and 1440×900 with 96/144/192 DPI under
`artifacts/tests/discussions/native`.
The native check includes Markdown and image cards; pass
`--discussions-image-fixture=<absolute-image-path>` to use a local image fixture
without network access.

`App.Tests --fad-check` verifies portable difficulty packages, local resource
conflicts, matching, adding difficulties, native merge choices, undo/redo, Library
rejection and the FAZ export chooser. `FruitsAtelier.App.exe --render-check
--fad-render-check` captures the File menu, difficulty context menu, FAZ choices,
native merge review and Library error in English and Chinese at two window sizes
and 96/144/192 DPI under `artifacts/tests/fad-native`.

The project targets .NET 10 with C# 12. Two `global.json` entry points pin SDK versions:

| Entry point | SDK | Purpose |
| --- | --- | --- |
| Repository root | 10.0.400 | Windows launch scripts and root builds |
| `macOS/` | 10.0.400 | Mac scripts and desktop CI |

Build on Windows from the repository root:

```powershell
dotnet build FruitsAtelier.sln -c Release -p:RestoreLockedMode=true
```

Desktop CI also builds from `macOS/`, which pins the same SDK:

```powershell
cd macOS
dotnet build ../FruitsAtelier.sln -c Release -p:RestoreLockedMode=true
```

See [Running on macOS](MACOS.md) for launch and packaging. Project files and `packages.lock.json` pin package versions; NuGet caches packages in `artifacts/packages`.

`App.Tests --droplet-defaults-check` verifies global Droplet Settings and Inspector
mode persistence, new-project initialization, both FSlider drawing modes after
reloading preferences, and preservation of existing project values. Legacy single-
and multi-difficulty projects and imported maps exercise compatible drawing defaults,
explicit randomization, cancellation, undo/redo, persistence and exported output.

### Windows sandbox permissions

`App.Tests --window-preferences-check` checks canvas switch persistence, settings
compatibility, window preference serialization, per-difficulty GS/DS, grid level,
Anchor Snap and beat-snap restoration, and temporary beat-grid isolation. It also
checks that snap slider movement defers saving until release. On Windows,
`FruitsAtelier.App.exe --render-check --window-preferences-render-check` launches
native windows and checks normal/maximized restoration after minimized and
fullscreen states, setup isolation and work-area clamping.

Before running checks from a restricted agent sandbox, inspect the selected
command's access requirements. Run Windows `dotnet build`, the full App regression
suite, tests that write system temporary directories or application-data settings,
and native window checks with the required execution permissions from the start.
Do not first run these commands with workspace-only access to discover a known
permission failure. Other focused checks may use the sandbox when their required
paths and services are accessible.

The installed .NET SDK may need Service Control Manager access during build setup;
denied access can produce a failed build with no compiler errors. Existing App
regressions also use system temporary directories and application-data settings,
so workspace-only write access can cause `UnauthorizedAccessException` failures.
Native window checks require access to the desktop session.

If a build or test fails at one of these permission boundaries, rerun it with the
required execution permissions before classifying it as a code regression. Keep
logs and generated validation artifacts under `artifacts/` and retain the checks'
existing settings isolation and cleanup.

Windows distribution uses the self-contained ZIP and extracted-executable check described in [Windows releases](RELEASING.md). Desktop CI checks packaging before a version is tagged; the tag workflow reuses successful desktop regression for identical source content when available and always builds and checks its own package. See [CI validation and reuse](RELEASING.md#ci-validation-and-reuse) for fallback and forced-validation behavior.

`App.Tests --library-search-check` compares indexed substring searches with the
independent catalog scan, including Unicode normalization, multiword and literal
punctuation queries, exact collection membership, metadata updates/deletions, and
publication after superseded input. The incremental Library suite includes it.
`App.Tests --library-search-profile <workspace-copy> <songs-root>` reports query
and initial page preparation time and managed allocations against a catalog copy.

`App.Tests --library-organization-check` checks field sorting, collapsed group
paging, historical source-file dates, collection creation/rename/removal, membership drafts,
restart restoration, content isolation web-link click isolation, compact default-collapsed groups, cached reordering and successful-open visit recording in English
and Chinese at compact and wide sizes. It also runs the favourites regression.
`FruitsAtelier.App.exe --render-check --library-organization-render-check` opens
native windows and captures grouping, sorting, collection membership and recent
visits at 980×620 and 1440×900 with 96/144/192 DPI in both languages under
`artifacts/tests/library-organization-native`.

`App.Tests --my-maps-check` checks exact case-sensitive mapper filtering, multiple
usernames, mixed-mapper sets, native project difficulties, persisted and cancelled
Settings drafts, and disabling/re-enabling standard scanning without source changes.
The library organization native render check also captures My Maps, the missing-name
prompt and General library preferences in English and Chinese at both window sizes.

`App.Tests --library-persistent-cache-check` verifies cached startup while a database
writer blocks reconciliation, first-frame cards and difficulty rows with the startup
worker held, cross-restart star reuse with a locked source file,
retained stars during scans, changed-source invalidation, damaged-cache fallback,
My Maps project covers and Windows thumbnail reuse without rereading the original.
Fixtures are isolated under `artifacts/tests`. The incremental Library check also
runs this suite. The native Library organization check captures the first frame
after restarting with a saved expanded group in both languages, window sizes and
all three DPI values.

`App.Tests --library-category-cache-check` verifies independent membership, search,
sort and disk caches for all four tabs, two FA Collections and two osu! Collections
with matching names. With the database startup worker held after restart, it
switches through every cached view, reorders osu! Collections, and verifies that
neither memory nor disk caches are reused for another identity. The incremental
Library suite includes this check. Native Library checks also capture switching
the four tabs and FA/osu! Collections after restart in both languages.

For an optional read-only startup comparison against the configured Library, run
`App.Tests --library-startup-profile`. It copies the SQLite catalog and view
preferences into `artifacts/tests`, reads source project metadata without saving
it, and compares query construction with cached-page restoration using
RecordingCanvas. It also creates isolated preview files for the four tabs and
all configured FA/osu! Collections. The report excludes native drawing and image decoding.

### Asynchronous fixture cleanup

Before deleting a Library fixture or replacing its `EditorView`, stop file
monitoring and call `CompleteLibraryCacheWrites()` on each view that used it.
`CloseLibrary()` alone does not wait for background preview-cache writes.
Deleting the root while a writer owns `library-preview-*.json.*.tmp` can fail
with a Windows sharing violation. Likewise, wait for gallery I/O to complete
before polling its archive contents; repeated reads can block atomic replacement
on Windows. Wait for the owning operation rather than adding a fixed delay or
ignoring cleanup failures.

### macOS resource fixture paths

Tests that open or import maps, create workspaces, or copy resources through
workspace/resource path validation must create their macOS fixtures beneath `/private/tmp`:

```csharp
string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
    "atelier-fixture-" + Guid.NewGuid().ToString("N"));
```

On macOS, `Path.GetTempPath()` can return a path beneath `/var/folders`; `/var`
is a symbolic link to `/private/var`. Resource validation rejects symbolic links
in any ancestor, so these fixtures can fail with `Resource paths cannot pass
through symbolic links: /var` even when the test created ordinary files.
`Path.GetFullPath()` does not resolve that link.

When this diagnostic occurs in a regression, check the fixture's complete path
and move its root to `/private/tmp`, following the Library and external-resource
tests. Keep the symbolic-link rejection in production code and its security
tests. Plain serialization tests that do not enter resource validation can keep
using the system temporary directory. Use a unique directory per fixture and
retain cleanup in `finally`. Run the affected suite and the macOS Desktop
regression on the final revision before release; a Windows pass does not verify
macOS path behavior.

## Automated regressions

`Core.Tests --pattern-placement-check` checks beat-relative placement, optional
horizontal speed preservation, bounds, stream/stack data, destination droplet
settings, saved AR, hyperdash starts and deterministic previews. `Formats.Tests --pattern-gallery-check`
checks snapshot isolation, AR-preserving `.fpt` round trips, rename/delete persistence,
validation and atomic gallery writes, folder migration, copy/move identity isolation,
folder merges/deletion, Default preservation, version 4 folder packages and import collisions.
`App.Tests --pattern-gallery-check` checks gallery input, placement choices/cancellation,
drop/undo, context rename/delete, fixed card sizes during dock resizing,
preview alignment, saved-AR spacing, canvas scroll-direction settings, scrollbar position,
Alt-wheel zoom limits and preview reuse,
Library-style search, multi-tag filtering, tag creation/colour/name edits and persistence,
detail-page editing, single/multiple-object context capture and source navigation while retaining unsaved maps.
It also checks range selection across offscreen cards, category switching and merging,
cross-folder clipboard shortcuts, Move to submenus with scrolling and multi-selection,
single/multiple-card category dragging, preview-title rename, selection-only context exports,
external FPT drops and the new/existing-folder import submenu,
conditional folder menus, folder rename/deletion with keep/delete/cancel choices,
import destinations, 50% minimum card zoom, two-column maximum sizing and geometry reuse.
`FruitsAtelier.App.exe --render-check --pattern-gallery-render-check` checks the
grid, large scrolling preview, naming/rename dialogs, context deletion, placement menu,
drag/drop and editing banner in
English and Chinese at compact/wide sizes and 96/144 DPI, including warm raster
cache reuse, the Category sidebar and divider, folder selection/merge/import menus,
50% cards, enlarged cards filling two columns in a wide dock, Move to submenus,
and nonempty folder deletion confirmations.
Gallery popup occlusion checks cover timeline resize cursors, hover feedback and
drag gestures; timeline layout checks retain normal resizing outside the popup.
Captures are written under `artifacts/tests/pattern-gallery-native`.

`App.Tests --new-difficulty-check` checks independent complete copies, named
creation, template settings persistence, blank creation, cancellation and empty
name validation, plus template creation/editing/deletion, invalid values and
the Library management entry point in English and Chinese at compact and
standard window sizes. Icon selection, restart persistence and preservation
when applying general settings are included.
Source dropdown checks cover disabled controls, scrolling, mouse and keyboard
selection, and HP/CS/AR/OD layout and tab navigation.
Template sorting checks cover upward/downward and cross-page dragging, save and
restart, cancellation and capture loss, and matching creation-dropdown order.
`FruitsAtelier.App.exe --render-check --new-difficulty-render-check` checks the
native creation dialog and both tabs at two sizes and three DPI values, with
captures under `artifacts/tests/new-difficulty-native`.

`App.Tests --map-import-settings-check` checks General import preferences, saved
defaults, cancelled drafts, remembered Keep/Convert choices and the 95% automatic
FA decision threshold. Uncertain suspected maps still require a source choice.

`Formats.Tests --slider-import-policy-check` checks ordinary import modes, confirmed
compensation, one to three traversals, repeated refreshes, conversion, mode toggles,
project persistence and undo against exported playback. `App.Tests --slider-batch-check`
checks localized source confirmation, Global import defaults, Ctrl+D, conversion,
cancellation, original Catch path display and existing-workspace isolation.
`FruitsAtelier.App.exe --render-check --osu-canvas-render-check --slider-import-render-check`
checks source/representation prompts and Droplet Settings in English/Chinese at two
window sizes and 96/144/192 DPI, including the original ordinary Legacy Catch path.
Captures are stored under `artifacts/tests/osu-canvas-native/import-*`.


`App.Tests --osu-link-check` verifies encoded timestamps, sequential combo
selection, source-object roundtrips, playback pausing, content isolation,
non-editor notifications, URI preservation, loop rejection, named-pipe activation,
registered-application discovery, icon and path rows, confirmation, the clear-binding action, the single fallback selector and
required setup after a system association. It
also runs the time-jump and settings layout regressions. Windows-specific routing
checks use isolated preferences and do not
change the OSU protocol association or launch an osu! client.
`FruitsAtelier.App.exe --render-check --osu-link-render-check` checks native
settings actions, notifications and activation during painting without timer messages in English and Chinese at two
window sizes and 96/144/192 DPI. Captures are stored under
`artifacts/tests/osu-links/native`; protocol actions use test callbacks.

`Formats.Tests --collaboration-check` checks same-project reference graphs,
half-open adjacent ranges, the inclusive final End, source edits/deletions, replacement of section red timing,
fractional beat-grid precision, boundary sample inheritance, repeated sliders,
FSliders, streams, banana showers, custom
samples, target STR precedence, cross-folder sample conflicts, project schemas
and undo/redo, bookmark section generation, unassigned section persistence and
strict overlap validation, complete slider/spinner section containment, exclusion
reports, per-object SV across different BPM/base SV, section sample transitions
without source SV-only lines, and optional same-position target hitsound
preservation. Repeated synchronization and moved/deleted source red points check
that section replacement never accumulates timing lines.
`App.Tests --collaboration-check` checks the dockable panel, active-difficulty
configuration, reverse references, osu! timestamps, integer milliseconds,
clipboard input, manual synchronization and automatic AiMod refresh after sync.
It also checks grouped incoming ranges, reverse synchronization, cleanup and lock
undo, saved locks and 30% pink overlays confined to the canvas playfield. The
Formats collaboration suite checks reverse timing and metadata, disjoint-range
replacement, locked insertions/deletions/moves and lock schemas. The canvas feedback
performance benchmark compares the same mixed fixture with section locks off/on.
On Windows, `FruitsAtelier.App.exe --render-check --collaboration-render-check`
checks native panel input and synchronization in English and Chinese at two
window sizes and 96/144/192 DPI. Captures are written under
`artifacts/tests/collaboration/native`.
`Formats.Tests --collaboration-map-check <difficulty.catchdiff>` reads a saved
workspace manifest and its documents directly to check that difficulty's manual
references without opening a recovery session or changing source files.

`App.Tests --dirty-exit-check` verifies Save / Discard / Cancel on leaving a linked
project, read-only background checks while dirty, and uninterrupted object placement
when an external conflict is detected.

`Formats.Tests --semantic-sync-check` runs the synchronization regressions, including
playable slider equivalence, position tolerance, independent SV capture and
normalized timing choices. `FruitsAtelier.App.exe --render-check
--semantic-sync-render-check` exercises the English and Chinese timing and playable-object tables at
two window sizes and captures unresolved/resolved cells under
`artifacts/tests/semantic-sync-native`.

`Formats.Tests --fa-only-check` checks default compatibility, per-difficulty persistence, source exclusion, portable snapshots, individual saves, stale export rejection, source-change and deletion-failure isolation, and reenabling generation. `App.Tests --fa-only-check` checks new-difficulty defaults and FA-only selection, Song Setup dock and modal controls, keep/delete/cancel choices, independent policies and restart in English and Chinese at two sizes. Native synchronization checks capture the creation choice, FA Settings and file-removal confirmation.

`Formats.Tests --full-sync-check` runs the focused connection-retention and version-history checks. `App.Tests --full-sync-check` checks full-sync confirmation and cancellation without writes, backup failure isolation, unchanged Stream retention, external object replacement, the named working snapshot, direct recovery, expired recovery points, undo and project isolation in English and Chinese at two window sizes. It also runs the synchronization and version-history UI regressions. Native synchronization rendering captures the full-sync confirmation. `Formats.Tests --semantic-sync-check` additionally verifies complete stream retention, edited/deleted/ambiguous stream rejection, external timing changes and project round trips.

`App.Tests --sync-details-check` checks playable event matching, position tolerance,
insertions and per-property unresolved/resolved colours.

`App.Tests --faz-check` checks portable project modes, native roundtrips, exact
identity/metadata matching, complete-folder coverage, resource isolation, ownership,
repeated imports, automatic file registration, archive validation and modal entry points.
`App.Tests --project-deletion-check` checks recycling failures, backup retention,
complete project/history/resource restoration and native system recycling. The native
fixture is left in the system Recycle Bin / Trash for restore verification.
`FruitsAtelier.App.exe --render-check
--faz-render-check` checks the native import dialog in English and Chinese at two
window sizes and DPI levels, including project-card selection and pagination,
saving captures under `artifacts/tests/faz-native`.

`App.Tests --difficulty-save-check` covers six-difficulty local and linked saves,
unchanged file timestamps, navigation and edits during saving, per-difficulty undo,
compressed history reconstruction and interrupted publication recovery.
`Core.Tests --deferred-history-check` checks deferred retention, idle cancellation,
new requests during maintenance, referenced rounds, unreadable references, pending
recovery and history compression. `App.Tests --storage-check` exercises the idle
scheduler and Settings controls in English and Chinese.
`App.Tests --history-maintenance-check` checks synchronization of a new difficulty
over the history limit and processing of queued retention in the idle Library.
`App.Tests --history-save-performance <artifact-directory>` measures save dispatch
and completion after adding a difficulty and after editing one, with six starting
difficulties, 2,500 unrelated references and more than 100 history rounds. It checks
the persisted authoring contents and writes timings to the specified directory.

`App.Tests --difficulty-save-performance` compares complete and single-difficulty
publication on identical one- and six-difficulty fixtures, including repeated
imported sliders. It reports CPU-thread allocations and elapsed storage work to
`artifacts/tests/difficulty-save-performance`; it does not measure native FPS.

`App.Tests --osu-canvas-check` verifies standard geometry, source-identity picking,
stacking, approach/disappearance, native editing, slider representation boundaries,
stable grid and anchor gestures, horizontal canvas overscan, independent DS,
undo and navigation. It also reports warm rendering and pointer
CPU/allocation measurements on a 10,000-circle fixture; its recording canvas
excludes native GPU rendering. `Formats.Tests --osu-model-check` verifies Mode=0
import/Catch export, coordinates and project compatibility, both slider conversion
directions, Legacy droplet effects, timing changes, failure atomicity and undo.

`App.Tests --canvas-feedback-check` covers cross-canvas hitsounds/NC/sample banks,
rotation shortcuts, live preview, cancellation, single-step undo and dialog dragging,
fixed Catch start/end times, curve constraints and rollback, two-dimensional
Stream conversion, placement movement connections and Timing range navigation.
Native `--render-check --osu-canvas-render-check --canvas-feedback-render-check`
captures the rotation preview, moved dialog, Catch preview and Stream dialog,
their results and inherited Timing Audio
entry in both languages, two window sizes and three DPI settings under
`artifacts/tests/osu-canvas-native/feedback`.

`App.Tests --rotation-preview-performance` measures cold input plus paint and warm
pointer/paint median and p95 for both canvases on a mixed 3,000-note fixture. It
also reports allocation and verifies cancellation restores the document. Its
recording canvas excludes native GPU rendering.

`App.Tests --legacy-droplet-worker-check` verifies responsive submission, prepared
export publication, failure rollback, save blocking, cancellation, undo/redo and
rejection of stale content or history transactions.
It also checks that the first note placement and deletion finish without a
progress dialog. `App.Tests --legacy-edit-map <difficulty.catchdiff>` runs this
first-edit check against a saved document in memory without writing its source.

`FruitsAtelier.App.exe --render-check --osu-canvas-render-check` exercises the
native Direct2D renderer in English/Chinese at two sizes and three DPI values,
using skin and geometric fallback rendering. Captures are written to
`artifacts/tests/osu-canvas-native`.
Add `--osu-configured-skin` to inspect the configured selected/default skin paths
without changing saved preferences.
Add `--slider-display-render-check` to the osu canvas native check for original
and processed Legacy paths with the Inspector open, in English/Chinese at both
window sizes and 96/144/192 DPI. The shared osu canvas suite verifies both effect
modes, narrow panels, warm cache reuse and unchanged content/export during switches.
Use `--osu-skin-folder=/path/to/skin` and `--osu-default-skin=/path/to/default.osk`
to keep those fixtures independent of concurrent preference changes.

Add `--osu-image-cache-check --osu-configured-skin` to run the native texture
residency regression. It warms a scene with multiple combo colours and requires
zero additional image decodes during repeated static and playback rendering.
The CPU timing report is written to `artifacts/tests/osu-canvas-native/image-residency.txt`.

Add `--osu-map-folder=/path/to/beatmap-set` to load each `.osu` file and capture
both canvases at the slider with the most control points. The check verifies a
complete Catch conversion, the requested seek position and unchanged content.
`Formats.Tests --import-roundtrip /path/to/map.osu` verifies an external map's
project and Catch export without writing its source. `--import-set-roundtrip`
accepts a folder and verifies the complete multi-difficulty project.
`--xnor-timing-check` and `--large-imported-path-check` exercise the synthetic
legacy edge cases and compact project storage without external fixtures.

`--osu-performance-map=/path/to/map.osu` with the native canvas check measures
pointer movement, seeking, zoom and continuous playback in both canvases at
168 DPI. It reports CPU and submission frame percentiles, process working-set
peak, private memory, managed memory and cumulative allocations in
`artifacts/tests/dense-map-performance.json`. Skin arguments apply to this check.
Use `--osu-performance-time=50836` to select a specific millisecond position;
otherwise the check starts at the slider with the most control points. Continuous
playback covers twenty seconds and reports the maximum as well as percentiles.
Audio samples are not sent to a hardware device. Cumulative allocations are not
simultaneous memory usage. The check waits for background star ratings before
sampling interactive frames. `App.Tests --map-performance` additionally reports
conversion and individual drawing phases with a recording canvas, excluding GPU
work. `Core.Tests --packed-slider-points-check`, `--hitsound-snapshot-check` and
`--imported-geometry-memo-check` verify lossless storage, snapshot isolation,
sample resolution and derived-cache invalidation.

`App.Tests --library-incremental-check` verifies scoped source scans, cached cards
remaining openable during refresh, standard-source protection and background
database locks. `App.Tests --benchmark-library-loading` creates 10,000 synthetic
standard/Catch difficulties in 1,000 sets under `artifacts/`, then measures cold and
warm scans, cached startup, query construction and a single-directory update.
Its report excludes native rendering, media and real library filesystem variance.

`Core.Tests --aimod-check` and `App.Tests --aimod-check` cover AiMod rules,
resource checks, read-only navigation, localization, Ctrl+Shift+A, table column
resizing/reordering, selection/copy and dock attachment.
Audio-length regressions cover playback-duration reuse, delayed loading, reloads
and the 80 percent boundary without rechecking on playback-position updates. Run
`Core.Tests --aimod-twin-bloom <path.osu>` against the matching local Twin Bloom
fixture to compare stable screenshot findings and category/severity totals.
`Core.Tests --aimod-ethanol <path.osu>` checks the Ethanol difficulty
`Usok ng Sigarilyo at Amoy ng Ethanol.` against stable's messages, timestamps,
category totals and overlapping source-object pairs.
`FruitsAtelier.App.exe --render-check --aimod-render-check` checks the native
dock panels and continuous scrolling at multiple sizes and DPI values and captures English/Chinese reports
under `artifacts/tests/aimod-native`.

`App.Tests --group-drag-boundary-check` checks fractional fruit selections dragged
to either playfield edge, preserving conversion, export, relative spacing,
undo/redo and cancellation.

`App.Tests --inspector-panels-check` covers exported Inspector metrics including
tiny droplets, base-SV normalization across BPM changes, immediate randomization edits,
undo, numeric validation, horizontal overflow and Song Setup category buttons.
The checks include mixed-mode selection, mode overrides, correction retention and
reset, and undo/redo. `Core.Tests --droplet-modes-check` checks mixed modes, cache
invalidation, schema 25/26 roundtrips, and v7/v14 NM/HR export.
Native docking checks capture Inspector and Droplet Settings at both sizes and
all three DPI values.


`App.Tests --pattern-notes-check` verifies nonmodal pattern conversion and editing,
selection changes, immediate edits and splitting, undo, independent notes input, and project and
workspace notes roundtrips. Native docking checks also capture these panels.

`App.Tests --docking-check` exercises cross-side moves, tab groups, cancellation,
live tab ordering, symmetric dock resizing, Layout and context-menu panel management,
and independent Mapping/Timing layouts in English and Chinese,
including settings-file roundtrips, restart restoration and invalid layout entries,
alongside the existing sidebar rendering and seeking checks.
`FruitsAtelier.App.exe --render-check --docking-render-check` runs the native
Direct2D gestures at two window sizes and three DPI values. Screenshots are
written under `artifacts/tests/docking-native`.

`FruitsAtelier.App.exe --render-check --dpi-render-check` checks window client
scaling at the 2520 × 1575 reference window, 3840 × 2160, 2560 × 1440,
1920 × 1080 and 1920 × 1200. The reference must retain 175% rendering. It injects
96/120/144/168/192/240 DPI notifications, compares native menu pixels at equal
client dimensions, and exercises button clicks, slider dragging, wheel input
and non-client frame sizing in English and Chinese. Captures and results are
written to `artifacts/tests/dpi-native`. These checks use real Direct2D rendering
with simulated DPI notifications; they do not change Windows display settings.

`App.Tests --object-snap-check` verifies full-beat search ranges across BPM changes
and independently of meter, bidirectional fruit and slider-start placement, exact X priority over Grid
and Distance Snap, undo, hover isolation, default preferences and Apply/Cancel.
Whole-object checks sweep horizontally in both directions with DS, object and
collinear candidates, with Grid Snap on/off, and verify ordering, export,
cancellation and one-step undo/redo.
On Windows, `FruitsAtelier.App.exe --render-check --object-snap-render-check`
checks native placement and configuration in English and Chinese at two window
sizes and three DPI settings, saving captures under `artifacts/tests/object-snap-native`.

`App.Tests --acknowledgements-check` verifies English-only consent, disabled
confirmation, cancellation, scrolling, startup blocking, persistent notice
revisions and failed saves, alongside first-time setup. On Windows,
`FruitsAtelier.App.exe --render-check --acknowledgements-render-check` captures
the unchecked, scrolled and checked dialog at three sizes and DPI settings under
`artifacts/tests/acknowledgements-native` without changing personal consent.
It also checks that Confirm presents the destination frame synchronously for
both setup and editor paths, including low-latency and fullscreen preferences.

`App.Tests --library-opening-check` verifies that opening a library project while
the workspace gate is held leaves rendering responsive, blocks duplicate opens
and competing operations, publishes once, retains the document on failure, and
retires results after a project replacement. It also checks direct difficulty-row
opening, exact difficulty selection for saved projects and source imports, and
selection after synchronization adds a new source difficulty.

`App.Tests --mapping-canvas-check` verifies zoom-independent background bounds,
independent dim drafts and persistence, image-drop replacement confirmation,
filename collisions, retained images, undo/redo and project/export references.
It also checks local maps after reopening, X-axis grid colour/opacity drafts,
reset and persistence, unchanged horizontal lines, the canvas dim control’s
fade timing, pointer capture, preference saving and zoom-independent bounds,
and settings layouts and testplay background controls. On Windows, run `FruitsAtelier.App.exe --render-check
--mapping-canvas-render-check` for native background caching, settings and
replacement-dialog screenshots under `artifacts/tests/mapping-canvas-native`.
The native check also verifies background pixels across repeated preview toggles
and window sizes, including image dimensions that expose crop-rounding errors.

`App.Tests --timeline-range-check` compares indexed marker visibility and banana
segment clipping with full scans, checks tied ordering, overlapping long objects,
snapshot replacement and draft tail updates, and runs selection, pattern and undo
regressions. It prints cold index setup and warm query CPU/allocation measurements
for a synthetic 100,000-marker timeline; these exclude rendering and audio latency.

`App.Tests --object-timeline-check` checks Stream/Stack fruit markers, combo
numbering, internal NC, grouped movement/deletion, persistence, export and undo.
On Windows, `FruitsAtelier.App.exe --render-check --object-timeline-render-check`
runs the focused native checks and writes screenshots and CPU/allocation samples
under `artifacts/tests/object-timeline-native` at 96, 144 and 192 DPI.

Test projects are console programs run with `dotnet run`. Format-export quantization and read-back diagnostic checks iterate over every available language, verifying messages and parameters against localization tables rather than assuming the default UI language. Repeat-count checks verify preserved edge samples and silent default assignment to new edges in every language.

After building the solution, run on Windows from the repository root:

```powershell
foreach ($suite in @('Core', 'Gameplay', 'App', 'Skinning', 'SkinArchive')) {
    dotnet run --no-build --project "tests/FruitsAtelier.$suite.Tests" -c Release
    if ($LASTEXITCODE -ne 0) { throw "$suite tests failed" }
}
dotnet run --no-build --project tests/FruitsAtelier.Formats.Tests -c Release -- --skip-external-fixtures
```

Run on Mac from the repository root:

```bash
bash scripts/Test-Mac.sh                      # Shared regressions and Mac input/audio checks
bash scripts/Test-Mac.sh --skip-device-tests  # Shared regressions, Mac key mapping and offline PCM; skips native device checks
bash scripts/Test-Mac.sh --native-only        # Mac input/audio checks only
```

[Desktop regression](../.github/workflows/desktop.yml) runs on pushes and PRs: Windows builds the solution and runs shared regressions; macOS runs shared regressions and packages the app. Results are available in [GitHub Actions](https://github.com/frankhjwx/FruitsAtelier/actions).

## Library scale benchmark

The opt-in `App.Tests --performance-scheduling` benchmark measures Timing frames
and Stream setup with synthetic data. `Formats.Tests --benchmark-sv-edits` measures
ordinary edits with an SV override and compares every export against uncached NM/HR
output. `App.Tests --sv-performance` measures SV/DPB draft input and Apply validation.
`App.Tests --sv-check` checks base SV compensation at Legacy heads, unused extreme
timing, FSlider generation, NM/HR playback, cache equivalence, persistence and undo.
Run the same fixtures and build configuration before and after an optimization;
capture console JSON in `artifacts/`. These are CPU/allocation measurements, not GPU
presentation or physical input latency.

Recovery compression can be measured against an existing project's history directory:

```powershell
dotnet run --no-build --project tests/FruitsAtelier.Core.Tests -c Release -- --benchmark-history <workspace/.sync-history/project-id>
```

The source remains read-only. The benchmark writes compressed payloads and its report
under `artifacts/history-compression`, verifies byte-for-byte decompression, and counts
every source file in the before/after totals. Identical payloads are measured once to
avoid repeating CPU work; their compressed sizes are counted for every occurrence.
This does not measure per-write latency or apply deduplication to actual backups.

Run the opt-in benchmark from the repository root after building:

```powershell
dotnet run --no-build --project tests/FruitsAtelier.App.Tests -c Release -- --benchmark-library
```

It creates 500,000 synthetic map records in distinct sets under `artifacts/library-scale/`, checks random SQL pages and the last set, and exercises 100 scrollbar jumps over 2,000 UI frames. `artifacts/library-scale/benchmark.json` records index time, page latency, UI timing, allocation and peak cached rows. Pass an existing fixture directory after `--benchmark-library` to reuse its database. The benchmark uses `RecordingCanvas`: it measures metadata/query/UI work, not GPU presentation, image decoding, real map scanning, or visible FPS. The native Windows `--render-check` separately checks asynchronous thumbnail decoding and drawing.

## External test resources

Run `dotnet run --project tests/FruitsAtelier.SkinArchive.Tests -c Release -- --import-archive <path.osk>`
to validate a user-owned skin archive and load its textures. The source archive is read-only;
the extracted cache stays under `artifacts/tests/skin-archive/external`.

To check an individual supported `.osu` without modifying it, run the Formats test executable with `--import-roundtrip <path>`. This verifies project persistence and v7/v14 export preserve raw object/timing lines and the converted Catch sequence, times and positions.

The repository contains synthetic format fixtures, older `.catchproj` compatibility fixtures, and OGG audio fixtures. These checks additionally require local resources:

- The two real-beatmap checks enabled by default in Formats need external beatmaps under `artifacts/beatmaps`. `--skip-external-fixtures` skips them and is used by CI and Mac scripts.
- Core's `--fixtures` option enables additional real-beatmap checks.
- A full Windows `Audio.Tests` run needs a default audio device and MP3 fixtures under `artifacts/beatmaps`. `--recovery-check` runs only injected output-failure checks; `--lifecycle-check` runs only playback lifecycle checks.
- Tests using a specific default skin or user project depend on uncommitted local files. Check test output for skipped cases.

`App.Tests --hitsound-check` checks sample resolution, preview scheduling and
stacked banana sounds in manual Testplay, autoplay and replay without device audio.

Automated device tests output silent PCM; sample comparisons happen before muting. Run Windows audio tests with:

```powershell
dotnet run --project tests/FruitsAtelier.Audio.Tests -c Release
```

Use `Audio.Tests --vorbis-check [path.ogg]` to check OGG decoding, playback and
paused/playing seeks. Without a path it uses the repository fixture; a supplied
project audio copy is read without modifying it.

`Audio.Tests --preroll-check` checks silent preparation PCM at multiple speeds and
channel counts, negative device positions, pause/resume and continuous playback
through zero using an injected output without opening an audio device.

`Audio.Tests --diagnostic-check` validates command/event correlation, repeated
pause positions with logging enabled, unsupported hitsound format identification,
issue markers without source-path disclosure, live report ZIP export excluding
map files, bounded queues and byte budgets, limited shutdown waits with stalled
writers, and continued loading when the diagnostic destination cannot be written.
It uses an injected output and does not play sound. To capture real-device lifecycle
checks, set `FRUITSATELIER_AUDIO_DIAGNOSTICS=1` before running `--lifecycle-check`.

`Audio.Tests --pause-check` checks pause/resume PCM alignment with a blocked
in-flight clock read, seek/pause ordering and rapid resume, plus real-device WAV
pause, seek and EOF replay. The paused position remains at the request snapshot
even when the device advances before the worker handles it.

`Audio.Tests --transition-check` verifies the pause/resume gain envelope across
sample rates, channels and partial-frame reads. An injected buffered output checks
that the device consumes the fade before stopping, paused seeks retain the resume
fade, and queued pause/play/seek requests preserve playback intent. No physical
audio device is used by this focused check.

## Window checks

On Windows, `FruitsAtelier.App.exe --render-check --audio-shutdown-check` checks
paused and playing window disposal with silent injected music and setup outputs.
Both outputs must be stopped and disposed while a pending library cache write is
still blocked, so background cleanup cannot extend playback after window closure.

`FruitsAtelier.App.exe --render-check --workspace-save-render-check` verifies the
once-per-project Songs export offer and repeated saves after reopening, in English
and Chinese at two window sizes. Its workspace fixtures stay under `artifacts/`.

`FruitsAtelier.App.exe --render-check --library-opening-render-check` holds the
workspace project gate while a library open is pending. It verifies native
loading frames, difficulty-row input, target selection and UI-thread publication in English and Chinese
at two window sizes and 96/144/192 DPI. High-star detail-list captures and fixtures stay under
`artifacts/tests/library-opening`.

`FruitsAtelier.App.exe --render-check --sample-banks-render-check` runs the focused
tool-palette checks in English and Chinese at both window sizes and 96/144/192 DPI.
It checks menu dismissal and unchanged document content, and captures the Sampleset
and Additions menus under `artifacts/tests/sample-banks`.

`FruitsAtelier.App.exe --screenshot-render-check` exercises F12 capture and reads
the native clipboard image back at 96/144/192 DPI in editing and paused testplay.
It verifies pixel dimensions, colour and row order, and writes bitmap previews and
`report.json` under `artifacts/tests/screenshot-native/`. This check replaces the
system clipboard with its captured image.

`FruitsAtelier.App.exe --testplay-render-check` independently exercises background
decoding, custom Skip and pause textures, mouse/Space skip, keyboard Retry/Back,
and both languages at wide, narrow and portrait sizes with 96/144/192 DPI.
It uses silent callbacks and writes `artifacts/tests/testplay-native/report.json`.
It also checks testplay shortcut dispatch before IME translation, process-key
press/release recovery, and 300 ms held retry while running and paused followed by
the 600 ms reaction countdown. Retry regressions verify frozen gameplay and delayed
audio restart until the same deadline used by Continue.
The shared App regressions additionally check dim transitions, frozen resume
countdowns, stale audio samples after skip, content isolation and preferences.
The Windows App retry PCM regression uses an injected silent output with the
real hitsound mixer to verify the pause loop during the hold and the retry click
after session audio reset.

The Windows `--render-check` injects nested timer and paint messages during audio
replacement, verifying that difficulty switches retain paused and playing
positions when the replacement audio becomes ready. This check uses a silent
fixture and an injected output without opening an audio device.

Audio diagnostic settings regressions cover deferred application, persistent
output profiles, scrolling at narrow sizes, issue markers, asynchronous export
and failure presentation, real-time marker key repeat handling, and unchanged map
content in English and Chinese.
Native Settings checks draw the Audio page throughout its scroll range.
`App.Tests --audio-settings-restart-native-check <FruitsAtelier.App.exe>` opens two
isolated silent native restore checks from serialized restart state, verifying
Audio settings reopening from Library and editor, retained scroll position, and
the editor’s active difficulty, paused time and speed. Settings tests verify the divider, deferred restart dispatch after
successful persistence, retained scrolling and no restart for unchanged options.

Settings preference regressions cover Audio sliders and the Appearance skin selector from both the Library and editor, shared values with the original controls, immediate persistence, menu dismissal, and unchanged beatmap content in English and Chinese. The Windows `--render-check` also exercises the Settings sliders and skin menu at its tested sizes and DPI values.

Update restart checks verify that Settings closes before the host prepares saving,
from both the Library and editor. Native rendering checks exercise the restart
button in both languages without installing an update or exiting the test process.

Shortcut routing regressions verify that Timing cannot move hidden Compose
selections, unsupported modifiers cannot delete timing rows or invoke base
transport commands, and export remains available outside Timing fields and modal
dialogs. Language dropdown checks cover arrow navigation, confirmation, Escape,
background shortcut isolation and unchanged document history in both languages.

Timing regressions cover draft cancellation, atomic apply/undo, clipboard session
isolation, red/green control points, reset/replacement, and FSlider time transforms.
Metronome tests compare scheduled timestamps for whole beats, Ctrl subdivisions,
pause and seek. The native render check draws the Timing page and all setup tabs
in English and Chinese at each tested window size and DPI.

The Windows `--render-check` injects nested paint/timer messages, nested native-modal
scopes, an abandoned drawing batch, and a real Direct2D wrong-state failure. It
checks that the renderer recovers, the error can be dismissed, and map content is
preserved. The intentional Direct2D exception is followed by a successful paint
lifecycle entry in the diagnostic log.

It also advances a fake update backend through checking, availability, download
progress, restart readiness, current-version and failure states. Checks cover both
paint polling and posted status notifications from an idle window with no pending
paint region. Notifications must invalidate the window and render the new status
without timer messages, input, resizing or minimizing.

The Windows `--render-check` also exercises testplay entry, movement, combo drawing,
return, catcher mirroring and binding settings at both window sizes and all tested
DPI values in English and Chinese. Testplay checks use silent callbacks. Shared App
tests cover key repeat/release, focus cancellation, end conditions, judging between
frames, custom bindings, and document isolation. Extended binding checks cover capture,
key labels, settings reload, movement/dash press and release, and reserved keys;
Mac key mappings also run without an audio device. `Audio.Tests --hitsound-check`
checks timestamped samples against music frames and preserves late catch attacks
without opening an audio device.

The App tests use an injected monotonic clock to check subframe taps, reversals,
dash changes, repeated audio snapshots, timestamp interpolation, device stalls,
and miss positions/opacity. Combo checks cover idle and miss fades, the delayed
main digit change, and removal of instruction text. Live trail checks compare
coarse and fine updates and preserve facing across reversals. Skin tests verify
combo prefixes, overlap, density and archive extraction. The Windows report also records nonblocking testplay
submission times and a queued Win32 key interrupting the frame wait. These hidden
window measurements do not measure physical keyboard-to-display latency.
The native report additionally measures synthetic messages delivered to the dedicated
testplay input thread while the owner UI thread is blocked. It checks that held
movement, release and catch callbacks continue without UI message processing, and
reports median, P95 and maximum queue-to-processing time. App tests compare shared
plate trajectories, retained snapshots, caught-only stacks and final effect expiry.
The same native check compares the 1000 Hz timer-only baseline with the 2000 Hz
bounded-tail worker, reporting actual median and P95 update intervals separately
from message dispatch latency. These rates do not measure hardware input or scanout.
Autoplay regressions cover Tab repeat/release, live judgement and sounds, return to
manual movement, and session reset. Held-key regressions cover movement and dash
at testplay entry, pause and Continue countdown, releases during audio restart,
focus loss, custom bindings and native startup key sampling with shortcut filtering.
Visual tests cover deterministic rotation,
banana arrival transforms, static editor sprites, combo palette offsets, and the
base-only additive hyperdash layer with independent overlay crop and rotation.

```bash
./Run-Editor-Mac.command --smoke-check
```

This opens a Mac window, checks fruit placement, undo, project round trips, both slider editing modes, and English/Chinese screenshots, then exits. Slider screenshots also cover narrow and scrolled properties panels. Screenshots go to `artifacts/macos-check` and logs to `artifacts/logs/macos.log`.

Shared Core and App regressions cover exact control-curve persistence, fixed AR reference ratios, bidirectional editing, local pen conversion and undo, control insertion/deletion, segment boundaries, global mode switching, and repeated-slider export. Switching editing modes must leave the document and history unchanged.

`Core.Tests --compact-arc-check` checks anchor counts and fixed-time trajectory
error against analytic circles across radii, arc spans, reference scales and both
directions, plus project round trips, endpoint identity, export and undo/redo.
`App.Tests --curve-edit-check` verifies local handle editing, unchanged mode
switches, control insertion/deletion and undo through editor input.

After changing input or drawing, manually check affected operations, language switching, window resizing, and file dialogs. Additional coverage is still needed for physical Windows window/audio behavior, Intel Mac, cross-display DPI, Mac MP3, and stable-client comparisons.

## Editing performance benchmark

`App.Tests --cache-lifetime-check` verifies that closing after both canvas modes
and clipboard use releases the document, conversion and exported read-back.
`App.Tests --project-memory-performance <folder>` opens all sibling `.osu`
difficulties, waits for ratings and measures memory before and after returning
to the library. Its final forced collection distinguishes live data from garbage.
`FruitsAtelier.App.exe --render-check --project-memory-folder=<folder>` repeats
opening and closing twice in a native Direct2D window with silent audio decoding.
Each round visits both canvases and renders ten seconds of simulated playback.
It reports working set, private memory, managed heap and process peak in
`artifacts/tests/project-memory/native.txt`. Stable measurements use the editor's
normal lifecycle collection; process peak includes temporary loading allocations.

`App.Tests --difficulty-switch-performance <path.osz>` opens the set three times
and switches through every difficulty forwards and backwards, entering and leaving
testplay each time. It records editor switching, testplay frame CPU, allocations,
Gen2 collections and retained heap. Windows additionally loads every audio file
through the real transport with silent PCM output and reports load latency and
process private memory. Gameplay uses an accelerated clock and `RecordingCanvas`;
these measurements exclude GPU presentation and physical input latency. Imported
fixtures and logs belong under `artifacts/`.

On Windows, `FruitsAtelier.App.exe --difficulty-switch-render-check <path.osz>`
repeats the same difficulty traversal with Direct2D and silent WASAPI music.
Each testplay starts within the map and samples 30 submitted frames. It also
exercises SR sorting and verifies unchanged content. Results are written to
`artifacts/difficulty-switch/native.json`. This hidden-window check uses the UI
simulation driver; it excludes physical display latency and dedicated input-thread
dispatch, which are covered separately by the native testplay checks.

Follow the [editing performance constraints](EDITING_PERFORMANCE.md) when changing
interactive paths. For a read-only benchmark of an existing `.osu` map, run the App
test executable with `--fruit-placement-performance <path.osu-or-catchdiff>`. It measures warm
conversion/export, fruit hover and repeated same-time replacement, including pointer
dispatch, rendering and per-operation allocations, plus Fruit/FSlider hover during
playback with a stationary pointer, an active slider draft, and Grid Snap on/off.
It reports export sequence validity to identify
fallback preview work. Edits stay in memory. This uses
`RecordingCanvas` and does not measure physical display latency.

`--testplay-edit-performance <path.osu-or-catchdiff>` compares twenty seconds of
testplay simulation before and after 600 in-memory fruit edits with undo history
retained. It uses an injected clock, silent callbacks and a counting canvas, and
reports CPU time, allocations, heap size and Gen2 collections. This accelerates the
edit workload; it does not reproduce wall-clock aging, device scheduling or GPU load.

Windows builds automatically aggregate UI performance into `editor.log`. Every
five seconds with processed window messages, an interval containing a sample of
at least 16 ms is written; fast-only intervals are discarded. Shutdown flushes the
remaining interval. Counters use fixed storage and do not write per input or frame.
Logs live in `artifacts/logs` for repository builds and
`%LOCALAPPDATA%/FruitsAtelier/logs` for distributed builds.

`UI performance` reports count, average, maximum, and count at or above 16 ms for
input queue age, input dispatch (including title updates), audio/update polling,
frame preparation, editor rendering, and combined `Submit` submission. Separate
`EndDraw` and `Present` counters distinguish drawing completion from presentation
waiting; both are nested within `Submit`. Conversion
snapshot comparison, rebuilding, and export/read-back are measured separately;
these are nested within editor rendering, and export is nested within rebuilding,
so their durations must not be added together. `InputToSubmit` measures the oldest
pending input's queue age plus time through the next completed submission. It is
an application-side latency estimate, not physical input-to-display latency.
Queue ages use the coarse Win32 message clock; coalesced mouse messages and modal
dialogs can affect these measurements. Native modal message loops are not sampled
by the main input-dispatch counter.

Each report includes the slowest dispatched input message ID, process-wide GC
collection deltas, and the editor state/object counts **at report time**. Startup
records the application/runtime/OS version and process ID. Request this log along
with the reproduction time and map when investigating stalls. Playback's five-second
average render rate alone cannot diagnose individual editing stalls. Shared
conversion counters are also available to tests; macOS does not enable or persist
these Windows host diagnostics.

Windows stall diagnostics also report `MessageDispatch` for every retrieved message,
including timer and paint handling, and the slowest message ID with testplay state
at dispatch start. `MessageWait` measures message retrieval while playback/testplay
is active, including sent messages processed inside retrieval. `FrameWait` measures
the DXGI frame-or-input wait. `FrameGap` measures intervals between paint attempts
while both endpoints are active; `frameGapTestplay` gives the testplay state at each
endpoint of the longest gap. Idle endpoints, minimized windows and native modal
scopes are excluded from this gap counter. Focus changes, suspension and scheduling
can still produce gaps, so a gap alone does not establish synchronous application work.

`LogWrite` measures synchronous application-log writes on the UI thread, including
failed writes. Background writes do not update UI counters. A performance summary's
own write appears in the following interval. Message dispatch contains the nested
paint stages, and frame gaps span drawing, waits and other UI work;
these counters must not be added together. They use the existing five-second aggregate
logging and fixed sample storage, without per-message or per-frame log writes.

Run the shared App test executable with `--benchmark-editing` to measure adding and
continuously dragging objects in synthetic maps of 1,000 fruits, 10,000 fruits,
and 1,000 FSliders. On macOS, from the repository root:

```bash
bash -c 'source scripts/macos-dotnet.sh; "$FA_DOTNET" run --project ../macOS/tests/App -c Release -- --benchmark-editing'
```

The report separates pointer handling, conversion, and drawing, and includes
current-thread allocations and submitted drawing commands. It uses a command-counting
canvas, so these are CPU measurements, not native rendering or end-to-end FPS.
Performance results depend on hardware and runtime warm-up; compare the same fixture
and environment. Functional tests compare cached conversion against full conversion
after edits to geometry, timing, repeats, source ordering, and RNG-consuming objects.

For slider endpoint dragging, run the App test executable with
`--slider-drag-performance <path.osu>`. This read-only benchmark alternates horizontal
movement of an imported slider's head and tail, reporting pointer-path CPU time,
counting-canvas rendering time, and current-thread allocations separately. It invokes
the selected-object drag path directly and excludes native input dispatch and GPU work.

The App test executable accepts `--slider-draft-performance <path.catchdiff>` for
new FSlider previews in Legacy and Pen modes. Use an isolated copy of the project.
It measures pointer dispatch, rendering and allocations for horizontal movement
and changing endpoint times, checks cancellation, and prints a digest of all
validated export text and NM/HR events for comparisons between builds.

The App test executable also accepts `--anchor-drag-performance <path.catchdiff>`
for a read-only benchmark of anchor pointer handling plus counting-canvas rendering.
It reports median/P95 CPU time after warm-up and verifies cancellation restores content.
The Core test executable accepts `--preserve-slider-positions <path.osu|path.catchdiff>`
to verify conversion with derandomization disabled, checking full-map event positions,
FSlider path alignment, project persistence, and export. Incompatible sliders remain
Legacy and are reported. Neither command saves the supplied map.

## Base SV validation benchmark

Run `FruitsAtelier.App.exe --render-check --sv-ds-render-check` for focused native
checks of the Timing **Adjust** entry and DS **SV / DPB** buttons. It checks both
languages, window sizes and 96/144/192 DPI, including Apply and undo, and captures
each unit mode under `artifacts/tests/sv-ds-native`.

Run `dotnet run --no-build --project tests/FruitsAtelier.App.Tests -c Release -- --sv-performance`
to measure SV/DPB draft input and Apply validation on 100 and 1,000 imported
Bezier sliders with repeats and BPM changes. Eight draft adjustments arrive
100 ms apart. The report separates input plus painting during the burst from
background validation after Apply, and records current-thread and process-wide
allocations. Regressions verify unit switching, SV limits, conflict loading,
playback preservation, cancellation, stale-result rejection and atomic undo.
The counting canvas measures CPU work; GPU presentation and physical display
latency are excluded.

## Playback rendering profile

`App.Tests --new-difficulty-profile <workspace-project-directory> <difficulty-name>`
reports difficulty copying, hitsound preload, first-frame rendering and export-plan
preparation time and calling-thread allocations. It reads the persisted project
without saving or exporting it. On Windows it also measures audio retirement after
silent playback and pause on an STA thread; it does not change system volume.
The regular UI performance log includes `DifficultyAdd`, `HitsoundPreload`,
`AudioReset`, `WorkspaceExportPlan`, `WorkspaceExportSave` and `WorkspaceExportReload` to distinguish these
stages when a native creation interaction stalls. Background worker allocation is
not included in the calling-thread allocation values.

For a read-only CPU profile of an existing `.osu` or `.catchdiff` file, run the App test executable with `--map-performance <path> [startMs]`. It measures playback from the supplied time (default 89038 ms) at 32% Zoom and 1/16 Snap, reporting render median/p95, allocation per frame, rendering phases, and transport/hitsound scheduling with silent callbacks. The counting canvas excludes GPU and device submission.

`App.Tests --independent-scale-check` also checks allocation-free dense coordinate
projection after resizing and seeking. On Windows,
`FruitsAtelier.App.exe --render-check --text-resources-render-check` checks bounded
native text resources during animation and layout reuse for dense labels.

On Windows, run `FruitsAtelier.App.exe --profile-map <path> [startMs]` in a hidden Direct2D window with the active default skin. It opens an in-memory workspace session for the selected `.osu` or `.catchdiff` and sibling difficulties, including periodic resource checks, then samples 3,600 warmed frames per case from the supplied time (default 70000 ms) at 100% zoom and 1/4 snap with preview closed, NM, HR, and paused fruit hover. Playback advances thirty seconds per case. The report separates drawing submission, total rendering including `EndDraw`/`Present`, and silent transport updates. Per-frame samples include allocations, GC collections, and image decode counts to identify isolated stalls. It also measures document comparison time. The report is written to `artifacts/logs/playback-profile.json` in repository builds, or the application log directory in distributed builds. This is not a visible-screen FPS measurement. Neither profile saves or imports the map into the workspace, plays audio, or changes system volume.

## Imported slider corpus

`Core.Tests --slider-recovery-check` covers compensated export/import round trips
for straight lines, quadratic/cubic Beziers with nonlinear time controls, circular
arcs, BPM changes, repeat counts, different tick rates and sharp droplet reversals.
It checks whole-path samples against original authoring, NM/HR export positions
within 4 pixels, unchanged event kinds/times, cancellation, undo, persistence and
safe rejection of an incompatible baseline.

For a read-only authored-map round trip, run:

```powershell
dotnet run --project tests/FruitsAtelier.Core.Tests -c Release -- --slider-roundtrip "path/to/difficulty.catchdiff" "artifacts/slider-roundtrip/sample"
```

This exports the source to `.osu`, explicitly treats it as a confirmed compensated
import, and converts it to FSliders. It reports the independent detection ratio;
it does not simulate an automatic source-confirmation decision. Stream/Stack
tracks are excluded from the slider restoration denominator. A restored slider
uses no more anchors than its original and stays within 4 pixels at 2,001
normalized first-span times. The report also lists control counts, retained Legacy
objects, allocation, runtime and actual NM/HR playback errors. Reports, exported
files and recovered documents go only to the supplied output directory. The source
file hash and in-memory authoring content must remain unchanged.

The Core test executable accepts `--slider-corpus <workspace>` for opt-in, read-only
checks against existing `.catchdiff` documents. It never saves or opens a workspace
recovery session. The report compares fitted anchor counts with the previous 0.001
linear simplification, measures sampled error on the longest converted sliders, and
requires complete conversion and checks start time, duration, and repeats for every source slider. External map data is not required by CI or committed.

```bash
bash -c 'source scripts/macos-dotnet.sh; "$FA_DOTNET" run --project ../tests/FruitsAtelier.Core.Tests -c Release -- --slider-corpus "/path/to/workspace"'
```

Synthetic regressions cover long smooth curves, sharp reversals, boundaries, repeated
sliders, random smooth inputs, cancellation, first-import prompts, current-difficulty
menu scope, per-difficulty undo, and stale asynchronous results. The macOS smoke check
captures both localized import prompts and the Edit menu in the native renderer.

## Hitsound scheduling profile

Run the muted native profile to compare the time spent submitting 32 simultaneous hits
per UI tick over 120 ticks. It reports p50/p95/max submission time, decoded PCM memory,
and any sample loads or engine creations during playback. This measures the audio dispatch
path, not end-to-end editor FPS or acoustic latency.

```bash
bash -c 'source scripts/macos-dotnet.sh; "$FA_DOTNET" run --project ../tests/FruitsAtelier.Mac.Tests -c Release -- --profile-hitsounds'
```

Pass `--profile-map /path/to/difficulty.catchdiff` to the same Mac test project for a
read-only whole-map comparison with hitsounds disabled and enabled. It reports sample
preload time/memory, event density, transport and drawing-command CPU quantiles. It does
not open/recover a workspace session or save user data, and excludes native GPU rendering.

Use `--check-resume-music /path/to/music.mp3` with the Mac native test project to check an
existing music file against muted WAV hitsounds. This read-only check exercises repeated
pause/resume with different pause lengths, seek, and cancellation during the startup lead.
The music fixture must be at least three seconds long. Tests compare the actual music
position and native PCM render timestamps; all device output stays muted.

## Legacy sample boundary comparison

Run the App test executable with `--legacy-map <difficulty.osu>` for a read-only
report of sample-bank/index/volume boundary corrections during the first 40 seconds.
Synthetic App regressions cover the inclusive 5 ms edge boundary, the slider body's
6 ms start lookup, tick inheritance across timing changes, mapset clipboard
scope, combo reference numbers, horizontal grids, and timestamp precision.

`App.Tests --cross-difficulty-clipboard-check` checks mixed parent types across
different AR/CS/STR, BPM and base SV, preserved Legacy duration and geometry,
unchanged target settings and source content, undo/redo, unsupported SV rollback,
clipboard reuse and project-session boundaries. Existing same-difficulty and
overflow clipboard checks run alongside these cases.

The feedback regressions cover V/End navigation, scroll direction, persistent independent volume controls, skin sample precedence and cache upgrades, and compatible versus conflicting close SV timing. Windows PCM checks verify live and scheduled hitsound gain separately from song gain; headless Mac CI exercises the native mixer with no audio device. Native window checks exercise all three volume sliders in both languages.

`App.Tests --song-dock-check` covers docked Song Setup, narrow-panel scrolling,
field focus, left docking, immediate edits, undo and retained Timing layouts, together
with the existing metadata and randomization checks. Native docking captures
include every Song Setup category and its scrolled controls.

Song Setup regressions cover shared metadata, independent difficulty settings,
romanised fields, modal isolation, cancellation, undo/redo, palette HEX input and
project/`.osu` persistence. Native window checks open all four tabs in both
languages at each supported test size and DPI and exercise the color picker.

## Synchronization regression coverage

`Formats.Tests --reference-discovery-check` verifies missing and relocated files,
normalized first-object filtering, complete-sequence rejection, later online IDs,
empty maps, cancellation and association ownership. `App.Tests
--synchronization-wait-check` also checks immediate navigation away from a read-only
search while save and publication waits remain in effect.

`App.Tests --synchronization-wait-check` checks that ordinary input and repeated
save shortcuts do not open waiting overlays, local publication still permits note
placement, and file/navigation waits resume on completion. Windows
`FruitsAtelier.App.exe --render-check --synchronization-wait-render-check` checks
the input and navigation behavior in English and Chinese at two sizes and
96/144/192 DPI.

Formats regressions exercise metadata and directory/file renames, uploaded IDs,
three-way field conflicts, timing updates, pending local choices, external deletion,
unavailable roots, truncated files, numeric spelling, identical copies, audio
replacement and recovery, duplicate ownership, unique-difficulty migration, baseline
persistence, emitted object mappings, multi-output curves, and interrupted exports.
Fixtures and retained recovery files live under `artifacts/tests/synchronization`.

Save-rewrite regressions cover fractional object times, implicit combo boundaries,
timing precision, and relocated break lines alongside storyboard commands. They
also verify real time, combo colour, SV, break and video edits remain reviewable,
and accepting a setting change preserves authoring precision and event text.

Timing synchronization regressions use editable curves whose exports add or
replace inherited SV points. They cover isolated green additions, edits and
deletions, sample edits at generated overrides, and revisiting retained choices
after restart. Shared UI checks verify one changed green produces one highlighted
row and preserves editable curves when applied in both languages and window sizes.
Native rendering checks also exercise the compact timing review.

Shared App tests cover asynchronous discovery, context rebasing through undo,
missing-file input isolation, explicit restoration, per-object choices, linked
deletion, and local-project exemptions in both languages and window sizes. Native
Windows `--render-check` draws conflict, missing and deletion states in English
and Chinese at each tested size and DPI without playing fixture audio.

Visual merge checks cover baseline-free field/object comparisons, sequential choices,
revisiting decisions, aligned highlights on both canvases, synchronized scrolling and
zoom, newer-save timestamps, and asynchronous result previews without content edits.
Native rendering checks include the baseline-free comparison and canvas navigation.

AR regressions compare note spacing with the editor's timing scale at AR 1, 5 and 9
and both window widths. Resolution-history checks cover repeated time moves across
anchors, ignored additions, older baseline migration, red/amber/green interval states,
clicking completed intervals and changing prior choices before applying the review.

File-monitor checks exercise notification coalescing, bounded overflow recovery,
native create/rename/delete events, archive/index exclusions and disposal. Editor
checks cover automatic metadata synchronization, directory identity recovery,
new difficulties, inactive conflict gating, pointer-capture deferral and external
changes during a pending merge. Apply must revalidate rather than commit a stale choice.

Project-library notification regressions add and remove external difficulties while
the My Maps view stays open, checking both card counts and detail rows. They
also verify muted retained FA entries, background filename/directory rediscovery,
duplicate-free listings and unchanged authoring/baseline files during discovery.

The focused `--testplay-render-check` also writes `background-cache.json` under
`artifacts/tests/testplay-native`. It compares the shared scene cache and resident
background path with a generated 4096×4096 image, recording warm frame time,
allocations and repeat decodes. The resident path must perform no warm decodes.

Version history regressions cover changed/unchanged saves, retained working copies,
deleted difficulty identities, project isolation, damaged snapshots, modal input,
selective restoration, save/restart and undo/redo. Native `--render-check` draws the
browser and restores a deleted difficulty in both languages at its tested window
sizes and DPI values. The editing benchmark verifies ordinary interactions while
version-history work remains outside the pointer and paint paths.

## Experimental lazer editing

`App.Tests --break-preview-check` compares placement movement overlays with
committed Break reconciliation and checks cancellation and undo in both modes.
`App.Tests --movement-placement-performance <path.osu>` runs the placement
benchmark with Movement Analysis enabled, including projected Break filtering.

`App.Tests --distance-snap-check` exercises the shared Mapping/lazer configuration
layout, base velocity edits and undo, preset persistence, and interactive preview.

`App.Tests --lazer-editing-check` covers the experimental mode and shared hit
lighting settings, including missing-field defaults, draft cancellation,
persistence, mode switching, retained Mapping preferences and content isolation.
The focused input, canvas and presentation regressions exercise the supported
lazer bindings, seek/scroll transitions, catcher drawing, caught-object removal,
movement guides and lighting boundaries. Lazer DS regressions cover global speed
persistence, fixed presets, preview interactions and map-dependent movement
colours with blended transitions. Core prefix-context regressions compare the
shared candidate evaluator with an independent full-sequence reference.

On Windows, run `FruitsAtelier.App.exe --render-check --lazer-editing-render-check`
for native Direct2D settings and canvas checks. The harness switches modes through
the settings controls, checks retained playback-line preferences and document
content, and captures canvas, preview, hit-lighting choices and Distance Snap
guides in English and Chinese at two logical window sizes and 96/144/192 DPI.
Images are written under `artifacts/tests/lazer-editing-native`. These checks
exercise the native renderer; they do not measure physical display latency or
audio-device timing.

For an optional read-only comparison on a representative `.osu` or `.catchdiff`,
run `App.Tests --lazer-map-performance <path>` alongside the existing
`--map-performance <path>` command. It enables lazer editing for the same
counting-canvas playback profile, reporting CPU time and allocations without
native GPU submission or physical display-latency measurements. The lazer profile
also measures visible guides with the maximum eight custom speeds after warming
their movement context cache.

## Banana shower patterns

`App.Tests --experimental-snap-check` checks the optional 1/24, 1/32 and 1/48
settings, cancellation/persistence, Ctrl+wheel families, Stream/Stack conversion,
Banana Shower fractions, export, project reopening and undo. On Windows,
`FruitsAtelier.App.exe --render-check --experimental-snap-render-check` captures
the settings and extended snap bar in English and Chinese at two window sizes
and three DPI settings under `artifacts/tests/experimental-snap-native`.

`Formats.Tests --banana-pattern-check` checks fraction boundaries, red-line
changes, grouped identities, persistence, undo, cache equivalence, exported NM/HR
randomness and base-SV changes with overlapping random parents.
`App.Tests --banana-edit-check` checks Ctrl+F, the full-map preview, draft
validation/cancellation, grouped range dragging, clipboard and undo.
On Windows, `FruitsAtelier.App.exe --render-check --banana-edit-render-check`
checks the native dialog, division lists and grouped canvas in English and Chinese
at three DPI settings and two window sizes. Captures and CPU/allocation samples
are written under `artifacts/tests/banana-edit/native`.

## Independent viewport scaling

`App.Tests --timeline-layout-check` checks both timelines' 50–100% height limits,
saved view preferences, cancellation, minimum-height selection and seeking,
transport alignment and the static playback-speed label with its expanded
speed buttons. `FruitsAtelier.App.exe --render-check --timeline-layout-render-check`
exercises resizing and speed selection at every UI-scale step in English and
Chinese at two client resolutions. Captures, including minimum heights, are
stored under `artifacts/tests/timeline-layout-native`.

`App.Tests --ui-scale-check` checks scale shortcuts with slider selections and persistence, selection-colour draft cancellation and HEX input on both canvases,
the 150% default, supported 25% steps,
settings normalization, draft cancellation, persistence, layout and content isolation.
`FruitsAtelier.App.exe --render-check --ui-scale-render-check` exercises every
UI-scale step in English and Chinese at two client resolutions, including native
settings selection, drawing, clicks, dragging and wheel input. Captures are stored
under `artifacts/tests/ui-scale-native`.

`App.Tests --independent-scale-check` checks the locked zoom behavior, independent
inline X/Y layout and limits, the 100% object-size and hit-region cap, smaller
minimum width, horizontal pan bounds, persisted preferences,
fruit dragging and undo, and experimental upper-timeline limits.
`FruitsAtelier.App.exe --render-check --canvas-scale-render-check` captures the
lock, X/Y controls, 200% scale, horizontal pan and dense 1/48 timeline in English
and Chinese at two sizes and three DPI values under
`artifacts/tests/canvas-scale-native`.

`App.Tests --conversion-docks-check` verifies direct conversion shortcuts, object action menus, live editing and merge undo. It compares right-click, long-press, Shift+F and timeline menus for fruits, FSliders, Legacy Sliders, streams, stacks, banana showers and multi-object selections, including enabled states, shortcuts, conversion reuse and delete/undo. `FruitsAtelier.App.exe --render-check --conversion-docks-render-check` captures the English/Chinese layouts and right-click/long-press menus at narrow and standard dock widths under `artifacts/tests/conversion-docks`.

`App.Tests --difficulty-tabs-check` verifies initial SR sorting, manual tab ordering,
pin/unpin, group boundaries, cancellation, rating refresh, keyboard navigation,
new difficulties and overflow in English and Chinese.
`FruitsAtelier.App.exe --render-check --difficulty-tabs-render-check` checks native
dragging and pinned tabs at two sizes and three DPI values in both languages,
with captures under `artifacts/tests/difficulty-tabs-native`.

`App.Tests --independent-sv-check` checks Legacy Inspector SV editing, undo/redo,
Shift duration locking and unchanged authoring timing. `Formats.Tests
--independent-sv-check` checks v7/v14 independent SV import, movement, export,
project persistence and compatible/incompatible coincident FSlider heads. Native
`--render-check --osu-canvas-render-check --independent-sv-render-check` exercises
Inspector input and Shift dragging in English/Chinese at both window sizes and
96/120/144/168/192 DPI, including compact corner controls, slider and button
input, and Catch coordinate readouts. `Formats.Tests --sv-timing-sync-check` checks timing/SV sync.

## Slider endpoint snapping

`App.Tests --slider-endpoint-snap-check` checks per-slider Inspector settings,
independent head/tail dragging, repeated Legacy slider length, undo/redo,
project persistence, representation conversion, timing boundaries and export.
`App.Tests --slider-endpoint-snap-performance` compares cold and warm endpoint
pointer dispatch and painting with global and independent snapping on the same
mixed fixture, reporting CPU time and per-thread allocation separately.
`App.Tests --experimental-snap-check` checks the global division control and
Stream/Stack conversion with experimental divisions enabled.
`FruitsAtelier.App.exe --render-check --slider-endpoint-snap-render-check`
checks Inspector input and endpoint menus in English and Chinese at two window
sizes and three DPI settings. Captures are stored under
`artifacts/tests/slider-endpoint-snap/native`.

`App.Tests --inspector-panels-check` also checks section collapse/expand, content
and conversion isolation, selection changes, actual endpoint fractions and NC
label separation in English and Chinese at narrow and standard widths. Native
`--slider-endpoint-snap-render-check` captures expanded/collapsed sections and
Catch endpoint labels alongside NC.

`App.Tests --same-time-placement-check` checks optional same-time placement in both canvases,
Catch slider replacement across X coordinates and the 2 ms boundary, draft cancellation,
circle retention when dragging an existing slider's head or moving the whole slider,
native circle/slider/spinner replacement, preview/export equivalence, project
roundtrips, undo/redo and Experimental setting
persistence and cancellation. Native `--render-check --experimental-snap-render-check`
also exercises its toggle in English and Chinese at compact and standard sizes.

## osu! collection browsing

`App.Tests --osu-collections-check` verifies official legacy database layouts before
and after the 2014, 2019 and 2025 format changes, exact difficulty subsets, changed
source hashes with retained osu! database identities, associated FA projects,
read-only membership controls, missing/malformed databases, background refresh,
and repeated browser retirement during an ordering transfer. The Library organization
check includes this suite. Native `--library-organization-render-check` also captures
the separate Collections sections and filtered osu! difficulties in both languages,
two sizes and three DPI values.

For an optional read-only report against an existing installation, run
`App.Tests --osu-collections-read <osu-root>`. It reports collection member paths
and difficulty names without importing projects or writing to the installation.
