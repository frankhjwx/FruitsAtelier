# Building and Testing

## SDKs and builds

The project targets .NET 8 with C# 12. Two `global.json` entry points pin SDK versions:

| Entry point | SDK | Purpose |
| --- | --- | --- |
| Repository root | 10.0.400 | Windows launch scripts and root builds |
| `macOS/` | 8.0.419 | Mac scripts and Windows/macOS CI |

Build on Windows from the repository root:

```powershell
dotnet build FruitsAtelier.sln -c Release -p:RestoreLockedMode=true
```

To build the Windows solution with SDK 8.0.419, enter `macOS/` first:

```powershell
cd macOS
dotnet build ../FruitsAtelier.sln -c Release -p:RestoreLockedMode=true
```

See [Running on macOS](MACOS.md) for launch and packaging. Project files and `packages.lock.json` pin package versions; NuGet caches packages in `artifacts/packages`.

Windows distribution uses the self-contained ZIP and extracted-executable check described in [Windows releases](RELEASING.md). Desktop CI checks packaging before a version is tagged; the tag workflow repeats the regressions and package check before publishing its assets.

## Automated regressions

Test projects are console programs run with `dotnet run`. Format-export quantization, read-back, and edge-sample diagnostic checks iterate over every available language, verifying messages and parameters against localization tables rather than assuming the default UI language.

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

Run the opt-in benchmark from the repository root after building:

```powershell
dotnet run --no-build --project tests/FruitsAtelier.App.Tests -c Release -- --benchmark-library
```

It creates 500,000 synthetic map records in distinct sets under `artifacts/library-scale/`, checks random SQL pages and the last set, and exercises 100 scrollbar jumps over 2,000 UI frames. `artifacts/library-scale/benchmark.json` records index time, page latency, UI timing, allocation and peak cached rows. Pass an existing fixture directory after `--benchmark-library` to reuse its database. The benchmark uses `RecordingCanvas`: it measures metadata/query/UI work, not GPU presentation, image decoding, real map scanning, or visible FPS. The native Windows `--render-check` separately checks asynchronous thumbnail decoding and drawing.

## External test resources

To check an individual supported `.osu` without modifying it, run the Formats test executable with `--import-roundtrip <path>`. This verifies project persistence and v14 export preserve raw object/timing lines and the converted Catch sequence, times and positions.

The repository contains synthetic format fixtures, older `.catchproj` compatibility fixtures, and OGG audio fixtures. These checks additionally require local resources:

- The two real-beatmap checks enabled by default in Formats need external beatmaps under `artifacts/beatmaps`. `--skip-external-fixtures` skips them and is used by CI and Mac scripts.
- Core's `--fixtures` option enables additional real-beatmap checks.
- A full Windows `Audio.Tests` run needs a default audio device and MP3 fixtures under `artifacts/beatmaps`. `--recovery-check` runs only injected output-failure checks; `--lifecycle-check` runs only playback lifecycle checks.
- Tests using a specific default skin or user project depend on uncommitted local files. Check test output for skipped cases.

Automated device tests output silent PCM; sample comparisons happen before muting. Run Windows audio tests with:

```powershell
dotnet run --project tests/FruitsAtelier.Audio.Tests -c Release
```

`Audio.Tests --diagnostic-check` validates command/event correlation, repeated
pause positions with logging enabled, unsupported hitsound format identification,
and continued loading when the diagnostic destination cannot be written. It uses
an injected output and does not play sound. To capture real-device lifecycle
checks, set `FRUITSATELIER_AUDIO_DIAGNOSTICS=1` before running `--lifecycle-check`.

`Audio.Tests --pause-check` checks pause/resume PCM alignment with a blocked
in-flight clock read, seek/pause ordering and rapid resume, plus real-device WAV
pause, seek and EOF replay. The paused position remains at the request snapshot
even when the device advances before the worker handles it.

## Window checks

The Windows `--render-check` injects nested paint/timer messages, nested native-modal
scopes, an abandoned drawing batch, and a real Direct2D wrong-state failure. It
checks that the renderer recovers, the error can be dismissed, and map content is
preserved. The intentional Direct2D exception is followed by a successful paint
lifecycle entry in the diagnostic log.

It also advances a fake update backend through checking, availability, download
progress, and restart readiness while dispatching only paint messages. Update
status must reach the view even when continuous rendering delays `WM_TIMER`.

The Windows `--render-check` also exercises testplay entry, movement, combo drawing,
return, catcher mirroring and binding settings at both window sizes and all tested
DPI values in English and Chinese. Testplay checks use silent callbacks. Shared App
tests cover key repeat/release, focus cancellation, end conditions, judging between
frames, custom bindings, and document isolation. Extended binding checks cover capture,
key labels, settings reload, movement/dash press and release, and reserved keys;
Mac key mappings also run without an audio device. `Audio.Tests --hitsound-check`
checks live catch samples at the next output frame without opening an audio device.

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
manual movement, and session reset. Visual tests cover deterministic rotation,
banana arrival transforms, static editor sprites, combo palette offsets, and the
base-only additive hyperdash layer with independent overlay crop and rotation.

```bash
./Run-Editor-Mac.command --smoke-check
```

This opens a Mac window, checks fruit placement, undo, project round trips, both slider editing modes, and English/Chinese screenshots, then exits. Slider screenshots also cover narrow and scrolled properties panels. Screenshots go to `artifacts/macos-check` and logs to `artifacts/logs/macos.log`.

Shared Core and App regressions cover exact control-curve persistence, fixed AR reference ratios, bidirectional editing, local pen conversion and undo, control insertion/deletion, segment boundaries, global mode switching, and repeated-slider export. Switching editing modes must leave the document and history unchanged.

After changing input or drawing, manually check affected operations, language switching, window resizing, and file dialogs. Additional coverage is still needed for physical Windows window/audio behavior, Intel Mac, cross-display DPI, Mac MP3, and stable-client comparisons.

## Editing performance benchmark

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

## Playback rendering profile

For a read-only CPU profile of an existing `.osu` file, run the App test executable with `--map-performance <path>`. It measures playback around 89 seconds at 32% Zoom and 1/16 Snap, reporting render median/p95, allocation per frame, rendering phases, and transport/hitsound scheduling with silent callbacks. The counting canvas excludes GPU and device submission.

On Windows, run `FruitsAtelier.App.exe --profile-map <path> [startMs]` in a hidden Direct2D window with the active default skin. It opens an in-memory workspace session for the selected `.osu` and sibling difficulties, including periodic resource checks, then measures thirty seconds from the supplied time (default 70000 ms) at 100% zoom and 1/4 snap with preview closed, NM, and HR. The report separates drawing submission, total rendering including `EndDraw`/`Present`, and silent transport updates. Per-frame samples include allocations, GC collections, and image decode counts to identify isolated stalls. It also measures document comparison time. The report is written to `artifacts/logs/playback-profile.json` in repository builds, or the application log directory in distributed builds. This is not a visible-screen FPS measurement. Neither profile saves or imports the map into the workspace, plays audio, or changes system volume.

## Imported slider corpus

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
6 ms start lookup, tick inheritance across timing changes, same-difficulty clipboard
scope, combo reference numbers, horizontal grids, and timestamp precision.

The feedback regressions cover V/End navigation, scroll direction, persistent independent volume controls, skin sample precedence and cache upgrades, and compatible versus conflicting close SV timing. Windows PCM checks verify live and scheduled hitsound gain separately from song gain; headless Mac CI exercises the native mixer with no audio device. Native window checks exercise all three volume sliders in both languages.
