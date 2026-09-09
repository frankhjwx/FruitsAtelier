# Default Catch Skin

The repository does not bundle default skin images. Default hitsound recordings are
packaged separately in [assets/audio/osu](../audio/osu/README.md). It builds and runs without a skin, displaying objects as basic shapes.

You may place a local `default.osk` here. The build copies it to the output directory only when it exists. At startup, the application extracts `skin.ini` and Catch PNGs; source builds cache them in `artifacts/skins`. This path is ignored by Git and is not uploaded with the source.

The toolbar's skin picker accepts `.osk` directly without manual extraction. It currently loads fruit, overlay, fruit-drop, their `@2x` images, and related colors; it does not load sounds, cursors, or other ruleset resources.

Skin copyright belongs to each author; the code's MIT license does not cover skin images. Verify asset licenses before distributing builds that include a local skin. See [Building and Testing](../../docs/TESTING.md) for optional skin-test resource requirements.
