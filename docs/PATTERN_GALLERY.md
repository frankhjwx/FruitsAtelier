# Pattern Gallery

Open **Layout → Pattern Gallery** to show the dockable gallery. The default layout
includes it in the right dock. Cards fill the panel, with a skin-rendered thumbnail
above each pattern name. A single row of coloured tag names sits immediately above
the name; a `+N` chip represents tags that do not fit. The enlarged preview displays
source information below its title, followed by all tags with a compact **Edit Tags** button beside them.
On cards, when a tag row does not fit, names longer than five Unicode characters
show their first five characters followed by an ellipsis. A row that fits retains
full names. Details always show complete tag names, wrapping as needed; cards keep the `+N` overflow indicator. **Edit Pattern** and **Go to Original Map** share one row of outlined buttons below the preview.
If the tags exceed the available
header space, scroll within the tag area to read the remaining tags independently
of the pattern preview. **Ctrl + wheel** over the card grid changes card size from
50% up to a size that fits two cards across the content area, with space for the
scrollbar. Narrow docks retain at least the original 100% maximum. Resizing the dock
changes the number of columns and clamps the card scale to the new maximum.
Select a card to open a larger preview beside the panel;
use the wheel, left-button drag or middle-button drag vertically within that preview to inspect a long pattern.
The enlarged panel blocks pointer input and hover feedback from controls underneath it.
The enlarged preview follows the canvas scroll direction, including the reverse-scroll setting.
Its scrollbar starts at the bottom and moves upward when browsing later objects above.
Use **Alt + wheel** over the enlarged preview to zoom. The initial scale is the
maximum; zooming out stops when the complete pattern fits. Zooming keeps the
time under the pointer in place where the preview bounds allow.
Previews start at the bottom and center the actual pattern horizontally, independent
of its original playfield position. This alignment does not change saved coordinates.
Right-click a card to rename or delete the saved pattern. Renaming retains its
identity, source information and geometry; deleting removes it from the local gallery.

The toolbar contains search and **Tag Filters**; then **Save selection**,
**Import patterns**, and **Export**. The **Tag Filters** menu contains tag filters and
**Manage Tags** in its first row, beside **All** and **None**, separated from the tag
list by a divider. **All** selects every tag; **None** clears the selection. Search
follows Library matching: case-insensitive Unicode normalization and whitespace-separated words
that must all occur in the pattern name, source artist/title/mapper/difficulty or
tag names. Ctrl+F focuses the search field. Selecting multiple tags matches any
selected tag. Filters affect browsing within the current folder only.

## Folders and selection

The **Category** list stays visible to the left of the card grid, separated by a
vertical divider. Click a category to display its patterns. The list scrolls
independently of the grid. The hierarchy has one folder level and one pattern level.
Right-click blank space in the list and choose **New folder** to create one.
A category's context menu offers **Rename Folder**, **Delete Folder**, and export.
Deleting a folder also deletes its patterns. **Merge folders** appears when multiple
folders are selected; **Paste** appears only when the gallery clipboard contains patterns.
Default always exists and cannot be renamed or removed. Existing flat galleries
load into Default. Captures save into the current category.

Ctrl-click toggles individual cards or folders. Shift-click selects the inclusive
range from the previous selection anchor in displayed order, including entries
outside the viewport. Ctrl+Shift adds that range to the existing selection.
Ctrl+A selects all entries in the focused gallery view. Search and tag changes
remove selections that are no longer visible in the filtered list.

Use Ctrl+C / Ctrl+X, open a destination folder, then Ctrl+V to copy or move selected
patterns. These commands are also available in the card context menu; right-click
an empty area or a folder for Paste. Copies have independent identities. Cutting
retains the source entries until a successful paste, then clears the move clipboard.
The clipboard belongs to the current gallery and clears when its workspace changes.
Gallery shortcuts operate only while the gallery has input focus.

Select multiple categories and right-click **Merge folders**, then select the destination
from the selected folders. All contents move to that destination; merged folders
are removed. If Default is merged into another folder, it remains as an empty folder.

Use **Edit Tags** in a pattern's details to assign existing tags or create one.
Each tag has a name and colour. **Tag Filters → Manage Tags** creates tags independently
of patterns and renames, recolours or removes a tag across every assigned pattern.
Choose **New tag** after the final visible tag in the list, enter its name and
colour, then **Create tag**. Selecting an existing tag opens its name and colour
for automatic editing. Valid changes take effect and save automatically; continuous
input and colour dragging are coalesced before writing the gallery. Switching tags
or closing the dialog flushes pending edits. Invalid names, duplicate names and
incomplete hex colours do not replace the last valid value.

Both **Edit Tags** and **Manage Tags** open the tag dialog. **Edit Tags** shows all
available tags with checkboxes: click a row to assign or unassign its tag to the
current pattern. Creating a tag assigns it immediately. Unassigned tags remain
available after reopening the gallery or removing their last assignment.
**Manage Tags** edits shared names and colours and can delete a tag throughout the
gallery. Drag a row in **Manage Tags** to change its position; the insertion line
marks the destination. While dragging, hover a paging arrow to reach another page.
Releasing outside the list cancels the move. Order saves automatically and is used
by the tag picker and preview labels; name and colour edits retain that position.
Empty lists explain how to create a tag, and paging controls appear only
when the list spans multiple pages.

The colour picker uses the same saturation/value chart and hue strip as Song Setup;
the hex field accepts an exact `#RRGGBB` value. Existing tags need no save action.
**Cancel** discards only an uncreated new tag.
Tag changes persist in the gallery without changing the source map or rebuilding
preview geometry. Tags are shared with `.fap` packages; existing local colours
take precedence when importing another pattern with the same tag name.

## Capture and sharing

Select consecutive complete parent objects on the map, choose **Save selection**,
and enter a name. Save selection is disabled without a complete object selection.
The object action menu also offers **Save to Pattern Gallery** for one or multiple objects,
including when the gallery panel is hidden. Fruits, FSliders, imported sliders, streams, stacks and banana
showers are supported. A saved pattern is an independent snapshot: editing its
source map does not change it. Stream and stack parameters and individual fruit
adjustments remain editable. Source artist, title, difficulty, creator and start
time are retained alongside the authoring data, AR and timing context.

**Export** offers selected patterns, the current folder, or the complete gallery.
The category context menu exports the selected folders.
Whole-gallery export includes unassigned tags and patterns hidden by filters.
Folders, including empty folders, travel inside the `.fap` package. A flat export
containing only Default's patterns receives a folder named after the export filename;
explicit folder exports preserve the folder name.

After choosing an import file, select **Import as new folders** (the default choice)
or an existing destination, including Default. New folders preserve package names;
conflicts are resolved case-insensitively by appending ` (1)`, ` (2)`, and so on.
Packages without folders use the import filename. Choosing an existing destination
places all imported patterns in that folder. Repeated imports create independent
copies when pattern IDs already exist; local patterns are never overwritten.
Packages contain authoring data, not audio, backgrounds,
skins or local source paths. Custom sample references do not bundle sample files.

The local gallery lives in `Patterns/gallery.fap` under the configured workspace.
Local source-file hints are stored separately in `Patterns/sources.local.json`.
Packages are ZIP files containing `manifest.json` and one project document per
pattern. Version 4 stores a flat folder catalog with IDs, names and pattern IDs.
Every pattern belongs to exactly one folder. Versions 1–3 remain readable: version 1
has no tags, version 2 has per-pattern tags, and version 3 adds the ordered shared
tag catalog. Catalog order preserves user-defined tag order, including unassigned
tags. Import validates IDs, unique folder names and membership, tags, exact archive
entries and bounded sizes before publishing the complete result. Writes use a temporary file and atomic
replacement. The package limit is 4,096 patterns, 64 MiB compressed and 256 MiB
expanded; individual documents also obey the project format's limits.

## Placement

Drag a card into the Catch canvas. The first object snaps to the current beat grid;
relative beat offsets determine the remaining times at the destination BPM.
Horizontal dragging moves the group within the 0–512 playfield. The complete
pattern is constrained together, so placement does not clamp individual objects.
Dropping creates one undoable edit with fresh object identities.

After dropping on the target map, choose **Keep horizontal spacing** or
**Preserve horizontal speed** from the placement menu, or cancel the drop.
**Preserve horizontal speed** scales horizontal distances by the time
ratio: moving a pattern from 120 BPM to 180 BPM makes its width two-thirds as large.
Without this option, horizontal distances remain unchanged. A pattern that cannot
fit the playfield is rejected. Imported slider geometry scales uniformly when
needed to preserve path progress. Curves and repeated sliders require a consistent
time ratio throughout each object; absolute-speed placement requires a consistent
ratio across the pattern. A placement crossing incompatible BPM changes reports
an error rather than distorting the saved geometry.

Destination timing, SliderTickRate and droplet settings control inserted objects.
The source timing, STR and AR remain available for the saved preview. Its vertical
spacing uses the saved AR; thumbnails fit the pattern by scaling both axes together.
Red hyperdash fruits are calculated from the complete preview sequence and use
the current skin's hyperdash colour. Preview sliders
are derandomized and banana showers use a fixed preview seed; preview processing
does not alter the saved data. Icons and large previews use the current skin with
white combo tint, without snap grids or movement helper lines. Slider bodies remain
visible, while streams and stacks appear as their generated fruits.

## Editing and source navigation

Click a card to open its details. **Artist - Title (Mapper) [Difficulty]** appears
below the title, followed by tags and **Edit Tags**. **Edit Pattern** and
**Go to Original Map** share the footer row.
Choose **Edit Pattern** to edit its snapshot in the main canvas. Only that pattern's
objects are present. The **Editing pattern** banner identifies the mode and offers
**Save and return**, **Discard and return**, and **Go to Original Map**.
Saving replaces the gallery snapshot; discarding keeps its prior version. Both
return to the previous map and editing position, retaining its unsaved content,
undo history, selection and view state. Ctrl+S saves the pattern in this mode.

**Go to Original Map** in the preview locates the original difficulty locally and seeks
to the saved start time. If the current workspace has unsaved content, the normal
Save / Discard / Cancel dialog is shown before navigation. Shared patterns can
resolve a source through the local Library; missing or ambiguous sources report
an error instead of opening a guessed match.

Using **Go to Original Map** while editing a pattern first asks whether to save or discard
the pattern draft, with Cancel keeping the draft open. Navigation then retains the
previous map in memory without requiring it to be saved. **Return to previous map**
restores it, including unsaved edits and history. If the source map was edited in
the meantime, returning asks how to handle those source-map changes.

## Rendering and editing boundaries

Preview conversion runs on a single background worker. Up to 64 thumbnail geometry
entries retain at most 512 sampled objects and 256 sampled slider spans with 10
points each, preserving each retained span's endpoints. The grid keeps its visible
working set within 48 cards by bounding the scrollable viewport
in exceptionally tall layouts. Card scale affects layout and raster size only. This lets
visible thumbnails finish warming and the gallery stop requesting idle frames.
One separate full-resolution preview is retained for the selected popup; it is
released when closed or replaced. Each full preview's path data is limited to
1,000,000 points. Sampling affects thumbnails only, never saved or placed geometry.
Windows and macOS thumbnail raster caches retain at most 128 textures / 16 MiB,
keyed by preview identity, skin, size and render scale. Snapshot changes invalidate geometry; skin and scale changes
invalidate the affected raster entries. Renderer resources retire with a difficulty.

Pointer movement updates only the requested time and horizontal offset. Placement
preparation uses local pattern data and a target snapshot captured once per gesture.
Full-map export validation runs on a worker at drop time; publication checks the
owning history, target content and conversion settings before committing the edit.
Cancelled or stale work cannot publish into a replacement document.
