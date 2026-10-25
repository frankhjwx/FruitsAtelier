# FruitsAtelier Development Guidelines

## Project Entry Points

- [README](README.md): startup instructions and documentation index.
- [Architecture](docs/ARCHITECTURE.md): modules and platform boundaries.
- Before changing interactive editing or its derived data, read [Editing performance](docs/EDITING_PERFORMANCE.md) and follow its hot-path and verification requirements.
- [Windows releases](docs/RELEASING.md): branch, tag, package, and publication workflow.
- After publishing a GitHub release, provide a copyable Discord Markdown announcement of at most 2,000 characters using the format in [Windows releases](docs/RELEASING.md#discord-announcement).
- Depending on the change, read [Editing Controls](docs/EDITOR_UI.md), [Data Model](docs/PROJECT_MODEL.md), [File Format](docs/STABLE_FORMAT.md), or [Localization Maintenance](docs/LOCALIZATION.md).

## Code Conventions

- Use C# 12 / .NET 10. SDK and NuGet versions are pinned in project configuration and lock files.
- Windows uses Win32 / DirectX / WASAPI; macOS uses Avalonia / AVAudioPlayer. Account for both platforms when changing shared editor code, and keep Core free of platform dependencies.
- `.catchproj` stores anchors, handles, and imported source context; converted paths and preview objects are derived from this data. Preserve compatibility with older projects when changing the persistence model.
- The editor's Y coordinate represents time; an exported slider's geometric Y is a path coordinate. Keep beat snapping separate from SliderTickRate.
- Apply content changes through editing transactions and undo history. Selection, viewport, and language settings must not modify beatmap content.
- Read GUI text and domain diagnostics from localization tables. Maintain matching Chinese and English keys and placeholders. Switching languages must not rewrite existing object names or file contents.
- When introducing or adapting external code, record its source and pinned revision, retain licenses, and update third-party notices.

## Validation and Documentation

- At the start of AI development, check the latest stable Mapset Verifier release with `python scripts/Update-MapsetVerifier.py`. In an agent environment with restricted network access, request or use network-enabled execution permissions on the first invocation (`sandbox_permissions="require_escalated"` for the Codex `exec_command` tool). Apply the same rule to dependency preparation commands that need downloads, including `Build-MapsetVerifier.py`. These are execution-tool permissions; ordinary terminal and CI commands stay unchanged. If the pin changes, prepare the engine and refresh its locks with `python scripts/Build-MapsetVerifier.py --update-locks --runtime <rid>` for `win-x64`, `osx-arm64` and `osx-x64`, then run the Mapset Verifier and affected desktop regressions before delivering the update. Retain the existing pin and report the limitation when the upstream check is unavailable with the required permissions. See [Mapset Verifier](docs/MAPSET_VERIFIER.md) for the update and packaging contract.

- Commit and push intermediate bug fixes to `dev` unless the user explicitly requests another destination.
- Run the affected checks described in [Building and Testing](docs/TESTING.md). Follow its [macOS resource fixture path rules](docs/TESTING.md#macos-resource-fixture-paths) when adding resource or workspace tests. Launch the application to check window and input changes.
- Automated audio tests output silent PCM while retaining the device clock and sample comparisons before muting. Do not change the system volume.
- Store build artifacts, test logs, and screenshots in `artifacts/`; do not commit them to Git.
- Write documentation for project readers: describe current behavior, usage, and design details needed for maintenance. Keep conversational prompt responses, progress reports, temporary acceptance-build paths, and one-off test counts in tasks or PRs.
- Maintain each rule in its relevant document and link to it elsewhere. Update documentation when behavior changes and remove obsolete conclusions.
- Use English as the baseline for project documentation and AI-facing instructions. Maintain technical and AI documentation in English; preserve README.zh-CN.md as the translated user entry point, alongside localization resources and translation-specific examples.
- Do not start subagents automatically. Use them only when the user explicitly requests parallel agent work.
