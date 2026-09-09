# Running on macOS

The Mac host uses Avalonia desktop windows, AVAudioPlayer for music, and a persistent AVAudioEngine mixer for hitsounds. Build scripts target the current machine architecture: `osx-arm64` for Apple Silicon and `osx-x64` for Intel.

## Running from source

Install .NET SDK **8.0.419** and Xcode Command Line Tools (`xcrun clang`). A project-local SDK can be installed from the repository root:

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

The output is `artifacts/macos/FruitsAtelier.app`, includes the .NET runtime, and can be launched by double-clicking. The script builds for the local architecture and applies ad-hoc signing. Public distribution additionally requires Developer ID signing and notarization.

Outside the repository, caches and logs are written to `~/Library/Application Support/FruitsAtelier`.

## Platform differences

Mac accepts both Command and Ctrl shortcuts. Delete / Backspace deletes objects; Backspace removes characters during numeric input.

Opening a beatmap archive loads its Catch difficulties into one project. Closing or replacing unsaved content offers Save, Discard, and Cancel.

See [Editing Controls](EDITOR_UI.md) for other operations and [Building and Testing](TESTING.md) for test commands.
