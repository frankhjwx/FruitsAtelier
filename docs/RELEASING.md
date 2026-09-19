# Windows packaging and releases

The Windows distribution is a self-contained **win-x64 ZIP**, including the .NET
runtime, native dependencies, editor assets, default audio, and third-party notices.
Extract the whole ZIP and launch `FruitsAtelier.exe`. Keep `Update.exe` and the
`current/` directory together. Windows users do not need
the SDK, a separate .NET runtime, or administrator access. The executable requires
Windows 10/11 x64 with DirectX 11 support; MP3 playback uses Windows Media Foundation.
Windows N users need Microsoft's Media Feature Pack.

The application is currently unsigned. This workflow does not produce an installer,
Windows ARM64 build, or macOS release. macOS development and packaging remain in
[the Mac guide](MACOS.md).

## Local package

From the repository root, using PowerShell 7 and the SDK pinned in `global.json`:

```powershell
./scripts/Publish-Windows.ps1
./scripts/Test-WindowsPackage.ps1 -Archive artifacts/releases/FruitsAtelier-0.8.2-win-x64.zip
```

The outputs are `artifacts/releases/FruitsAtelier-VERSION-win-x64.zip` and its
`.zip.sha256` checksum, plus a Velopack full `.nupkg` and
`releases.win-x64.json` update feed. The default version comes from `Directory.Build.props`
(currently 0.8.2); `-Version` overrides it for a tagged release. `build-info.json` records the version, source commit, SDK, RID, and whether
the local checkout had uncommitted changes. Executable version metadata uses the
same version and commit. Release builds run from the clean tagged commit.
The window title displays this version beside the application name, omitting the
source commit suffix and retaining any prerelease label.

The package includes the English [user manual source](USER_MANUAL.md). To include
its PDF edition, install Python and ReportLab, render the manual, then pass the
output to the packaging script:

```powershell
python -m pip install reportlab==4.4.9
python scripts/Build-UserManual.py
./scripts/Publish-Windows.ps1 -UserManual artifacts/releases/FruitsAtelier-User-Manual.pdf
```

The PDF is generated under `artifacts/releases` and copied to the root of the ZIP.
Review its rendered pages after editing the manual. Keep generated PDFs out of Git.

`WindowsRelease.pubxml` pins the bundled runtime version and disables trimming and
single-file bundling so filesystem assets and reflection-dependent libraries remain
available. RID-specific `packages.win-x64.lock.json` files keep publishing separate
from the normal development lock files. Update the runtime pin and these lock files
together when adopting a runtime security update; installed .NET updates do not
update a self-contained package. Regenerate the release locks explicitly:

```powershell
dotnet restore src/FruitsAtelier.App/FruitsAtelier.App.csproj -r win-x64 -p:PublishProfile=WindowsRelease -p:SelfContained=true -p:NuGetLockFilePath=packages.win-x64.lock.json --force-evaluate
```

Local `assets/skins/default.osk` is excluded from release packages. User workspaces,
Songs directories, credentials, and application preferences are never copied into
the distribution. Each publish uses a fresh staging directory under `artifacts/`.
Release logs use `%LOCALAPPDATA%/FruitsAtelier/logs`; repository builds use
`artifacts/logs`. Use **Library → Settings → Application updates** to check, download, and
explicitly save and restart into an update. The Windows client uses the public
GitHub Releases source, excludes prereleases, and checks at most once per day
on startup when automatic checks are enabled. Failed checks also consume that
day's automatic attempt; manual checks remain available. Downloaded updates are
retained across launches but never applied implicitly. Development builds do not
self-update. macOS packaging does not yet include an updater.

Velopack SDK and CLI versions are pinned to 1.2.0 in the project and
`.config/dotnet-tools.json`. Full update packages are used; no delta packages or
installer are generated. `current/` is replaced during updates. Keep user projects
and skins outside it; the application refuses an update if configured data lives
there or another instance of the same installation is running. Preferences live
in the existing application data directory; `updates.json` stores the automatic
check preference and last attempt time.

Existing plain ZIP installations require one manual download and extraction of
the updater-enabled portable package. Subsequent versions update in place.
The homepage is not part of the update path.

## GitHub Release

1. Commit and push the reviewed changes, including both release lock files.
2. Tag that commit with `vMAJOR.MINOR.PATCH`, optionally followed by `-alpha.N`,
   `-beta.N`, or `-rc.N` (N starts at 1). Numeric version components must be at most
   65534. For example:

   ```bash
   git tag -a v0.1.0 -m "FruitsAtelier 0.1.0"
   git push origin v0.1.0
   ```

3. The **Windows release** workflow validates the tag, builds and tests that commit,
   publishes the ZIP, and launches its extracted exe for the package smoke check.
4. After all checks pass, it uploads the portable ZIP, SHA256, full update package and update feed to a draft GitHub Release
   and then publishes it. Suffixed tags become prereleases. The workflow uses the
   repository `GITHUB_TOKEN`; no personal token is needed. Actions must be enabled
   and repository policy must allow the release job's `contents: write` permission.

The manual **Run workflow** entry builds an existing tag and stores downloadable
Actions artifacts without publishing a GitHub Release. The normal desktop regression
also checks Windows packaging, so changes can be validated before tagging.

A failed test or package check prevents release publication. Re-running a failed
tag workflow can complete an unfinished draft; published releases are never
overwritten. Use a new tag for corrections and protect release tags from deletion
or movement. The release job verifies the tag still points to the build's commit.

The package check runs the unpacked executable from a different working directory
and a path containing spaces and Unicode, with .NET runtime discovery redirected
to a nonexistent directory. The exe verifies that it actually loaded its bundled
runtime, loads native SQLite, validates embedded localization and assets, decodes
the packaged hitsounds, and exercises tempo processing and map conversion without
producing sound. It does not certify every GPU/audio-driver combination. Verify
window rendering and audio on a Windows machine before choosing a release tag.

## Update integration check

Desktop CI packages two test versions, rejects a deliberately corrupted download,
then upgrades the older portable installation and checks the restarted version,
bundled runtime, native dependencies, and a user file outside `current/`.
The check uses a local file feed without network access or audio output:

```powershell
./scripts/Publish-Windows.ps1 -Version 0.0.2-alpha.1 -OutputDirectory artifacts/update-old
./scripts/Publish-Windows.ps1 -Version 0.0.2-alpha.2 -OutputDirectory artifacts/update-new
./scripts/Test-WindowsUpdate.ps1 -Archive artifacts/update-old/FruitsAtelier-0.0.2-alpha.1-win-x64.zip -FeedDirectory artifacts/update-new -ExpectedVersion 0.0.2-alpha.2
```

Publish the feed together with its matching packages before making a GitHub
Release public. A release without these assets cannot serve automatic updates.
The stable client must never point at the CI test feeds.
