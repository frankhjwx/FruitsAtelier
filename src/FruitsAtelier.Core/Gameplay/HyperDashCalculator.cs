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

public enum CatchMovementMode { Walk, Dash, HyperDash, Stand }

public readonly record struct CatchMovementRange(double Distance, double StandLimit, double WalkLimit, double DashLimit, CatchMovementMode Mode);

public readonly record struct HyperDashContext
{
    internal HyperDashContext(double standLimit, double halfCatcherWidth, int lastDirection, double lastExcess)
        => (StandLimit, HalfCatcherWidth, LastDirection, LastExcess) = (standLimit, halfCatcherWidth, lastDirection, lastExcess);

    public double StandLimit { get; }
    public double HalfCatcherWidth { get; }
    public int LastDirection { get; }
    public double LastExcess { get; }
}

public readonly record struct HyperDashEvaluation(HyperDashState State, HyperDashContext NextContext);

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

    public static HyperDashState[] Calculate(IReadOnlyList<ConvertedCatchObject> objects, double circleSize, bool includeTinyDroplets = false)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var context = CreateContext(circleSize);
        Validate(objects);
        var states = new HyperDashState[objects.Count];
        var indices = OrderedIndices(objects, includeTinyDroplets);
        for (int i = 0; i + 1 < indices.Length; i++)
        {
            int currentIndex = indices[i], nextIndex = indices[i + 1];
            var evaluation = EvaluatePair(objects[currentIndex], objects[nextIndex], context, nextIndex);
            states[currentIndex] = evaluation.State;
            context = evaluation.NextContext;
        }
        return states;
    }

    public static HyperDashContext CreateContext(double circleSize)
    {
        double standLimit = CatchSize.CatchWidth(circleSize) / 2;
        double halfCatcherWidth = standLimit;
        // Stable tests hyperdash against the full catcher width, not its narrower catching margin.
        halfCatcherWidth /= CatchSize.AllowedCatchRange;
        return new(standLimit, halfCatcherWidth, 0, halfCatcherWidth);
    }

    /// <summary>Returns the incoming movement context at each original input index; excluded kinds have no context.</summary>
    public static HyperDashContext?[] CreateContexts(IReadOnlyList<ConvertedCatchObject> objects, double circleSize, bool includeTinyDroplets = false)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var context = CreateContext(circleSize);
        Validate(objects);
        var contexts = new HyperDashContext?[objects.Count];
        var indices = OrderedIndices(objects, includeTinyDroplets);
        for (int i = 0; i < indices.Length; i++)
        {
            contexts[indices[i]] = context;
            if (i + 1 < indices.Length)
                context = EvaluatePair(objects[indices[i]], objects[indices[i + 1]], context, indices[i + 1]).NextContext;
        }
        return contexts;
    }

    /// <summary>Evaluates one departure using its incoming context, without rebuilding the preceding sequence.</summary>
    public static HyperDashEvaluation Evaluate(ConvertedCatchObject from, ConvertedCatchObject to, HyperDashContext context, int targetIndex = 0)
    {
        ValidateObject(from);
        ValidateObject(to);
        return EvaluatePair(from, to, context, targetIndex);
    }

    /// <summary>Evaluates a hypothetical position without allocating a converted object.</summary>
    public static HyperDashEvaluation Evaluate(double fromTimeMs, double fromX, double toTimeMs, double toX, HyperDashContext context, int targetIndex = 0)
    {
        ValidateCoordinates(fromTimeMs, fromX);
        ValidateCoordinates(toTimeMs, toX);
        return EvaluatePair(fromTimeMs, fromX, toTimeMs, toX, context, targetIndex);
    }

    private static void Validate(IReadOnlyList<ConvertedCatchObject> objects)
    {
        for (int i = 0; i < objects.Count; i++)
            ValidateObject(objects[i]);
    }

    private static void ValidateObject(ConvertedCatchObject item)
        => ValidateCoordinates(item.TimeMs, item.X);

    private static void ValidateCoordinates(double timeMs, double x)
    {
        if (!double.IsFinite(timeMs) || timeMs < 0 || timeMs > int.MaxValue)
            throw new ArgumentOutOfRangeException("objects", L.Get("core.hyperdash.timeRange"));
        if (!double.IsFinite(x))
            throw new ArgumentOutOfRangeException("objects", L.Get("core.hyperdash.finiteX"));
    }

    private static int[] OrderedIndices(IReadOnlyList<ConvertedCatchObject> objects, bool includeTinyDroplets)
    {
        // Stable sorting preserves source order for simultaneous objects. Prefix objects retain excess movement context.
        return Enumerable.Range(0, objects.Count)
            .Where(i => objects[i].Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet
                || includeTinyDroplets && objects[i].Kind == CatchObjectKind.TinyDroplet)
            .OrderBy(i => objects[i].TimeMs).ToArray();
    }

    private static HyperDashEvaluation EvaluatePair(ConvertedCatchObject current, ConvertedCatchObject next, HyperDashContext context, int nextIndex)
        => EvaluatePair(current.TimeMs, current.X, next.TimeMs, next.X, context, nextIndex);

    private static HyperDashEvaluation EvaluatePair(double currentTime, double currentPosition, double nextTime, double nextPosition, HyperDashContext context, int nextIndex)
    {
        double standLimit = context.StandLimit, halfCatcherWidth = context.HalfCatcherWidth;
        int lastDirection = context.LastDirection;
        double lastExcess = context.LastExcess;
        HyperDashState state;
        float currentX = Math.Clamp((float)currentPosition, 0, 512);
        float nextX = Math.Clamp((float)nextPosition, 0, 512);
        int direction = nextX > currentX ? 1 : -1;

        // The source truncates each timestamp separately and computes its quarter-frame grace as float.
        double timeToNext = (int)nextTime - (int)currentTime - 1000f / 60f / 4;
        double distanceToNext = Math.Abs(nextX - currentX) - (lastDirection == direction ? lastExcess : halfCatcherWidth);
        float distanceToHyper = (float)(timeToNext - distanceToNext);
        if (distanceToHyper < 0)
        {
            state = new(nextIndex, 0);
            lastExcess = halfCatcherWidth;
        }
        else
        {
            state = new(null, distanceToHyper);
            lastExcess = Math.Clamp(distanceToHyper, 0, halfCatcherWidth);
        }
        if (nextTime > currentTime)
        {
            double distance = Math.Abs(Math.Clamp(nextPosition, 0, 512) - Math.Clamp(currentPosition, 0, 512));
            double dashLimit = Math.Max(0, Math.Abs(nextX - currentX) + timeToNext - distanceToNext);
            // Walking is estimated from the departure centre to the target's catching edge.
            // Hyperdash still uses the full-sequence official threshold above.
            double walkLimit = Math.Min(dashLimit, (nextTime - currentTime) * .5 + standLimit);
            state = state with { Movement = new(distance, standLimit, walkLimit, dashLimit,
                distance <= standLimit ? CatchMovementMode.Stand : state.IsHyperDash ? CatchMovementMode.HyperDash
                    : distance <= walkLimit ? CatchMovementMode.Walk : CatchMovementMode.Dash) };
        }
        return new(state, new(standLimit, halfCatcherWidth, direction, lastExcess));
    }
}
