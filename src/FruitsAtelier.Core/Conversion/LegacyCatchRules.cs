using L = FruitsAtelier.Localization.Strings;
// Portions adapted from osu!lazer, Copyright (c) ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence; see LICENCE.osu.txt and UPSTREAM.md in this directory.
namespace FruitsAtelier.Core;

internal struct CatchLegacyRandom
{
    private uint x;
    private uint y;
    private uint z;
    private uint w;

    public CatchLegacyRandom(int seed)
    {
        x = (uint)seed;
        y = 842502087;
        z = 3579807591;
        w = 273326509;
    }

    internal readonly bool SameState(CatchLegacyRandom other) => x == other.x && y == other.y && z == other.z && w == other.w;

    public int Next()
    {
        uint t = x ^ (x << 11);
        x = y;
        y = z;
        z = w;
        w = w ^ (w >> 19) ^ t ^ (t >> 8);
        return (int)(w & 0x7fffffff);
    }

    public int NextTinyOffset() => (int)(-20 + Next() * (1.0 / 2147483648.0) * 40);
    public double NextDouble() => Next() * (1.0 / 2147483648.0);
}

internal enum SliderEventKind { Head, Tick, LegacyLastTick, Tail, Repeat }
internal readonly record struct SliderEvent(SliderEventKind Kind, double TimeMs, double Progress);
internal sealed record NestedCatchEvent(CatchObjectKind Kind, double TimeMs, double Progress)
{
    public int RawOffset { get; set; }
}

internal static class LegacyCatchRules
{
    internal const double MinimumSliderVelocityMultiplier = 0.1;
    internal const double MaximumSliderVelocityMultiplier = 10;
    internal const double MaximumPathLength = 100_000;
    internal const int MaximumNestedObjects = 50_000;

    public static double Velocity(double beatLength, double sliderMultiplier, double sv)
    {
        double inheritedBeatLengthMagnitude = Math.Clamp((float)(100 / sv),
            (float)(100 / MaximumSliderVelocityMultiplier), (float)(100 / MinimumSliderVelocityMultiplier));
        return 100 * sliderMultiplier / (beatLength * (inheritedBeatLengthMagnitude / 100));
    }

    public static List<NestedCatchEvent> CreateNested(double start, double duration, double velocity, double tickDistance, double length, int spanCount = 1)
    {
        if (length > MaximumPathLength)
            throw new CatchConversionException(L.Get("core.legacyRules.lengthLimit"));
        var nested = new List<NestedCatchEvent>();
        SliderEvent? previous = null;
        foreach (var current in Events(start, duration, velocity, tickDistance, length, spanCount))
        {
            if (previous is SliderEvent last)
            {
                double interval = (int)current.TimeMs - (int)last.TimeMs;
                if (interval > 80)
                {
                    double spacing = interval;
                    while (spacing > 100) spacing /= 2;
                    for (double elapsed = spacing; elapsed < interval; elapsed += spacing)
                        Add(new(CatchObjectKind.TinyDroplet, last.TimeMs + elapsed,
                            last.Progress + elapsed / interval * (current.Progress - last.Progress)));
                }
            }

            // LegacyLastTick does not create a fruit, but remains the origin for the last tiny interval.
            previous = current;
            if (current.Kind == SliderEventKind.Tick)
                Add(new(CatchObjectKind.Droplet, current.TimeMs, current.Progress));
            else if (current.Kind is SliderEventKind.Head or SliderEventKind.Tail or SliderEventKind.Repeat)
                Add(new(CatchObjectKind.Fruit, current.TimeMs, current.Progress));
        }
        return nested;

        void Add(NestedCatchEvent item)
        {
            if (nested.Count >= MaximumNestedObjects)
                throw new CatchConversionException(L.Get("core.legacyRules.objectLimit"));
            nested.Add(item);
        }
    }

    public static void ApplyRandomSequence(List<NestedCatchEvent> nested, ref CatchLegacyRandom rng)
    {
        foreach (var item in nested)
        {
            if (item.Kind == CatchObjectKind.TinyDroplet) item.RawOffset = rng.NextTinyOffset();
            else if (item.Kind == CatchObjectKind.Droplet) rng.Next();
        }
    }

    private static IEnumerable<SliderEvent> Events(double start, double duration, double velocity, double tickDistance, double length, int spanCount)
    {
        tickDistance = Math.Clamp(tickDistance, 0, length);
        yield return new(SliderEventKind.Head, start, 0);
        var tickProgress = new List<double>();
        if (tickDistance > 0)
        {
            int ticks = 0;
            for (double distance = tickDistance; distance <= length; distance += tickDistance)
            {
                if (distance >= length - velocity * 10) break;
                if (++ticks > MaximumNestedObjects) throw new CatchConversionException(L.Get("core.legacyRules.tickLimit"));
                tickProgress.Add(distance / length);
            }
        }
        for (int span = 0; span < spanCount; span++)
        {
            double spanStart = start + span * duration;
            bool reversed = span % 2 == 1;
            for (int tick = 0; tick < tickProgress.Count; tick++)
            {
                double progress = tickProgress[reversed ? tickProgress.Count - 1 - tick : tick];
                yield return new(SliderEventKind.Tick, spanStart + (reversed ? 1 - progress : progress) * duration, progress);
            }
            if (span < spanCount - 1) yield return new(SliderEventKind.Repeat, spanStart + duration, (span + 1) % 2);
        }
        double finalStart = start + (spanCount - 1) * duration;
        double totalDuration = duration * spanCount;
        double lastTickTime = Math.Max(start + totalDuration / 2, finalStart + duration - 36);
        double lastProgress = (lastTickTime - finalStart) / duration;
        yield return new(SliderEventKind.LegacyLastTick, lastTickTime, spanCount % 2 == 0 ? 1 - lastProgress : lastProgress);
        yield return new(SliderEventKind.Tail, start + totalDuration, spanCount % 2);
    }
}

internal class CatchConversionException(string message) : Exception(message);
