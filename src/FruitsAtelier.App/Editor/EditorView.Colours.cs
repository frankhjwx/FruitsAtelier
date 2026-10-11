using FruitsAtelier.Core;
using FruitsAtelier.App.Skinning;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly Dictionary<Guid, (int Skin, int Beatmap)> comboIndices = [];
    private readonly Dictionary<(Guid Id, int EventIndex), (int Skin, int Beatmap)> comboEventIndices = [];
    private readonly HashSet<(Guid Id, int EventIndex)> newComboEvents = [];
    private uint[] beatmapColours = [];

    private void BuildComboColours()
    {
        comboIndices.Clear();
        comboEventIndices.Clear();
        newComboEvents.Clear();
        comboEntries = BuildComboEntries().ToArray();
        int index = 0, offsets = 0;
        bool first = true, afterSpinner = false;
        foreach (var parent in ComboEntries())
        {
            int flags = parent.Flags;
            if ((flags & 4) != 0) newComboEvents.Add((parent.Id, parent.EventIndex));
            bool spinner = parent.IsBanana;
            if (!spinner && (first || afterSpinner || (flags & 4) != 0))
            { index++; offsets += 1 + ((flags >> 4) & 7); }
            comboIndices.TryAdd(parent.Id, (index, offsets));
            if (parent.EventIndex > 0) comboEventIndices[(parent.Id, parent.EventIndex)] = (index, offsets);
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
        var indices = comboEventIndices.GetValueOrDefault((item.SourceId, item.EventIndex), comboIndices.GetValueOrDefault(item.SourceId));
        if (beatmapColours.Length > 0) return beatmapColours[indices.Beatmap % beatmapColours.Length];
        var colours = skin?.ComboColours;
        // Catch skin colours advance per parent; beatmap colours retain NC offsets.
        int index = skinIndices.GetValueOrDefault(item.SourceId) + 1;
        return colours is { Count: > 0 } ? colours[index % colours.Count] : 0xFFFFFF;
    }

    private uint ComboColour(Guid sourceId, bool useFallbackPalette = false, int eventIndex = 0)
    {
        var indices = comboEventIndices.GetValueOrDefault((sourceId, eventIndex), comboIndices.GetValueOrDefault(sourceId));
        if (beatmapColours.Length > 0) return beatmapColours[indices.Beatmap % beatmapColours.Length];
        var colours = skin?.ComboColours;
        if (colours is { Count: > 0 }) return colours[indices.Skin % colours.Count];
        return useFallbackPalette ? CatchSkin.DefaultComboColours[indices.Skin % CatchSkin.DefaultComboColours.Count] : 0xFFFFFF;
    }
}
