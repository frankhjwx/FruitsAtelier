using FruitsAtelier.Core;

internal static class StoryboardIntervalTests
{
    internal static void Run()
    {
        var random = new Random(75241);
        var source = new List<StoryboardSprite>();
        for (int i = 0; i < 1200; i++)
        {
            double start = i < 2 ? 0 : random.Next(-100, 1000);
            double end = i < 2 ? 1000000 : start + random.Next(0, 100);
            var group = new StoryboardCommandGroup();
            group.Commands.Add(new(9, 0, start, end, 1, 1, 0));
            if (i % 97 == 0)
            {
                group.Trigger = "HitSound";
                group.TriggerStart = i % 194 == 0 ? double.MinValue : 50;
                group.TriggerEnd = i % 194 == 0 ? double.MaxValue : 500;
            }
            var sprite = new StoryboardSprite
            {
                Path = $"image-{i % 23}.png", Frames = [$"image-{i % 23}.png", $"frame-{i % 11}.png"],
                X = i, Y = 240, Layer = (StoryboardLayer)(i % 5), Order = source.Count
            };
            sprite.Groups.Add(group); sprite.Compile(); source.Add(sprite);
        }
        var scene = new StoryboardScene(source, [], [], [], false, null);
        var sorted = source.OrderBy(s => s.Start).ToArray();
        var actual = new List<StoryboardSpriteState>();
        var expected = new List<StoryboardSpriteState>();
        var preload = new List<string>(); var expectedPreload = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        StoryboardTrigger[] triggers = [new("HitSound", 100), new("HitSound", 300)];
        double[] times = [double.NegativeInfinity, -101, 0, 50, 100, 500, 1000, 2000, 1000000,
            1000001, double.PositiveInfinity, double.NaN, .. Enumerable.Range(0, 120).Select(_ => (double)random.Next(-100, 1200)),
            .. source.Take(30).SelectMany(s => new[] { s.Start, s.End })];
        foreach (double time in times)
        foreach (bool passing in new[] { true, false })
        {
            expected.Clear();
            foreach (var sprite in sorted)
            {
                if (!(sprite.Start <= time)) break;
                if (sprite.End < time || sprite.Layer == StoryboardLayer.Fail && passing || sprite.Layer == StoryboardLayer.Pass && !passing) continue;
                var state = sprite.Evaluate(time, triggers);
                if (state.Opacity > 0 && state.ScaleX != 0 && state.ScaleY != 0) expected.Add(state);
            }
            expected.Sort(static (a, b) => { int layer = ((int)a.Layer).CompareTo((int)b.Layer); return layer == 0 ? a.Order.CompareTo(b.Order) : layer; });
            scene.Evaluate(time, actual, passing, triggers);
            if (!actual.SequenceEqual(expected)) throw new Exception($"Interval query changed states at {time}, passing={passing}.");
            foreach (double ahead in new[] { -1d, 0, 100, 2000 })
            {
                expectedPreload.Clear(); expectedSeen.Clear();
                foreach (var sprite in sorted)
                {
                    if (!(sprite.Start <= time + ahead)) break;
                    if (sprite.End < time) continue;
                    foreach (string path in sprite.Frames) if (expectedSeen.Add(path)) expectedPreload.Add(path);
                }
                scene.Preload(time, ahead, preload, seen);
                if (!preload.SequenceEqual(expectedPreload)) throw new Exception($"Interval query changed preload order at {time}, lookahead={ahead}.");
            }
        }
        for (int i = 0; i < 50; i++) scene.Evaluate(500, actual);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) scene.Evaluate(500, actual);
        if (GC.GetAllocatedBytesForCurrentThread() - allocated > 1024)
            throw new Exception("Warm storyboard queries allocate per-frame sorting or traversal objects.");
        var empty = new StoryboardScene([], [], [], [], false, null);
        empty.Evaluate(0, actual); empty.Preload(0, 2000, preload, seen);
        if (actual.Count != 0 || preload.Count != 0) throw new Exception("Empty scene retained query output.");
    }
}
