# Catch PNG skin rendering

The texture mapping and size rules are independently implemented from these MIT-licensed osu!lazer references at [`48c4800e3ae4ee752452cdff83bd3787ccf3105f`](https://github.com/ppy/osu/tree/48c4800e3ae4ee752452cdff83bd3787ccf3105f):

- `osu.Game.Rulesets.Catch/Objects/Fruit.cs` and `FruitVisualRepresentation.cs`: full-map visual index modulo four maps to pear, grapes, apple, orange.
- `osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyFruitPiece.cs`, `LegacyDropletPiece.cs`, `LegacyBananaPiece.cs`: base and overlay PNG names; droplet factor 0.8.
- `osu.Game.Rulesets.Catch/Objects/Drawables/DrawableTinyDroplet.cs`: tiny droplets use another factor of 0.5.
- `osu.Game.Rulesets.Catch/Objects/Drawables/DrawableBanana.cs`: banana arrival scale is 0.6 of the normal CS scale; the approach animation starts at `0.6 + 1.6 * RandomSingle(3)` and interpolates to 0.6 over its preempt interval.
- `osu.Game/Skinning/LegacySkin.cs`: prefer `@2x`, with two physical pixels per logical pixel.
- `osu.Game/Skinning/LegacySkinExtensions.cs`: crop each texture axis around its centre to at most 160 logical pixels; do not stretch smaller textures or shrink oversized artwork.
- `osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyCatchHitObjectPiece.cs`: multiply the base sprite by its tint and leave the overlay untinted; `HyperDashFruit` falls back to `HyperDash`.

`CatchSkin.Draw` receives the caller's CS-scaled nominal fruit diameter, whose unscaled reference is 128 game units. Each PNG keeps its own width and height, including transparent padding. A 128 × 128 base image therefore fills the nominal fruit diameter; a 256 × 256 `@2x` image has the same display size. Source rectangles are in original image pixels, destinations in DIP. Rotation is intentionally omitted. Hyperdash uses the caller's red tint; lazer's separate additive halo is not reproduced.

Static editor bananas use `CatchSize.BananaScaleFactor = 0.6`, the referenced arrival scale, for both base and overlay PNGs. Their destination size is `croppedLogicalSize * nominalFruitDiameter / 128 * 0.6`; the geometric fallback radius is `FruitRadius(CS) * 0.6`. Both views share these size rules. The upstream `Banana` model does not change the normal CS scale, and `LegacyBananaPiece` only crops to 160 logical pixels, so the drawable's scale must be applied separately. Random approach scaling and rotation remain omitted. See [DrawableBanana.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/Drawables/DrawableBanana.cs), [Banana.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/Banana.cs) and [LegacyBananaPiece.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyBananaPiece.cs).

`CatchSkin.Bounds` returns the union of the base and overlay destination rectangles using the same scale and geometry as `Draw`. It includes transparent padding and the logical centre crop, but excludes the enlarged hyperdash layer. An absent sprite or invalid nominal diameter returns null so the caller can use geometric fallback bounds. This is a rectangle for hit testing, not a per-pixel alpha test.

`.osk` importing is owned by the application; this loader reads an extracted folder's `skin.ini`, `fruit-*.png`, and `reversearrow.png` / `reversearrow@2x.png` files. The extraction cache uses a versioned directory so packages imported before reverse-arrow support are extracted again.

Timeline repeat markers use the skin's reverse arrow at a 128-unit nominal size, preserving aspect ratio and transparent padding, capped to twice the marker diameter. Arrow sprites remain unrotated in the horizontal timeline. Missing or undecodable images use the editor's geometric right arrow. The filename and right-facing convention follow the [osu! skinning specification](https://osu.ppy.sh/wiki/en/Skinning/osu%21#reversearrow.png).

No upstream drawable or framework implementation is embedded. PNG decoding belongs to the platform canvas: Windows Imaging Component on Windows and Avalonia bitmaps on macOS. Invalid or missing PNGs return failure for the caller's geometric fallback.

`CatchSkin.DrawCatcher` uses `fruit-catcher-idle-0` or `fruit-catcher-idle`, preferring @2x. It retains raw logical dimensions and aligns logical Y=16 with the catch line at scale 0.35 times the CS catcher scale. This follows `osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyCatcher.cs` at the pinned revision above; the idle pose is static while position follows autoplay.

Catcher trails and hyperdash afterimages use additive sprite blending, with opacity supplied by map-time effects. Body tint uses HyperDash; afterimages use HyperDashAfterImage, falling back to HyperDash and then red. HyperDashFruit remains independent. Effect timing follows CatcherArea.cs and CatcherTrail.cs at the pinned revision.
