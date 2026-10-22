# Pattern Gallery

Open **Layout → Pattern Gallery** to show the dockable gallery. The default layout
includes it in the right dock. Cards fill the panel, with a skin-rendered thumbnail
above each pattern name. Cards have a fixed size; resizing the dock changes the
number of columns and wraps the cards without resizing the thumbnails.
Select a card to open a larger preview beside the panel;
use the wheel or drag vertically within that preview to inspect a long pattern.
The enlarged preview follows the canvas scroll direction, including the reverse-scroll setting.
Ctrl-click cards to select multiple patterns for export.
Previews start at the bottom and center the actual pattern horizontally, independent
of its original playfield position. This alignment does not change saved coordinates.
Right-click a card to rename or delete the saved pattern. Renaming retains its
identity, source information and geometry; deleting removes it from the local gallery.

## Capture and sharing

Select consecutive complete parent objects on the map, choose **Save selection**,
and enter a name. Fruits, FSliders, imported sliders, streams, stacks and banana
showers are supported. A saved pattern is an independent snapshot: editing its
source map does not change it. Stream and stack parameters and individual fruit
adjustments remain editable. Source artist, title, difficulty, creator and start
time are retained alongside the authoring data, AR and timing context.

**Export all** or **Export selected** creates a `.fap` (Fruits Atelier Pattern)
package. **Import** merges a package into the gallery. Identical entries with the
same ID are skipped; different content using an existing ID is rejected without
replacing the gallery. Packages contain authoring data, not audio, backgrounds,
skins or local source paths. Custom sample references do not bundle sample files.

The local gallery lives in `Patterns/gallery.fap` under the configured workspace.
Local source-file hints are stored separately in `Patterns/sources.local.json`.
The version 1 package is a ZIP containing `manifest.json` and one project document
per pattern. Import validates IDs, names, exact archive entries and bounded sizes
before publishing the complete result. Writes use a temporary file and atomic
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

Double-click a card to edit its snapshot in the main canvas. Only that pattern's
objects are present. The **Editing pattern** banner identifies the mode and offers
**Save and return**, **Discard and return**, and **Go to source**.
Saving replaces the gallery snapshot; discarding keeps its prior version. Both
return to the previous map and editing position, retaining its unsaved content,
undo history, selection and view state. Ctrl+S saves the pattern in this mode.

**Go to source** in the preview locates the original difficulty locally and seeks
to the saved start time. If the current workspace has unsaved content, the normal
Save / Discard / Cancel dialog is shown before navigation. Shared patterns can
resolve a source through the local Library; missing or ambiguous sources report
an error instead of opening a guessed match.

Using **Go to source** while editing a pattern first asks whether to save or discard
the pattern draft, with Cancel keeping the draft open. Navigation then retains the
previous map in memory without requiring it to be saved. **Return to previous map**
restores it, including unsaved edits and history. If the source map was edited in
the meantime, returning asks how to handle those source-map changes.

## Rendering and editing boundaries

Preview conversion runs on a single background worker. Up to 64 thumbnail geometry
entries retain at most 512 sampled objects and 256 sampled slider spans with 10
points each, preserving each retained span's endpoints. The grid keeps its visible
working set within 48 cards by bounding the scrollable viewport
in exceptionally tall layouts. Card dimensions remain fixed. This lets
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
