// Based on ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f, Catcher.cs and CaughtObject.cs.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public readonly record struct CatchPlateSprite(ConvertedCatchObject Object, double X, double Y, float Opacity);

public sealed class CatchPlatePreview
{
    private sealed record Entry(ConvertedCatchObject Object, double X, double Y);
    private sealed record Batch(Entry[] Entries, double End, double CatcherX);
    private readonly Batch[] batches;
    private readonly (Entry Entry, double CatcherX)[] droplets;
    private readonly double plateOffset;

    public CatchPlatePreview(IReadOnlyList<ConvertedCatchObject> objects, CatchAutoPreview autoplay,
        double circleSize, HashSet<(Guid SourceId, int EventIndex)> comboEnds)
    {
        var groups = new List<Batch>();
        var stack = new List<Entry>();
        var drops = new List<(Entry, double)>();
        var occupied = new Dictionary<(int X, int Y), List<Entry>>();
        double spacing = CatchSize.FruitDiameter(circleSize) * 10 / 64;
        plateOffset = -5 * CatchSize.Scale(circleSize) * 2;
        uint random = 0xA341316C;
        double Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return random / (double)uint.MaxValue; }
        foreach (var item in objects)
        {
            double catcher = autoplay.At(item.TimeMs).X;
            if (item.Kind != CatchObjectKind.TinyDroplet)
            {
                double x = item.X - catcher, y = 0;
                double radius = spacing * (item.Kind == CatchObjectKind.Droplet ? .8 : item.Kind == CatchObjectKind.Banana ? .6 : 1);
                while (Collides(x, y, radius)) { x += (Next() * 2 - 1) * radius; y -= .01 + Next() * 5; }
                var entry = new Entry(item, x, y);
                if (item.Kind == CatchObjectKind.Droplet) drops.Add((entry, catcher));
                else
                {
                    stack.Add(entry);
                    var key = Cell(x, y);
                    if (!occupied.TryGetValue(key, out var cell)) occupied[key] = cell = [];
                    cell.Add(entry);
                }
            }
            if (comboEnds.Contains((item.SourceId, item.EventIndex)))
            {
                if (stack.Count > 0) groups.Add(new(stack.ToArray(), item.TimeMs, catcher));
                stack.Clear(); occupied.Clear();
            }
        }
        if (stack.Count > 0) groups.Add(new(stack.ToArray(), double.PositiveInfinity, 0));
        batches = groups.ToArray(); droplets = drops.ToArray();
        (int, int) Cell(double x, double y) => ((int)Math.Floor(x / spacing), (int)Math.Floor(y / spacing));
        bool Collides(double x, double y, double radius)
        {
            var (cx, cy) = Cell(x, y);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (occupied.TryGetValue((cx + dx, cy + dy), out var cell))
                    foreach (var other in cell)
                        if ((other.X - x) * (other.X - x) + (other.Y - y) * (other.Y - y) < radius * radius) return true;
            return false;
        }
    }
    public IEnumerable<CatchPlateSprite> At(double time, double catcherX)
    {
        int low = 0, high = batches.Length;
        while (low < high) { int mid = (low + high) / 2; if (batches[mid].End + 750 <= time) low = mid + 1; else high = mid; }
        for (int i = low; i < batches.Length; i++)
        {
            var batch = batches[i];
            if (batch.Entries[0].Object.TimeMs > time) break;
            foreach (var entry in batch.Entries)
            {
                if (entry.Object.TimeMs > time) break;
                yield return time < batch.End ? new(entry.Object, catcherX + entry.X, plateOffset + entry.Y, 1)
                    : Exploded(entry, batch.CatcherX, time - batch.End);
            }
        }
        low = 0; high = droplets.Length;
        while (low < high) { int mid = (low + high) / 2; if (droplets[mid].Entry.Object.TimeMs + 750 <= time) low = mid + 1; else high = mid; }
        for (int i = low; i < droplets.Length && droplets[i].Entry.Object.TimeMs <= time; i++)
            yield return Exploded(droplets[i].Entry, droplets[i].CatcherX, time - droplets[i].Entry.Object.TimeMs);
    }
    private CatchPlateSprite Exploded(Entry entry, double catcherX, double elapsed)
    {
        double y = elapsed <= 250 ? -50 * Math.Sin(elapsed / 250 * Math.PI / 2)
            : -50 + 100 * (1 - Math.Cos((elapsed - 250) / 500 * Math.PI / 2));
        return new(entry.Object, catcherX + entry.X * (1 + 6 * elapsed / 1000), plateOffset + entry.Y + y,
            (float)Math.Clamp(1 - elapsed / 750, 0, 1));
    }
}
