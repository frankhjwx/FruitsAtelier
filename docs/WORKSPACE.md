# Workspace and Local Library

**My Projects** shows the difficulty count and names from each saved workspace manifest. **All Songs** shows the source set's difficulties. Opening or resuming an existing workspace from the Library checks its source folders for additional `.osu` difficulties and offers to import them. Import appends difficulties without replacing existing edits or undo history; save the project to persist them.

Creating a new difficulty in osu! saves the current edits into a new workspace difficulty and a new `.osu`, then activates the new difficulty. The original difficulty retains its last saved content and export link; its source `.osu` is unchanged. The new workspace difficulty retains editable FSliders and handles. Files recorded only as older export targets are offered for import when they are not already represented by a project difficulty.

Right-click an editor difficulty tab to open its source `.osu`, saved `.catchdiff`, or containing folder. Files open in a text editor; unavailable files are disabled. For workspace difficulties, the folder action opens the workspace directory. **Open osu! Songs folder** opens the difficulty's source beatmap directory, falling back to its export directory when no source is linked.

The application opens in the Library and supports osu!stable's Songs directory. Choose a workspace and the osu!stable installation root in **Settings**. The editor derives `Songs` and `Skins` from that root; existing settings pointing to a `Songs` folder migrate to its parent. Songs is optional and may be configured later; startup and entering the library do not automatically open Settings. When configured, Songs and the workspace must be separate directories. Creating, saving, saving as, and opening workspace projects, including **My Projects**, work without Songs. Scanning Songs and exporting into Songs require it; standalone `.osu` export does not. Settings are stored in `FruitsAtelier/library.json` under the system application-data directory, independently of the launch directory.

Saving an imported difficulty with no export record opens the export choices before writing its edits. Once a difficulty has been exported to Songs, including an overwrite or a newly created difficulty, Save / Ctrl+S updates its linked `.osu` directly. Ctrl+E always opens the manual export panel. Export associations persist in the workspace manifest.

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
```

`project.catchdiff` is the project manifest. It stores a stable project ID, name, difficulty order, difficulty IDs, filenames, Songs/external-folder origins, and each difficulty's source and export fingerprints. Other `.catchdiff` files contain complete documents using schema 1 document encoding, or schema 3 when exact control curves are present, including handles, curve controls and AR references, timing, and original imported context. The manifest schema remains 1. Filenames are generated from the original Metadata Artist, Title, Creator, and project difficulty name, with characters invalid across platforms removed. Numeric suffixes resolve collisions. Filenames are not identities.

Saving first creates a complete temporary project directory, then publishes it by renaming directories. An interrupted save can recover from the retained `.previous` directory; the manifest and difficulty files never combine different save versions. Resource paths are recorded relative to the final project location. Saving does not copy resources: external directories stay in place, and complete OSZ contents remain separately in `Resources`, unaffected by project snapshot saves.

Older `.catchproj` files still open; the next save writes a workspace project. Save As creates an independent project copy in the current workspace.

## Importing external resources

While editing, resource existence is checked in the background every three seconds. Resource paths are deduplicated and reused while the document snapshot is unchanged; storyboard parsing does not repeat in the paint loop. Completed results are applied only to the matching project and content snapshot, so edits or project switches cannot publish stale missing-file warnings. Initial load and explicit save/export checks refresh the resource state immediately.

Right-click a beatmap card to start or continue editing, open its project folder, or open its associated osu! Songs beatmap folder. Unavailable folders are disabled. The menu also offers **New project**, **Import folder…**, and **Import beatmap / OSZ…**, including when opened on empty list space. Import folder registers the selected source directory and recursively scans its Catch beatmaps. Files remain in place; the directory is neither modified nor copied. **Import beatmap / OSZ…** and the editor's Open action also accept `.osu` and `.osz`; imported sources are registered automatically and the project is immediately saved to the workspace. Reopening an existing source continues its existing project without overwriting edits. An external `.osu` manually selected when adding a difficulty also registers its source directory.

Drag one or more `.osz` beatmaps or `.osk` skins onto the Library to import them into the current workspace. Beatmaps create or resume workspace projects; the last beatmap in a multi-file drop opens in the editor. Skins are stored in `workspace/Skins` and the last imported skin becomes active. Dropping archives does not copy or export files into osu! Songs or Skins. Unsupported files are ignored, and drops are disabled outside the Library browser.

OSZ contents are fully extracted to `workspace/Resources/<SHA-256 content fingerprint>/`, preserving directory structure, empty directories, audio, backgrounds, videos, storyboards, and other files. Identical archives reuse a directory. The original OSZ path remains recorded in the database; moving or deleting the archive later does not affect extracted resources. Failed extraction does not publish an incomplete directory. Path traversal, symbolic links, duplicate names, and oversized archives are rejected. Current limits are 1 GiB per archive, 20000 entries, 16 MiB per `.osu`, 256 MiB per other file, and 512 MiB total extracted data. Preserving videos and storyboards does not imply preview support.

External sources are stored in the current workspace's `library.db`, scanned after restart, and searched alongside Songs without requiring Songs to be configured. Missing source directories retain their registration and index, produce library errors, and appear as missing references in existing projects. Rescan after restoring the original directory. A manually imported source cannot be the workspace or its ancestor; the application-managed `Resources` directory is an exception.

## Library and search

The sidebar contains **All songs** and **My projects**. Only sets without an existing associated source or export file under the configured Songs directory receive a blue background and a **Not in osu! Songs** badge. Sets present in Songs use the standard card and selection backgrounds. Unconfigured Songs directories use the standard appearance. Projects are checked across their saved difficulties, including export targets; names are not used to match unrelated copies. Status is refreshed when library pages reload or the library is rescanned. Each set card shows its title and `artist // mapper`. Background thumbnails fit their frames proportionally, without stretching. Scroll the beatmap list with the wheel, drag its contents, or drag its right-hand scrollbar. The difficulty list has its own scrollbar. Double-click a set, press Enter with a set selected, or use its editing button to enter the editor. **← Library** and Esc close the editor and return to the library. Unsaved changes first prompt for Save, Discard, or Cancel. Save persists the workspace before leaving; Discard leaves the saved files unchanged; Cancel or a failed save keeps the editor open. Entering a map from the library loads its saved project. The current set remains visible on return. Search text, selection, list position and difficulty-list position are remembered separately for each category in `workspace/library-view.json`, including across restarts. Background scans preserve the list position.

The library scans `.osu` metadata directly and indexes only Mode=2. Entering the editor supports v12, v13 and v14 through the format reader. Separate Songs directories identify separate beatmap sets; titles and online IDs do not merge sets. Starting an edit imports the directory's Catch difficulties into the workspace, while an existing associated project offers **Continue editing**. Double-clicking a beatmap card in **All Songs** or **My Projects** performs the same open operation. A single click only selects the card. **My Projects** also includes new projects without Songs associations.

Search uses Unicode normalization and case-insensitive substring matching across Title, TitleUnicode, Artist, ArtistUnicode, Creator, Version, Tags, and Source. All search terms must match. Original and romanized metadata are supported, but missing readings are not inferred. SQL uses bound parameters.

Scanning runs in the background and updates incrementally by modification time and size. During a scan, changed indexes refresh the partial results at most once every five seconds; completing the scan refreshes the final results. SQLite WAL allows searches alongside writes. Unreadable individual maps are skipped; their count appears in scan progress without leaving persistent file-error banners. Stale entries are removed only after their root finishes successfully. It runs on startup, when due on entering the library, and during the library page's once-per-minute check; F5 requests an immediate scan. Inaccessible directories report errors and retain their index; disconnected Songs does not prevent opening existing projects.

Searches build a disk-backed set index on a worker. The UI reads 64 sets per page, keeps at most eight pages, and draws only visible rows. Scrollbar jumps use indexed row positions rather than loading earlier pages. The selected set's difficulties are paged separately, and Catch stars are calculated only for visible difficulties, with at most 512 cached ratings. Search refreshes do not replace a selection or scroll position changed while the query was running. Search/index construction and filesystem scanning still depend on library size; they do not run on the drawing thread.

Library backgrounds use a separate thumbnail cache on Windows and macOS. Two background decoders produce images bounded to 192 × 152 pixels; the cache holds at most 128 entries and evicts individual least-recently-used entries. Unavailable thumbnails leave the card background visible while loading. File reads and decoding occur off the drawing thread, and cached entries are reconsidered after one minute. The Windows GPU cache is independently capped at 128 thumbnails. See [Building and Testing](TESTING.md#library-scale-benchmark) for the 500,000-map synthetic benchmark and its measurement limits.

The database's maps/projects/project_sources tables are rebuildable indexes. The external_sources table contains persistent external-folder registrations and original OSZ paths. Project manifests and difficulty files hold authored data. Original `.osu` contents before export overwrites are also stored in export_backups; rebuilding the index does not delete them. Do not delete a database containing backups as if it were disposable cache data.

## Resource errors

Opening a project and refreshing the editor check source `.osu` files and song audio. Missing required references produce a red editor error bar; **View details** shows full paths. Editing and saving remain available, but exporting into Songs with missing required references fails. Standalone `.osu` export remains available. Restoring the original path clears the error; audio can also be replaced from the File menu. Videos, backgrounds, storyboard sprites and animation frames, and custom samples are optional: missing files do not produce a persistent error or block export. Their original references remain in the project and exported `.osu`.

## Explicit export

Adding difficulties, browsing, and searching do not write to Songs. Save from the File menu or Ctrl/Cmd+S updates the linked Songs file for an already exported difficulty; an imported difficulty without an export record opens the export choices first. Export or Ctrl/Cmd+E always opens the overlay directly. The map editor stays visible underneath, with canvas input blocked until Cancel or Esc dismisses the overlay. Save As creates a project copy only when its source directories are not already associated with a workspace project. A source directory can belong to only one newly created project; attempts to create another report the existing project path.

Select an export mode, edit its fields, then use the single action button. New difficulty is selected initially. Up/Down changes the mode, Tab focuses the name field where applicable, and Enter activates the action when the name field is not focused. Repeating the save shortcut inside the overlay preserves the current input.

- **Export .osu to another location:** shows the difficulty name and a **Choose location…** action that opens the native save dialog without requiring Songs or a saved workspace project. Exports only the current difficulty, with the entered Version and BeatmapID 0. Audio, backgrounds and custom samples remain file references and must be supplied separately. This does not change the project, saved state, or linked export target. The original source file is protected against overwrite; choose another filename.
- **Update an existing difficulty in osu!:** displays the target path, hides the new-name field, and validates the source or last-export fingerprint. External changes, a missing target, or a target outside the current Songs directory prevent overwrite.
- **Create a new difficulty in osu!:** accepts a Version and previews an osu!-style `.osu` filename (including the destination directory for saved projects). It cannot overwrite an existing filename, and the new file has BeatmapID 0.

The first Songs export of an unassociated project creates a new Songs subdirectory. Export completes conversion and conflict checks first, copies referenced resources as needed, then writes `.osu` and saves the target association. Other difficulties remain unchanged. Export several difficulties by switching and exporting each one.

When a source file changes externally, export as a new difficulty or reimport the external `.osu` before continuing. The application does not merge both sets of edits automatically. Export does not replace project saving or guarantee that stable immediately refreshes its own library.
