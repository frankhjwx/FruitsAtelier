# Mapset Verifier

Open **Edit → Mapset Verifier**, or add the dock tab through **Layout**.
The panel offers **General** and the current difficulty. Switching the editor's
difficulty tabs switches the canvas and the difficulty report together. Each
timestamp in an issue seeks independently; issues associated with another loaded
difficulty select that difficulty before seeking. Groups collapse and provide
**Copy all**. Check titles and messages retain upstream English wording.

The top row contains General/current-difficulty status tabs, a refresh icon and
the upstream version. Status icons summarize the complete category, independently
of display filters. Colored severity pills select one severity at a time; clicking
the active pill restores all severities. The default view is unfiltered.
Error/information pills appear when present. Filters use the cached report and
preserve the current check snapshot. Chevrons expand or collapse check groups.
The right scrollbar supports thumb dragging and track clicks. Clickable timestamps
show a hand cursor and an underline on hover. **Copy all** uses a hand cursor
and hover highlight, then shows the same localized clipboard confirmation as
osu! Web for two seconds, fading during the final 400 ms.

**Refresh** checks an exported snapshot of all loaded difficulties, including
unsaved edits, with the current tiny-droplet compensation setting. It does not
write original beatmaps or create undo entries. Content commits, undo/redo and difficulty-set changes schedule an automatic check
after one second without another edit. Checks wait for editing gestures and
transactions to finish and for the panel to be visible. New edits retire an
in-flight check so older results cannot publish. Viewport, selection and playback
position changes do not schedule checks. **Refresh** also runs a check immediately. Difficulty interpretations affect checking only. General checks see the
loaded project and its copied mapset media, rather than every difficulty present
on disk. Media/storyboards and `skin.ini` are copied to temporary storage, with a
512 MiB / 50,000-file limit; conflicting resource names fail explicitly. Checking
runs in a cancellable standalone process with a three-minute timeout.

## Local preparation and AI development

The engine is a separate self-contained GPL-3.0 executable. The editor exchanges
request/result JSON files and does not reference upstream assemblies. The pin in
`eng/mapset-verifier.json` specifies release, immutable commit and archive SHA-256.
Prepare that exact version with Python 3 and the pinned .NET SDK:

```sh
python scripts/Build-MapsetVerifier.py
```

Local builds locate the engine under `artifacts/mapset-verifier/<runtime>/`.
Windows and Mac publication scripts prepare and bundle the pinned engine, its
licenses and corresponding source under `mapset-verifier/`. The adapter source
and its build instructions are in `tools/MapsetVerifier.Host/`.

The Mac app keeps the engine in `Contents/MacOS/mapset-verifier/` and its
dependency licenses and corresponding source in
`Contents/Resources/mapset-verifier/`, so resource directories are not treated as
nested code bundles during signing. Packaging verifies the complete app signature.

AI development follows the upstream-check rule in [AGENTS.md](../AGENTS.md).
`python scripts/Update-MapsetVerifier.py` explicitly checks GitHub's latest formal
release; drafts and prereleases are excluded. Run with `--update-locks` on the
build script when accepting a changed pin, for `win-x64`, `osx-arm64` and `osx-x64`.
`Run-Editor.cmd` and ordinary builds neither select a newer version nor perform
upstream update checks. An offline session can continue using the existing pin.

## GitHub update PRs

The **Mapset Verifier upstream** workflow checks daily at 03:23 UTC and supports
manual dispatch. A newer stable release updates the pin and runtime dependency
locks on `codex/update-mapset-verifier`, then creates or updates a PR targeting
`dev`. It explicitly dispatches **Desktop regression** on that branch because PRs
created with `GITHUB_TOKEN` do not trigger ordinary PR workflows. The regression
includes the actual engine, shared editor interaction checks and desktop packaging.
The workflow does not merge, tag or publish FruitsAtelier releases. Review and
merge the PR, then use the normal [release process](RELEASING.md).

Scheduled workflows become active when this workflow reaches the repository's
default branch. GitHub Actions must permit `GITHUB_TOKEN` to create pull requests.
Build/API failures fail the workflow instead of silently advancing the pin.

## Verification and dependency limits

`App.Tests --mapset-verifier-check` covers timestamp tokenization, active difficulty
identity, task retirement, resource snapshots and interaction without content edits.
`App.Tests --mapset-verifier-engine-check` runs actual upstream checks against an
exported fixture with two unsubmitted difficulties. Windows `--render-check
--mapset-verifier-render-check` checks timestamp input and captures English/Chinese
panels at compact/wide sizes and 96/144/192 DPI in
`artifacts/tests/mapset-verifier/native/`.

The pinned upstream dependency graph currently reports NuGet advisories for
AutoMapper, MessagePack, SignalR and ImageSharp. Those warnings remain visible in
engine builds; the integration does not suppress audit results or silently replace
upstream dependency APIs. The separate process, resource allowlist, cancellation
and timeout limit operational exposure but do not constitute a security sandbox.
Review these dependencies before distributing the engine with untrusted mapsets.
