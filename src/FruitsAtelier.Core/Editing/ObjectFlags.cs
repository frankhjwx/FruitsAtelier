using System.Globalization;

namespace FruitsAtelier.Core;

/// <summary>Edits stable flags without discarding sample banks, custom samples or combo colours.</summary>
public static class ObjectFlags
{
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static int I(string? value) => int.TryParse(value, out int result) ? result : 0;

    private static (string Line, Action<string> Save, int Spans) Source(MapDocument document, Guid id)
    {
        if (document.Fruits.FirstOrDefault(f => f.Id == id) is { } fruit)
            return (fruit.OriginalLine ?? $"{N(fruit.X)},192,{N(fruit.TimeMs)},1,0,0:0:0:0:", s => fruit.OriginalLine = s, 0);
        if (document.Tracks.FirstOrDefault(t => t.Id == id) is { } track)
            return (track.OriginalLine ?? $"{N(track.Nodes[0].X)},192,{N(track.Nodes[0].TimeMs)},2,0,L|256:192,{track.SpanCount},1", s => track.OriginalLine = s, track.SpanCount);
        if (document.ImportedSliders.FirstOrDefault(t => t.Id == id) is { } slider)
        {
            string path = slider.PathType + "|" + string.Join('|', slider.ControlPoints.Skip(1).Select(p => N(p.X) + ":" + N(p.GeometryY)));
            return (slider.OriginalLine ?? $"{N(slider.X)},{N(slider.Y)},{N(slider.TimeMs)},2,0,{path},{slider.SpanCount},{N(slider.PixelLength)}", s => slider.OriginalLine = s, slider.SpanCount);
        }
        var banana = document.BananaShowers.First(b => b.Id == id);
        return (banana.OriginalLine ?? $"256,192,{N(banana.TimeMs)},8,0,{N(banana.EndTimeMs)},0:0:0:0:", s => banana.OriginalLine = s, 0);
    }

    public static bool NewCombo(MapDocument document, Guid id) => (I(Source(document, id).Line.Split(',')[3]) & 4) != 0;

    public static int[] SampleBanks(MapDocument document, Guid id, bool additions, int? edge = null)
    {
        var source = Source(document, id);
        var fields = source.Line.Split(',');
        int column = additions ? 1 : 0;
        int sampleIndex = source.Spans > 0 ? 10 : (I(fields[3]) & 8) != 0 ? 6 : 5;
        int Bank(string text)
        {
            var parts = text.Split(':');
            return parts.Length > column ? Math.Clamp(I(parts[column]), 0, 3) : 0;
        }
        int bank = fields.Length > sampleIndex ? Bank(fields[sampleIndex]) : 0;
        bool stream = document.Tracks.Any(t => t.Id == id && t.StreamSnapDivisor is not null);
        string[] sets = !stream && source.Spans > 0 && fields.Length > 9 ? fields[9].Split('|') : [];
        var values = Enumerable.Range(0, stream ? 1 : source.Spans + 1).Select(i => i < sets.Length && sets[i].Length > 0 ? Bank(sets[i]) : bank).ToArray();
        var inherited = additions ? InheritedSampleFields.AdditionSet : InheritedSampleFields.NormalSet;
        foreach (var sample in document.HitsoundOverrides.Where(o => o.SourceId == id && (edge is null || o.EdgeIndex == edge)))
        {
            int value = sample.InheritedFields.HasFlag(inherited) ? 0 : additions ? sample.Sample.AdditionSet : sample.Sample.NormalSet;
            if (sample.EdgeIndex >= 0 && sample.EdgeIndex < values.Length) values[sample.EdgeIndex] = value;
        }
        if (edge is { } index && index >= 0 && index < values.Length) return [values[index]];
        return values.Concat(document.HitsoundOverrides.Where(o => o.SourceId == id && (o.EdgeIndex < 0 || o.EdgeIndex >= values.Length))
            .Select(o => o.InheritedFields.HasFlag(inherited) ? 0 : additions ? o.Sample.AdditionSet : o.Sample.NormalSet)).ToArray();
    }

    public static void SetSampleBank(MapDocument document, Guid id, bool additions, int bank, int? edge = null)
    {
        if (bank is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(bank));
        var source = Source(document, id);
        var fields = source.Line.Split(',');
        int column = additions ? 1 : 0;
        string Set(string? text, int length)
        {
            var parts = (text ?? "").Split(':', length);
            int oldLength = parts.Length;
            Array.Resize(ref parts, length);
            for (int i = 0; i < length; i++) if (i >= oldLength || string.IsNullOrEmpty(parts[i])) parts[i] = i == 4 ? "" : "0";
            parts[column] = bank.ToString(CultureInfo.InvariantCulture);
            return string.Join(':', parts);
        }
        int sampleIndex = source.Spans > 0 ? 10 : (I(fields[3]) & 8) != 0 ? 6 : 5;
        if (fields.Length <= sampleIndex) Array.Resize(ref fields, sampleIndex + 1);
        if (source.Spans > 0)
        {
            var sets = fields[9]?.Split('|') ?? [];
            string fallback = string.Join(':', (fields[10] ?? "0:0").Split(':').Take(2));
            var result = Enumerable.Range(0, source.Spans + 1).Select(i => i < sets.Length && sets[i].Length > 0 ? sets[i] : fallback).ToArray();
            for (int i = 0; i < result.Length; i++) if (edge is null || edge == i) result[i] = Set(result[i], 2);
            fields[9] = string.Join('|', result);
            fields[8] ??= string.Join('|', Enumerable.Repeat(fields[4], result.Length));
        }
        if (edge is null || source.Spans == 0) fields[sampleIndex] = Set(fields[sampleIndex], 5);
        source.Save(string.Join(',', fields));
        var inherited = additions ? InheritedSampleFields.AdditionSet : InheritedSampleFields.NormalSet;
        for (int i = 0; i < document.HitsoundOverrides.Count; i++)
        {
            var item = document.HitsoundOverrides[i];
            if (item.SourceId != id || edge is not null && item.EdgeIndex != edge) continue;
            document.HitsoundOverrides[i] = item with
            {
                Sample = additions ? item.Sample with { AdditionSet = Math.Max(1, bank) } : item.Sample with { NormalSet = Math.Max(1, bank) },
                InheritedFields = bank == 0 ? item.InheritedFields | inherited : item.InheritedFields & ~inherited
            };
        }
    }

    public static void SetNewCombo(MapDocument document, Guid id, bool enabled)
    {
        var source = Source(document, id);
        var fields = source.Line.Split(',');
        int type = I(fields[3]);
        fields[3] = N(enabled ? type | 4 : type & ~4);
        source.Save(string.Join(',', fields));
    }

    public static void SetStreamNewCombo(MapDocument document, Guid id, int eventIndex, double timeMs, bool enabled)
    {
        if (eventIndex == 0) { SetNewCombo(document, id, enabled); return; }
        var track = document.Tracks.First(t => t.Id == id && t.StreamSnapDivisor is not null);
        double offset = timeMs - track.Nodes[0].TimeMs;
        if (!double.IsFinite(offset) || offset <= 0) throw new ArgumentOutOfRangeException(nameof(timeMs));
        track.StreamNewComboOffsets.RemoveAll(at => Math.Abs(at - offset) < 1e-7);
        if (enabled) { track.StreamNewComboOffsets.Add(offset); track.StreamNewComboOffsets.Sort(); }
    }

    public static int[] Sounds(MapDocument document, Guid id, int? edge = null)
    {
        var source = Source(document, id);
        var fields = source.Line.Split(',');
        int sound = I(fields[4]);
        if (source.Spans == 0) return [document.HitsoundOverrides.FirstOrDefault(o => o.SourceId == id && o.EdgeIndex == 0)?.Sample.Additions ?? sound];
        string[] old = fields.Length > 8 ? fields[8].Split('|') : [];
        int[] values = Enumerable.Range(0, source.Spans + 1)
            .Select(i => i < old.Length && old[i].Length > 0 ? I(old[i]) : sound).ToArray();
        foreach (var sample in document.HitsoundOverrides.Where(o => o.SourceId == id && o.EdgeIndex >= 0 && o.EdgeIndex < values.Length))
            values[sample.EdgeIndex] = sample.Sample.Additions;
        return edge is { } index && index >= 0 && index < values.Length ? [values[index]] : values;
    }

    public static void SetSound(MapDocument document, Guid id, int flag, bool enabled, int? edge = null)
    {
        var source = Source(document, id);
        var fields = source.Line.Split(',');
        int Apply(int value) => enabled ? value | flag : value & ~flag;
        if (source.Spans == 0) fields[4] = N(Apply(I(fields[4])));
        else
        {
            var sounds = Sounds(document, id);
            for (int i = 0; i < sounds.Length; i++) if (edge is null || edge == i) sounds[i] = Apply(sounds[i]);
            if (fields.Length < 11) Array.Resize(ref fields, 11);
            fields[8] = string.Join('|', sounds.Select(Numeric));
            fields[9] ??= string.Join('|', Enumerable.Repeat("0:0", sounds.Length));
            fields[10] ??= "0:0:0:0:";
            if (edge is null || edge == 0) fields[4] = N(Apply(I(fields[4])));
        }
        source.Save(string.Join(',', fields));
        for (int i = 0; i < document.HitsoundOverrides.Count; i++)
        {
            var sample = document.HitsoundOverrides[i];
            if (sample.SourceId == id && (edge is null || sample.EdgeIndex == edge))
                document.HitsoundOverrides[i] = sample with { Sample = sample.Sample with { Additions = Apply(sample.Sample.Additions) } };
        }
        static string Numeric(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
