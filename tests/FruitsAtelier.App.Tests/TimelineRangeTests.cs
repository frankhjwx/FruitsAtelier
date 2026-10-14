using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class TimelineRangeTests
{
    public static void Run()
    {
        var random = new Random(437);
        var starts = Enumerable.Range(0, 100000).Select(i => (double)(i / 3) * 10).ToArray();
        var ends = starts.Select((time, i) => i % 17 == 0 ? double.NegativeInfinity
            : time + (i % 101 == 0 ? 300000 : random.Next(500))).ToArray();
        var range = new TimelineVisibleRange();
        var cold = Stopwatch.StartNew(); range.Rebuild(starts, ends); cold.Stop();
        var actual = new List<int>(); var expected = new List<int>();
        foreach (double from in new[] { -1000d, 0, 1000, 170000, 330000, 700000 })
            foreach (double width in new[] { 0d, 200, 10000, 700000 })
            {
                range.Fill(from, from + width, actual); Scan(from, from + width);
                if (!actual.SequenceEqual(expected)) throw new Exception("Indexed timeline changed visibility or tied paint order.");
            }
        // Replacing a snapshot must discard intervals removed by edits or undo.
        range.Rebuild([0, 10, 10], [100, 10, 20]); range.Fill(20, 20, actual);
        if (!actual.SequenceEqual(new[] { 2, 0 })) throw new Exception("Timeline range retained stale intervals or lost inclusive endpoints.");
        range.UpdateEnd(0, 15); range.UpdateEnd(1, 30); range.Fill(20, 20, actual);
        if (!actual.SequenceEqual(new[] { 2, 1 })) throw new Exception("Draft tail range update retained a stale end.");
        range.Rebuild([], []); range.Fill(0, 100, actual);
        if (actual.Count != 0) throw new Exception("Empty timeline retained markers.");
        var segments = Enumerable.Range(0, 10000).Select(i => new BananaShower { TimeMs = i * 20, EndTimeMs = i * 20 + 10 }).ToArray();
        foreach (double from in new[] { -1d, 0, 10, 11, 20, 190000, 200000 })
        {
            int first = EditorView.FirstVisibleBananaSegment(segments, from);
            var visible = segments.Skip(first).TakeWhile(s => s.TimeMs <= from + 100);
            if (!visible.SequenceEqual(segments.Where(s => s.EndTimeMs >= from && s.TimeMs <= from + 100)))
                throw new Exception("Indexed banana segments changed clipping at a boundary or gap.");
        }
        range.Rebuild(starts, ends);
        for (int i = 0; i < 100; i++) { range.Fill(170000, 171000, actual); Scan(170000, 171000); }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var indexed = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) range.Fill(170000 + i, 171000 + i, actual);
        indexed.Stop(); long indexBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        var scan = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) Scan(170000 + i, 171000 + i);
        scan.Stop(); long scanBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine($"Timeline visibility CPU (100,000 markers, 1,000 queries): build={cold.Elapsed.TotalMilliseconds:F3} ms; scan={scan.Elapsed.TotalMilliseconds:F3} ms/{scanBytes} B; index={indexed.Elapsed.TotalMilliseconds:F3} ms/{indexBytes} B.");

        void Scan(double from, double through)
        {
            expected.Clear();
            for (int i = starts.Length - 1; i >= 0; i--)
                if (starts[i] <= through && ends[i] >= from) expected.Add(i);
        }
    }
}
