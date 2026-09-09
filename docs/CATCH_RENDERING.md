# Catch Rendering and Conversion

Both views share actual conversion results, AR fall scaling, CS sizes, and skin rendering. Current conditions are NM / 1×, with multiple timing points, standalone fruits, imported L/B/P/C sliders, mixed linear/Bezier tracks with repeats, and banana showers. The real audio transport drives current time; without audio, explicitly indicated manual positioning remains available.

## AR and center positions

- The playfield is 512 units wide; fall starts at Y=−100 and ends at the catch line Y=340, a distance of 440 (geometric playfield height is 384).
- For AR ≤ 5, preempt is `1200 + 120 × (5 − AR)` ms; for AR > 5, it is `1200 − 150 × (AR − 5)` ms.
- AR is first converted to float, and the piecewise result is truncated to integer milliseconds. AR 0 / 5 / 8 / 10 gives 1800 / 1200 / 750 / 450 ms.
- For effective displayed width W, `DIP/ms = (440 / preemptMs) × (W / 512)`.
- With remaining time Δt, `screenY = catchLineY − Δt × DIP/ms`. The preview shows converted objects only for `0 ≤ Δt ≤ preemptMs`.

The preview fits the 512:440 region proportionally into its panel with margins. The main canvas's **Restore AR scale** uses the same formula, with current time fixed at the play line 25% above the drawing area's bottom. Ctrl+scroll freely adjusts zoom. Restored scaling follows width and AR without changing the model.

The main play line is `plotBottom − plotHeight × 0.25`. Playback, seek, AR restoration, and resize retain that placement. Empty space is allowed before/after beatmap boundaries; paused navigation permits manual panning. Bottom navigation moves continuously.

Pinned sources: [CatchPlayfieldAdjustmentContainer.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/UI/CatchPlayfieldAdjustmentContainer.cs), [CatchHitObject.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/CatchHitObject.cs), and [IBeatmapDifficultyInfo.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Beatmaps/IBeatmapDifficultyInfo.cs).

This project truncates preempt to integer milliseconds; the reference version's scrolling time range retains fractions, creating a rounding difference for fractional AR.

## CS and skin sizing

CatchScale follows legacy CircleSize, retaining float arithmetic boundaries:

```text
cs = (float)CircleSize
scale = (float)(1.0f − 0.7f × ((cs − 5) / 5)) / 2
nominal fruit diameter = 128 × scale
full catcher width = 106.75 × (2 × scale)
effective catch width = full catcher width × 0.8
```

Multiply these dimensions by view width / 512. At CS=5, nominal fruit diameter is 64 units. Basic-shape fallback Droplet / TinyDroplet radii are `16 × scale` / `8 × scale`; these differ from legacy PNG visible-size rules.

PNGs use original logical dimensions; `@2x` logical dimensions are half their pixel dimensions. Each axis is center-cropped to at most 160 logical pixels rather than scaling the whole oversized image down. Target size is `cropped logical size × nominal fruit diameter / 128 × view width / 512`, additionally multiplied by 0.8 for drops, 0.4 for tiny droplets, and 0.6 for bananas. Transparent margins count toward size; overlays do not inherit base-image tint.

Bananas use the static arrival scale 0.6 in both views, scaling base and overlay together. The fallback radius is `FruitRadius(CS) × 0.6`. Random scale/rotation animations are not implemented; this visual simplification does not change banana RNG consumption order.

Selection hit testing uses the union of actual base/overlay destination rectangles with a minimum click tolerance. It excludes the enlarged hyperdash layer and does not test pixel alpha. Missing textures fall back to the corresponding geometric sizes. Clicking any slider Fruit / Droplet / TinyDroplet selects the entire slider by SourceId; selection does not change conversion output.

Fruit variants cycle pear / grapes / apple / orange by index in the complete parent-object order. Nested slider fruits inherit the parent index. The repository does not bundle a skin; an optional local default imports from `assets/skins/default.osk`, and the skin picker accepts other archives. Missing skins use basic shapes. See [Architecture](ARCHITECTURE.md) for ZIP limits and [Third-party notices](../THIRD_PARTY_NOTICES.md) for asset licensing.

Sizing sources: [LegacyRulesetExtensions.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Rulesets/Objects/Legacy/LegacyRulesetExtensions.cs), [Catcher.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/UI/Catcher.cs). Skin sources and mappings are in [Skinning/REFERENCE.md](../src/FruitsAtelier.App/Skinning/REFERENCE.md).

## Actual streams, tiny droplets, and hyperdash

Time–X tracks evaluate each anchor's OutgoingKind (null inherits track Kind), supporting mixed linear and Bezier segments. A first-span path satisfies velocity constraints; SpanCount then generates head / tick / repeat / legacy-last-tick / tail and tiny events. Later traversals reuse first-span nodes. Objects follow arc length with fixed Legacy RNG applied. Splitting preserves segment shape and does not automatically create a tick. SliderTickRate is independent of editing beat subdivisions.

Legacy Sliders use original L/B/P/C geometry, declared length, and repeats. Reversed spans preserve corresponding tick positions and generate repeat fruits. **Convert to FSlider** from properties or the context menu replaces one slider in a transaction with a fitted time–X track for its first span, preserving parent ID, source order, SpanCount, and original samples. Conversion compares object types/times and verifies TinyDroplet alignment; failure preserves the Legacy Slider. See [Project Model](PROJECT_MODEL.md) for current fitting rules and tolerances.

Parent start times and source order across the whole map determine RNG consumption. Even for overlapping times, one stream's complete nested sequence is processed before the next parent. Ordinary Droplets lie on the path and consume rotation randomness only; TinyDroplets receive lateral RNG offsets. Legacy Sliders retain offsets, while FSliders reverse-adjust exported geometry to match the target time–X track. New FSliders require alignment. Imported conversion may save a non-compensated approximate track when boundaries, shared repeats, or SV prevent strict alignment; automatic SV is capped at stable's 10. Failed tracks leave an incomplete result instead of displaying fabricated streams.

Hyperdash uses stably time-sorted Fruits / Droplets, excluding TinyDroplets and Bananas. It truncates each time to an integer and uses the `1000f / 60f / 4` time allowance, full catcher half-width, previous direction, and remaining movement. A negative allowance marks the current departure object. Sources: [CatchBeatmapProcessor.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmapProcessor.cs) and [CatchBeatmap.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmap.cs).

Ordinary objects use white base tint. Hyperdash adds a 1.2× underlay in the skin's HyperDashFruit color, falling back to HyperDash / red. Full additive glow, rotation, combo colors, and hit effects are not reproduced.

## Layers

Main-canvas curves render above actual objects, at 100% opacity when selected and 50% otherwise. Hiding curves retains objects. Legacy overlays use the original path; FSliders draw their authored mixed segments rather than connecting random tiny positions as a substitute track.
