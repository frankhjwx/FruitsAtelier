# Experimental Discord test packages

The `Discord preview package` workflow tests an exact commit from a
`codex/discord-<task-id>` branch. An external controller supplies the Discord task
ID, branch name and full commit SHA through `workflow_dispatch`. The workflow
must be installed on `main`; it rejects a SHA that no longer matches the named
branch before checking out source.

Windows and macOS checks run on GitHub-hosted runners with read-only repository
permissions. The workflow receives no OpenAI, Discord or source-write secrets.
Windows runs the regression suites, prepares the pinned Mapset Verifier, and
builds and verifies a self-contained package. macOS runs the existing regression
script with device tests disabled.

Successful Windows jobs upload the package, checksums and corresponding-source
archive to this repository's Actions artifacts, retained for seven days. Users
with a GitHub login can download them from the run page. These packages use
`0.0.0-alpha.<run-number>` and should be extracted into separate test installations.
The workflow does not publish a Release, update the stable feed or merge source.
Check the whole run's conclusion: a Windows artifact can exist even when macOS
validation fails.
