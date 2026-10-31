# Architecture

## Platforms and dependencies

The application uses C# 12 / .NET 10. Windows and macOS share the editor, data model, and file formats, while providing separate windowing, drawing, and audio implementations.

| Layer | Windows | macOS |
| --- | --- | --- |
| Entry point | `FruitsAtelier.App`, `net10.0-windows` | `FruitsAtelier.Mac`, `net10.0` |
| Window and input | Win32, DPI messages, native file dialogs | Avalonia desktop window and file picker |
| Drawing | DX11 / DXGI, Direct2D / DirectWrite, Vortice 3.6.2 | Avalonia 11.3.7, `MacCanvas` implements `ICanvas` |
| Embedded website | WebView2 on a dedicated STA loop, child HWND | WKWebView hosted by Avalonia NativeControlHost |
| PNG | Windows Imaging Component | Avalonia bitmaps |
| Audio | NAudio shared-mode WASAPI; Media Foundation / NVorbis / WAV reader | AVAudioEngine music with AVAudioUnitTimePitch; separate PCM hitsound mixer; NVorbis for OGG |

See [Building and Testing](TESTING.md) for SDK selection and build commands, and [Third-party notices](../THIRD_PARTY_NOTICES.md) for package versions and licenses.

## Embedded osu! browser

`EditorView.Browser` owns the dock toolbar, copy feedback, viewport and timestamp
routing. Platform hosts own website navigation and session storage. The shared
renderer draws all toolbar controls outside the native webpage rectangle. Active
editor overlays suppress that rectangle instead of competing with native child
views for drawing order. Website URLs and navigation state cross into the editor;
HTML, cookies and authentication tokens do not.

Windows uses Microsoft.Web.WebView2 1.0.4258.31 and an installed Evergreen WebView2
Runtime. Its dedicated profile is `%LOCALAPPDATA%/FruitsAtelier/Browser/osu`.
Browser shutdown disposes views and waits briefly for the browser process to finish
writing profile data. macOS uses the persistent `WKWebsiteDataStore.defaultDataStore`
owned by the FA application identity, separate from Safari. The AppKit/WebKit bridge
is compiled alongside the native audio bridge in `libFruitsAtelierAudio.dylib`.
Both hosts keep the embedded view alive while its tab is hidden. Public discussion
annotations continue using the anonymous snapshot service, independently of login.

## Source layout

Both renderers keep the bookmark toolbar texture in its own resident cache slot,
outside the 64 MiB scene-image cache. Scene images are evicted individually by
least recent use. Cache slots are released with the renderer and replaced when
their source image version changes.

| Directory | Responsibility |
| --- | --- |
| `src/FruitsAtelier.Core/Model` | Documents, FSliders, imported objects, and timing |
| `Core/Formats` | `.osu` reading/writing, project JSON, and atomic saves |
| `Core/Editing`, `Core/Curves`, `Core/Timing` | Transactions, undo, time coordinates, curve evaluation, and beat snapping |
| `Core/Conversion`, `Core/Gameplay` | Path generation, Catch events, RNG, sizing, and hyperdash |
| `Core/Localization` | Language tables, formatting, and validation |
| `src/FruitsAtelier.App/Editor` | Shared layout, input, selection, clipboard, and conversion cache |
| `App/Rendering`, `App/Platform`, `App/Audio` | Windows host and resource management |
| `App/Skinning` | Shared skin mapping, sizing, and cropping |
| `src/FruitsAtelier.Mac` | Mac window, canvas, audio, and native audio bridge |
| `tests` | Console test projects |
| `macOS/tests` | Cross-platform project entry points for shared App / Skinning / SkinArchive tests |

`Core/` and `App/` abbreviate their respective source project directories. The Mac project links `EditorView`, `ICanvas`, skinning, and beatmap archive source files, and references the Core project. Core does not reference window or graphics-device types.

## Video and storyboard playback

`Core/Storyboards` derives immutable sprites, commands, animation frames, samples and
video references from retained `.osu` sections and the shared mapset `.osb`.
Commands use binary searches per property; loops calculate their iteration from
the requested timestamp. A balanced index over start-sorted sprites stores each
subtree's maximum end time, so persistent backgrounds do not retain expired sprites
in evaluation or preload queries. Traversal preserves start order; drawing sorts
by layer and source order without boxing enum values.
Evaluation has no forward playback cursor and supports alternating distant seeks.
Uniform and vector scale remain separate, multiplicative properties, matching osu!.
All image paths resolve within the mapset; source event text remains unchanged.
Shared `.osb` files are limited to 32 MiB before decoding, with a byte-count check
during reading to reject files that grow past the limit.
Playback, AiMod resource analysis and hitsound cleanup use this same admission
limit. AiMod also shares its expanded-output budget across all analyzed storyboard
files while keeping each file's variable definitions separate.
Variable keys and values retain their whitespace, including command indentation.
Independent single-character macros use a lookup pass; overlapping, recursive or
other definitions retain insertion-ordered substitution. Parsing splits only on LF
and checks cancellation during substitution and sprite compilation. Event lines
and each substitution result are limited to 33,554,432 UTF-16 code units;
growth is checked before allocating expanded output. A reader also limits the sum
of final expanded lines to 268,435,456 UTF-16 code units across its source files;
intermediate substitutions count only against the per-line limit. These bounds
limit expansion size, while cancellation remains responsible for retiring work.
Oversized expansion reports a loading or analysis failure without modifying
authored Events or Variables. Compilation
distributes commands into property tracks without repeated per-property filtering
and computes initial values without materializing sorted intermediate sequences.

`EditorView.Storyboard` owns the active scene, reusable frame/preload buffers and
asynchronous preparation. Events and Variables snapshot identities and resource-directory
changes invalidate scene preparation. General snapshots are compared only when changed:
edits to ordinary field values reuse the scene, while `WidescreenStoryboard` lines,
line positions used by parser diagnostics, and embedded section boundaries remain
part of the cache key. The file monitor also invalidates changed mapset media.
Gameplay preparation keys include committed playable identity and history revision;
one cancellable worker computes health and hit-sample trigger matching metadata. Retired workers
cannot publish into a new difficulty. Difficulty retirement cancels both jobs and
releases renderer geometry through `ICanvas.ReleaseDifficultyResources`. An active
project's difficulty switch within the same resource directory preserves unchanged
decoded storyboard images, native textures and the single resident background and
video decoder. Resource versions include file size, modification time and creation
time, checked at this transition. Previous image requests and pins retire while
the existing bounded caches remain available to the next scene. Failed image
decodes remain cached until the file changes; only the new scene's requested
failures supply its diagnostic. New scene parsing merges its own Events, Variables
and General with the shared `.osb` again, preserving variable scope and source
order. Gameplay, triggers and sample cursors remain specific to the difficulty.
Project replacement, switching resource directories and Library exit perform full
renderer retirement.
Map opening includes storyboard parsing, gameplay preparation and the current
visible images reaching the renderer. The existing opening overlay remains visible
until the prepared editor frame can be published together. Known missing images
complete preparation with a diagnostic. Image sets beyond the resident texture
budget complete with a visible warning and bounded rendering rather than keeping
the map closed indefinitely. A seek updates audio, editing data and the transport
immediately. Video and storyboard drawing retain their last complete frame until
the newest target's resources are ready; a legitimately empty storyboard target
publishes immediately. The previous storyboard image set remains pinned during
that replacement. Windows permits up to 256 MiB for the displayed set plus 256 MiB
for its pending replacement, with a 512 MiB hard limit, then trims back to the
ordinary 256 MiB budget after publication. Renderer resource replacement invalidates
the displayed-image readiness without delaying transport input.
Background save, synchronization and file-monitor completion still pump while the
opening overlay is shown. Esc or a library request can leave preparation through
the ordinary Save/Discard/Cancel decision; its dialog appears above the neutral
loading screen. Cancel resumes preparation and leaving retires the pending jobs.

Video receives the existing audio-derived song position minus its event offset.
The static background remains below the video. A video's first and final 500 ms
use linear fades, so it covers the background in between and reveals it at either
end. Storyboard coordinates retain 480 logical pixels vertically and centre X=320.
The widescreen flag selects a 640- or 853⅓-wide mask, intersected with the host panel.
Non-widescreen storyboards place black bars outside their 4:3 background region,
before drawing editor/gameplay objects; dimming does not recolour those bars.
Video uses centred cover sizing; video-only scenes use the full available panel.
Background dim reduces RGB brightness. Overlay
sprites draw above gameplay with unchanged alpha, including black opaque portions
at 100% dim. Force background dim suppresses background media in Catch preview
and testplay; Mapping retains its independent media display. All viewports share
the renderer's video decoder. The editor retains it while any viewport requests
video in the current frame and stops it when none does, including full-screen
pages and force-dim testplay. A requested frame still being decoded counts as
video demand. During same-directory difficulty preparation an idle decoder is
suspended rather than closed. Identical files and offsets reuse the current frame;
another offset seeks the same decoder and suppresses the previous difficulty's
frame until the requested timestamp is available. Ordinary frames with no visible
video demand still stop the decoder, including positions before the video offset.

`LibrarySettings.EnableVideoStoryboard` controls visuals across every viewport.
Changing it retires scene and gameplay workers, clears displayed sprite buffers,
and releases renderer media resources on the next frame, including while Settings
is visible. Disabling uses the reader's sample-only path, which skips visual
commands before field parsing or resource resolution. Sample snapshots and their
transport scheduling cursors survive preference changes while the replacement
sample scene is prepared; ordinary samples do not depend on visual decoding.

`CatchStoryboardHealth` adapts `CatchHealthProcessor.GetHealthIncreaseFor`,
`LegacyDrainingHealthProcessor.ComputeDrainRate` and the expanded break intervals
from `DrainingHealthProcessor.ApplyBeatmap`, at the revision recorded in third-party
notices. Drain calibration visits fruits, droplets and bananas; tiny droplets are
excluded from calibration but their actual hit/miss health adjustments still apply.
The ordinary Catch preview stays in Pass and records only hitsounds actually
dispatched during playback. Scheduling ahead adds pending events, not trigger
history. A continuously advancing published audio consumption clock must cross
their timestamps before they are committed; the final pause clock commits already
consumed events before cancelling the remaining queue. Seek, pause and speed changes
cancel pending events without inventing triggers or clearing recorded history.
An immediate dispatch counts even at zero output volume. Repeated visits query
the retained event timestamps, so seeking before an event removes its effect and
seeking forward can reproduce an event that already happened. Difficulty retirement
clears this session-only history; ordinary metadata preparation rebuilds matching
metadata around it. No trigger history is written to the document.

Testplay observes every judgement at `CatchTestplay.Judged`, before UI snapshots
can discard transient data, and records actual hit-sound, HitObjectHit and
health-threshold transition triggers. The journal supports arbitrary historical
reads. Health at least 0.5 selects Pass; crossing that threshold emits Passing/Failing.
Initial full health does not emit a transition trigger. This health affects
storyboard presentation without ending editor testplay. Starting/retrying testplay
creates a fresh judgement journal independently of the ordinary preview history.
Repeated activations cancel the prior instance's future commands. Explicit nonzero
trigger groups also cancel sibling triggers on the same sprite, following the
[compound-command contract](https://osu.ppy.sh/wiki/en/Storyboard/Scripting/Compound_Commands).
Cancelled transforms retain their value at cancellation until a newer command
replaces that property; random-time evaluation reconstructs this directly.

Storyboard samples share the platform hitsound mixer and transport scheduling.
Their cursor resets on pause, seek, speed changes and difficulty retirement; video
audio is unused. Referenced samples that cannot decode do not become synthetic
catch hitsounds. Missing or malformed media is reported in the editor status.

Playback uses mapset images. The retained `UseSkinSprites` source setting does not
currently replace them with skin textures. Commands attached directly to a video
event are preserved for export, while video playback uses the event offset,
centred cover sizing and standard start/end fades. These boundaries differ from
osu! features outside the supported sprite/animation and trigger playback path.

## Workspace and library

`Core/Workspace` provides project-directory transactions, SQLite indexing, metadata scanning, resource-reference diagnostics, and explicit export plans. Shared `EditorView.Library` implements the library/settings pages, and `EditorView.Export` draws the modal export overlay above the editor; platform hosts handle folder selection, audio changes, and resource export. Library scanning and star calculations run in the background. The UI reads committed maps during scanning and completed star results. See [Workspace](WORKSPACE.md).

Library loading remains active until completed worker results have been published
by the UI pump, including startup indexing and browser page/detail updates.

Opening a library project runs on a background worker on both platforms. The library
shows a loading overlay and blocks further content operations until the result is
published on the UI thread. Waiting for workspace maintenance's project lock does
not block window painting or message handling. Failed opens retain the current
project and display the error.

## Editing and conversion

The Compose canvas has Catch time–X and osu!standard geometric presentations.
The latter owns a 512×384 coordinate transform, standard stacking/visibility
snapshot and independent viewport. Both share transport and the exported Catch
preview. Switching presentation leaves content and history unchanged; explicit
slider representation conversion commits through history. Native slider path
strokes retain immutable point-array identities in bounded renderer geometry
caches (128 entries), released or evicted with their renderer owner.

Hosts map input to DIP coordinates before passing it to `EditorView`. Content changes commit through `EditorHistory` transactions; a drag, batch operation, or curve draft becomes one undo step. Selection and viewport are separate session state.

The Windows host derives its drawing, layout and pointer scale from client pixel
dimensions: 168 DPI (175%) multiplied by the smaller of width / 2520 and height / 1575.
This preserves the original 175% appearance of the 1440 × 900 logical startup window.
Equal client dimensions produce equal UI sizes regardless of monitor DPI. Compact
startup windows instead fit their own 820 × 740 or 880 × 620 logical layouts.
Monitor DPI applies only to native window decorations and dialogs.
`WM_GETDPISCALEDSIZE` preserves the pending client pixel size across monitor DPI
changes by adjusting only the non-client frame. Initial window sizes and minimum
bounds are proportional to monitor resolution and bounded by its work area. macOS retains
Avalonia's native scaling.

The saved `LibrarySettings.UiScalePercent` multiplies the normal editor scale by
its value / 150 on both hosts. It defaults to 150 and supports 50–200 in steps of
25. Windows applies it to the client-derived drawing DPI; macOS transforms its
drawing context and divides pointer coordinates and layout dimensions by the same
factor. Startup dialogs retain their fitted scale. Settings applies this preference
without changing document content or undo history.

The conversion cache compares document snapshots and Tiny compensation settings. Changes trigger synchronous conversion of the full document before viewport culling. The editor also caches the exported osu read-back events for gameplay displays; their times and coordinates reflect file quantization while mapped source identities retain editing links. Language changes rebuild diagnostic caches. RNG and hyperdash use the complete playable object sequence.

The canvas and object timeline share an immutable timing lookup built alongside the conversion snapshot. It sorts timing groups once and uses binary search for local BPM, meter and SV. Playback reuses it; content changes, undo and document replacement rebuild it. Grid drawing must not rebuild timing groups for each visible tick.

Drawing goes through `ICanvas`; the editor owns no device resources. During Windows playback, `WM_PAINT` requests the next frame and `Present(1)` presents it. Mac requests redraws with an approximately 16 ms timer. Each platform audio backend supplies playback position.

Windows suppresses nested paint/timer work while a frame is active or a native
modal dialog owns input. File, folder, skin and fatal-error dialogs share this
modal scope, and repaint resumes when the outermost dialog closes. Interrupted
frames close their Direct2D batch without presenting. A paint failure pauses
playback and rebuilds the renderer while retaining the document, then displays
the error in the editor. If recovery cannot draw the error, rendering remains
suspended while a native error dialog is shown.

Audio replacement suppresses transport polling while the old transport is being
disposed. Disposal can pump STA window messages; polling its retiring clock would
prematurely complete the new difficulty's position initialization.

Testplay interpolates timestamped audio samples with a monotonic clock. Key events
advance gameplay to their processing time before changing the held actions, so a
press and release between rendered frames still produces movement. Windows testplay
receives Raw Input on a dedicated thread. Key messages interrupt its high-resolution
timer immediately; held movement, judgement and live sound dispatch target 2000 Hz.
The worker sleeps until 0.2 ms before each deadline, then keeps checking messages
through the remaining bounded interval. This trades additional CPU time during
testplay for lower timer wake-up delay; it does not change device polling rates.
The UI draws detached session snapshots without holding the simulation lock.
DXGI frame readiness directly wakes the UI message loop instead of being polled
on a separate frame timer. The wait also wakes for window input and preserves a
consumed frame-ready signal for WM_PAINT. Rendering submits with `Present(0,
DoNotWait)` and a one-frame queue. GPU backpressure skips a draw. Mac processes key
transitions on its UI thread and requests testplay redraws through Avalonia's
display animation callback. Drawing frequency does not define input sampling.

Windows dispatches editor and testplay shortcuts before IME message translation
when no text field or key-binding capture is active. IME process-key messages
recover their original virtual key before translation; releases use the physical
scan code so held retry and other key latches can clear under a Chinese IME.

Preview and testplay share `EditorView.Catcher` for the catcher body, geometric
fallback, sprite facing and trail rendering. Automatic preview and live gameplay
provide movement history as `CatchTrail` values; sprite selection, effect timing,
colours and scaling have one drawing implementation.
`CatchPlate` also owns the shared caught-fruit stacking and release trajectories;
preview builds a seekable history, while testplay feeds actual judgements and prunes
expired effects. Both views use the same plate drawing method.

See [Project Model](PROJECT_MODEL.md) for data and conversion flow, and [Catch Rendering and Conversion](CATCH_RENDERING.md) for display formulas.

## Files and resources

Core handles text and project serialization. Hosts handle dialogs, archive extraction, and resource copying. Workspace OSZ import preserves complete archives as described in [Workspace](WORKSPACE.md); the legacy supported-file importer extracts supported entries. `.osk` import extracts `skin.ini`, Catch PNGs and supported hitsound samples. Importers validate paths, duplicate entries, links, and extraction limits, and write temporary directories before publishing caches.

Skin archives are limited to 256 MiB, selected files to 16 MiB each and 64 MiB total, and ZIP entries to 20000. Imported skin archives and extracted images are stored under `workspace/Skins`. Extraction preserves resource paths relative to a single `skin.ini`, including nested numeric fonts in lazer legacy exports; archives without configuration must keep selected resources in one folder. The versioned cache prevents reuse of older flattened extracts. Users configure their own default skin `.osk` file in Settings; no default skin is copied into build outputs or packages. See [Skins](../assets/skins/README.md).

## Audio and lifecycle

Window shutdown stops testplay and releases music, setup preview and hitsound
outputs before waiting for browser shutdown or filesystem cleanup on both platforms.

Windows `AudioTransport` serializes load, play, pause, and seek operations on a worker; the UI reads immutable state snapshots. MP3 decoding fills a bounded PCM cache continuously. Pause holds the published position captured when requested and rebuilds output at that source frame; later seeks take precedence. Seek and EOF replay also rebuild output. SoundTouch.Net 2.3.2 changes music tempo while preserving pitch; timestamped hitsounds are mixed afterward at their original sample speed. Device elapsed time is multiplied by tempo to recover map time. Changing speed rebuilds output at its consumed position. See the [Windows audio reference](../src/FruitsAtelier.App/Audio/REFERENCE.md).

Mac schedules an AVAudioPlayerNode through `Native/Audio.m`; AVAudioUnitTimePitch changes music tempo with pitch fixed at zero. Map position uses the output render timestamp, scheduled start, source position and tempo. The time-pitch unit is bypassed at 100%. NVorbis first decodes OGG into capacity-limited PCM WAV. Stale load results are discarded, and replay after EOF rebuilds the player. Hitsounds preload project PCM on a worker and submit timestamps to a persistent native mixer; see [Hitsounds](HITSOUNDS.md). See [Running on macOS](MACOS.md).

Preview hitsound scheduling, sample resolution, and platform playback are documented in [Hitsounds](HITSOUNDS.md).

On Windows resize, release the Direct2D target attached to the back buffer before resizing DXGI buffers, then recreate it. Skip presentation at zero size. Lost mouse capture or focus cancels active interactions. Mac maps its corresponding events to the same editor cancellation methods.

## Interactive editing performance

See [Editing performance](EDITING_PERFORMANCE.md) for hot-path constraints, cache
ownership, correctness boundaries, and review requirements.

The editor keeps a per-instance `CatchConversionCache`. Unchanged FSliders, imported
sliders, and banana showers reuse their derived output only when their source content,
conversion settings, timing points, and incoming legacy RNG state match. Cached entries
restore the outgoing RNG state, so a change to an earlier parent invalidates downstream
results wherever the random sequence changes. Failed conversions are not cached.
The normal converter remains available without a cache for export and independent checks.

Dragging a slider fruit or droplet keeps one history transaction and restores only that
source between candidate positions. Imported sliders are fitted once per drag, and
candidate validation shares the editor conversion cache. During content drags, including
anchors, handles, and legacy controls, rendering uses the current conversion; export
quantization and read-back are refreshed after release or cancellation.

Canvas and preview rendering select the visible interval from time-sorted catch objects
by binary search. Offscreen anchors
are culled, and timeline events share pixel-sized markers (hyperdash takes precedence).
The object timeline caches its ordered source intervals against the conversion result.
Imported slider ends reuse converted durations, so repainting does not rebuild every
slider's geometry and timing state. Content changes invalidate these intervals along
with conversion; scrolling, playback and selection reuse them.
Break intervals are parsed from source Events once per conversion snapshot and reused
by the canvas, object timeline, and transport. This avoids scanning storyboard-heavy
Events sections on every frame while preserving edits and undo through conversion invalidation.
Hit testing rejects distant curve segments before sampling them. Editing snapshots
copy existing identities without generating replacement IDs, and group dragging uses
direct target lookup. Undo/redo still retains independent document snapshots.

Star calculation runs against separate snapshots on a bounded background worker and
waits for an active content drag to finish. See [Catch difficulty](CATCH_DIFFICULTY.md).

### Testplay background and menu resources

The active beatmap background occupies one resident texture slot independent of
scene-sprite LRU eviction on Windows and macOS. Replacing its path or file version
replaces the slot; renderer disposal releases it. Decoded size is bounded to 64 MiB.
This prevents large backgrounds from evicting skin sprites on every frame.
Pause and button samples are prepared before play. Windows mixes the pause loop
through the independent audition output; macOS uses a loop voice in its persistent
native mixer. Both respect effects volume and fade the loop on dismissal.
