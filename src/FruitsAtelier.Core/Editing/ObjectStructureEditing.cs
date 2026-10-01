using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record SliderMergeSelection(Guid[] Ids, bool AllowsLinear, string? Error);

public static class ObjectStructureEditing
{
    public static SliderMergeSelection MergeSelection(MapDocument document, IEnumerable<Guid> selection)
    {
        var selected = selection.ToHashSet();
        if (document.BananaShowers.Any(b => selected.Contains(b.Id))) return new([], false, L.Get("merge.bananaSelected"));
        var parents = document.Fruits.Select(f => (f.Id, f.TimeMs, f.SourceOrder))
            .Concat(document.Tracks.Where(t => t.Nodes.Count >= 2).Select(t => (t.Id, TimeMs: t.Nodes[0].TimeMs, t.SourceOrder)))
            .Concat(document.ImportedSliders.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
            .OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        var indices = Enumerable.Range(0, parents.Length).Where(i => selected.Contains(parents[i].Id)).ToArray();
        var ids = indices.Select(i => parents[i].Id).ToArray();
        if (ids.Length < 2) return new(ids, false, L.Get("merge.selectMultiple"));
        if (indices[^1] - indices[0] + 1 != indices.Length) return new(ids, false, L.Get("merge.notConsecutive"));
        bool linear = !document.Tracks.Where(t => selected.Contains(t.Id)).Any(t =>
            Enumerable.Range(0, t.Nodes.Count - 1).Any(i => t.Nodes[i].OutgoingCurve is not null || CurveMath.SegmentKind(t, i) != CurveKind.Linear))
            && !document.ImportedSliders.Any(s => selected.Contains(s.Id) && s.PathType != 'L');
        return new(ids, linear, null);
    }

    public static Guid[] BreakStreams(MapDocument document, IEnumerable<Guid> selection)
    {
        var ids = selection.ToHashSet();
        var tracks = document.Tracks.Where(t => ids.Contains(t.Id) && t.StreamSnapDivisor is not null)
            .OrderBy(t => t.Nodes[0].TimeMs).ThenBy(t => t.SourceOrder).ToArray();
        var parentOrder = document.Fruits.Select(f => (f.Id, Time: f.TimeMs, f.SourceOrder))
            .Concat(document.Tracks.Select(t => (t.Id, Time: t.Nodes[0].TimeMs, t.SourceOrder)))
            .OrderBy(p => p.Time).ThenBy(p => p.SourceOrder)
            .Select((p, index) => (p.Id, index)).ToDictionary(p => p.Id, p => p.index);
        var timing = new TimingMap.Lookup(document);
        (Fruit Fruit, int ParentOrder)[] fruits;
        try
        {
            fruits = tracks.SelectMany(track => SliderFruitStream.Convert(document, track, timing).Select(item => (new Fruit
            {
                TimeMs = item.TimeMs, X = item.X, SourceOrder = track.SourceOrder,
                OriginalLine = SliderFruitStream.FruitLine(track, item.EventIndex,
                    Math.Round(item.X, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture),
                    Math.Round(item.TimeMs, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture))
            }, parentOrder[track.Id]))).ToArray();
        }
        catch (CatchConversionException ex) { throw new InvalidOperationException(ex.Message, ex); }
        document.Tracks.RemoveAll(t => tracks.Contains(t));
        // Expanded fruits retain the stream's parent traversal order at tied event times.
        var ordered = document.Fruits.Select(f => (Fruit: f, ParentOrder: parentOrder[f.Id])).Concat(fruits)
            .OrderBy(p => p.Fruit.TimeMs).ThenBy(p => p.Fruit.SourceOrder).ThenBy(p => p.ParentOrder)
            .Select(p => p.Fruit).ToArray();
        document.Fruits.Clear(); document.Fruits.AddRange(ordered);
        return fruits.Select(p => p.Fruit.Id).ToArray();
    }

    public static void ClearInternalAnchors(CurveTrack track)
    {
        if (track.Nodes.Count < 2) return;
        track.Nodes.RemoveRange(1, track.Nodes.Count - 2);
        track.Kind = CurveKind.Linear;
        foreach (var node in track.Nodes)
        {
            node.HandleIn = node.HandleOut = default;
            node.OutgoingKind = null; node.OutgoingCurve = null;
        }
    }

    public static CurveTrack Merge(MapDocument document, IEnumerable<Guid> selection, bool curved, bool derandomizeDroplets = true)
    {
        var chosen = MergeSelection(document, selection);
        if (chosen.Error is not null) throw new InvalidOperationException(chosen.Error);
        if (!curved && !chosen.AllowsLinear) throw new InvalidOperationException(L.Get("merge.curvedOnly"));
        // Work on a detached document so a failed fit or overlap cannot partially consume the selection.
        var working = document.DeepClone();
        var merged = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true };
        var preservedSegments = new HashSet<Guid>();
        foreach (Guid id in chosen.Ids)
        {
            if (working.Fruits.FirstOrDefault(f => f.Id == id) is { } fruit)
            {
                Append(merged, new Anchor { TimeMs = fruit.TimeMs, X = fruit.X });
                continue;
            }
            var track = working.Tracks.FirstOrDefault(t => t.Id == id)
                ?? ImportedSliderEditing.ConvertToTrack(working, id, derandomizeDroplets: derandomizeDroplets).Track;
            if ((long)track.Nodes.Count * track.SpanCount + merged.Nodes.Count > ImportedSlider.MaximumControlPoints)
                throw new InvalidOperationException(L.Get("merge.tooManyPoints"));
            double start = track.Nodes[0].TimeMs, duration = track.Nodes[^1].TimeMs - start;
            for (int span = 0; span < track.SpanCount; span++)
            {
                var part = track.DeepClone(); part.SpanCount = 1;
                if (span % 2 != 0) SliderControlEditing.Reverse(part);
                foreach (var node in part.Nodes)
                {
                    node.Id = Guid.NewGuid(); node.TimeMs += span * duration; node.OutgoingKind ??= part.Kind;
                    if (node.OutgoingCurve is { } curve) foreach (var control in curve.Controls) control.Id = Guid.NewGuid();
                }
                for (int i = 0; i < part.Nodes.Count; i++)
                {
                    var node = Append(merged, part.Nodes[i], i > 0);
                    if (i < part.Nodes.Count - 1) preservedSegments.Add(node.Id);
                }
            }
        }
        if (merged.Nodes.Count < 2) throw new InvalidOperationException(L.Get("merge.sameTime"));
        if (!curved) ClearLinearSegments(merged);
        if (curved)
            for (int i = 0; i < merged.Nodes.Count - 1; i++)
            {
                var a = merged.Nodes[i]; var b = merged.Nodes[i + 1];
                if (preservedSegments.Contains(a.Id)) continue;
                double slopeA = i == 0 ? (b.X - a.X) / (b.TimeMs - a.TimeMs)
                    : (b.X - merged.Nodes[i - 1].X) / (b.TimeMs - merged.Nodes[i - 1].TimeMs);
                double slopeB = i + 2 == merged.Nodes.Count ? (b.X - a.X) / (b.TimeMs - a.TimeMs)
                    : (merged.Nodes[i + 2].X - a.X) / (merged.Nodes[i + 2].TimeMs - a.TimeMs);
                double step = (b.TimeMs - a.TimeMs) / 3;
                a.HandleOut = new(step, Math.Clamp(a.X + slopeA * step, 0, 512) - a.X);
                b.HandleIn = new(-step, Math.Clamp(b.X - slopeB * step, 0, 512) - b.X);
                a.OutgoingKind = CurveKind.Bezier;
            }
        merged.SourceOrder = document.Fruits.FirstOrDefault(f => f.Id == chosen.Ids[0])?.SourceOrder
            ?? document.Tracks.FirstOrDefault(t => t.Id == chosen.Ids[0])?.SourceOrder
            ?? document.ImportedSliders.First(s => s.Id == chosen.Ids[0]).SourceOrder;
        merged.OriginalLine = MergedSamples(document, chosen.Ids[0], chosen.Ids[^1]);
        var wanted = chosen.Ids.ToHashSet();
        working.Fruits.RemoveAll(f => wanted.Contains(f.Id));
        working.Tracks.RemoveAll(t => wanted.Contains(t.Id));
        working.ImportedSliders.RemoveAll(s => wanted.Contains(s.Id));
        working.Tracks.Add(merged);
        var errors = CurveMath.Validate(working);
        if (errors.Count > 0) throw new InvalidOperationException(errors[0]);
        var writeCache = new OsuWriteCache();
        var converted = writeCache.Convert(working);
        if (!converted.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), converted.Diagnostics));
        OsuBeatmapWriter.Serialize(working, cache: writeCache);
        document.Fruits.RemoveAll(f => wanted.Contains(f.Id));
        document.Tracks.RemoveAll(t => wanted.Contains(t.Id));
        document.ImportedSliders.RemoveAll(s => wanted.Contains(s.Id));
        document.Tracks.Add(merged);
        return merged;
    }

    private static Anchor Append(CurveTrack track, Anchor node, bool preserveIncoming = false)
    {
        if (track.Nodes.LastOrDefault() is { } last)
        {
            double gap = node.TimeMs - last.TimeMs;
            if (gap < -1e-7) throw new InvalidOperationException(L.Get("merge.overlap"));
            if (gap < CurveMath.MinimumAnchorSpacingMs)
            {
                if (Math.Abs(gap) > 1e-7 || Math.Abs(last.X - node.X) > 1e-7)
                    throw new InvalidOperationException(L.Get("merge.sameTime"));
                last.HandleOut = node.HandleOut; last.OutgoingKind = node.OutgoingKind; last.OutgoingCurve = node.OutgoingCurve;
                return last;
            }
            if (!preserveIncoming) { last.OutgoingCurve = null; last.HandleOut = default; last.OutgoingKind = CurveKind.Linear; }
        }
        if (track.Nodes.Count >= ImportedSlider.MaximumControlPoints) throw new InvalidOperationException(L.Get("merge.tooManyPoints"));
        track.Nodes.Add(node);
        return node;
    }

    private static void ClearLinearSegments(CurveTrack track)
    {
        foreach (var node in track.Nodes)
        {
            node.HandleIn = node.HandleOut = default;
            node.OutgoingKind = CurveKind.Linear; node.OutgoingCurve = null;
        }
    }

    private static string MergedSamples(MapDocument document, Guid first, Guid last)
    {
        string[] Fields(Guid id) => (document.Fruits.FirstOrDefault(f => f.Id == id)?.OriginalLine
            ?? document.Tracks.FirstOrDefault(t => t.Id == id)?.OriginalLine
            ?? document.ImportedSliders.FirstOrDefault(s => s.Id == id)?.OriginalLine)?.Split(',') ?? [];
        string Sample(Guid id, string[] fields) => document.Fruits.Any(f => f.Id == id)
            ? fields.ElementAtOrDefault(5) ?? "0:0:0:0:" : fields.ElementAtOrDefault(10) ?? "0:0:0:0:";
        string EdgeSet(Guid id, string[] fields, bool tail)
        {
            if (!document.Fruits.Any(f => f.Id == id) && fields.Length > 9)
                return tail ? fields[9].Split('|')[^1] : fields[9].Split('|')[0];
            var sample = Sample(id, fields).Split(':');
            return string.Join(':', sample.Take(2));
        }
        var head = Fields(first); var tail = Fields(last);
        int flags = head.Length > 3 && int.TryParse(head[3], out int type) ? type & (4 | 112) : 0;
        int headSound = ObjectFlags.Sounds(document, first)[0], tailSound = ObjectFlags.Sounds(document, last)[^1];
        return $"0,192,0,{2 | flags},{headSound},L|256:192,1,1,{headSound}|{tailSound},{EdgeSet(first, head, false)}|{EdgeSet(last, tail, true)},{Sample(first, head)}";
    }
}
