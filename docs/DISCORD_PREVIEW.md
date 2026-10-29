# Experimental Discord test packages

The `Discord preview package` workflow tests an exact commit from a
shared `codex-discord-bug-fix` branch. An external controller supplies the Discord task
ID, branch name and full commit SHA through `workflow_dispatch`. The workflow
must be installed on `main`; it rejects a SHA that no longer matches the named
branch before checking out source.

Preview runs share a fixed concurrency group. The controller accepts one repair
at a time and waits for its package result before releasing its workflow slot.
Subsequent repairs retain the branch's current commits; it is not reset between
requests.

Windows and macOS checks run on GitHub-hosted runners with read-only repository
permissions. The workflow receives no OpenAI, Discord or source-write secrets.
Windows runs the regression suites, prepares the pinned Mapset Verifier, and
builds and verifies a self-contained package. macOS runs the existing regression
script with device tests disabled.

Successful Windows jobs upload the package, checksums and corresponding-source
archive to this repository's Actions artifacts, retained for seven days. Users
with a GitHub login can download them from the run page. These packages use
`0.0.2-alpha.<run-number>` and should be extracted into separate test installations.
The workflow does not publish a Release, update the stable feed or merge source.
Check the whole run's conclusion: a Windows artifact can exist even when macOS
validation fails.

## Download and try a Windows package

1. Sign in to GitHub, then open the shared public FruitsAtelier Actions run link
   (or find the `Discord preview package` run under
   [Actions](https://github.com/frankhjwx/FruitsAtelier/actions)).
2. Confirm that the whole run completed successfully, including both Windows and
   macOS checks, before downloading the experimental package.
3. In the run page's **Artifacts** section, click `windows-experimental-<task-id>`
   (the `windows-experimental-` prefix followed by your Discord task ID) to download
   the artifact ZIP. Extract it to access the application ZIP, checksums and
   corresponding-source archive.
4. Extract the entire application ZIP into a new, separate test installation
   folder, not over your stable installation. Keep its files together and launch
   `FruitsAtelier.exe` from that folder.
