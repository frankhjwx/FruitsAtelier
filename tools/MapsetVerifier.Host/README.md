# Mapset Verifier host

This standalone GPL-3.0 program hosts the built-in checks of
[Naxesss/MapsetVerifier](https://github.com/Naxesss/MapsetVerifier).
`eng/mapset-verifier.json` pins the upstream release, commit and source archive
SHA-256. The editor communicates through a versioned JSON file protocol; it does
not link Mapset Verifier assemblies. No plugins, web server, updater or playback
device are started by this host.

Build with .NET 10 and Python 3:

```sh
python scripts/Build-MapsetVerifier.py
```

The script verifies and extracts the upstream source archive, builds this host
against its pinned projects, and writes a self-contained executable to
`artifacts/mapset-verifier/<runtime>/`. Publish pruning follows the unused game
resource/native exclusions in upstream `src/MapsetVerifier.csproj` at the pinned
revision. `Upstream.Build.targets` excludes the web-hosting Serilog package from the
logging project; no upstream source files are edited. The output retains licenses
and source directions. The full upstream archive, host and launcher sources,
dependency locks and build materials are in the separate
`artifacts/mapset-verifier/FruitsAtelier-MapsetVerifier-TAG-source.zip` attachment.

Publication passes `--shared-runtime <editor-publish-directory>` to use the exact
.NET version from the editor's runtime configuration. The engine then publishes
without a runtime to `<runtime>-shared/`. A separate GPL-3.0 launcher in
`tools/MapsetVerifier.Launcher/` runs beside the editor on its bundled runtime and
loads the engine's entry point with its own dependency resolver. Copy the engine
directory to `mapset-verifier/`; the build script installs only the launcher's
apphost, assembly, dependency manifest and runtime configuration beside the editor.

Invoke with two absolute paths: a request JSON and a destination result JSON. The
request contains `Folder` (an exported mapset snapshot) and `Difficulties` entries
with `Id`, `File` and optional `Interpretation` (`Easy`, `Normal`, `Hard`, `Insane`,
`Expert`). The result has `Schema: 1`, `General` issues and per-difficulty issues.
Check titles, causes and messages retain upstream wording. Check errors retain
their upstream `Error` level rather than being classified as ranking problems.

This directory's source is licensed under GPL-3.0; see `LICENSE`. FruitsAtelier's
editor source retains its own license.
