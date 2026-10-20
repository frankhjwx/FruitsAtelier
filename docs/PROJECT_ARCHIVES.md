# Portable FA projects

`.faz` (Fruits Atelier Zip) carries native authoring data between computers.
Use File → **Export project (.faz)** or the corresponding library card action,
then choose the complete or FA project option before selecting a save location.
Editor exports capture all open
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
Link imports immediately check every difficulty and open synchronization review
for differences. An unchanged Songs file hash does not bypass review when the
imported authoring differs; a shared baseline is established only after resolution. Newly installed Songs maps receive a baseline from the same native
snapshot that generated the OSZ. Existing target ownership prevents duplicate
synchronization owners; the import screen offers to open a recommended target's
existing project. A repeated archive offers the previously imported project.
Importing a separate copy gives its project and difficulties new local identities.

## Desktop integration

On startup, Windows checks the current user's `.faz` and `.fad` registrations and automatically
registers the current executable and document icon when missing or outdated.
Administrator access is unnecessary. An unchanged registration is left intact.
Windows may retain a user-selected default application. Double-click opening is forwarded to
an already-running FA instance through a pipe restricted to the current user.

macOS bundles declare the `.faz` and `.fad` document types and their independent document
icon. Finder open events are queued until startup dialogs and other modal operations
finish. Both hosts retain unsaved-change prompts when replacing an open project.

## Portable individual difficulties

`.fad` (Fruits Atelier Difficulty) carries one difficulty's native authoring data
and available resources. Use **File → Export as .fad…** for the current difficulty,
or **Export as .fad…** in a difficulty tab's context menu for that tab. Unsaved edits
are included; exporting does not change the saved state or local Songs associations.

Open the corresponding project in the editor, then use **File → Import difficulty
(.fad)…**, open the file, or drop it onto the editor. Library imports, drops and
desktop activation show an error asking the user to open the corresponding project
first; a `.fad` never creates a standalone library project.

Matching uses the native difficulty ID first. For copies with different local IDs,
the song must match by positive BeatmapSetID or nonempty Artist and Title; a unique
positive BeatmapID, then an exact difficulty name, identifies an existing difficulty.
A missing difficulty is added. Existing difficulties enter the synchronization
comparison UI before changes are applied. Cancel and Keep local preserve current
content. Metadata, timing and section choices are independent of the native object
and FA settings group, which is reviewed as one unit to preserve exact editable
curves, handles, streams and other authoring state. Updates form one undo step and
retain the receiver's native difficulty ID, local audio and Songs associations.

Imported resources are checked before application. Different files with the same
relative name cause an error; existing resources are never overwritten. Added
resources remain available after undoing an update because other difficulties may
use them. The original `.osu` is not written by importing a `.fad`; save and export
follow the usual workspace rules. After importing a collaborator's difficulty,
use **Collab Mapping → Sync references** to update the destination difficulty.

The `.fad` ZIP uses the version 1 container described below with
`Format: FruitsAtelierDifficulty`, `Complete: false`, and exactly one difficulty.
Package resource paths use relative paths with slash separators. Local source,
export and synchronization associations are not carried into the receiving project.

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
