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

    public static void SetNewCombo(MapDocument document, Guid id, bool enabled)
    {
        var source = Source(document, id);
        var fields = source.Line.Split(',');
        int type = I(fields[3]);
        fields[3] = N(enabled ? type | 4 : type & ~4);
        source.Save(string.Join(',', fields));
    }

    public static int[] Sounds(MapDocument document, Guid id, int? edge = null)
    {
        var source = Source(document, id);
        var fields = source.Line.Split(',');
        int sound = I(fields[4]);
        if (source.Spans == 0) return [sound];
        string[] old = fields.Length > 8 ? fields[8].Split('|') : [];
        int[] values = Enumerable.Range(0, source.Spans + 1)
            .Select(i => i < old.Length && old[i].Length > 0 ? I(old[i]) : sound).ToArray();
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
        static string Numeric(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
