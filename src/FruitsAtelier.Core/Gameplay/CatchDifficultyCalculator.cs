// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f (difficulty version 20260706).
// Copyright (c) ppy Pty Ltd. MIT licence: LICENSE.osu.txt. See docs/CATCH_DIFFICULTY.md.
namespace FruitsAtelier.Core;

public readonly record struct CatchDifficultyResult(double StarRating, int MaxCombo);

/// <summary>No-mod Catch movement difficulty, calculated from the complete converted object sequence.</summary>
public static class CatchDifficultyCalculator
{
    public const int Version = 20260706;
    private readonly record struct Movement(float Distance, float ExactDistance, double StrainTime);

    public static CatchDifficultyResult Calculate(IReadOnlyList<ConvertedCatchObject> objects, double circleSize)
    {
        ArgumentNullException.ThrowIfNull(objects);
        float halfWidth = CatchSize.CatchWidth(circleSize) * 0.5f;
        halfWidth *= 1 - Math.Max(0, (float)circleSize - 5.5f) * 0.0625f;
        // Validate the entire stream and preserve stable ordering for simultaneous objects.
        var hyper = HyperDashCalculator.Calculate(objects, circleSize);
        var indices = Enumerable.Range(0, objects.Count)
            .Where(i => objects[i].Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet)
            .OrderBy(i => objects[i].TimeMs).ToArray();
        if (indices.Length < 2) return new(0, indices.Length);

        float scale = 41 / halfWidth;
        float player = Math.Clamp((float)objects[indices[0]].X, 0, 512) * scale;
        var movements = new List<Movement>(indices.Length - 1);
        var peaks = new List<double>();
        double strain = 0, peak = 0;
        double sectionEnd = Math.Ceiling(objects[indices[1]].TimeMs / 750) * 750;
        for (int i = 1; i < indices.Length; i++)
        {
            var current = objects[indices[i]];
            var last = objects[indices[i - 1]];
            float position = Math.Clamp((float)current.X, 0, 512) * scale;
            float nextPlayer = Math.Clamp(player, position - 25, position + 25);
            var movement = new Movement(nextPlayer - player, position - player, Math.Max(40, current.TimeMs - last.TimeMs));
            player = hyper[indices[i - 1]].IsHyperDash ? position : nextPlayer;
            movements.Add(movement);

            while (current.TimeMs > sectionEnd)
            {
                if (peak > 0) peaks.Add(peak);
                peak = strain * Decay(sectionEnd - last.TimeMs);
                sectionEnd += 750;
                // Once floating-point decay underflows, empty sections cannot contribute.
                if (peak == 0 && sectionEnd < current.TimeMs)
                    sectionEnd = Math.Ceiling(current.TimeMs / 750) * 750;
            }
            strain = strain * Decay(current.TimeMs - last.TimeMs) + Evaluate(movements, hyper[indices[i - 1]]);
            peak = Math.Max(peak, strain);
        }
        if (peak > 0) peaks.Add(peak);
        peaks.Sort((a, b) => b.CompareTo(a));
        double difficulty = 0, weight = 1;
        foreach (double value in peaks) { difficulty += value * weight; weight *= 0.94; }
        return new(Math.Sqrt(difficulty) * 4.59, indices.Length);
    }

    private static double Decay(double milliseconds) => Math.Pow(0.2, milliseconds / 1000);

    private static double Evaluate(List<Movement> movements, HyperDashState lastHyper)
    {
        int index = movements.Count - 1;
        var current = movements[index];
        var last = index > 0 ? movements[index - 1] : default;
        double weightedTime = current.StrainTime + 16;
        double addition = Math.Pow(Math.Abs(current.Distance), 1.3) / 510;
        if (Math.Abs(current.Distance) > 0.1)
        {
            if (index >= 1 && Math.Abs(last.Distance) > 0.1 && Math.Sign(current.Distance) != Math.Sign(last.Distance))
            {
                double bonus = Math.Min(50, Math.Abs(current.Distance)) / 50;
                double antiflow = Math.Max(Math.Min(70, Math.Abs(last.Distance)) / 70, 0.38);
                double ratio = weightedTime / 1000;
                addition += 21 / Math.Sqrt(last.StrainTime + 16) * bonus * antiflow * Math.Max(1 - ratio * ratio * ratio, 0);
            }
            addition += 12.5 * Math.Min(Math.Abs(current.Distance), 82) / 246 / Math.Sqrt(weightedTime);
        }
        int linearCount = 0;
        for (int i = 0; i < Math.Min(index, 10); i++)
        {
            var previous = movements[index - i - 1];
            if (Math.Sign(current.Distance) != Math.Sign(previous.Distance) || current.Distance == 0 || previous.Distance == 0) break;
            double spacing = Math.Abs(current.Distance / current.StrainTime);
            double previousSpacing = Math.Abs(previous.Distance / previous.StrainTime);
            if (Math.Abs(spacing / previousSpacing - 1) > 0.05) break;
            linearCount++;
        }
        addition *= Math.Pow(0.7, linearCount);
        if (lastHyper.DistanceToHyperDash <= 20)
            addition *= 1 + (lastHyper.IsHyperDash ? 0 : 5.7) * ((20 - lastHyper.DistanceToHyperDash) / 20)
                * Math.Pow(Math.Min(current.StrainTime, 265) / 265, 1.5);
        if (index >= 2 && Math.Abs(current.ExactDistance) <= 82
            && current.ExactDistance == -last.ExactDistance && last.ExactDistance == -movements[index - 2].ExactDistance
            && current.StrainTime == last.StrainTime && last.StrainTime == movements[index - 2].StrainTime)
            addition = 0;
        return addition / weightedTime;
    }
}
