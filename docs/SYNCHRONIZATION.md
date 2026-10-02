# Workspace synchronization

An FA difficulty keeps its authoring identity independently of its `.osu` filename,
directory, metadata, online IDs and audio. Synchronization never reconstructs an
unchanged object's controls from exported geometry. A known external file has one
active FA owner in the configured workspace; historical and recovery copies are
excluded from ownership.

Synchronization failure messages wrap within the dialog. Scroll over the message
area to read diagnostics that exceed its height.

Saving and synchronizing the open difficulty retain the current timeline position
and viewport. Content-only synchronization keeps the existing audio transport.
Replacing audio, including changed bytes at the same path, reloads it at the
current timeline position.

Saving local changes to Artist, Title, Creator (mapper), or Version (difficulty name)
renames the linked `.osu` to `Artist - Title (Creator) [Version].osu` in its existing
directory. Automatic synchronization and explicit overwrite export both update
source/export associations and baselines to the new path, preserving one difficulty.
Invalid filename characters are removed using the workspace filename rules. A
filename occupied by another file blocks the save without overwriting either file.
Edits to other fields alone preserve the existing filename.

## Discovery and identity

Opening or resuming an existing project, checking synchronization from a difficulty
tab, and saving a linked difficulty check its external files. The editor also
checks periodically in the background. Opening or explicitly checking a project
with missing paths can trigger a wider Songs search.
Filesystem notifications for Songs, the workspace and linked external directories
also queue background checks. Notifications are coalesced after 750 ms of quiet;
they request content inspection rather than directly importing or deleting data.
Associated directory renames trigger wider identity discovery. Library indexing
refreshes for source changes, including newly added sets. Database, temporary and
recovery-history writes are excluded. FA saves and exports are checked against the
accepted content baseline, so their notifications do not reimport unchanged objects.
Bounded notification queues fall back to reconciliation on overflow. Watchers retry
unavailable roots, activation requests a fresh check, and periodic scans remain as
a fallback when notifications are lost. Both desktop hosts release watchers on close.

Conflicts in inactive difficulties update their status without opening a dialog.
Entering the affected difficulty opens its review. A conflict in the current editor
opens review after pointer capture, text entry and other editing dialogs finish.
Browsing the library does not open conflict review automatically. Notifications
received during review remain queued; the displayed comparison and green choices
stay intact. Apply checks the external map and compared audio again. If they changed,
the review is refreshed before applying; choices survive only for unchanged conflict
contents and authoring source identities. Changed audio always requires a fresh choice.

All songs and My projects retain their last loaded, paged results when switching
categories. The selected category refreshes in the background while its cached
rows remain visible. A changed search discards an incompatible cached view;
changing workspace or osu! roots clears both category views.

The My projects library combines saved FA difficulties with newly indexed Catch
difficulties in associated source directories. Counts and detail rows refresh
together without opening the editor. Deleted unimported files leave the list;
saved difficulties with missing sources remain muted, with the card counting
available and missing difficulties separately. Missing references trigger read-only
identity discovery in a separate background queue, including renamed directories.
Indexed rows appear before this search finishes and show pending reference status.
Completed discovery refreshes visible cards and details without clearing the list.
Retiring a library search cancels its pending discovery work. Recovered
references remove the missing badge without duplicating the FA entry. Library
discovery does not save authoring or accept a synchronization baseline; opening the
project performs the normal synchronization and conflict checks.
Scans and merge preparation use detached snapshots; stale results are discarded.
Background scanning does not run conversion in pointer or painting hot paths.
Reference discovery runs in the background without pausing playback or blocking
editing and difficulty navigation. Missing difficulty tabs show "Finding reference..."
while their scan runs, then retain a missing badge if no match is found. Opening a
project or explicitly checking synchronization can search all of Songs for moved
files; periodic checks only inspect associated folders. Edits made during discovery
are compared against the returned candidates again without repeating the file scan.
Only applying changes and resolving conflicts temporarily own editor input. Missing
references alone do not open a dialog; an explicit check or linked save/export offers
repair. Required resources are still validated before export.

Existing paths are checked by content, not only modification time and size. When a
path disappears, the last synchronized object sequence locates rename candidates.
Positive beatmap and set IDs provide additional candidates when objects changed.
Multiple candidates require explicit association. Identical copies at different
live paths remain separate files. An unavailable root or an unreadable live file
is not evidence of deletion.
Rename discovery excludes files already held by surviving exact associations,
including copies with identical objects or online IDs. If multiple missing
difficulties compete for one unclaimed candidate, they require explicit association;
inferred matches alone never trigger duplicate-owner cleanup.
Unrelated malformed or oversized beatmaps are skipped during discovery. A linked
file that is malformed or exceeds the reader limit is reported as unavailable.

New Catch difficulties in associated directories are added without replacing
existing authoring or undo history. The library indexes only Catch difficulties;
other osu! modes are skipped during scanning and import.

## Baselines and conflicts

Each manifest difficulty can reference a separate `.catchsync` baseline through
`SyncFile`: external text,
the corresponding serialized authoring snapshot, per-output-object source IDs,
external and authoring audio content hashes, historical paths and locally retained field overrides. These
sidecars are published atomically with the difficulty files. Older
manifests remain readable. An unchanged legacy fingerprint can establish a baseline;
changed legacy files without a baseline show a two-version comparison. Each differing
field and object/group requires a choice because neither side can be identified as
the source of a change. Review categories appear left to right as General, Editor,
Metadata, Difficulty, Events, Timing, Colours and Objects, followed by audio or
additional sections when present. Clean tabs are grey, unresolved tabs red,
partially resolved tabs amber, and fully resolved tabs green. Clean categories
remain readable. Choices stay in their category; object choices advance to the
next unresolved group there. Whole-map choices remain available.

Applying a resolution retains the comparison with an applying message until
publication finishes. Saved and working authoring are archived once before the
resolution; publication reuses that recovery round. Object-only resolutions retain
the existing audio session and transport position when the audio path and baseline
hash are unchanged.

Conflict review shows FA and osu! on side-by-side editor canvases with synchronized
time ranges and zoom. Their common default scale uses the current FA difficulty's
AR and playfield width, matching the editor canvas. Selecting a conflict restores
that AR scale and locates its start; long groups remain scrollable rather than
being compressed to fit. Current conflicting objects and related curve controls are
highlighted; missing counterparts are labelled. Mouse wheel scrolls both maps and
Ctrl+wheel zooms them together. Other field differences use side-by-side text.
The Objects tab describes the changed properties in each group with explicit left
and right values: time, position, New Combo, colour skip, hitsounds, sample settings,
slider path, span count and length, or banana shower end time. Unmatched entries
are identified by side rather than assuming an ambiguous replacement is a move.
The detail box scrolls independently of the canvases. Canvas time labels and
object references use `mm:ss:ms`, with three millisecond digits.
The optional result pane previews chosen resolutions, using FA for unresolved items;
it does not save or export. File timestamps identify the more recently saved version,
and unsaved FA edits are labelled separately. A newer timestamp does not resolve
individual conflicts automatically.

Review actions have persistent button outlines. Below the per-side choices, the
progress row contains the result-preview toggle; paging and full-version inspection
occupy the next row, followed by whole-map choices, Apply, and Cancel. Paging uses
the same outlined chevrons as other editor controls.

Full-width rectangles mark corresponding object intervals: red for unresolved conflicts,
amber yellow for previously resolved differences, and green for choices made in the
current review. Amber borders, translucent fill and status labels distinguish review
intervals from banana objects. Clicking a rectangle chooses that side for its group,
just like the per-side choice buttons. Every item remains available until Apply,
including green items that can be changed again.
After a choice, the rejected side's interval and highlight use muted grey while
the retained side keeps its resolution color. Action labels use normal weight;
progress and page counts receive stronger emphasis.
Retained FA decisions persist with their external object groups and local
source IDs. Unchanged retained differences are labelled **Resolved, select to re-resolve** and can
be selected again in the comparison. They keep their prior choice by default and do
not prompt automatically. The difficulty-tab synchronization action also opens these
retained differences for review without new edits. Editing that group in osu! again
creates an ordinary unresolved conflict. Moving an object across unchanged
anchors keeps related unmatched removals and insertions in one review group.

Local saving preserves the baseline. Synchronization compares the current external
text and audio content with the last resolved external version, and FA authoring
with the corresponding authoring snapshot. While the external version is unchanged,
any new FA content edit automatically exports the complete difficulty when
synchronization runs. This includes metadata such as Tags, settings, sections,
notes, curves, timing, break reconciliation and undo/redo. Generated objects and SV
are exported together. Source-path discovery and derived audio duration refreshes
do not count as authoring edits.

Automatic export validates the complete map and resources, preserves authoring
and undo history, backs up the external file, and records emitted text and object
source mappings as the new baseline. The existing export recovery receipt protects
an interrupted publication. A final fingerprint check rejects a new external save
instead of overwriting it. Export and conversion run on the synchronization worker,
after active edits finish. Temporarily invalid maps or missing required resources
remain in FA with the export diagnostic; they do not overwrite `.osu` or block
background difficulty discovery. Correcting the content allows a later check to
export it.

Publishing local edits against an unchanged external version keeps playback,
viewport navigation and note editing available. Completion acknowledges only the
published authoring snapshot; edits made during publication remain dirty and are
included in the next comparison. Project/file operations wait for publication.
Conflict resolution and external changes retain their guarded apply boundary.
An automatically completed save does not repeat the export on the UI thread.

A changed external version returns to field/object comparison and resolution.
Without a baseline, differences still require explicit choices. An unchanged
retained choice remains available for review; a subsequent FA content edit exports
the resulting complete FA version against the resolved external version. Timing
comparison includes inherited points. Applying external context retains authoring
objects and rebases that context through local undo snapshots.

General, Editor, Metadata, Difficulty and Colours show every field present on
either side, including unchanged context, in fixed format-field order. Additional
keys follow in ordinal order. Conflicting rows highlight the changed text and allow
choosing either value; unchanged rows are read-only. Events, TimingPoints and
unknown sections use ordered text review. Text pages contain at most
4096 UTF-16 code units plus a boundary surrogate pair. Only the current page is
compared and wrapped; unchanged frames reuse that layout and draw visible rows.
Use the top arrows to change text pages or jump to the first/last page, and the
wheel to scroll within a page. The bottom arrows move between object groups. Choosing
a side applies the complete field or section, including text on other pages.
Full-version inspection remains available for searching or reading a whole storyboard.
Emitted timing that differs from its unchanged authoring baseline does not itself
create a conflict. Accepted choices remain resolved until their contents change.

Object comparisons preserve sequence order and meaningful fields while normalizing
basic numeric spelling and line endings. Slider lengths use 15 significant digits
for comparison, matching osu! save precision; omitted empty edge fields and the
default slider hit-sample suffix compare equal to explicit defaults. Position,
time, curve controls, and non-default sound fields remain significant. Persisted
retained decisions use the same normalization when reviewed again.
Unique unchanged lines anchor changed
runs. Unambiguous same-time/type replacements can be selected separately; uncertain
runs remain explicit groups. Outputs sharing an FA curve resolve together. Keeping
an FA group retains its controls; accepting external geometry can replace those
controls with imported objects. The dialog supports complete FA/external versions,
per-field or object-group choices, and inspection of complete version text. All
choices must be resolved before the affected difficulty can be edited.

An accepted local choice remains pending until explicit export or a subsequent FA
content edit triggers automatic export; another external edit must not silently
overwrite it. Resolution saves authoring and updates the external
observation baseline without implicitly exporting the chosen version.

## Audio

Audio is a versioned resource, not a required identity match. Filename and byte
changes are checked independently. Accepted audio updates reload playback and
waveform state without retiming objects. Conflicting local/external replacements
require a choice. Broken references require locating or restoring audio.

Content-addressed audio copies under `workspace/.sync-history/resources` retain
previous bytes, including when the original file is overwritten. Selecting an old
FA audio version materializes a playable resource reference without overwriting the
new external audio. Saving and export do not imply that a replacement recording is
still aligned with existing object times.

## Missing files and deletion

A missing associated difficulty is muted and labelled in the editor and project
library. Entering it requires restoring the `.osu`, associating an existing file,
or deleting the FA copy. Cancelling does not unlock the affected difficulty.
Unlinked local projects are exempt. Ordinary save never recreates a missing target.

The difficulty-tab **Delete difficulty** action deletes the FA difficulty and its
currently associated `.osu`. Unlinked or externally missing difficulties delete
only their remaining FA data. Shared audio and other resources are not deleted.
Deleting the final difficulty retires its project and returns to the library.

**Delete local version** on a difficulty tab discards its FA authoring and imports
the current associated `.osu` into a fresh local difficulty file. The `.osu` is
unchanged. A missing or ambiguous source stops the operation without discarding
local data. Cancelling the confirmation also preserves the current edits.

The library's **Delete local project** action removes the workspace project and
its source association from My projects, while keeping the source maps in All
songs. Opening a source map again creates a new workspace project. Both local
deletion actions retain recovery copies under `.sync-history`.

Deletion archives saved and current authoring and the external file first. A
deletion journal permits rollback of an interrupted external removal. Empty
projects are retired by directory rename. Recovery copies are retained under
`workspace/.sync-history`; they are excluded from ordinary indexing.

## Version history

The right-hand comparison uses the same category tabs, ordered fields, text
highlights and object-group descriptions as synchronization review. The left side
is the current difficulty; the right side is the selected historical difficulty.
All categories remain available: grey tabs have no differences and red tabs have
changes. Objects open at the first changed group; arrows and highlighted ranges
navigate other groups. Comparison is read-only; restoring remains a separate action.

**Edit → Version history…** lists retained versions for the open workspace project,
newest first. Each entry shows its local date/time, operation, and whether it is the
saved version or the working copy captured before a change. Both are available when
an operation archived unsaved edits. History includes earlier names and difficulties
that were subsequently deleted. It reads both compressed and legacy `.sync-history`
archives. Changed workspace saves also retain their previous authoring state.
Unchanged saves do not add versions. The storage retention policy below still applies.

### Compressed recovery files

New authoring snapshots, synchronization baselines, external `.osu` recovery copies,
and pending export receipts are compressed on each write. Each logical file has a
binary companion with the suffix `.catchbackup`; current workspace authoring files
keep their existing JSON format. Snapshot directories and logical file locations stay
the same, so relative resource paths resolve without extracting a temporary project.
Version browsing decompresses only the selected project file in memory.

The version 1 envelope contains eight bytes `46 41 43 42 01 00 00 00` (`FACB`,
version 1), a little-endian signed 32-bit uncompressed byte count, a 32-byte SHA-256
of the original bytes, and a Brotli payload written with `CompressionLevel.Optimal`.
Readers bound decompressed authoring files to the 128 MiB project limit and verify
the length and checksum. Export receipt containers allow 512 MiB for their embedded
project and synchronization baseline strings; the project reader still enforces its
128 MiB limit. The payload preserves the original serialized bytes, including unknown
source text and synchronization context. This uses .NET's built-in compression on
both platforms. Backup writes favor lower compression cost over the smallest
possible payload. Workspace saves, automatic synchronization and storage maintenance
run on background workers. An ordinary save acknowledges only its captured document;
newer edits remain dirty and undoable. Save-and-close and save-before-export wait for
successful publication before continuing. Automatic synchronization reuses its
saved/current archive when publishing, without creating a second identical save round.

Writes flush a temporary binary file before atomically publishing it. Legacy JSON
snapshots remain readable. History maintenance compresses retained legacy authoring,
baseline and `.osu` files, including files inside retired project directories, only
when compression reduces their size. It verifies each published copy before removing
the original. Conversion preserves snapshot timestamps, identities and all versions
otherwise protected by retention; it does not recompress audio resources.

At startup, a separate worker converts existing uncompressed recovery documents,
when the library is idle. Opening the editor or starting playback defers the next
file; an already started file can finish. It runs below normal priority on Windows
and waits two seconds between files. Compression and staging verification happen outside the
project save lock; publication briefly takes the lock and checks that the source
still contains the original bytes. A flushed, published binary must pass checksum
and byte-for-byte verification before the original is deleted. Changed sources,
conflicting binaries, and failed conversions retain their originals. Closing the
editor or switching workspaces cancels migration; the next startup retries remaining
files. Current project files, library databases, and audio are not converted.

The startup worker also prepares a bounded in-memory index of snapshot path
references. JSON parsing runs outside the project lock. The write-time 100-round
limit reuses this index for immutable binary snapshots while reading current files
and changed snapshots afresh. Length or modification-time changes invalidate an
entry; full storage cleanup still validates all reference documents and audio hashes.

Select a version, then a difficulty. The window compares metadata and shows aligned
current/historical object previews, including editable curves. Both previews use the
active editor's AR scroll scale and initially show its current timeline position,
so a long map remains readable. Scroll the version and
difficulty lists independently. Scroll over either preview to move both timelines:
wheel up views later times and wheel down views earlier times, independently of
Reverse canvas scrolling.
Ctrl+wheel changes their common time span. Up/Down selects versions, Left/Right selects
difficulties, and Escape closes the window. Browsing does not modify authoring or write
external files. Loading, decoding and preview preparation share one background worker
per editor. Navigation replaces the pending selection; closing discards pending work.
A damaged version reports an error without hiding the other retained entries.

**Restore this difficulty** first archives the current project, including unsaved edits,
then restores only the selected difficulty. Existing difficulties with available sources
keep their current source association and gain one undo step. Deleted difficulties and
existing difficulties with missing sources
return with their original identity as unlinked local copies, so missing sources or
another difficulty's live file cannot block restoration. Undo/redo of an existing
difficulty also restores its previous/local association. Other difficulties remain
unchanged. Save to persist the restored version; later synchronization/export follows
the normal rules. Recovery history is scoped
to the current project and does not automatically restore an entirely deleted project.

## Duplicate ownership and interrupted export

Duplicate owners block normal editing, export and external deletion. The user
selects the retained FA version/project. Unique difficulties move into that project
before redundant containers are retired. Conflicting duplicate versions are archived.
This operation does not delete the shared `.osu`. Importing an already open source
selects the existing difficulty instead of adding another owner.

Workspace exports write an authoring recovery receipt before updating `.osu`.
If publication is interrupted, reopening publishes the recorded authoring snapshot
and leaves external bytes alone. A later external edit remains visible as a change.
Recovery and duplicate cleanup favor preserving data over silently choosing a version.

Identity recovery is limited to available evidence and the configured workspace
and source roots. Arbitrarily replaced files, unknown workspaces and indistinguishable
copies cannot be assigned a trustworthy automatic identity. Use explicit association
and version selection in those cases.

See [Building and Testing](TESTING.md) for the regression commands.

## Metadata text review

New external differences in Title, TitleUnicode, Artist, ArtistUnicode, Creator,
Version, Source, Tags, BeatmapID and BeatmapSetID require explicit choices. FA edits
against the unchanged resolved external version use automatic export.
These metadata fields share one scrollable page with aligned FA and osu!
rows. Click a value to retain that side for its row. Changed text fragments use red
for unresolved differences, amber for unchanged previously accepted differences,
and green for choices made in the current review; unchanged text stays neutral.
Rows remain available for changing a choice until Apply. Object conflicts retain
their canvas pages. Field-only resolutions preserve authoring objects and undo
history. An unchanged accepted mismatch remains reviewable; another edit requires
a fresh decision. Other fields and sections use the paged text review described above.

## Storage maintenance

Settings → Workspace contains a vertically scrollable storage section below the
workspace and osu! folder fields. It shows byte totals and shares for projects, imported
songs and skins, audio backups, recovery history, caches, and other files, plus the
eight largest top-level folders/files. A divider separates storage from the folder
settings. The scrollbar and wheel move the whole content
area while Apply stays fixed. **Open workspace folder** opens the active workspace
in the system file manager. Accounting and maintenance run on background workers.
Storage actions use the active workspace, not an unapplied path draft.

Automatic maintenance runs after startup while idle in the library without an open
workspace project, then at most once per day during that app session. Ordinary
history expires after 30 days and each set has a 100-round limit. A round is one
snapshot directory for the whole set; saved and working authoring in that directory
count together. Each new archive enforces this limit for its set immediately,
without compacting legacy data or pruning other sets. The count limit also applies
to snapshots from the last 24 hours. Referenced rounds count toward the limit and
leave fewer slots for ordinary rounds. Active recovery defers pruning; if references
alone require more than 100 rounds, those dependencies remain protected until they
can be released.

A 1 GiB history budget removes older eligible snapshots first. Every set's newest
snapshot and referenced snapshots are protected; the last 24 hours protect against
age/budget pruning but do not exempt ordinary rounds from the count limit. The
budget is therefore a soft limit. Deleted or retired sets retain their newest
recovery copy.

Audio remains content-addressed and deduplicated. Cleanup traces hashes and paths
from current authoring, synchronization baselines, and retained history before
removing old unreferenced audio. Derived playback copies are collected separately
when no retained document references their path, even if the canonical audio hash
is still retained. Resources used by open documents are protected even before
their references are saved. Referenced audio is recovery data, even if the same
bytes currently exist in Songs. Newly captured audio receives a 24-hour grace period
while its baseline is published. Unknown resource filenames are retained. Reference scanning reads explicit path fields and embedded authoring documents;
object source lines and metadata text are never interpreted as paths. Cleanup
refuses linked filesystem paths, aborts before deletion on unreadable reference
documents, and defers when export/deletion recovery or project publication is pending.

**Clean history** applies the same retention policy immediately and schedules retained
legacy snapshots for the same background conversion. **Clear cache**
removes temporary comparison files and reconstructible library map rows, then
reindexes sources. It preserves project/source registrations, version snapshots,
imported music, skins, and current authoring. Both operations may reclaim old
unreferenced audio; neither deletes a referenced audio backup. The page reports
reclaimed bytes or the reason maintenance could not finish.
