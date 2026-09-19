// Based on ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f, Catcher.cs and CaughtObject.cs.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public sealed class CatchPlate(double circleSize)
{
    private sealed record Entry(ConvertedCatchObject Object, double X, double Y);
    private sealed record Batch(Entry[] Entries, double End, double CatcherX, bool Explode);
    private readonly List<Batch> batches = [];
    private readonly List<(Entry Entry, double CatcherX)> droplets = [];
    private readonly List<Entry> stack = [];
    private readonly Dictionary<(int X, int Y), List<Entry>> occupied = [];
    private readonly double spacing = CatchSize.FruitDiameter(circleSize) * 10 / 64;
    private readonly double plateOffset = -5 * CatchSize.Scale(circleSize) * 2;
    private uint random = 0xA341316C;
    private double Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return random / (double)uint.MaxValue; }

    public void Judge(ConvertedCatchObject item, double catcherX, bool caught, bool comboEnd)
    {
        if (caught && item.Kind != CatchObjectKind.TinyDroplet)
        {
            double x = item.X - catcherX, y = 0;
            double radius = spacing * (item.Kind == CatchObjectKind.Droplet ? .8 : item.Kind == CatchObjectKind.Banana ? .6 : 1);
            while (Collides(x, y, radius)) { x += (Next() * 2 - 1) * radius; y -= .01 + Next() * 5; }
            var entry = new Entry(item, x, y);
            if (item.Kind == CatchObjectKind.Droplet) droplets.Add((entry, catcherX));
            else
            {
                stack.Add(entry);
                var key = Cell(x, y);
                if (!occupied.TryGetValue(key, out var cell)) occupied[key] = cell = [];
                cell.Add(entry);
            }
        }
        if (comboEnd)
        {
            if (stack.Count > 0) batches.Add(new(stack.ToArray(), item.TimeMs, catcherX, caught));
            stack.Clear(); occupied.Clear();
        }
    }
    private (int, int) Cell(double x, double y) => ((int)Math.Floor(x / spacing), (int)Math.Floor(y / spacing));
    private bool Collides(double x, double y, double radius)
    {
        var (cx, cy) = Cell(x, y);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            if (occupied.TryGetValue((cx + dx, cy + dy), out var cell))
                foreach (var other in cell)
                    if ((other.X - x) * (other.X - x) + (other.Y - y) * (other.Y - y) < radius * radius) return true;
        return false;
    }
    public bool HasTransientAt(double time) => batches.Count > 0 && batches[^1].End + 750 > time
        || droplets.Count > 0 && droplets[^1].Entry.Object.TimeMs + 750 > time;
    public void Prune(double time)
    {
        batches.RemoveAll(b => b.End + 750 <= time);
        droplets.RemoveAll(d => d.Entry.Object.TimeMs + 750 <= time);
    }
    public IEnumerable<CatchPlateSprite> At(double time, double catcherX)
    {
        int low = 0, high = batches.Count;
        while (low < high) { int mid = (low + high) / 2; if (batches[mid].End + 750 <= time) low = mid + 1; else high = mid; }
        for (int i = low; i < batches.Count; i++)
        {
            var batch = batches[i];
            if (batch.Entries[0].Object.TimeMs > time) break;
            foreach (var entry in batch.Entries)
            {
                if (entry.Object.TimeMs > time) break;
                yield return time < batch.End ? new(entry.Object, catcherX + entry.X, plateOffset + entry.Y, 1)
                    : batch.Explode ? Exploded(entry, batch.CatcherX, time - batch.End)
                    : new(entry.Object, batch.CatcherX + entry.X,
                        plateOffset + entry.Y + 75 * (1 - Math.Cos((time - batch.End) / 750 * Math.PI / 2)),
                        (float)Math.Clamp(1 - (time - batch.End) / 750, 0, 1));
            }
        }
        foreach (var entry in stack)
            if (entry.Object.TimeMs <= time) yield return new(entry.Object, catcherX + entry.X, plateOffset + entry.Y, 1);
        low = 0; high = droplets.Count;
        while (low < high) { int mid = (low + high) / 2; if (droplets[mid].Entry.Object.TimeMs + 750 <= time) low = mid + 1; else high = mid; }
        for (int i = low; i < droplets.Count && droplets[i].Entry.Object.TimeMs <= time; i++)
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
