using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.Core;

internal static class AutoPreviewPerformanceTests
{
    public static void Run()
    {
        var random = new Random(1031);
        var objects = Enumerable.Range(0, 240).Select(i => new ConvertedCatchObject(
            Guid.NewGuid(), i, (CatchObjectKind)(i % 4), random.Next(0, 100) * 10,
            random.NextDouble() * 512, 0, 0, 0)).ToArray();
        foreach (bool lazer in new[] { false, true })
        foreach (double cs in new[] { 0d, 5d, 10d })
        foreach (var traversal in new IReadOnlyList<ConvertedCatchObject>?[] { null, objects.Reverse().ToArray() })
        foreach (var input in new[] { objects, Array.Empty<ConvertedCatchObject>(), objects.OrderBy(o => o.TimeMs).ToArray() })
        {
            var expected = new AutoPreviewReference(input, cs, traversal, lazer);
            var actual = new CatchAutoPreview(input, cs, traversal, lazer);
            foreach (double time in Enumerable.Range(-1, 1050).Select(i => i + .25).Append(double.NaN).Append(double.PositiveInfinity))
            {
                Check(expected.At(time).Equals(actual.At(time)), "autoplay frame");
                Check(expected.HyperDashingAt(time) == actual.HyperDashingAt(time), "hyperdash interval");
                Check(expected.HyperTintAt(time).Equals(actual.HyperTintAt(time)), "hyperdash tint");
                Check(expected.FacingLeftAt(time) == actual.FacingLeftAt(time), "facing direction");
                Check(expected.HyperStarts(time, time + 20).SequenceEqual(actual.HyperStarts(time, time + 20)), "interval starts");
            }
        }
        var warm = new CatchAutoPreview(objects, 5);
        Query();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Query();
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warm direct playback queries allocate no managed memory");
        void Query()
        {
            for (int i = 0; i < 1000; i++)
            {
                _ = warm.HyperDashingAt(i); _ = warm.HyperTintAt(i);
                _ = warm.FacingLeftAt(i); _ = warm.At(i);
            }
        }
        Console.WriteLine("PASS Autoplay reference equivalence: empty, unordered and tied events, parent traversal, CS and lazer variants");
    }

    public static void Benchmark()
    {
        var objects = Enumerable.Range(0, 100_000).Select(i => new ConvertedCatchObject(
            Guid.NewGuid(), i, (CatchObjectKind)(i % 4), i * 7.5, (i * 197) % 513, 0, 0, 0)).ToArray();
        var original = new AutoPreviewReference(objects, 5);
        var current = new CatchAutoPreview(objects, 5);
        double checksum = 0;
        void OriginalQueries() { for (int i = 0; i < 100_000; i++) { double time = i * 7.5; checksum += original.HyperTintAt(time); if (original.HyperDashingAt(time)) checksum++; if (original.FacingLeftAt(time)) checksum++; } }
        void CurrentQueries() { for (int i = 0; i < 100_000; i++) { double time = i * 7.5; checksum += current.HyperTintAt(time); if (current.HyperDashingAt(time)) checksum++; if (current.FacingLeftAt(time)) checksum++; } }
        // Warm the batch callers as well as the searches before sampling tiered JIT code.
        for (int i = 0; i < 40; i++) { OriginalQueries(); CurrentQueries(); }
        var report = new { Objects = objects.Length, DurationMs = objects[^1].TimeMs,
            OriginalConstruction = Measure(() => GC.KeepAlive(new AutoPreviewReference(objects, 5))),
            CurrentConstruction = Measure(() => GC.KeepAlive(new CatchAutoPreview(objects, 5))),
            OriginalQueries = Measure(OriginalQueries), CurrentQueries = Measure(CurrentQueries), Checksum = checksum };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Map(string path)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
        var document = OsuBeatmapReader.ReadFile(path);
        var conversion = CatchStreamConverter.Convert(document);
        Check(conversion.Success, "source conversion succeeds");
        foreach (var objects in new[] { conversion.Objects, CatchPreviewMods.HardRock(document, conversion) })
        foreach (bool lazer in new[] { false, true })
        {
            var expected = new AutoPreviewReference(objects, document.CircleSize, lazerEditor: lazer);
            var actual = new CatchAutoPreview(objects, document.CircleSize, lazerEditor: lazer);
            foreach (var item in objects)
            foreach (double time in new[] { item.TimeMs - .25, item.TimeMs, item.TimeMs + .25 })
            {
                Check(expected.At(time).Equals(actual.At(time)), "NM/HR map frame");
                Check(expected.HyperDashingAt(time) == actual.HyperDashingAt(time), "NM/HR map hyperdash");
                Check(expected.HyperTintAt(time).Equals(actual.HyperTintAt(time)), "NM/HR map tint");
                Check(expected.FacingLeftAt(time) == actual.FacingLeftAt(time), "NM/HR map facing");
            }
        }
        Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "source remains unchanged");
        Console.WriteLine($"PASS NM/HR autoplay reference, classic/lazer: {conversion.Objects.Count} events; {path}");
    }

    private static object Measure(Action action)
    {
        var elapsed = new double[7]; var allocated = new long[7];
        for (int i = 0; i < elapsed.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(); long start = Stopwatch.GetTimestamp();
            action(); elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(elapsed); Array.Sort(allocated);
        return new { MedianMs = elapsed[3], MaxMs = elapsed[^1], AllocatedBytes = allocated[3] };
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Autoplay differs from independent reference: " + message); }
}
