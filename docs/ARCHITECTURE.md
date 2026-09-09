# Architecture

## Platforms and dependencies

The application uses C# 12 / .NET 8. Windows and macOS share the editor, data model, and file formats, while providing separate windowing, drawing, and audio implementations.

| Layer | Windows | macOS |
| --- | --- | --- |
| Entry point | `FruitsAtelier.App`, `net8.0-windows` | `FruitsAtelier.Mac`, `net8.0` |
| Window and input | Win32, DPI messages, native file dialogs | Avalonia desktop window and file picker |
| Drawing | DX11 / DXGI, Direct2D / DirectWrite, Vortice 3.6.2 | Avalonia 11.3.7, `MacCanvas` implements `ICanvas` |
| PNG | Windows Imaging Component | Avalonia bitmaps |
| Audio | NAudio shared-mode WASAPI; Media Foundation / NVorbis / WAV reader | AVAudioPlayer music; AVAudioEngine PCM hitsound mixer; NVorbis for OGG |

See [Building and Testing](TESTING.md) for SDK selection and build commands, and [Third-party notices](../THIRD_PARTY_NOTICES.md) for package versions and licenses.

## Source layout

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

## Workspace and library

`Core/Workspace` provides project-directory transactions, SQLite indexing, metadata scanning, resource-reference diagnostics, and explicit export plans. Shared `EditorView.Library` implements the library/settings/export pages; platform hosts handle folder selection, audio changes, and resource export. Library scanning and star calculations run in the background, with completed results read by the UI. See [Workspace](WORKSPACE.md).

## Editing and conversion

Hosts map input to DIP coordinates before passing it to `EditorView`. Content changes commit through `EditorHistory` transactions; a drag, batch operation, or curve draft becomes one undo step. Selection and viewport are separate session state.

The conversion cache compares document snapshots and Tiny compensation settings. Changes trigger synchronous conversion of the full document before viewport culling. Language changes rebuild diagnostic caches. Both views share the conversion result; RNG and hyperdash use the complete object sequence.

Drawing goes through `ICanvas`; the editor owns no device resources. During Windows playback, `WM_PAINT` requests the next frame and `Present(1)` presents it. Mac requests redraws with an approximately 16 ms timer. Each platform audio backend supplies playback position.

See [Project Model](PROJECT_MODEL.md) for data and conversion flow, and [Catch Rendering and Conversion](CATCH_RENDERING.md) for display formulas.

## Files and resources

Core handles text and project serialization. Hosts handle dialogs, archive extraction, and resource copying. Workspace OSZ import preserves complete archives as described in [Workspace](WORKSPACE.md); the legacy supported-file importer extracts supported entries. `.osk` import extracts `skin.ini` and Catch PNGs. Importers validate paths, duplicate entries, links, and extraction limits, and write temporary directories before publishing caches.

Skin archives are limited to 256 MiB, selected files to 16 MiB each and 64 MiB total, and ZIP entries to 20000. Source builds cache resources under `artifacts/beatmaps` and `artifacts/skins`; the standalone Mac application uses `~/Library/Application Support/FruitsAtelier`. See [Skins](../assets/skins/README.md) for the optional default skin.

## Audio and lifecycle

Windows `AudioTransport` serializes load, play, pause, and seek operations on a worker; the UI reads immutable state snapshots. MP3 decoding fills a bounded PCM cache continuously. Pause can reuse an active WASAPI session; seek and EOF replay rebuild output. See the [Windows audio reference](../src/FruitsAtelier.App/Audio/REFERENCE.md).

Mac calls AVAudioPlayer through `Native/Audio.m` and obtains position from the player. NVorbis first decodes OGG into capacity-limited PCM WAV. Stale load results are discarded, and replay after EOF rebuilds the player. Hitsounds preload project PCM on a worker and submit timestamps to a persistent native mixer; see [Hitsounds](HITSOUNDS.md). See [Running on macOS](MACOS.md).

Preview hitsound scheduling, sample resolution, and platform playback are documented in [Hitsounds](HITSOUNDS.md).

On Windows resize, release the Direct2D target attached to the back buffer before resizing DXGI buffers, then recreate it. Skip presentation at zero size. Lost mouse capture or focus cancels active interactions. Mac maps its corresponding events to the same editor cancellation methods.

## Interactive editing performance

The editor keeps a per-instance `CatchConversionCache`. Unchanged FSliders, imported
sliders, and banana showers reuse their derived output only when their source content,
conversion settings, timing points, and incoming legacy RNG state match. Cached entries
restore the outgoing RNG state, so a change to an earlier parent invalidates downstream
results wherever the random sequence changes. Failed conversions are not cached.
The normal converter remains available without a cache for export and independent checks.

Canvas and preview rendering select the visible interval from time-sorted catch objects
by binary search. Sidebar labels are formatted only for visible rows, offscreen anchors
are culled, and timeline events share pixel-sized markers (hyperdash takes precedence).
Hit testing rejects distant curve segments before sampling them. Editing snapshots
copy existing identities without generating replacement IDs, and group dragging uses
direct target lookup. Undo/redo still retains independent document snapshots.

Star calculation runs against separate snapshots on a bounded background worker and
waits for an active content drag to finish. See [Catch difficulty](CATCH_DIFFICULTY.md).
