using L = FruitsAtelier.Localization.Strings;
// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f,
// osu.Game.Rulesets.Catch/Beatmaps/CatchBeatmapProcessor.cs (initialiseHyperDash).
// Copyright (c) ppy Pty Ltd. Licensed under the MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public readonly record struct HyperDashState(int? TargetIndex, float DistanceToHyperDash)
{
    public bool IsHyperDash => TargetIndex.HasValue;
    public CatchMovementRange? Movement { get; init; }
}

public enum CatchMovementMode { Walk, Dash, HyperDash }

public readonly record struct CatchMovementRange(double Distance, double WalkLimit, double DashLimit, CatchMovementMode Mode);

public static class HyperDashCalculator
{
    public static HashSet<(Guid SourceId, int EventIndex)> GetHyperDashStarts(
        IReadOnlyList<ConvertedCatchObject> objects, double circleSize)
    {
        var states = Calculate(objects, circleSize);
        var starts = new HashSet<(Guid SourceId, int EventIndex)>();
        for (int i = 0; i < states.Length; i++)
            if (states[i].IsHyperDash) starts.Add((objects[i].SourceId, objects[i].EventIndex));
        return starts;
    }

    public static HyperDashState[] Calculate(IReadOnlyList<ConvertedCatchObject> objects, double circleSize)
    {
        ArgumentNullException.ThrowIfNull(objects);
        double halfCatcherWidth = CatchSize.CatchWidth(circleSize) / 2;
        // Stable tests hyperdash against the full catcher width, not its narrower catching margin.
        halfCatcherWidth /= CatchSize.AllowedCatchRange;
        var states = new HyperDashState[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
            var item = objects[i];
            if (!double.IsFinite(item.TimeMs) || item.TimeMs < 0 || item.TimeMs > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(objects), L.Get("core.hyperdash.timeRange"));
            if (!double.IsFinite(item.X))
                throw new ArgumentOutOfRangeException(nameof(objects), L.Get("core.hyperdash.finiteX"));
        }

        // Stable sorting preserves source order for simultaneous objects. Prefix objects retain excess movement context.
        var indices = Enumerable.Range(0, objects.Count)
            .Where(i => objects[i].Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet)
            .OrderBy(i => objects[i].TimeMs).ToArray();
        int lastDirection = 0;
        double lastExcess = halfCatcherWidth;
        for (int i = 0; i + 1 < indices.Length; i++)
        {
            int currentIndex = indices[i], nextIndex = indices[i + 1];
            var current = objects[currentIndex];
            var next = objects[nextIndex];
            float currentX = Math.Clamp((float)current.X, 0, 512);
            float nextX = Math.Clamp((float)next.X, 0, 512);
            int direction = nextX > currentX ? 1 : -1;

            // The source truncates each timestamp separately and computes its quarter-frame grace as float.
            double timeToNext = (int)next.TimeMs - (int)current.TimeMs - 1000f / 60f / 4;
            double distanceToNext = Math.Abs(nextX - currentX) - (lastDirection == direction ? lastExcess : halfCatcherWidth);
            float distanceToHyper = (float)(timeToNext - distanceToNext);
            if (distanceToHyper < 0)
            {
                states[currentIndex] = new(nextIndex, 0);
                lastExcess = halfCatcherWidth;
            }
            else
            {
                states[currentIndex] = new(null, distanceToHyper);
                lastExcess = Math.Clamp(distanceToHyper, 0, halfCatcherWidth);
            }
            if (next.TimeMs > current.TimeMs)
            {
                double distance = Math.Abs(nextX - currentX);
                double dashLimit = Math.Max(0, distance + timeToNext - distanceToNext);
                // Walking is estimated from the departure centre to the target's catching edge.
                // Hyperdash still uses the full-sequence official threshold above.
                double walkLimit = Math.Min(dashLimit, (next.TimeMs - current.TimeMs) * .5 + CatchSize.CatchWidth(circleSize) / 2);
                states[currentIndex] = states[currentIndex] with { Movement = new(distance, walkLimit, dashLimit,
                    states[currentIndex].IsHyperDash ? CatchMovementMode.HyperDash
                        : distance <= walkLimit ? CatchMovementMode.Walk : CatchMovementMode.Dash) };
            }
            lastDirection = direction;
        }
        return states;
    }
}
