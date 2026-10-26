using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.Core;

internal static class StoryboardCompileTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static void Run()
    {
        var random = new Random(84931);
        for (int sample = 0; sample < 300; sample++)
        {
            var actual = Fixture(random, random.Next(7));
            var reference = Copy(actual);
            Check(actual, reference);
            foreach (var sprite in new[] { actual, reference })
                foreach (var group in sprite.Groups)
                    if (group.Commands.Count > 0) group.Commands.RemoveAt(0);
            Check(actual, reference);
        }
        var ties = new StoryboardSprite { X = 320, Y = 240 };
        var first = new StoryboardCommandGroup { Offset = 100 };
        first.Commands.AddRange([new(9, 0, 0, 100, 0, 0, 8), new(9, 0, 200, 300, 0, 1, 9),
            new(0, 0, 0, 100, 10, 20, 2), new(0, 0, 0, 100, 30, 40, 2)]);
        var second = new StoryboardCommandGroup { Offset = 100 };
        second.Commands.AddRange([new(9, 0, 0, 100, 1, 1, 1), default]);
        ties.Groups.AddRange([first, second]); Check(ties, Copy(ties));
        var extremes = new StoryboardSprite { X = 320, Y = 240 };
        var overflow = new StoryboardCommandGroup { Offset = 1e308 };
        double duration = 1e308 - -1e308;
        overflow.Commands.AddRange([new(0, 0, 100, 200, 5, 10, 4), new(0, 0, -1e308 + duration * 0, 1e308 + duration * 0, 0, 1, 2),
            new(9, 0, -1e308 + duration * 0, 1e308 + duration * 0, 0, 1, 3)]);
        var infiniteEnd = new StoryboardCommandGroup { Offset = 1e308, Count = 2 };
        infiniteEnd.Commands.AddRange([new(1, 0, -1e308, 1e308, 0, 1, 1), new(9, 0, 1e308, 1e308, 1, 1, 2)]);
        extremes.Groups.AddRange([overflow, infiniteEnd]); Check(extremes, Copy(extremes));
        foreach (string literal in new[] { "NaN", "Infinity", "-Infinity" })
        {
            var rejected = StoryboardReader.Read($"Sprite,Foreground,Centre,\"a.png\",320,240\n M,0,{literal},1000,0,0,1,1", null,
                Path.GetFullPath("artifacts/tests/storyboard-compile"));
            if (rejected.Diagnostics.Count == 0 || rejected.SpriteCount != 0)
                throw new Exception("Storyboard Reader accepted a non-finite literal command time.");
        }
        Console.WriteLine("PASS storyboard Compile matches the original stable ordering, defaults, alpha boundaries, loops and trigger peers");
    }

    private static void Check(StoryboardSprite actual, StoryboardSprite reference)
    {
        actual.Compile(); OriginalCompile(reference);
        bool ordinary = (bool)typeof(StoryboardSprite).GetField("ordinaryCommands", Private)!.GetValue(actual)!;
        bool expectedOrdinary = (bool)typeof(StoryboardSprite).GetField("ordinaryCommands", Private)!.GetValue(reference)!;
        if (!actual.Start.Equals(reference.Start) || !actual.End.Equals(reference.End) || actual.HasCommands != reference.HasCommands
            || ordinary != expectedOrdinary)
            throw new Exception("Compiled sprite bounds or ordinary-command state differs from the original algorithm.");
        var initial = (double[])typeof(StoryboardSprite).GetField("initial", Private)!.GetValue(actual)!;
        var expectedInitial = (double[])typeof(StoryboardSprite).GetField("initial", Private)!.GetValue(reference)!;
        if (!initial.SequenceEqual(expectedInitial)) throw new Exception("Compiled initial property values differ from the original algorithm.");
        for (int i = 0; i < actual.Groups.Count; i++)
        {
            var a = actual.Groups[i]; var b = reference.Groups[i];
            if (!a.First.Equals(b.First) || !a.Last.Equals(b.Last) || !a.Period.Equals(b.Period) || !a.Commands.SequenceEqual(b.Commands)
                || a.CancellationWindowEnd != b.CancellationWindowEnd
                || !a.CancellationPeers.Select(peer => actual.Groups.IndexOf(peer)).SequenceEqual(b.CancellationPeers.Select(peer => reference.Groups.IndexOf(peer))))
                throw new Exception("Compiled command bounds, retained source commands or trigger peers differ from the original algorithm.");
            for (int property = 0; property < 14; property++)
                if (!a.Tracks[property].SequenceEqual(b.Tracks[property])) throw new Exception("Compiled property tracks changed their stable source ordering.");
        }
        StoryboardTrigger[] triggers = [new("HitSound", 0), new("Passing", 150, 1), new("HitSound", 300, 2),
            new("Passing", 650), new("HitSound", 900, 1), new("Passing", 1200, 2)];
        foreach (double time in new[] { -300d, 0d, 50d, 100d, 200d, 1000d, 500d, 5000d, 150d, 2000d })
            foreach (var activation in new IReadOnlyList<StoryboardTrigger>?[] { null, triggers })
                if (actual.Evaluate(time, activation) != reference.Evaluate(time, activation))
                    throw new Exception($"Compiled sprite evaluation differs from the original algorithm at {time}ms.");
    }

    private static StoryboardSprite Fixture(Random random, int groups)
    {
        var sprite = new StoryboardSprite { Path = "frame.png", Frames = ["a.png", "b.png", "c.png"], FrameDelay = 50,
            LoopAnimation = random.Next(2) == 0, X = random.Next(640), Y = random.Next(480), OriginX = .5, OriginY = .5 };
        for (int g = 0; g < groups; g++)
        {
            var group = new StoryboardCommandGroup { Offset = random.Next(-2, 7) * 100, Count = random.Next(1, 5),
                Trigger = random.Next(3) == 0 ? (random.Next(2) == 0 ? "HitSound" : "Passing") : null,
                TriggerStart = random.Next(3) * 100, TriggerEnd = 1000 + random.Next(5) * 100, TriggerGroup = random.Next(3) };
            int count = random.Next(41);
            for (int c = 0; c < count; c++)
            {
                int property = random.Next(14); double start = random.Next(8) * 100;
                double from = property == 9 ? random.Next(3) * .5 : random.Next(-2, 5);
                double to = property == 9 ? random.Next(3) * .5 : random.Next(-2, 5);
                group.Commands.Add(new(property, 0, start, start + random.Next(4) * 50, from, to, random.Next(8)));
            }
            sprite.Groups.Add(group);
        }
        return sprite;
    }

    private static StoryboardSprite Copy(StoryboardSprite source)
    {
        var copy = new StoryboardSprite { Path = source.Path, Frames = source.Frames.ToArray(), FrameDelay = source.FrameDelay,
            LoopAnimation = source.LoopAnimation, X = source.X, Y = source.Y, OriginX = source.OriginX, OriginY = source.OriginY,
            Layer = source.Layer, Order = source.Order };
        foreach (var group in source.Groups)
        {
            var clone = new StoryboardCommandGroup { Offset = group.Offset, Count = group.Count, Trigger = group.Trigger,
                TriggerStart = group.TriggerStart, TriggerEnd = group.TriggerEnd, TriggerGroup = group.TriggerGroup };
            clone.Commands.AddRange(group.Commands); copy.Groups.Add(clone);
        }
        return copy;
    }

    private static void OriginalCompile(StoryboardSprite sprite)
    {
        foreach (var group in sprite.Groups)
        {
            group.First = group.Commands.Count == 0 ? 0 : group.Commands.Min(c => c.Start);
            group.Last = group.Commands.Count == 0 ? 0 : group.Commands.Max(c => c.End);
            group.Period = Math.Max(0, group.Last - group.First);
            group.Tracks = Enumerable.Range(0, 14).Select(p => group.Commands.Where(c => c.Property == p).OrderBy(c => c.Start).ThenBy(c => c.Order).ToArray()).ToArray();
        }
        foreach (var group in sprite.Groups.Where(g => g.Trigger is not null))
        {
            group.CancellationPeers = sprite.Groups.Where(g => g.Trigger is not null && (ReferenceEquals(g, group) || group.TriggerGroup != 0 && g.TriggerGroup == group.TriggerGroup)).ToArray();
            group.CancellationWindowEnd = group.CancellationPeers.Max(g => g.TriggerEnd);
        }
        var initial = (double[])typeof(StoryboardSprite).GetField("initial", Private)!.GetValue(sprite)!;
        initial[0] = sprite.X; initial[1] = sprite.Y; initial[2] = initial[3] = initial[4] = initial[9] = 1;
        initial[6] = initial[7] = initial[8] = 255;
        for (int p = 0; p < 13; p++)
        {
            var first = sprite.Groups.Where(g => g.Trigger is null).SelectMany(g => g.Tracks[p].Select(c => (Command: c, Start: c.Start + g.Offset)))
                .OrderBy(c => c.Start).ThenBy(c => c.Command.Order).FirstOrDefault();
            if (first.Command != default && (p < 10 || first.Command.Start == first.Command.End)) initial[p] = first.Command.From;
        }
        var ordinary = sprite.Groups.Where(g => g.Trigger is null && g.Commands.Count > 0).ToArray();
        typeof(StoryboardSprite).GetField("ordinaryCommands", Private)!.SetValue(sprite, ordinary.Length > 0);
        sprite.Start = ordinary.Length == 0 ? double.MaxValue : ordinary.Min(g => g.Offset + g.First);
        sprite.End = ordinary.Length == 0 ? double.MinValue : ordinary.Max(g => g.Offset + g.Last + g.Period * (g.Count - 1));
        var alphas = ordinary.SelectMany(g => g.Tracks[9].Select(c => (Command: c, Start: c.Start + g.Offset))).OrderBy(c => c.Start).ToArray();
        if (alphas.Length > 0 && alphas[0].Command.From == 0)
            foreach (var alpha in alphas)
                if (alpha.Command.From > 0 || alpha.Command.To > 0) { sprite.Start = alpha.Start; break; }
        foreach (var trigger in sprite.Groups.Where(g => g.Trigger is not null))
        {
            sprite.Start = Math.Min(sprite.Start, trigger.TriggerStart + trigger.First);
            sprite.End = Math.Max(sprite.End, trigger.TriggerEnd + trigger.Last);
        }
    }

    internal static void RunPerformance()
    {
        var random = new Random(22914);
        var current = Enumerable.Range(0, 2048).Select(_ => Fixture(random, 3)).ToArray();
        var reference = current.Select(Copy).ToArray();
        foreach (var sprite in current.Take(64)) sprite.Compile();
        foreach (var sprite in reference.Take(64)) OriginalCompile(sprite);
        Measure("original Compile", reference, OriginalCompile);
        Measure("current Compile", current, static sprite => sprite.Compile());

        static void Measure(string label, StoryboardSprite[] sprites, Action<StoryboardSprite> compile)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            foreach (var sprite in sprites) compile(sprite);
            Console.WriteLine($"{label}: {sprites.Length} identical-fixture sprites, CPU={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3}ms, allocated={GC.GetAllocatedBytesForCurrentThread() - allocated}B");
        }
    }
}
