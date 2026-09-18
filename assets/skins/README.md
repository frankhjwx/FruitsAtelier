# Default Catch Skin

The repository does not bundle default skin images. Default hitsound recordings are
packaged separately in [assets/audio/osu](../audio/osu/README.md). It builds and runs without a skin, displaying objects as basic shapes.

Set a user-owned default skin `.osk` file in Library Settings. Builds do not copy or publish local skin archives. An optional local `default.osk` in this directory is used only by skin regression tests.

The toolbar's skin picker accepts `.osk` directly without manual extraction. It currently loads fruit, overlay, fruit-drop, their `@2x` images, and related colors; it does not load sounds, cursors, or other ruleset resources.

Skin copyright belongs to each author; the code's MIT license does not cover skin images. Users supply their own skin files; application packages do not include them. See [Building and Testing](../../docs/TESTING.md) for optional skin-test resource requirements.
