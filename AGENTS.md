# FruitsAtelier Development Guidelines

## Project Entry Points

- [README](README.md): startup instructions and documentation index.
- [Architecture](docs/ARCHITECTURE.md): modules and platform boundaries.
- Depending on the change, read [Editing Controls](docs/EDITOR_UI.md), [Data Model](docs/PROJECT_MODEL.md), [File Format](docs/STABLE_FORMAT.md), or [Localization Maintenance](docs/LOCALIZATION.md).

## Code Conventions

- Use C# 12 / .NET 8. SDK and NuGet versions are pinned in project configuration and lock files.
- Windows uses Win32 / DirectX / WASAPI; macOS uses Avalonia / AVAudioPlayer. Account for both platforms when changing shared editor code, and keep Core free of platform dependencies.
- `.catchproj` stores anchors, handles, and imported source context; converted paths and preview objects are derived from this data. Preserve compatibility with older projects when changing the persistence model.
- The editor's Y coordinate represents time; an exported slider's geometric Y is a path coordinate. Keep beat snapping separate from SliderTickRate.
- Apply content changes through editing transactions and undo history. Selection, viewport, and language settings must not modify beatmap content.
- Read GUI text and domain diagnostics from localization tables. Maintain matching Chinese and English keys and placeholders. Switching languages must not rewrite existing object names or file contents.
- When introducing or adapting external code, record its source and pinned revision, retain licenses, and update third-party notices.

## Validation and Documentation

- Run the affected checks described in [Building and Testing](docs/TESTING.md). Launch the application to check window and input changes.
- Automated audio tests output silent PCM while retaining the device clock and sample comparisons before muting. Do not change the system volume.
- Store build artifacts, test logs, and screenshots in `artifacts/`; do not commit them to Git.
- Write documentation for project readers: describe current behavior, usage, and design details needed for maintenance. Keep conversational prompt responses, progress reports, temporary acceptance-build paths, and one-off test counts in tasks or PRs.
- Maintain each rule in its relevant document and link to it elsewhere. Update documentation when behavior changes and remove obsolete conclusions.
- Keep AI-facing project instructions in English.
- Do not start subagents automatically. Use them only when the user explicitly requests parallel agent work.
