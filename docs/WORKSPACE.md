# Workspace and Local Library

Switching difficulties within the open project preserves the current timeline position, including when returning to a previously visited difficulty. Opening another project starts at the beginning.

**My Projects** shows the difficulty count and names from each saved workspace manifest. **All Songs** shows the source set's difficulties. Opening or resuming an existing workspace reconciles external files and adds new Catch difficulties while preserving existing edits and undo history. See [Synchronization](SYNCHRONIZATION.md) for identity recovery, conflicts, audio and deletion.

**Favourites** lists saved favourite song sets and local projects. Right-click a library card and use the first menu item to add or remove it. Favourite cards show a small yellow star in the upper-left corner in every category. Favourites and category navigation are stored in `library-view.json` in the workspace; they do not change beatmap content. A song set with an associated project appears once in Favourites, with its project difficulties.

The first Songs export of a local difficulty creates its `.osu` and links it to the existing workspace difficulty, retaining its ID and editable authoring data. Creating a new difficulty from an already associated source saves the current edits into a new workspace difficulty and a new `.osu`, then activates the new difficulty. The original difficulty retains its last saved content and export link; its source `.osu` is unchanged. The new workspace difficulty retains editable FSliders and handles. Files recorded only as older export targets are offered for import when they are not already represented by a project difficulty.

Right-click an editor difficulty tab to open its source `.osu`, saved `.catchdiff`, or containing folder. Files open in a text editor; unavailable files are disabled. For workspace difficulties, the folder action opens the workspace directory. **Open osu! Songs folder** opens the difficulty's source beatmap directory, falling back to its export directory when no source is linked.

The application opens in the Library and supports osu!stable's Songs directory. Choose a workspace and the osu!stable installation root in **Settings**. The editor derives `Songs` and `Skins` from that root; existing settings pointing to a `Songs` folder migrate to its parent. Songs is optional and may be configured later; startup and entering the library do not automatically open Settings. When configured, Songs and the workspace must be separate directories. Creating, saving, and opening workspace projects, including **My Projects**, work without Songs. Scanning Songs and exporting into Songs require it; standalone `.osu` export does not. Settings are stored in `FruitsAtelier/library.json` under the system application-data directory, independently of the launch directory.

For a project with no existing associated source or export file in the configured Songs directory, File → Save / Ctrl+S first saves the workspace project, then offers to export the current difficulty to Songs. **Keep in workspace** or Esc leaves the workspace save intact and does not write to Songs. For an unassociated local difficulty, **Export to osu! Songs** immediately exports with its current name and links the existing local difficulty; no difficulty-name prompt is shown. Without a configured Songs directory, Save writes only the workspace project. The first Save on an imported Songs difficulty opens export choices before saving or starting synchronization. This choice remains required until a Songs export has been explicitly confirmed for that difficulty. Cancelling preserves its source `.osu` and unsaved edits; background synchronization cannot publish those edits before confirmation. Older projects without a recorded confirmation require this choice again, even if an export target already exists. Once a difficulty has been explicitly exported to Songs, including an overwrite or a newly created difficulty, Save / Ctrl+S updates its linked `.osu` directly, and new FA content automatically synchronizes against the unchanged resolved `.osu` version as described in [Synchronization](SYNCHRONIZATION.md#baselines-and-conflicts). The manual export panel remains available after automatic synchronization; cancelling it keeps the synchronized content. Ctrl+Alt+E exports an unassociated local difficulty directly when Songs is configured; associated difficulties open the manual export panel. Export associations and per-difficulty confirmations persist in the workspace manifest.

Library scans remove deleted map records from directories enumerated successfully, even when another map cannot be read. Unreadable maps and inaccessible subdirectories retain their cached records.

## File structure

```text
Workspace/
  library.db
  Skins/
    Archives/
      content-fingerprint.osk
    Imported/
      extracted-content-fingerprint/
  Resources/
    OSZ-content-fingerprint/
      original directories and all files
  Song title [short-project-ID]/
    project.catchdiff
    Artist - Title (Creator) [Difficulty].catchdiff
    difficulty-ID.catchsync
```

`project.catchdiff` is the project manifest. It stores a stable project ID, name, difficulty order, difficulty IDs, filenames, Songs/external-folder origins, and each difficulty's source and export fingerprints. Other `.catchdiff` files contain complete documents using the encoding described in [Project Model](PROJECT_MODEL.md), including handles, curve controls and AR references, stream and stack settings, timing, and original imported context. The manifest schema remains 1 and difficulties optionally retain synchronization baselines as described in [Synchronization](SYNCHRONIZATION.md). Filenames are generated from the original Metadata Artist, Title, Creator, and project difficulty name, with characters invalid across platforms removed. Numeric suffixes resolve collisions. Filenames are not identities.

Saving first creates a complete temporary project directory, then publishes it by renaming directories. An interrupted save can recover from the retained `.previous` directory; the manifest and difficulty files never combine different save versions. Resource paths are recorded relative to the final project location. Saving does not copy resources: external directories stay in place, and complete OSZ contents remain separately in `Resources`, unaffected by project snapshot saves.

Older `.catchproj` files still open; the next save writes a workspace project.

## Importing external resources

Drop one MP3, OGG or WAV onto the Library browser or editor to create a project. The setup dialog
requires a song title, artist, mapper and difficulty name. Nothing is created until
all four fields are filled and **Create** is confirmed; Cancel retains the previous
project. Unsaved edits use the normal Save, Discard or Cancel prompt before setup.
Title and artist each have separate original and romanised fields, using the same
controls as Song Setup. Romanised fields are editable when the original contains
non-ASCII characters. ASCII originals supply both spellings; leaving an editable
romanised field blank also uses the original. Both spellings are retained in the
saved project and optional Songs export.
Audio is copied to a dedicated directory under `workspace/Resources`, so moving the
original audio file does not break the saved project. Creation runs in the background and
opens the saved project when complete.

**Also create in osu! Songs** is selected initially when an osu! folder is configured.
It creates one Catch `.osu` and an audio copy in a new Songs subdirectory, linked to
the same workspace difficulty for subsequent saves. Clear the switch to create only
the local project. Without an osu! folder, creation is local only; configure it in
Settings to enable Songs creation. If creation fails, the dialog retains its fields
for correction or retry. Audio drops must contain one audio file and cannot be mixed
with beatmap or skin archives.

While editing, resource existence is checked in the background every three seconds. Resource paths are deduplicated and reused while the document snapshot is unchanged; storyboard parsing does not repeat in the paint loop. Completed results are applied only to the matching project and content snapshot, so edits or project switches cannot publish stale missing-file warnings. Initial load and explicit save/export checks refresh the resource state immediately.

Right-click a beatmap card to start or continue editing, open its project folder, or open its associated osu! Songs beatmap folder. Unavailable folders are disabled. The menu also offers **New project**, **Import folder…**, and **Import beatmap / OSZ…**, including when opened on empty list space. Import folder registers the selected source directory and recursively scans its Catch beatmaps. Files remain in place; the directory is neither modified nor copied. **Import beatmap / OSZ…** and the editor's Open action also accept `.osu` and `.osz`; imported sources are registered automatically and the project is immediately saved to the workspace. Reopening an existing source continues its existing project without overwriting edits. An external `.osu` manually selected when adding a difficulty also registers its source directory.

Drag one or more `.osz` beatmaps or `.osk` skins onto the Library to import them into the current workspace. Beatmaps create or resume workspace projects; the last beatmap in a multi-file drop opens in the editor. Skins are stored in `workspace/Skins` and the last imported skin becomes active. Dropping archives does not copy or export files into osu! Songs or Skins. Unsupported files are ignored, and archive drops are disabled outside the Library browser.

OSZ contents are fully extracted to `workspace/Resources/<SHA-256 content fingerprint>/`, preserving directory structure, empty directories, audio, backgrounds, videos, storyboards, and other files. Identical archives reuse a directory. The original OSZ path remains recorded in the database; moving or deleting the archive later does not affect extracted resources. Failed extraction does not publish an incomplete directory. Path traversal, symbolic links, duplicate names, and oversized archives are rejected. Current limits are 1 GiB per archive, 20000 entries, 16 MiB per `.osu`, 256 MiB per other file, and 512 MiB total extracted data. Preserving videos and storyboards does not imply preview support.

External sources are stored in the current workspace's `library.db`, scanned after restart, and searched alongside Songs without requiring Songs to be configured. Missing source directories retain their registration and index, produce library errors, and appear as missing references in existing projects. Rescan after restoring the original directory. A manually imported source cannot be the workspace or its ancestor; the application-managed `Resources` directory is an exception.

## Library and search

Right-click a set card for grouped actions separated by divider lines: enter editing; open, delete or create an FA workspace project; open the osu! Songs folder; export or import maps. Export includes all Catch difficulties in an `.osz`. Deleting a local project removes it from My projects and retains a recovery copy without deleting the source set; opening the source again creates a new project. See [Synchronization](SYNCHRONIZATION.md#missing-files-and-deletion) for deletion behavior.

Difficulty-tab menus group local `.catchdiff` and workspace-folder actions first, followed by `.osu` and Songs-folder actions, synchronization review, and deletion on both sides.

The sidebar contains **All songs** and **My projects**. Only sets without an existing associated source or export file under the configured Songs directory receive a blue background and a **Not in osu! Songs** badge. Sets present in Songs use the standard card and selection backgrounds. Unconfigured Songs directories use the standard appearance. Projects are checked across their saved difficulties, including export targets; names are not used to match unrelated copies. Status is refreshed when library pages reload or the library is rescanned. Each set card shows its title and `artist // mapper`. Background thumbnails fit their frames proportionally, without stretching. Scroll the beatmap list with the wheel, drag its contents, or drag its right-hand scrollbar. The difficulty list has its own scrollbar. Double-click a set, press Enter with a set selected, or use its editing button to enter the editor. **← Library** and Esc close the editor and return to the library. Unsaved changes first prompt for Save, Discard, or Cancel. Save persists the workspace before leaving; Discard leaves the saved files unchanged; Cancel or a failed save keeps the editor open. Entering a map from the library loads its saved project. The current set remains visible on return. Search text, selection, list position and difficulty-list position are remembered separately for each category in `workspace/library-view.json`, including across restarts. Background scans preserve the list position.

The library scans `.osu` metadata directly. Only Catch difficulties (Mode 2) are indexed; other modes are skipped. Entering the editor supports v12, v13, v14 and stable-compatible lazer v128 through the format reader. Separate Songs directories identify separate beatmap sets; titles and online IDs do not merge sets. Starting an edit imports the directory's Catch difficulties into the workspace, while an existing associated project offers **Continue editing**. Double-clicking a beatmap card in **All Songs** or **My Projects** performs the same open operation. A single click only selects the card. **My Projects** also includes new projects without Songs associations.

Search uses Unicode normalization and case-insensitive substring matching across Title, TitleUnicode, Artist, ArtistUnicode, Creator, Version, Tags, and Source. All search terms must match. Original and romanized metadata are supported, but missing readings are not inferred. SQL uses bound parameters.

Scanning runs in the background and updates incrementally by modification time and size. During a scan, changed indexes refresh the partial results at most once every five seconds; completing the scan refreshes the final results. SQLite WAL allows searches alongside writes. Unreadable individual maps are skipped; their count appears in scan progress without leaving persistent file-error banners. Stale entries are removed only after their root finishes successfully. It runs on startup, when due on entering the library, and during the library page's once-per-minute check; F5 requests an immediate scan. Inaccessible directories report errors and retain their index; disconnected Songs does not prevent opening existing projects.

Searches build a disk-backed set index on a worker. The UI reads 64 sets per page, keeps at most eight pages, and draws only visible rows. Scrollbar jumps use indexed row positions rather than loading earlier pages. The selected set's difficulties are paged separately, and Catch stars are calculated only for visible difficulties, with at most 512 cached ratings. Search refreshes do not replace a selection or scroll position changed while the query was running. Search/index construction and filesystem scanning still depend on library size; they do not run on the drawing thread.

Library backgrounds use a separate thumbnail cache on Windows and macOS. Two background decoders produce images bounded to 192 × 152 pixels; the cache holds at most 128 entries and evicts individual least-recently-used entries. Unavailable thumbnails leave the card background visible while loading. File reads and decoding occur off the drawing thread, and cached entries are reconsidered after one minute. The Windows GPU cache is independently capped at 128 thumbnails. See [Building and Testing](TESTING.md#library-scale-benchmark) for the 500,000-map synthetic benchmark and its measurement limits.

The database's maps/projects/project_sources tables are rebuildable indexes. The external_sources table contains persistent external-folder registrations and original OSZ paths. Project manifests and difficulty files hold authored data. Original `.osu` contents before export overwrites are also stored in export_backups; rebuilding the index does not delete them. Do not delete a database containing backups as if it were disposable cache data.

## Resource errors

Opening a project and refreshing the editor check source `.osu` files and song audio. Reference discovery runs in the background; difficulty tabs show searching or missing references without a persistent editor error bar. Missing source files do not block authoring. Conflicting changes still require explicit resolution. Local workspace saving preserves authoring data, while exporting into Songs with missing required references fails. Unlinked local difficulties retain their editing workflow. Restoring the original path clears the missing state; audio can also be replaced from the File menu. Videos, backgrounds, storyboard sprites and animation frames, and custom samples are optional: missing files do not produce a persistent error or block export. Their original references remain in the project and exported `.osu`.

## Explicit export

The editor's File menu also offers **Export .osz…**. It saves every difficulty in the current project, plus available audio, image, video, and storyboard resources, to a location chosen in the native save dialog. This does not change workspace or Songs associations.

Adding difficulties, browsing, and searching do not write to Songs. Save from the File menu or Ctrl/Cmd+S follows the save behavior described above, including the optional Songs export after saving a workspace-only project. Export directly publishes an unassociated local difficulty with its current name when Songs is configured; otherwise it opens the overlay. The map editor stays visible underneath, with canvas input blocked until Cancel or Esc dismisses the overlay. A source directory can belong to only one newly created project; attempts to create another report the existing project path.

Select an export mode, edit its fields, then use the single action button. A confirmed Songs association initially selects Update; other difficulties initially select New difficulty. A unassociated local difficulty bypasses this panel and exports using its current name when Songs is configured. Up/Down changes the mode, Tab focuses the name field where applicable, and Enter activates the action when the name field is not focused. Repeating the save shortcut inside the overlay preserves the current input.

- **Export .osu to another location:** shows the difficulty name and a **Choose location…** action that opens the native save dialog without requiring Songs or a saved workspace project. Exports only the current difficulty, with the entered Version and BeatmapID 0. Audio, backgrounds and custom samples remain file references and must be supplied separately. This does not change the project, saved state, or linked export target. The original source file is protected against overwrite; choose another filename.
- **Update an existing difficulty in osu!:** displays the target path, hides the new-name field, and validates the source or last-export fingerprint. Unresolved external changes, a missing target, duplicate ownership, or a target outside the current Songs directory prevent overwrite.
- **Create a new difficulty in osu!:** accepts a Version and previews an osu!-style `.osu` filename (including the destination directory for saved projects). It cannot overwrite an existing filename, and the new file has BeatmapID 0. For an unassociated local difficulty, this saves and links the current difficulty without adding a `.catchdiff`. For an already associated source, current edits belong to a new difficulty and its `.catchdiff`; the original difficulty returns to its saved authoring state. Opening or cancelling the export choices does not save edits into the original difficulty.

The first Songs export of an unassociated project creates a new Songs subdirectory. Export completes conversion and conflict checks first, copies referenced resources as needed, then writes `.osu` and saves the target association. Other difficulties remain unchanged. Export several difficulties by switching and exporting each one.

External changes are reconciled against persistent synchronization baselines. Changed fields with different values and changed objects require version choices. Missing and duplicate associations must be resolved before editing. See [Synchronization](SYNCHRONIZATION.md). Export does not replace project saving or guarantee that stable immediately refreshes its own library.
