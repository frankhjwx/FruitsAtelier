# Catch Star Rating

Difficulty tabs show the **No Mod, 1×** Catch movement difficulty of the current edited content. Core's `CatchDifficultyCalculator` uses algorithm version **20260706** from ppy/osu commit `48c4800e3ae4ee752452cdff83bd3787ccf3105f`. It does not infer difficulty from the difficulty name or treat AR as a star rating.

## Calculation

The complete Catch conversion result is stably sorted by time. Only Fruit and Droplet objects participate, including slider heads, ticks, repeats, and tails. TinyDroplets and Bananas contribute neither movement stars nor maximum combo. Hyperdash preprocessing uses the full sequence. Catcher width follows CS, including the official extra width adjustment above CS 5.5.

Positions are normalized to a catcher half-width of 41, with minimum required movement and a position tolerance of 16; hyperdashes reset the player's position. Each step uses at least 40 ms of strain time and accounts for distance, direction changes, continuous linear-spacing decay, edge dashes, and repeated small movements. Movement strain decays by 0.2 per second, with peaks recorded every 750 ms. Peaks are sorted descending and summed with successively decreasing weights of 0.94; finally, `stars = sqrt(difficulty) * 4.59`.

## Display and invalidation

Stars are calculated in the background against independent document snapshots, with one editor star task at a time. When the document changes, the last successful value remains visible with a spinner. Before the first result, the display shows `0.00★` and a spinner. A successfully calculated zero-star result stops the spinner.

During object drags or unfinished FSlider/banana drafts, the old value remains until editing completes. Continuous edits coalesce pending changes; stale snapshot results are not published. Replacing a project cancels its queued calculations. Conversion failures retain the cached value, stop the spinner, and show `!` with a status message; further edits retry. The cache lasts only for the current session.

The official Catch ruleset icon uses gamma-2.2 RGB interpolation over the website's star-color scale:

| Star threshold | Color |
| --- | --- |
| < 0.1 / unavailable | `#AAAAAA` |
| 0.1 / 1.25 / 2 / 2.5 | `#4290FB` / `#4FC0FF` / `#4FFFD5` / `#7CFF4F` |
| 3.3 / 4.2 / 4.9 / 5.8 | `#F6F05C` / `#FF8068` / `#FF4E6F` / `#C645B8` |
| 6.7 / 7.7 / ≥ 9 | `#6563DE` / `#18158E` / `#000000` |

High-star icons receive a light backing for dark interfaces. Sources and licenses are in [Third-party notices](../THIRD_PARTY_NOTICES.md).

## Comparisons and limits

Gameplay regressions contain 21 reference values obtained by directly running the pinned official `CatchDifficultyHitObject`, `MovementEvaluator`, `Movement`, `StrainSkill`, and `StrainDecaySkill` implementations. They cover alternating jumps, linear movement, small repeated movements, long breaks, fractional times, simultaneous objects, and stacks at CS 3/5/8, with tolerance 1e-12. Independent tests verify Droplet, TinyDroplet, and Banana participation and input boundaries. App tests cover cache behavior for edits, undo, CS, drafts, and tab switching.

Ratings use the current complete object sequence converted by the editor. Integer time/coordinate quantization in `.osu` export may slightly change the read-back rating. Website values depend on the server's calculation version and stored beatmap; agreement with different algorithm versions or stale website caches is not guaranteed. Mod ratings and pp are not currently calculated.
