# Portable FA projects

`.faz` (Fruits Atelier Zip) carries native authoring data between computers.
Use File → **Export complete project (.faz)** or **Export FA project (.faz)**,
or the corresponding library card actions. Editor exports capture all open
difficulties, including their unsaved edits, without changing save state or Songs.
Library exports read the persisted project.

Complete archives contain a playable `.osz` and the native project. FA-only archives
contain the native project and resources without an OSZ. Both retain editable
curves, handles, difficulty order, timing, metadata and project notes. Audio,
custom samples, images, videos and storyboard files are included when available.
Missing required audio stops export; optional resources may be absent.

## Import and association

Open or drop one `.faz` on either the library or editor. Unsaved edits use the
normal save/discard/cancel prompt. Processing runs in a background worker.
The import choices appear after archive validation without waiting for a library
scan. Association recommendations wait for an existing scan in the background,
then query its index without starting a second full scan. Independent import,
installation and cancellation remain available while recommendations load.

- **Independent project** installs native data and resources in the workspace.
  No Songs files are written or linked. With Songs configured, the library shows
  the project as **Not in osu! Songs**.
- **Link existing maps** shows local projects as library-style cards with artwork,
  metadata and difficulty counts. Choose one Songs folder containing a unique match
  for every archived difficulty; additional local difficulties are allowed. Incomplete
  folders and matches spread across folders cannot be linked. Locate a project
  manually by choosing any `.osu` in its folder; the same completeness check applies.
  Linked projects use local resources and install no packaged resources.
- **Install in Songs**, available for complete archives, creates a new uniquely
  named Songs folder and a linked workspace project. To reuse existing maps,
  choose **Link existing maps** instead. Configure Songs in Settings first if absent.

Recommendations search the library database by positive BeatmapID first, then
positive BeatmapSetID, then exact, case-sensitive `Artist` and `Title` fields.
The artist/title fallback requires both nonempty fields and does not use substring,
romanisation or fuzzy search. Within a set, BeatmapID or the exact difficulty name
identifies a difficulty. A source SHA-256 can disambiguate duplicate versions;
otherwise ambiguous folders are excluded. Recommendations use the library index;
candidate folders are reread during discovery and before association. IDs establish identity, not content equality.

Associating existing maps preserves native editing data and does not write `.osu`.
Differences enter the existing synchronization review before editing or subsequent
publication. Newly installed Songs maps receive a baseline from the same native
snapshot that generated the OSZ. Existing target ownership prevents duplicate
synchronization owners; the import screen offers to open a recommended target's
existing project. A repeated archive offers the previously imported project.
Importing a separate copy gives its project and difficulties new local identities.

## Desktop integration

On startup, Windows checks the current user's `.faz` registration and automatically
registers the current executable and document icon when missing or outdated.
Administrator access is unnecessary. An unchanged registration is left intact.
Windows may retain a user-selected default application. Double-click opening is forwarded to
an already-running FA instance through a pipe restricted to the current user.

macOS bundles declare the document type, `.faz` extension and independent document
icon. Finder open events are queued until startup dialogs and other modal operations
finish. Both hosts retain unsaved-change prompts when replacing an open project.

## Version 1 container

The ZIP root contains `manifest.json` and `project.catchproj`. The latter uses the
existing versioned native multi-project serializer; import materializes the current
workspace `.catchdiff` layout. The manifest has `Format: FruitsAtelier`, `Version: 1`,
`Complete`, `Files` (relative file paths to SHA-256 hashes), and `SourceHashes`
(native difficulty IDs to original source-file hashes), and `ResourceRoots`
(native difficulty IDs to their relative resource directories). The JSON property names
are case-sensitive. Unknown container versions and unknown manifest fields fail.

Complete containers include `beatmap.osz`. Native references point into its logical
`song/` extraction root; resources are not duplicated in the outer archive.
FA-only containers use `resources/<difficulty-ID>/` and a `.fa-resource` reference
anchor, allowing resources to survive without an external `.osu` association. Difficulties
sharing a resource directory and audio reuse one packaged and installed resource copy.
Source/export absolute paths, synchronization baselines, export consent, library
databases and recovery history are not transported.

Import bounds archives to 1 GiB, 20,000 cumulative entries and 1 GiB cumulative
extracted bytes, including nested OSZ data; ordinary entries are limited to 256 MiB,
while the embedded OSZ container may occupy up to 1 GiB.
It rejects path traversal, links, duplicate case-insensitive entry names, invalid
native paths and checksum mismatches. Hashes detect corruption, not authenticity.
Resource name collisions in complete OSZ export fail instead of replacing content.

Imports publish only new directories. A flushed workspace journal records the
project, resource and optional Songs directories. Recovery rolls back unpublished
imports, or retains a published project and its dependencies. Library scans recover
pending journals before indexing. The project manifest retains `ArchiveFingerprint`
to identify repeated packages across later saves; it is not a synchronization identity.

## Deletion and temporary storage

Deleting a local project defaults to removing its local files and version history.
Select **Keep a recovery backup** in the confirmation dialog to retain a restorable
copy instead. Unreferenced FAZ and audio-import resource directories are removed
with permanent deletion; resources referenced by other projects or retained
snapshots are preserved. Songs files are not removed.

The editor reclaims abandoned `.faz-staging` directories on startup and shutdown.
Active staging directories hold exclusive leases so another running instance's
import or export is preserved. Interrupted operations are reclaimed on the next
startup after the operating system releases their leases.
