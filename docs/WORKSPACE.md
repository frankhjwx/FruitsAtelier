# Workspace and Local Library

The application supports osu!stable's Songs directory. Use **Library** beside the main menu to open the library page, then choose a workspace and Songs directory in **Settings**. Songs is optional and may be configured later; startup and entering the library do not automatically open Settings. When configured, Songs and the workspace must be separate directories. Creating, saving, saving as, and opening workspace projects, including **My Projects**, work without Songs. Scanning Songs and exporting into Songs require it; standalone `.osu` export does not. Settings are stored in `FruitsAtelier/library.json` under the system application-data directory, independently of the launch directory.

## File structure

```text
Workspace/
  library.db
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

**Import folder…** on the library's left side registers the selected source directory and recursively scans its Catch beatmaps. Files remain in place; the directory is neither modified nor copied. **Import beatmap / OSZ…** and the editor's Open action also accept `.osu` and `.osz`; imported sources are registered automatically and the project is immediately saved to the workspace. Reopening an existing source continues its existing project without overwriting edits. An external `.osu` manually selected when adding a difficulty also registers its source directory.

OSZ contents are fully extracted to `workspace/Resources/<SHA-256 content fingerprint>/`, preserving directory structure, empty directories, audio, backgrounds, videos, storyboards, and other files. Identical archives reuse a directory. The original OSZ path remains recorded in the database; moving or deleting the archive later does not affect extracted resources. Failed extraction does not publish an incomplete directory. Path traversal, symbolic links, duplicate names, and oversized archives are rejected. Current limits are 1 GiB per archive, 20000 entries, 16 MiB per `.osu`, 256 MiB per other file, and 512 MiB total extracted data. Preserving videos and storyboards does not imply preview support.

External sources are stored in the current workspace's `library.db`, scanned after restart, and searched alongside Songs without requiring Songs to be configured. Missing source directories retain their registration and index, produce library errors, and appear as missing references in existing projects. Rescan after restoring the original directory. A manually imported source cannot be the workspace or its ancestor; the application-managed `Resources` directory is an exception.

## Library and search

The library scans `.osu` metadata directly and indexes only Mode=2. Entering the editor still requires v14, as supported by the current format reader. Separate Songs directories identify separate beatmap sets; titles and online IDs do not merge sets. Starting an edit imports the directory's Catch difficulties into the workspace, while an existing associated project offers **Continue editing**. Double-clicking a beatmap card in **All Songs** or **My Projects** performs the same open operation. A single click only selects the card, and unsaved changes still prompt first. **My Projects** also includes new projects without Songs associations.

Search uses Unicode normalization and case-insensitive substring matching across Title, TitleUnicode, Artist, ArtistUnicode, Creator, Version, Tags, and Source. All search terms must match. Original and romanized metadata are supported, but missing readings are not inferred. SQL uses bound parameters.

Scanning runs in the background and updates incrementally by modification time and size. It runs on startup, when entering the library, on manual refresh, and during the library page's once-per-minute check. Inaccessible directories report errors and retain their index; disconnected Songs does not prevent opening existing projects. Lists draw only visible rows. Selecting a beatmap set calculates Catch stars in the background.

The database's maps/projects/project_sources tables are rebuildable indexes. The external_sources table contains persistent external-folder registrations and original OSZ paths. Project manifests and difficulty files hold authored data. Original `.osu` contents before export overwrites are also stored in export_backups; rebuilding the index does not delete them. Do not delete a database containing backups as if it were disposable cache data.

## Resource errors

Opening a project and refreshing the editor check source `.osu` files, audio, Events resource references (including animation frames), and custom object samples. Missing resources produce a red editor error bar; **View details** shows full paths. Editing and saving remain available, but exporting into Songs with missing resources fails. Standalone `.osu` export remains available. Restoring the original path clears the error; audio can also be replaced from the File menu.

## Explicit export

Saving, adding difficulties, browsing, and searching do not write to Songs. Choose Export from the File menu or press Ctrl/Cmd+E to open the export page for the current difficulty:

- **Export .osu to…:** opens the native save dialog without requiring Songs or a saved workspace project. Exports only the current difficulty, with the entered Version and BeatmapID 0. Audio, backgrounds and custom samples remain file references and must be supplied separately. This does not change the project, saved state, or linked export target. The original source file is protected against overwrite; choose another filename.
- **Overwrite associated difficulty:** displays the target path and validates its source or last-export fingerprint. External changes, a missing target, or a target outside the current Songs directory prevent overwrite.
- **Export as new difficulty:** accepts a Version and generates an osu!-style `.osu` filename. It cannot overwrite an existing filename, and the new file has BeatmapID 0.

The first Songs export of an unassociated project creates a new Songs subdirectory. Export completes conversion and conflict checks first, copies referenced resources as needed, then writes `.osu` and saves the target association. Other difficulties remain unchanged. Export several difficulties by switching and exporting each one.

When a source file changes externally, export as a new difficulty or reimport the external `.osu` before continuing. The application does not merge both sets of edits automatically. Export does not replace project saving or guarantee that stable immediately refreshes its own library.
