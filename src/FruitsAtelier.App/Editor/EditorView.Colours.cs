using FruitsAtelier.Core;
using FruitsAtelier.App.Skinning;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly Dictionary<Guid, (int Skin, int Beatmap)> comboIndices = [];
    private uint[] beatmapColours = [];

    private void BuildComboColours()
    {
        comboIndices.Clear();
        var lines = Document.Fruits.Select(f => (f.Id, f.OriginalLine))
            .Concat(Document.Tracks.Select(t => (t.Id, t.OriginalLine)))
            .Concat(Document.ImportedSliders.Select(t => (t.Id, t.OriginalLine)))
            .Concat(Document.BananaShowers.Select(t => (t.Id, t.OriginalLine))).ToDictionary(p => p.Id, p => p.OriginalLine);
        var spinners = Document.BananaShowers.Select(s => s.Id).ToHashSet();
        int index = 0, offsets = 0;
        bool first = true, afterSpinner = false;
        foreach (var parent in ClipboardParents(Document).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder))
        {
            var parts = lines[parent.Id]?.Split(',');
            int flags = parts is { Length: > 3 } && int.TryParse(parts[3], out int value) ? value : 0;
            bool spinner = spinners.Contains(parent.Id);
            if (!spinner && (first || afterSpinner || (flags & 4) != 0))
            { index++; offsets += 1 + ((flags >> 4) & 7); }
            comboIndices[parent.Id] = (index, offsets);
            first = false; afterSpinner = spinner;
        }
        var colours = new SortedDictionary<int, uint>();
        foreach (string line in Document.OriginalSections.Where(s => s.Name.Equals("Colours", StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Lines))
        {
            var parts = line.Split(':', 2);
            string name = parts[0].Trim();
            if (parts.Length != 2 || !name.StartsWith("Combo", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(name[5..], out int number) || number < 1) continue;
            var rgb = parts[1].Split("//", 2)[0].Split(',');
            if (rgb.Length == 3 && byte.TryParse(rgb[0].Trim(), out byte r) && byte.TryParse(rgb[1].Trim(), out byte g)
                && byte.TryParse(rgb[2].Trim(), out byte b)) colours[number] = (uint)(r << 16 | g << 8 | b);
        }
        beatmapColours = colours.Values.ToArray();
    }

    private uint ObjectColour(ConvertedCatchObject item)
    {
        if (item.Kind == CatchObjectKind.Banana) return CatchObjectVisual.BananaColour(item.TimeMs);
        return ComboColour(item.SourceId);
    }

    private uint ComboColour(Guid sourceId, bool useFallbackPalette = false)
    {
        var indices = comboIndices.GetValueOrDefault(sourceId);
        if (beatmapColours.Length > 0) return beatmapColours[indices.Beatmap % beatmapColours.Length];
        var colours = skin?.ComboColours;
        if (colours is { Count: > 0 }) return colours[indices.Skin % colours.Count];
        return useFallbackPalette ? CatchSkin.DefaultComboColours[indices.Skin % CatchSkin.DefaultComboColours.Count] : 0xFFFFFF;
    }
}
