// Adapted from ppy/osu 7318ef41b01bdc618d1bc1ec86de617223705077,
// osu.Game.Rulesets.Catch/Skinning/Legacy/LegacyHitExplosion.cs.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public readonly record struct CatchLightingEvent(ConvertedCatchObject Object, double Offset, int Combo);

public readonly record struct CatchLightingState(float TallOpacity, float TallScaleX, float TallScaleY,
    float WideOpacity, float WideScaleX, float WideScaleY);

public static class CatchHitLighting
{
    public const double Lifetime = 700;

    public static CatchLightingState At(CatchLightingEvent entry, double time)
    {
        double age = time - entry.Object.TimeMs;
        if (age < 0 || age >= Lifetime) return default;
        float comboScale = Math.Clamp(entry.Combo / 200f, .35f, 1.125f);
        float tallProgress = 1 - MathF.Pow(1 - (float)Math.Clamp(age / 160, 0, 1), 2);
        float wideProgress = 1 - MathF.Pow(1 - (float)Math.Clamp(age / 500, 0, 1), 2);
        return new(entry.Object.Kind is CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet
                ? 0 : (float)Math.Max(0, 1 - age / 300),
            1 + (16 * comboScale - 1) * tallProgress, .9f + .2f * tallProgress,
            (float)(1 - age / 700), .9f, 1 + .3f * wideProgress);
    }
}
