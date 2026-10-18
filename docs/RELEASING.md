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
./scripts/Test-WindowsPackage.ps1 -Archive artifacts/releases/FruitsAtelier-1.1.1-win-x64.zip
```

The outputs are `artifacts/releases/FruitsAtelier-VERSION-win-x64.zip` and its
`.zip.sha256` checksum, plus a Velopack full `.nupkg` and
`releases.win-x64.json` update feed. The default version comes from `Directory.Build.props`
(currently 1.1.1); `-Version` overrides it for a tagged release. `build-info.json` records the version, source commit, SDK, RID, and whether
the local checkout had uncommitted changes. Executable version metadata uses the
same version and commit. Release builds run from the clean tagged commit.
The window title displays this version beside the application name, omitting the
source commit suffix and retaining any prerelease label.

For a Windows audio diagnostic package, pass `-AudioDiagnostics` and a separate
output directory, for example `-Version 0.9.9-beta.1` with
`-OutputDirectory artifacts/audio-diagnostic-release`. The package includes an
enabling marker and [capture instructions](AUDIO-DIAGNOSTICS.txt). This creates
local package artifacts; it does not publish a GitHub release.
Test launchers in `current/` compare event-driven/polling output with requested
10/50 ms buffers, plus two display modes with frame diagnostics. Each creates a separate capture directory and ZIP beside `current/`,
including the build manifest, run profile, editor log and audio logs. The package
check verifies the launchers and actual diagnostic log creation.

Normal packages and the GitHub release workflow omit `-AudioDiagnostics`, so
detailed audio logging is off by default. The manifest records `audioDiagnostics`,
and the package check verifies that it matches the enabling marker. A disabled
logger creates no diagnostic file, queue, background writer, or PCM probe. Normal
error logging remains available. Explicit diagnostic capture can still be enabled
as described in the [audio reference](../src/FruitsAtelier.App/Audio/REFERENCE.md).

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
GitHub Releases source, excludes prereleases, and checks on every
startup when automatic checks are enabled. Manual checks and retries remain available. Downloaded updates are
retained across launches but never applied implicitly. Development builds do not
self-update. macOS packaging does not yet include an updater.

When a stable release's list entry lacks the Windows feed or full package, the
client fetches that release by its numeric ID before looking for updates. The
resolved assets are also used for package downloads. A failed lookup or an empty
or incomplete update asset set reports a failed check, rather than claiming the
installed version is current. Releases containing only legacy ZIPs or other
platforms' assets remain outside the Windows update feed.

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

The release source is the **merge commit of the release PR on `main`**. Development
continues on `dev`; merge `main` back into `dev` after each release PR so `dev`
contains the published history. Use a merge commit for `dev` → `main` PRs to retain
the shared ancestry. `main` requires a PR and passing `windows` and `macos` checks
against an up-to-date base. Do not bypass protection or force-push either branch.
A package built locally from `dev` is a preflight check; the tag workflow builds
the published artifacts.

1. On `dev`, commit and push the reviewed changes. Set `Directory.Build.props` to
   the release version, update the version shown in all four READMEs and this guide,
   and add user-facing notes at `docs/releases/vMAJOR.MINOR.PATCH.md` (use the full
   tag for prereleases). Keep both release lock files committed. Run the relevant
   checks in [Building and Testing](TESTING.md), and wait for the **Desktop
   regression** on `dev` to pass on Windows and macOS. Fix failures on `dev` and
   wait for the new commit's checks; an earlier passing run does not validate a
   later commit.
2. Open a `dev` → `main` pull request. If `dev` is behind `main`, merge
   `origin/main` into `dev` and push before completing the PR. Wait for the PR's
   required checks on its final revision, then merge it through GitHub using
   **Create a merge commit**. Record the PR's final head SHA and resulting merge
   SHA. Use those recorded commits for release verification, since `dev` may
   advance after the PR merges.

3. Fetch and verify the release commit, then synchronize `dev`. The following
   PowerShell commands require a clean working tree. Replace the two placeholders
   with the full SHAs from the merged PR:

   ```powershell
   $releaseHead = '<final PR head SHA>'
   $releaseCommit = '<PR merge SHA>'
   git fetch origin --prune --tags
   if ($LASTEXITCODE -ne 0) { throw 'Fetch failed.' }
   git merge-base --is-ancestor $releaseHead $releaseCommit
   if ($LASTEXITCODE -ne 0) { throw 'Release commit does not contain the reviewed PR head.' }
   git merge-base --is-ancestor $releaseCommit origin/main
   if ($LASTEXITCODE -ne 0) { throw 'Release commit is not on remote main.' }
   git switch dev
   if ($LASTEXITCODE -ne 0) { throw 'Cannot switch to dev.' }
   git merge --ff-only origin/dev
   if ($LASTEXITCODE -ne 0) { throw 'Resolve local dev divergence before continuing.' }
   git merge --no-edit origin/main
   if ($LASTEXITCODE -ne 0) { throw 'Resolve and commit the merge before continuing.' }
   git push origin dev
   if ($LASTEXITCODE -ne 0) { throw 'Dev synchronization was not pushed.' }
   git merge-base --is-ancestor origin/main origin/dev
   if ($LASTEXITCODE -ne 0) { throw 'Remote dev is still behind the fetched main.' }
   ```

   When `dev` has no additional commits, this synchronization is a fast-forward;
   otherwise it creates a merge commit preserving the newer development work.

4. Create the annotated `vMAJOR.MINOR.PATCH` tag at `$releaseCommit`,
   optionally followed by `-alpha.N`, `-beta.N`, or `-rc.N` (N starts at 1). Numeric
   version components must be at most 65534. Confirm that the version in
   `Directory.Build.props` and `docs/releases/<tag>.md` at that commit match the
   intended release. In the same PowerShell session, replace the tag placeholder:

   ```powershell
   $tag = 'v<MAJOR.MINOR.PATCH>'
   if (git tag --list $tag) { throw 'Tag already exists; do not replace it.' }
   git tag -a $tag $releaseCommit -m "FruitsAtelier $tag"
   if ($LASTEXITCODE -ne 0) { throw 'Tag creation failed.' }
   $tagCommit = git rev-parse "refs/tags/$tag^{commit}"
   if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $releaseCommit) { throw 'Tag points to the wrong commit.' }
   git push origin "refs/tags/$tag"
   if ($LASTEXITCODE -ne 0) { throw 'Tag push failed.' }
   ```

   Keep the tag on the recorded release commit even if either branch advances.
   The workflow checks tag syntax and identity; it does not enforce `main`
   ancestry or PR approval. These checks are the release maintainer's responsibility.

5. Wait for the tag-triggered **Windows release** workflow. It validates the tag,
   reuses matching desktop regression evidence when available (otherwise running
   Windows regression), packages that exact commit as a self-contained ZIP, runs the
   extracted executable, checks its bundled runtime, and publishes a GitHub
   Release. Suffixed tags become prereleases. The workflow uses the repository
   `GITHUB_TOKEN`; no personal token is needed. Actions must be enabled and
   repository policy must allow the release job's `contents: write` permission.
6. Verify the published Release is not a draft and contains the versioned ZIP and
   SHA256, fixed-name ZIP and SHA256, full `.nupkg`, and
   `releases.win-x64.json`. Compare the ZIP checksum and confirm a public download
   succeeds. Confirm the packaged `build-info.json` records `$releaseCommit` and
   the intended version. Verify anonymous access to
   `https://api.github.com/repos/frankhjwx/FruitsAtelier/releases?per_page=10&page=1`:
   the published release's embedded `assets` list must include
   `releases.win-x64.json` and its matching full `.nupkg`. Checking the separate
   `/releases/<id>/assets` endpoint or direct download URLs alone is insufficient;
   Velopack skips releases whose embedded asset list lacks the update feed.
   Confirm an older stable installation discovers the new version before
   announcing in-app update availability. Fetch again and verify
   `git merge-base --is-ancestor origin/main origin/dev` succeeds; if `main`
   advanced, repeat the synchronization before handing off on `dev`.

### CI validation and reuse

**Desktop regression** runs on pushes to `dev` and PRs targeting `main` or `dev`.
Pushes to `main` and tags do not start another desktop run. Its required `windows`
and `macos` jobs keep their existing names; `main` still requires both checks and
an up-to-date PR base. New commits cancel obsolete runs for the same branch or PR.

Each job compares the checked-out Git tree (the merge-result tree for a PR) with
up to 30 recent successful `dev` push runs of `desktop.yml` in this repository.
Reuse requires identical tracked content, including workflows, scripts and lock
files, and successful Windows and Mac regression steps that actually executed.
Runs that only reused evidence cannot supply it. The source run must have completed
successfully, including Windows package/upgrade checks and Mac packaging. The job
summary links the original run. A changed merge result, missing evidence, unavailable
API, or insufficient read permission falls back to normal regression. An unfinished
dev run is not evidence: wait for dev validation before opening the release PR to
avoid overlapping full runs.

The tag workflow uses the same evidence to avoid repeating Windows regression.
It always builds a fresh package from the tagged commit with the release SDK and
runs the extracted-executable check. Version/commit metadata, bundled-runtime checks,
checksums, publication and anonymous updater discovery remain part of each release.
NuGet package caches reduce restore downloads; compiled outputs and release packages
are not reused. Already-compressed release assets are uploaded without recompression.

Use **Run workflow** on Desktop regression to force a full check. The manual Windows
release entry always builds and tests an existing tag and stores downloadable Actions
artifacts without publishing a GitHub Release. Manual runs do not reuse validation.

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

## Discord announcement

After each GitHub release passes publication and download verification, provide
an English Discord announcement in a fenced `markdown` block in the release
handoff. Keep the copyable message at or below **2,000 characters**, counting
Markdown, spaces, newlines and URLs (excluding the surrounding code fence).
Check its length before delivery. Save a copy under
`artifacts/release-<tag>/announcement.md`; generated announcements stay out of Git.
The maintainer copies the message to Discord.

Use the following order, matching the established announcements:

1. A bold version link: `**[<tag> Release](<GitHub release URL>)**`.
2. `**FruitsAtelier <tag>** is now available! :tangerine:`. Stable releases from
   v1.1.0 onward omit the Beta product label.
3. `**What's new**` with concise, user-facing bullets and bold feature names.
4. `**Fixes & improvements**` with the most useful fixes. Omit an empty section.
5. `**Download / Update**` with the release's fixed-name **Windows x64 ZIP**
   link, extraction and launch instructions, and a reminder to keep `Update.exe`
   and `current/` together. Include **Settings → Application updates**, then
   download, save, and restart only after verifying discovery by an older stable
   installation.
6. `Feedback and bug reports are welcome!`

Base every claim on the released diff and verified assets. Prioritize highlights
and shorten bullets to fit the limit; the release link supplies the full notes.

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
