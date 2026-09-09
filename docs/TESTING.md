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
bash scripts/Test-Mac.sh --skip-device-tests  # Shared regressions; skips the entire native Mac test project
bash scripts/Test-Mac.sh --native-only        # Mac input/audio checks only
```

[Desktop regression](../.github/workflows/desktop.yml) runs on pushes and PRs: Windows builds the solution and runs shared regressions; macOS runs shared regressions and packages the app. Results are available in [GitHub Actions](https://github.com/frankhjwx/FruitsAtelier/actions).

## External test resources

The repository contains synthetic format fixtures, older `.catchproj` compatibility fixtures, and OGG audio fixtures. These checks additionally require local resources:

- The two real-beatmap checks enabled by default in Formats need external beatmaps under `artifacts/beatmaps`. `--skip-external-fixtures` skips them and is used by CI and Mac scripts.
- Core's `--fixtures` option enables additional real-beatmap checks.
- A full Windows `Audio.Tests` run needs a default audio device and MP3 fixtures under `artifacts/beatmaps`. `--recovery-check` runs only injected output-failure checks; `--lifecycle-check` runs only playback lifecycle checks.
- Tests using a specific default skin or user project depend on uncommitted local files. Check test output for skipped cases.

Automated device tests output silent PCM; sample comparisons happen before muting. Run Windows audio tests with:

```powershell
dotnet run --project tests/FruitsAtelier.Audio.Tests -c Release
```

## Window checks

```bash
./Run-Editor-Mac.command --smoke-check
```

This opens a Mac window, checks fruit placement, undo, project round trips, and English/Chinese screenshots, then exits. Screenshots go to `artifacts/macos-check` and logs to `artifacts/logs/macos.log`.

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

## Imported slider corpus

The Core test executable accepts `--slider-corpus <workspace>` for opt-in, read-only
checks against existing `.catchdiff` documents. It never saves or opens a workspace
recovery session. The report compares fitted anchor counts with the previous 0.001
linear simplification, measures sampled error on the longest converted sliders, and
lists preserved failures. External map data is not required by CI or committed.

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
