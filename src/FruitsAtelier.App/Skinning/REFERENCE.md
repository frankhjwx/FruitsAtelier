# Catch PNG skin rendering

## Testplay interface

Skip uses `play-skip.png` or consecutive `play-skip-{n}.png` frames starting at
zero, preferring `@2x` per frame. `AnimationFramerate` sets the frame rate; when
unset, one animation cycle lasts one second. The sprite is bottom-right aligned.
Pause uses `pause-overlay.png` (or `.jpg`), `pause-continue.png`, `pause-retry.png`
and `pause-back.png`. Overlay and buttons retain their full logical dimensions,
without the fruit-specific 160px crop. Their scale is viewport height / 768;
the overlay is centred and button centres are at 224, 400 and 576 reference pixels.
Mouse hover enlarges buttons to 110% over 200 ms with OutQuint easing.
Keyboard selection uses the same enlargement and a pair of arrows.
`arrow-pause.png` and `arrow-warning.png` override `play-warningarrow.png`;
only the shared fallback is tinted blue for selection and red for warnings.
Right-side arrows are mirrored. Warnings appear in the last three seconds of
the intro/break and during the pause menu's 600 ms resume fade.
The pause menu fades in linearly over 300 ms, including the overlay, controls and dim setting.
This duration follows frame measurements of stable; lazer's `GameplayMenuOverlay`
uses a different 200 ms transition and is not the timing reference for this fade.

The pause cursor uses `cursor`, `cursormiddle` and `cursortrail`, preferring `@2x`.
`CursorCentre`, `CursorRotate`, `CursorExpand` and `CursorTrailRotate` are respected.
A middle sprite enables the continuous additive 500 ms trail; otherwise trail
points use normal blending and fade over 150 ms. Trails have fixed-capacity storage.
The cursor and trail are hidden during running gameplay.

Each missing component falls back to the configured default skin, then to the
localized built-in control. Click/hover samples use the matching
`pause-{continue,retry,back}-{click,hover}` names, falling back to `menuhit` and
`menuclick` (`menuback` for Back). Generic `pause-hover` is also supported.
`pause-loop` loops while paused and fades out over 200 ms. Samples use the audition output
so they remain audible while music
is paused. The skin-sound preference applies to these samples too.
Disabling skin sounds selects the packaged osu! samples, including interface sounds.
Their pinned source paths and hashes are in `assets/audio/osu/manifest.json`.
These resources are included in `.osk` extraction cache version 11.

Flow and animation references use the pinned osu! revision below:
`SkipOverlay`, `MasterGameplayClockContainer`, `PauseOverlay`, `GameplayMenuOverlay`,
`LegacyCursor`, `LegacyCursorTrail`, `CursorTrail`, `DialogButton`, `BreakTracker`, `BreakOverlay`, `UserDimContainer` and
`LegacySkinExtensions`. Legacy artwork placement follows the official
[interface skin specification](https://osu.ppy.sh/wiki/en/Skinning/Interface).

The texture mapping and size rules are independently implemented from these MIT-licensed osu!lazer references at [`48c4800e3ae4ee752452cdff83bd3787ccf3105f`](https://github.com/ppy/osu/tree/48c4800e3ae4ee752452cdff83bd3787ccf3105f):

- `osu.Game.Rulesets.Catch/Objects/Fruit.cs` and `FruitVisualRepresentation.cs`: full-map visual index modulo four maps to pear, grapes, apple, orange.
- `osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyFruitPiece.cs`, `LegacyDropletPiece.cs`, `LegacyBananaPiece.cs`: base and overlay PNG names; droplet factor 0.8.
- `osu.Game.Rulesets.Catch/Objects/Drawables/DrawableTinyDroplet.cs`: tiny droplets use another factor of 0.5.
- `osu.Game.Rulesets.Catch/Objects/Drawables/DrawableBanana.cs`: banana arrival scale is 0.6 of the normal CS scale; the approach animation starts at `0.6 + 1.6 * RandomSingle(3)` and interpolates to 0.6 over its preempt interval.
- `osu.Game/Skinning/LegacySkin.cs`: prefer `@2x`, with two physical pixels per logical pixel.
- `osu.Game/Skinning/LegacySkinExtensions.cs`: crop each texture axis around its centre to at most 160 logical pixels; do not stretch smaller textures or shrink oversized artwork.
- `osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyCatchHitObjectPiece.cs`: multiply the base sprite by its tint and leave the overlay untinted; `HyperDashFruit` falls back to `HyperDash`.

`CatchSkin.Draw` receives the caller's CS-scaled nominal fruit diameter, whose unscaled reference is 128 game units. Each PNG keeps its own width and height, including transparent padding. A 128 × 128 base image therefore fills the nominal fruit diameter; a 256 × 256 `@2x` image has the same display size. Source rectangles are in original image pixels, destinations in DIP. The gameplay views rotate each layer around its destination centre. Hyperdash draws a 1.2x base-only additive halo at 70% opacity before the normal tinted base and white overlay. The main editing canvas uses zero rotation.

Static editor bananas use `CatchSize.BananaScaleFactor = 0.6`, the referenced arrival scale, for both base and overlay PNGs. Their destination size is `croppedLogicalSize * nominalFruitDiameter / 128 * 0.6`; the geometric fallback radius is `FruitRadius(CS) * 0.6`. Both views share these size rules. The upstream `Banana` model does not change the normal CS scale, and `LegacyBananaPiece` only crops to 160 logical pixels, so the drawable's scale must be applied separately. Right-side preview and testplay apply deterministic approach scaling and rotation through `CatchObjectVisual`; caught objects retain their arrival transforms. See [DrawableBanana.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/Drawables/DrawableBanana.cs), [Banana.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/Banana.cs) and [LegacyBananaPiece.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyBananaPiece.cs).

`CatchSkin.Bounds` returns the union of the base and overlay destination rectangles using the same scale and geometry as `Draw`. It includes transparent padding and the logical centre crop, but excludes the enlarged hyperdash layer. An absent sprite or invalid nominal diameter returns null so the caller can use geometric fallback bounds. This is a rectangle for hit testing, not a per-pixel alpha test.

`.osk` importing is owned by the application; this loader reads an extracted folder's `skin.ini`, `fruit-*.png`, standard hit-circle and slider-endpoint PNGs, `reversearrow.png` / `reversearrow@2x.png`, and numeric font glyphs. The v5 extraction cache includes standard circle resources; retained source archives allow older imported skins to be upgraded.

The upper timeline uses `hitcircle` and `hitcircleoverlay`, with `sliderstartcircle`
and `sliderendcircle` overrides for slider endpoints. Base images receive combo
colour; overlays and numbers remain white. The number font uses `HitCirclePrefix`
(default `default`) and `HitCircleOverlap`, independently of the Catch combo font.
`HitCircleOverlayAboveNumber` controls their local layer order. Endpoint overrides
without an overlay do not borrow `hitcircleoverlay`. Images prefer @2x and retain
transparent padding: the 128-unit texture has a nominal 118-unit visible circle, capped at twice the visible diameter. HitCircle glyphs additionally use the legacy 0.8 scale.
Slider tracks use `SliderTrackOverride` when set, otherwise combo colour, with
0.7 track opacity and no separate perimeter; endpoint artwork supplies the circle edges. Missing circle artwork or digits use geometric
and text fallbacks. Objects are painted in descending start time, then descending
end time and source order, keeping each object's number within its own layer.

Testplay combo uses `ComboPrefix` (default `score`) and `ComboOverlap` from `[Fonts]`.
Digits use raw texture dimensions, prefer @2x and preserve transparent padding.
Its glyph selection and animation reference `LegacySpriteText.cs`,
`LegacyRollingCounter.cs`, `RollingCounter.cs` and `LegacyCatchComboCounter.cs`
at the pinned revision. Timing and placement are documented in
[Catch rendering](../../../docs/CATCH_RENDERING.md#testplay).

Timeline repeat markers use the skin's reverse arrow at a 128-unit nominal size, preserving aspect ratio and transparent padding, capped to twice the marker diameter. Arrow sprites remain unrotated in the horizontal timeline. Missing or undecodable images use the editor's geometric right arrow. The filename and right-facing convention follow the [osu! skinning specification](https://osu.ppy.sh/wiki/en/Skinning/osu%21#reversearrow.png).

No upstream drawable or framework implementation is embedded. PNG decoding belongs to the platform canvas: Windows Imaging Component on Windows and Avalonia bitmaps on macOS. Invalid or missing PNGs return failure for the caller's geometric fallback.

`CatchSkin.DrawCatcher` uses `fruit-catcher-idle-0` or `fruit-catcher-idle`, preferring @2x. It retains raw logical dimensions and aligns logical Y=16 with the catch line at scale 0.35 times the CS catcher scale. This follows `osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyCatcher.cs` at the pinned revision above; the idle pose is static while position follows autoplay.

Catcher trails and hyperdash afterimages use additive sprite blending, with opacity supplied by map-time effects. The three colours are read from the skin.ini `[CatchTheBeat]` section: body tint uses HyperDash; afterimages use HyperDashAfterImage, falling back to HyperDash and then red; fruit glow uses HyperDashFruit, falling back to HyperDash and then red. Effect timing follows CatcherArea.cs and CatcherTrail.cs at the pinned revision.
