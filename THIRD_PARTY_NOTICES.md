# Third-party dependencies

The application uses the following NuGet packages, pinned by project files and `packages.lock.json`:

| Package | Version | License |
| --- | --- | --- |
| Vortice.Direct3D11 / Direct2D1 / DXGI / DirectX | 3.6.2 | MIT |
| Vortice.Mathematics | 1.9.2 | MIT |
| SharpGen.Runtime / SharpGen.Runtime.COM | 2.2.0-beta | MIT |
| NAudio.Core / NAudio.WinMM / NAudio.Wasapi | 2.2.1 | MIT |
| NAudio.Vorbis | 1.5.0 | MIT |
| NVorbis | 0.10.4 | MIT |

Package metadata is available in the restored package `.nuspec` files under `artifacts/packages`. Windows builds use the installed .NET 8 runtime; the self-contained macOS bundle includes it. Audio source references and limits are recorded in [Audio/REFERENCE.md](src/FruitsAtelier.App/Audio/REFERENCE.md); licence texts in `Audio/Licenses` are copied to builds.

Source and notices: [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows), [Vortice.Mathematics](https://github.com/amerkoleci/Vortice.Mathematics), [SharpGenTools](https://github.com/SharpGenTools/SharpGenTools).

The editor owns its models, curve editing and transactions. Catch conversion and gameplay calculations adapt MIT-licensed osu!lazer algorithms; the original osu!framework host and drawable classes are not bundled.

## Catch algorithms and display calculations

The AR/preempt and field geometry calculations refer to ppy/osu commit `48c4800e3ae4ee752452cdff83bd3787ccf3105f`: `osu.Game.Rulesets.Catch/Objects/CatchHitObject.cs`, `osu.Game/Beatmaps/IBeatmapDifficultyInfo.cs`, and `osu.Game.Rulesets.Catch/UI/CatchPlayfieldAdjustmentContainer.cs`. The original framework and UI classes are not bundled. See docs/CATCH_RENDERING.md for calculation boundaries.

All osu! references below use that same commit of [ppy/osu](https://github.com/ppy/osu/tree/48c4800e3ae4ee752452cdff83bd3787ccf3105f):

- `src/FruitsAtelier.Core/Conversion`: slider events, legacy RNG and Catch stream conversion adapted from `SliderEventGenerator.cs`, `JuiceStream.cs`, `JuiceStreamPath.cs`, `SliderPath.cs`, `LegacyRulesetExtensions.cs`, `LegacyRandom.cs`, `CatchBeatmapProcessor.cs` and `CatchBeatmap.cs`. Full source paths and boundaries: [UPSTREAM.md](src/FruitsAtelier.Core/Conversion/UPSTREAM.md); MIT text retained in `LICENCE.osu.txt`.
- `src/FruitsAtelier.Core/Gameplay`: Catch size and hyperdash rules from `osu.Game/Rulesets/Objects/Legacy/LegacyRulesetExtensions.cs`, `osu.Game.Rulesets.Catch/UI/Catcher.cs`, and `osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmapProcessor.cs`; MIT text retained in `LICENSE.osu.txt`.
- `src/FruitsAtelier.App/Skinning`: texture selection, density, crop and scale rules from legacy Catch skin pieces, `Fruit.cs`, `DrawableTinyDroplet.cs`, `LegacySkin.cs` and `LegacySkinExtensions.cs`. Full source paths: [REFERENCE.md](src/FruitsAtelier.App/Skinning/REFERENCE.md).

The following MIT notice applies to the adapted osu! source portions, not to independently licensed skin artwork.

Imported Bezier, perfect-circle and Catmull path approximation also adapts `osu.Framework/Utils/PathApproximator.cs` and `CircularArcProperties.cs` from [ppy/osu-framework commit e01524d1492885d8b00ac88b38e7963d76d7d454](https://github.com/ppy/osu-framework/tree/e01524d1492885d8b00ac88b38e7963d76d7d454). Its separate MIT notice is retained in `src/FruitsAtelier.Core/Conversion/LICENCE.osu-framework.txt`. The framework runtime is not bundled.

Copyright (c) 2025 ppy Pty Ltd <contact@ppy.sh>.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

## User-supplied skin assets

No default skin artwork is distributed in this repository. `assets/skins/default.osk` is an optional, Git-ignored local archive; when present, it is copied to local build outputs. Artwork rights remain with their respective owners. Permission to use an archive locally is not a grant to publicly redistribute the artwork; confirm the applicable resource licence before distributing a build containing it. Without a skin, the editor uses geometric fallback rendering. Importing an `.osk` never executes its contents.

## macOS host dependencies

The separate `FruitsAtelier.Mac` project uses Avalonia 11.3.7 (MIT;
Copyright 2013–2025 The AvaloniaUI Project), MicroCom.Runtime 0.11.0 (MIT;
Copyright 2021 Nikita Tsukanov), SkiaSharp 2.88.9 and HarfBuzzSharp 8.3.1.1.
The complete dependency versions and integrity hashes are in its `packages.lock.json`.
License texts and Skia/HarfBuzz third-party notices are in
`src/FruitsAtelier.Mac/Licenses` and are copied into the application bundle.
Avalonia source version: `0834dbbbb9252406b08f2e74e8f328cc5ba502ee`.
No Avalonia source files were copied into the editor.

OGG decoding reuses NVorbis 0.10.4 under the existing license above.
`Native/Audio.m` is project-owned glue to the system AVFoundation framework.
The self-contained local bundle also includes Microsoft's .NET runtime and its
bundled license and third-party notices. Local ad-hoc signing is not Developer ID
signing or notarization.

## Catch difficulty and ruleset icon

No-mod Catch star calculation adapts the MIT-licensed `CatchDifficultyCalculator`,
`CatchDifficultyHitObject`, `MovementEvaluator`, `Movement`, `StrainSkill` and
`StrainDecaySkill` from ppy/osu commit `48c4800e3ae4ee752452cdff83bd3787ccf3105f`
(difficulty version 20260706). The MIT notice is retained in
`src/FruitsAtelier.Core/Gameplay/LICENSE.osu.txt`; see [calculation boundaries](docs/CATCH_DIFFICULTY.md).

The unmodified Catch icon is from ppy/osu-resources commit
`d8d01c29ce0f298159aea3644b947d8b4a1882a2`, under CC BY-NC 4.0.
Its source, attribution and full licence ship in [assets/icons/osu](assets/icons/osu/README.md).
The colour scale follows the published star-rating stops in osu!web
`resources/js/utils/beatmap-helper.ts` at commit `61b0a41431395f86ff68fa12744f3266448ec885`;
interpolation is independently implemented as gamma-2.2 RGB interpolation.
