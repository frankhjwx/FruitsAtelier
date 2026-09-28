# Workspace synchronization

An FA difficulty keeps its authoring identity independently of its `.osu` filename,
directory, metadata, online IDs and audio. Synchronization never reconstructs an
unchanged object's controls from exported geometry. A known external file has one
active FA owner in the configured workspace; historical and recovery copies are
excluded from ownership.

## Discovery and identity

Opening or resuming an existing project, checking synchronization from a difficulty
tab, and saving a linked difficulty check its external files. The editor also
checks periodically in the background. Missing paths trigger a wider Songs search.
Scans and merge preparation use detached snapshots; stale results are discarded.
Background scanning does not run conversion in pointer or painting hot paths.
During checking and applying synchronization, editor input is blocked and the
bottom-left status bar shows progress. The check itself has no dialog; differences
requiring a choice and synchronization problems open the resolution interface.

Existing paths are checked by content, not only modification time and size. When a
path disappears, the last synchronized object sequence locates rename candidates.
Positive beatmap and set IDs provide additional candidates when objects changed.
Multiple candidates require explicit association. Identical copies at different
live paths remain separate files. An unavailable root or an unreadable live file
is not evidence of deletion.
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
Ctrl+wheel zooms them together. Field differences appear above the canvases. The
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
source IDs. Unchanged retained differences are labelled **Already resolved** and can
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
