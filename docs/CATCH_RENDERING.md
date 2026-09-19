# Catch Rendering and Conversion

Both views use actual conversion results, AR fall scaling, CS sizes, and skin rendering, with multiple timing points, standalone fruits, imported L/B/P/C sliders, mixed linear/Bezier tracks with repeats, and banana showers. The canvas uses the map's original settings. Catch Preview is a collapsed-by-default, resizable right sidebar with mutually exclusive NM, Easy and Hard Rock modes. The real audio transport drives current time; without audio, explicitly indicated manual positioning remains available.

Easy multiplies AR and CS by 0.5. Hard Rock multiplies AR by 1.4 and CS by 1.3, capped at 10. Hard Rock's preview replays complete-parent RNG ordering, including standalone-fruit offsets, slider droplet draws and banana draws, against the existing converted geometry. Each mode recalculates hyperdash indicators with its effective CS. Preview results are cached by conversion result and mode, and only the current time window is drawn. These controls do not edit, save or export modified beatmap settings. Debug curves continue to show the authored paths.

## AR and center positions

- The playfield is 512 units wide; fall starts at Y=−100 and ends at the catch line Y=340, a distance of 440 (geometric playfield height is 384).
- For AR ≤ 5, preempt is `1200 + 120 × (5 − AR)` ms; for AR > 5, it is `1200 − 150 × (AR − 5)` ms.
- AR is first converted to float, and the piecewise result is truncated to integer milliseconds. AR 0 / 5 / 8 / 10 gives 1800 / 1200 / 750 / 450 ms.
- For effective displayed width W, `DIP/ms = (440 / preemptMs) × (W / 512)`.
- With remaining time Δt, `screenY = catchLineY − Δt × DIP/ms`. The preview queries through its upper edge plus sprite overscan, drawing partially entering sprites before their centres enter the viewport.

The preview offers 4:3, 16:9 and Fit modes. Mode and Resolution controls remain at the top; standard viewports are centred horizontally and vertically in the remaining sidebar area. The preview area represents the full game view, including the catcher. Standard modes have no frame. Fit retains its outer boundary and an internal, bottom-aligned 4:3 reference frame covering the complete legacy game view, including the catcher area. No catch-line guide is drawn. Standard modes fit the full game viewport into the panel: the field width is `viewportHeight × 1024/768 × 0.8`, its top is at 15% of viewport height, and the catch line is another 340 map units below that top. Fit fills the available panel and uses 80% of its inset reference-frame width for the playfield, with the same legacy catch-line placement inside that frame. Increasing panel height therefore exposes more future notes while retaining object proportions and AR-based fall speed.

The automatic catcher follows converted objects using cached replay frames and binary-search interpolation, including when seeking backwards. Its movement follows the pinned [CatchAutoGenerator.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Replays/CatchAutoGenerator.cs) rules. Legacy skin artwork aligns its top-origin offset of 16 logical pixels to the Y=340 catch line, with artwork scale `0.35 × 2 × CatchScale × fieldWidth/512`. The idle pose uses the first animation frame or static idle image, without the fruit texture crop. Missing artwork uses a geometric catcher. See [LegacyCatcher.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyCatcher.cs).

The main canvas uses the same timing formula with its zoomed playfield width. The Zoom slider and Ctrl+scroll scale that width, object sizes, and time spacing together, from 256 DIP to the available width with edge padding. Reset view restores full width. These view changes preserve map coordinates and beatmap AR/CS.

The main play line is `plotBottom − plotHeight × 0.25`. Playback, seek, view reset, and resize retain that placement. Empty space is allowed before/after beatmap boundaries; paused navigation permits manual panning. Bottom navigation moves continuously.

Pinned sources: [CatchPlayfieldAdjustmentContainer.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/UI/CatchPlayfieldAdjustmentContainer.cs), [CatchHitObject.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Objects/CatchHitObject.cs), and [IBeatmapDifficultyInfo.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Beatmaps/IBeatmapDifficultyInfo.cs).

This project truncates preempt to integer milliseconds; the reference version's scrolling time range retains fractions, creating a rounding difference for fractional AR.

Catcher dash trails sample map time every 16 ms and fade from 0.4 opacity over 800 ms with OutQuint easing. Hyperdash starts at the departure fruit, colours the body over a 180 ms transition, and adds a 1200 ms afterimage that rises 10 field units and grows from 0.95 to 1.2 scale. Skin sprites use additive blending for trails and afterimages; geometric fallback ghosts use alpha blending. `HyperDash` and `HyperDashAfterImage` skin colours are independent from `HyperDashFruit`. Effects reconstruct from cached movement and hyperdash intervals when seeking, rather than accumulating render-frame history. These rules reference `Catcher.cs`, `CatcherArea.cs`, `CatcherTrail.cs` and `CatcherTrailDisplay.cs` at the pinned ppy/osu revision above.

Caught Fruits and Bananas remain on the plate at half size with deterministic collision-separated offsets; Droplets eject immediately and TinyDroplets do not stack. At the last converted event of the parent before a New Combo (and at the final parent), the stack explodes: 250 ms upward, 500 ms downward, horizontal spreading and a 750 ms fade. Plate batches and transient droplets are cached and queried by time so seeks restore them without replaying all prior frames. Stack offset randomness is deterministic for preview rather than sharing the runtime random generator. Source rules: Catcher.cs, CaughtObject.cs, CaughtDroplet.cs and CatchBeatmapProcessor.cs at the pinned revision.

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

Fruit variants cycle pear / grapes / apple / orange by index in the complete parent-object order. Nested slider fruits inherit the parent index. The repository does not bundle a skin; users can configure a default skin `.osk` file in Library Settings and import custom archives through the skin selector. Each image resolves from the selected custom skin, then the configured default skin, then the existing geometric renderer. Base and overlay images resolve independently; unreadable images also try the default. See [Architecture](ARCHITECTURE.md) for ZIP limits and [Third-party notices](../THIRD_PARTY_NOTICES.md) for asset licensing.

Sizing sources: [LegacyRulesetExtensions.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game/Rulesets/Objects/Legacy/LegacyRulesetExtensions.cs), [Catcher.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/UI/Catcher.cs). Skin sources and mappings are in [Skinning/REFERENCE.md](../src/FruitsAtelier.App/Skinning/REFERENCE.md).

## Actual streams, tiny droplets, and hyperdash

Time–X tracks evaluate each anchor's OutgoingKind (null inherits track Kind), supporting mixed linear and Bezier segments. A first-span path satisfies velocity constraints; SpanCount then generates head / tick / repeat / legacy-last-tick / tail and tiny events. Later traversals reuse first-span nodes. Objects follow arc length with fixed Legacy RNG applied. Splitting preserves segment shape and does not automatically create a tick. SliderTickRate is independent of editing beat subdivisions.

Legacy Sliders use original L/B/P/C geometry, declared length, and repeats. Reversed spans preserve corresponding tick positions and generate repeat fruits. **Convert to FSlider** from properties or the context menu replaces one slider in a transaction with a fitted time–X track for its first span, preserving parent ID, source order, SpanCount, and original samples. Conversion compares object types/times and verifies TinyDroplet alignment; failure preserves the Legacy Slider. See [Project Model](PROJECT_MODEL.md) for current fitting rules and tolerances.

Parent start times and source order across the whole map determine RNG consumption. Even for overlapping times, one stream's complete nested sequence is processed before the next parent. Ordinary Droplets lie on the path and consume rotation randomness only; TinyDroplets receive lateral RNG offsets. Legacy Sliders retain offsets, while FSliders reverse-adjust exported geometry to match the target time–X track. New FSliders require alignment. Imported conversion may save a non-compensated approximate track when boundaries, shared repeats, or SV prevent strict alignment; automatic SV is capped at stable's 10. Failed tracks leave an incomplete result instead of displaying fabricated streams.

Hyperdash uses stably time-sorted Fruits / Droplets, excluding TinyDroplets and Bananas. It truncates each time to an integer and uses the `1000f / 60f / 4` time allowance, full catcher half-width, previous direction, and remaining movement. A negative allowance marks the current departure object. Sources: [CatchBeatmapProcessor.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmapProcessor.cs) and [CatchBeatmap.cs](https://github.com/ppy/osu/blob/48c4800e3ae4ee752452cdff83bd3787ccf3105f/osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmap.cs).

Fruit and droplet bases use combo colours, while overlays remain white. Beatmap
`[Colours]` takes precedence over skin colours and honours combo-skip offsets;
skin colours advance at combo boundaries without those offsets. Nested slider
objects inherit their parent colour. Bananas use the three deterministic yellow
tints from `Banana.cs`. Without a palette or skin, ordinary geometric objects stay white.

Hyperdash draws only the base texture as a 1.2× additive underlay at 70% opacity,
tinted with `HyperDashFruit` (falling back to `HyperDash` / red), followed by the
normal base and white overlay. Caught and missed objects retain the hyperdash glow.
Windows and Mac apply the same source crop, rotation and layer order.

Only right-side preview and F5 testplay apply `CatchObjectVisual` animation.
Fruits have a deterministic angle within ±20 degrees. Droplets rotate 720 degrees
over preempt + 2000 ms. Bananas interpolate between two angles within ±180 degrees
and shrink from `0.6 + 1.6 * RandomSingle(3)` to the 0.6 arrival scale over preempt.
Caught objects retain their arrival transform; missed bananas continue interpolating.
The seed is the truncated object time and uses the pinned osu! `StatelessRNG`.
The main editing canvas keeps zero rotation and banana arrival size.

## Testplay

The editor's testplay session consumes the same converted preview stream, including
preview difficulty and Hard Rock positions. Movement uses 0.5 playfield units per
map millisecond while walking and 1.0 while dashing, bounded to X=0..512. Opposing
direction keys cancel. Catch judgement uses the CS-dependent catch width at each
object's timestamp; updates split movement at those timestamps to avoid skipping
judgements on slow frames. Hyperdash activates only after catching its departure
object. Fruits and droplets build or break combo; bananas and tiny droplets do not.
Only caught objects trigger testplay hitsounds; preview lookahead scheduling is
disabled during testplay. Windows uses a separate persistent live mixer with
10 ms requested WASAPI latency, bypassing music read-ahead. Mac submits them to
its live hitsound mixer. First-note judgement waits for the audio device to advance.

Tab toggles autoplay without seeking or resetting combo. It uses the same cached
automatic catcher path as the preview and feeds live judgements, sounds, stacks
and effects. Held Tab toggles once; another press restores manual control. Each
new session starts in manual mode.

Input and drawing use a continuous monotonic clock between audio samples, with
50 ms half-life drift correction and at most 100 ms of extrapolation if the audio
device stalls. Audio startup holds the map clock until its position advances.
The catcher integrates each key transition before applying its new held state;
short taps between frames are retained. See [Architecture](ARCHITECTURE.md) for
platform frame scheduling.

Live dash history samples the integrated movement every 16 ms. Ordinary and
hyperdash trails use the 800 ms OutQuint fade; entering hyperdash adds the 1200 ms
rising, expanding afterimage described above. Each entry retains its original
position and facing, including when the player reverses direction.

The following combo display uses legacy `ComboPrefix` / `ComboOverlap` glyphs
(default prefix `score`), with @2x density handling and no `x` suffix. It starts
hidden. Following `LegacyCatchComboCounter`, an increment overlays a growing,
400 ms fading burst; the main counter changes after 250 ms and pulses. It fades
after 1000 ms of inactivity over 300 ms. A broken combo rolls down and fades over
400 ms. The counter follows catcher X; its vertical centre uses `CatcherArea`'s
350-unit bottom margin with a centre origin, placing it 175 field units above
the plate. Missing font glyphs use a numeric text fallback with the same timing.

The catch interval includes both edges. A caught object disappears from the falling
stream immediately. A missed object keeps its X and continues below the catch line
at the same scroll speed while fading over 250 ms, following
`DrawableCatchHitObject.UpdateHitStateTransforms` and `ConstantScrollAlgorithm` at
the pinned osu! revision.

Caught fruit and bananas enter the same half-size stack used by automatic preview;
droplets use its immediate release animation and tiny droplets do not stack.
The stack follows the live catcher. At a combo end, a catch releases the stack with
the preview explosion; a miss drops it 75 units over 750 ms with InSine easing and
fade, following `Catcher.applyDropAnimation`. Released objects retain their release
position independently of subsequent catcher movement. Completion waits for the
last miss and plate animation unless the music ends or the user exits first.

Movement and facing reference `Catcher.cs` and `CatcherArea.cs` at the pinned osu!
revision above. Session entry/return was compared with
`osu.Game/Screens/Edit/GameplayTest/EditorPlayer.cs` at the same revision. Testplay
skips prior objects and starts a fresh combo, uses no failure or results screen,
and restores the starting playhead on exit. See [Editing controls](EDITOR_UI.md#testplay).

## Layers

Main-canvas curves render above actual objects, at 100% opacity when selected and 50% otherwise. Hiding curves retains objects. Legacy overlays use the original path; FSliders draw their authored mixed segments rather than connecting random tiny positions as a substitute track.
