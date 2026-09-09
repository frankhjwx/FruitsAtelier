# Migrating Older Projects

VibeCatchEditor was renamed **FruitsAtelier**, and the editable `VCE Slider` was renamed **FSlider**. Imported objects that have not been converted are still called Legacy Sliders.

- Solution names, project directories, namespaces, and application names use FruitsAtelier. The launch scripts remain `Run-Editor.cmd` and `Run-Editor-Mac.command`.
- `.catchproj` still supports schema 1 and its original property names; existing files need no conversion. Existing project titles and object names remain unchanged. Newly created sliders use the FSlider default name. See [Project Model](PROJECT_MODEL.md) for current persistence formats.
- The standalone Mac application caches resources in `~/Library/Application Support/FruitsAtelier`. Resources referenced by existing projects continue to resolve through their original paths. Old cache directories are not automatically migrated or deleted.

The [vibecatch-schema1.catchproj](../tests/FruitsAtelier.Formats.Tests/Fixtures/vibecatch-schema1.catchproj) fixture verifies read/write compatibility in the Formats tests.
