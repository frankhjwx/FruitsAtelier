# Running on macOS

The Mac host uses Avalonia desktop windows, AVAudioEngine with AVAudioPlayerNode for prepared music PCM, and a persistent AVAudioEngine mixer for hitsounds. SoundTouch.Net processes tempo changes before playback, using the same settings as the Windows transport; 100% playback bypasses processing. A bounded half-second PCM queue is replenished off the audio render thread. Music and scheduled hitsounds share a host-clock origin at every speed. Seek, pause/resume and speed changes retire buffered PCM and restart processing at the retained source position.

Immediate auditions start at the next available render frame to preserve short attacks. Panel auditions prepare missing samples asynchronously, while library and Precise HS auditions replace the previous audition. Stopping or replacing the project cancels pending panel playback.

Build scripts target the current machine architecture: `osx-arm64` for Apple Silicon and `osx-x64` for Intel.

## Running from source

Install .NET SDK **10.0.400** and Xcode Command Line Tools (`xcrun clang`). A project-local SDK can be installed from the repository root:

```bash
bash scripts/Install-Mac-SDK.sh
```

Double-click [Run-Editor-Mac.command](../Run-Editor-Mac.command) in the root directory, or run:

```bash
./Run-Editor-Mac.command
```

The script prefers `artifacts/dotnet/dotnet`, otherwise uses `dotnet` on PATH, and changes to `macOS/` to apply that directory's SDK version settings.

## Packaging

```bash
bash scripts/Publish-Mac.sh
```

The output is `artifacts/macos/FruitsAtelier.app`, includes the .NET runtime, and can be launched by double-clicking. Mapset Verifier uses that same bundled runtime through its separate launcher. Its corresponding-source ZIP is generated beside the app; distribute it alongside the app as described in [Mapset Verifier](MAPSET_VERIFIER.md). The script builds for the local architecture and applies ad-hoc signing. Public distribution additionally requires Developer ID signing and notarization.

Outside the repository, caches and logs are written to `~/Library/Application Support/FruitsAtelier`.

## Platform differences

Mac accepts both Command and Ctrl shortcuts. Delete / Backspace deletes objects; Backspace removes characters during numeric input.

Opening a beatmap archive loads its Catch difficulties into one project. Closing or replacing unsaved content offers Save, Discard, and Cancel.

See [Editing Controls](EDITOR_UI.md) for other operations and [Building and Testing](TESTING.md) for test commands.

## Native audio and render regressions

`bash scripts/Test-Mac.sh --native-only` includes actual PCM onset/pitch checks at
10%–150% speed, pitch-adjusted playback, seek/resume, negative lead-in, immediate
sample integrity and panel/library audition cancellation. Output is muted after
PCM observations. The native test executable also accepts `--pcm-alignment-check`
and `--audition-check` to run these groups separately. Onset checks compare the
first source PCM in each playback session; time-stretching may redistribute an
interior transient's energy within its processing grain.

`bash Run-Editor-Mac.command --smoke-check --mac-host-check` opens generated
workspace fixtures through real Avalonia render frames, checks focus callbacks,
then injects a recovery error and retries. Fixtures and captures remain under
`artifacts/macos-check/host`. Render-time refresh requests are coalesced and posted
after the active frame, including refreshes requested by error recovery.
