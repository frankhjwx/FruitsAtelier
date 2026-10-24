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
against its unmodified projects, and writes a self-contained executable to
`artifacts/mapset-verifier/<runtime>/`. Publish pruning follows the unused game
resource/native exclusions in upstream `src/MapsetVerifier.csproj` at the pinned
revision. The output includes the GPL license, upstream source archive, this host's
source and build instructions, and dependency metadata/license files.

Invoke with two absolute paths: a request JSON and a destination result JSON. The
request contains `Folder` (an exported mapset snapshot) and `Difficulties` entries
with `Id`, `File` and optional `Interpretation` (`Easy`, `Normal`, `Hard`, `Insane`,
`Expert`). The result has `Schema: 1`, `General` issues and per-difficulty issues.
Check titles, causes and messages retain upstream wording. Check errors retain
their upstream `Error` level rather than being classified as ranking problems.

This directory's source is licensed under GPL-3.0; see `LICENSE`. FruitsAtelier's
editor source retains its own license.
