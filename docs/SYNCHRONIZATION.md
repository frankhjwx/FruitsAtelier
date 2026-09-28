# Workspace synchronization

An FA difficulty keeps its authoring identity independently of its `.osu` filename,
directory, metadata, online IDs and audio. Synchronization never reconstructs an
unchanged object's controls from exported geometry. A known external file has one
active FA owner in the configured workspace; historical and recovery copies are
excluded from ownership.

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
the source of a change. Choosing an item advances to the next unresolved difference;
previous/next navigation retains choices for review. Whole-map choices remain available.

Conflict review shows FA and osu! on side-by-side editor canvases with synchronized
time ranges and zoom. Their common default scale uses the current FA difficulty's
AR and playfield width, matching the editor canvas. Selecting a conflict restores
that AR scale and locates its start; long groups remain scrollable rather than
being compressed to fit. Current conflicting objects and related curve controls are
highlighted; missing counterparts are labelled. Mouse wheel scrolls both maps and
Ctrl+wheel zooms them together. Non-metadata field differences appear above the canvases. The
optional result pane previews chosen resolutions, using FA for unresolved items;
it does not save or export. File timestamps identify the more recently saved version,
and unsaved FA edits are labelled separately. A newer timestamp does not resolve
individual conflicts automatically.

Full-width rectangles mark corresponding object intervals: red for unresolved conflicts,
amber yellow for previously resolved differences, and green for choices made in the
current review. Amber borders, translucent fill and status labels distinguish review
intervals from banana objects. Clicking a rectangle returns to that item; every item
remains available until Apply, including green items that can be changed again.
Retained FA decisions persist with their external object groups and local
source IDs. Unchanged retained differences are labelled **Resolved, select to re-resolve** and can
be selected again in the comparison. They keep their prior choice by default and do
not prompt automatically. The difficulty-tab synchronization action also opens these
retained differences for review without new edits. Editing that group in osu! again
creates an ordinary unresolved conflict. Moving an object across unchanged
anchors keeps related unmatched removals and insertions in one review group.

Local saving preserves the baseline. Successful export records the actual emitted
text and source mapping. Synchronization compares external fields with the external
baseline, and authoring fields with the authoring baseline. One-sided field changes
merge automatically; different edits to the same field require a choice. Timing is
handled as an ordered section, including inherited points. Applying external context
retains authoring objects and rebases that context through local undo snapshots.

Object comparisons preserve sequence order and meaningful fields while normalizing
basic numeric spelling and line endings. Unique unchanged lines anchor changed
runs. Unambiguous same-time/type replacements can be selected separately; uncertain
runs remain explicit groups. Outputs sharing an FA curve resolve together. Keeping
an FA group retains its controls; accepting external geometry can replace those
controls with imported objects. The dialog supports complete FA/external versions,
per-field or object-group choices, and inspection of complete version text. All
choices must be resolved before the affected difficulty can be edited.

An accepted local choice remains pending until export; another external edit must
not silently overwrite it. Resolution saves authoring and updates the external
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

Deletion archives saved and current authoring and the external file first. A
deletion journal permits rollback of an interrupted external removal. Empty
projects are retired by directory rename. Recovery copies are retained under
`workspace/.sync-history`; they are excluded from ordinary indexing.

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

Differences in Title, TitleUnicode, Artist, ArtistUnicode, Creator, Version, Source,
Tags, BeatmapID and BeatmapSetID require explicit choices, including unilateral edits.
All differing metadata fields share one scrollable page with aligned FA and osu!
rows. Click a value to retain that side for its row. Changed text fragments use red
for unresolved differences, amber for unchanged previously accepted differences,
and green for choices made in the current review; unchanged text stays neutral.
Rows remain available for changing a choice until Apply. Object conflicts retain
their canvas pages. Metadata-only resolutions preserve authoring objects and undo
history. An unchanged accepted mismatch remains reviewable; another edit requires
a fresh decision. Other sections retain their existing three-way merge rules.

## Storage maintenance

Settings → Workspace storage shows byte totals and shares for projects, imported
songs and skins, audio backups, recovery history, caches, and other files, plus the
largest top-level folders/files. Accounting and maintenance run on background workers.
The displayed root is the active workspace, not an unapplied path draft.

Automatic maintenance runs after startup while idle in the library without an open
workspace project, then at most once per day during that app session. Ordinary
history expires after 30 days or beyond 10 snapshots per project. A 1 GiB history
budget removes older eligible snapshots first. Every project's newest snapshot,
referenced snapshots, and snapshots from the last 24 hours are protected, so the
budget is a soft limit. Deleted or retired projects retain their newest recovery copy.

Audio remains content-addressed and deduplicated. Cleanup traces hashes and paths
from current authoring, synchronization baselines, and retained history before
removing old unreferenced audio. Derived playback copies are collected separately
when no retained document references their path, even if the canonical audio hash
is still retained. Resources used by open documents are protected even before
their references are saved. Referenced audio is recovery data, even if the same
bytes currently exist in Songs. Newly captured audio receives a 24-hour grace period
while its baseline is published. Unknown resource filenames are retained. Cleanup
refuses linked filesystem paths, aborts before deletion on unreadable reference
documents, and defers when export/deletion recovery or project publication is pending.

**Clean history** applies the same retention policy immediately. **Clear cache**
removes temporary comparison files and reconstructible library map rows, then
reindexes sources. It preserves project/source registrations, version snapshots,
imported music, skins, and current authoring. Both operations may reclaim old
unreferenced audio; neither deletes a referenced audio backup. The page reports
reclaimed bytes or the reason maintenance could not finish.
