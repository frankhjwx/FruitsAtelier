using L = FruitsAtelier.Localization.Strings;
// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f.
// Sources: LegacyRulesetExtensions.CalculateScaleFromCircleSize; Catcher;
// DrawablePalpableCatchHitObject; DropletPiece; DrawableTinyDroplet; DrawableBanana.
// Copyright (c) ppy Pty Ltd. Licensed under the MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public static class CatchSize
{
    public const float BaseCatcherWidth = 106.75f;
    public const float AllowedCatchRange = 0.8f;
    // Use the banana's arrival scale for static editor objects; its random approach animation is not rendered.
    public const float BananaScaleFactor = 0.6f;

    public static float Scale(double circleSize)
    {
        if (!double.IsFinite(circleSize) || circleSize < 0 || circleSize > 10)
            throw new ArgumentOutOfRangeException(nameof(circleSize), L.Get("core.size.circleSize"));
        // The legacy rule receives a float CS and rounds to float before halving.
        double difficulty = (float)circleSize;
        return (float)(1.0f - 0.7f * ((difficulty - 5) / 5)) / 2;
    }

    public static float FruitDiameter(double circleSize) => 128 * Scale(circleSize);
    public static float FruitRadius(double circleSize) => 64 * Scale(circleSize);
    public static float BananaRadius(double circleSize) => FruitRadius(circleSize) * BananaScaleFactor;
    public static float DefaultDropletRadius(double circleSize) => 32 * Scale(circleSize);
    public static float DefaultTinyDropletRadius(double circleSize) => 8 * Scale(circleSize);
    public static float CatcherWidth(double circleSize) => BaseCatcherWidth * (Scale(circleSize) * 2);
    public static float CatchWidth(double circleSize) => CatcherWidth(circleSize) * AllowedCatchRange;
}
