using FruitsAtelier.Core;

internal static class HyperDashContextTests
{
    public static void Run()
    {
        var random = new Random(39207);
        var objects = Enumerable.Range(0, 600).Select(i => Object(
            Math.Floor(random.NextDouble() * 800) + random.NextDouble(),
            random.NextDouble() * 700 - 90, (CatchObjectKind)(i % 4))).ToArray();
        foreach (double cs in new[] { 0d, 4d, 5d, 10d })
        foreach (bool tiny in new[] { false, true })
        {
            var expected = OriginalCalculation(objects, cs, tiny);
            Check(expected.SequenceEqual(HyperDashCalculator.Calculate(objects, cs, tiny)), "full calculation preserves the independent sequence oracle");
            var contexts = HyperDashCalculator.CreateContexts(objects, cs, tiny);
            var indices = Ordered(objects, tiny);
            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];
                Check(contexts[index].HasValue, "included object has incoming context");
                if (i + 1 == indices.Length) continue;
                int next = indices[i + 1];
                var result = HyperDashCalculator.Evaluate(objects[index], objects[next], contexts[index]!.Value, next);
                Check(result.State == expected[index], "context evaluation matches independent full-sequence oracle");
                Check(result.NextContext == contexts[next], "outgoing context becomes the next incoming context");
                var scalar = HyperDashCalculator.Evaluate(objects[index].TimeMs, objects[index].X,
                    objects[next].TimeMs, objects[next].X, contexts[index]!.Value, next);
                Check(scalar == result, "scalar candidate evaluation matches object evaluation");
            }
            for (int i = 0; i < objects.Length; i++)
                if (!indices.Contains(i)) Check(!contexts[i].HasValue, "excluded object has no movement context");
        }

        // A preceding near-limit dash consumes excess reach in the continuing direction.
        var history = new[] { Object(0, 100), Object(100, 240), Object(200, 350) };
        var prefix = HyperDashCalculator.CreateContexts(history, 5);
        var continuing = HyperDashCalculator.Evaluate(history[1], history[2], prefix[1]!.Value);
        var reversal = HyperDashCalculator.Evaluate(100, 240, 200, 130, prefix[1]!.Value);
        Check(continuing.State.IsHyperDash && !reversal.State.IsHyperDash, "same-direction excess and reversal use different reach");
        var fresh = HyperDashCalculator.Evaluate(history[1], history[2], HyperDashCalculator.CreateContext(5));
        Check(!fresh.State.IsHyperDash && fresh.State.Movement?.Mode == CatchMovementMode.Dash,
            "speed above one logical pixel per ms can remain a regular dash");
        var wide = HyperDashCalculator.Evaluate(0, 100, 100, 230, HyperDashCalculator.CreateContext(0));
        var narrow = HyperDashCalculator.Evaluate(0, 100, 100, 230, HyperDashCalculator.CreateContext(10));
        Check(!wide.State.IsHyperDash && narrow.State.IsHyperDash, "circle size changes the actual hyperdash threshold");
        Check(HyperDashCalculator.Evaluate(0, 100, 100, 105, HyperDashCalculator.CreateContext(5)).State.Movement?.Mode == CatchMovementMode.Stand,
            "catching width permits standing");
        Check(HyperDashCalculator.Evaluate(0, 100, 100, 160, HyperDashCalculator.CreateContext(5)).State.Movement?.Mode == CatchMovementMode.Walk,
            "walking uses the catching edge");
    }

    private static ConvertedCatchObject Object(double time, double x, CatchObjectKind kind = CatchObjectKind.Fruit)
        => new(Guid.NewGuid(), 0, kind, time, x, x, x, 0);

    private static int[] Ordered(IReadOnlyList<ConvertedCatchObject> objects, bool tiny)
        => Enumerable.Range(0, objects.Count).Where(i => objects[i].Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet
            || tiny && objects[i].Kind == CatchObjectKind.TinyDroplet).OrderBy(i => objects[i].TimeMs).ToArray();

    // An independent sequence oracle keeps rounding and history regressions visible.
    private static HyperDashState[] OriginalCalculation(IReadOnlyList<ConvertedCatchObject> objects, double cs, bool tiny)
    {
        double standLimit = CatchSize.CatchWidth(cs) / 2;
        double halfWidth = standLimit / CatchSize.AllowedCatchRange;
        var states = new HyperDashState[objects.Count];
        int lastDirection = 0;
        double lastExcess = halfWidth;
        var indices = Ordered(objects, tiny);
        for (int i = 0; i + 1 < indices.Length; i++)
        {
            int index = indices[i], nextIndex = indices[i + 1];
            var current = objects[index];
            var next = objects[nextIndex];
            float currentX = Math.Clamp((float)current.X, 0, 512);
            float nextX = Math.Clamp((float)next.X, 0, 512);
            int direction = nextX > currentX ? 1 : -1;
            double timeToNext = (int)next.TimeMs - (int)current.TimeMs - 1000f / 60f / 4;
            double distanceToNext = Math.Abs(nextX - currentX) - (lastDirection == direction ? lastExcess : halfWidth);
            float distanceToHyper = (float)(timeToNext - distanceToNext);
            if (distanceToHyper < 0)
            {
                states[index] = new(nextIndex, 0);
                lastExcess = halfWidth;
            }
            else
            {
                states[index] = new(null, distanceToHyper);
                lastExcess = Math.Clamp(distanceToHyper, 0, halfWidth);
            }
            if (next.TimeMs > current.TimeMs)
            {
                double distance = Math.Abs(Math.Clamp(next.X, 0, 512) - Math.Clamp(current.X, 0, 512));
                double dashLimit = Math.Max(0, Math.Abs(nextX - currentX) + timeToNext - distanceToNext);
                double walkLimit = Math.Min(dashLimit, (next.TimeMs - current.TimeMs) * .5 + standLimit);
                states[index] = states[index] with { Movement = new(distance, standLimit, walkLimit, dashLimit,
                    distance <= standLimit ? CatchMovementMode.Stand : states[index].IsHyperDash ? CatchMovementMode.HyperDash
                        : distance <= walkLimit ? CatchMovementMode.Walk : CatchMovementMode.Dash) };
            }
            lastDirection = direction;
        }
        return states;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
