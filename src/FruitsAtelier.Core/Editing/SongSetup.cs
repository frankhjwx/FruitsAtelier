using System.Globalization;

namespace FruitsAtelier.Core;

public static class SongSetup
{
    public static readonly string[] SharedKeys = ["Artist", "ArtistUnicode", "Title", "TitleUnicode", "Creator", "Source", "Tags"];

    public static string Get(MapDocument document, string section, string key, string fallback = "")
        => OsuBeatmapReader.Setting(document, section, key) ?? fallback;

    public static void Set(MapDocument document, string section, string key, string? value)
    {
        if (value is not null && value.Any(char.IsControl)) throw new ArgumentException(FruitsAtelier.Localization.Strings.Get("song.singleLine"));
        var sections = document.OriginalSections.Where(s => s.Name == section).ToArray();
        if (Get(document, section, key) == value && value is not null) return;
        foreach (var item in sections.Reverse())
        for (int i = item.Lines.Count - 1; i >= 0; i--)
            if (item.Lines[i].Split(':', 2)[0].Trim() == key)
            {
                if (value is not null) { item.Lines[i] = key + ":" + value; return; }
                item.Lines.RemoveAt(i);
            }
        if (value is null)
        { document.OriginalSections.RemoveAll(s => s.Name == section && s.Lines.Count == 0); return; }
        var target = sections.LastOrDefault();
        if (target is null) { target = new() { Name = section }; document.OriginalSections.Add(target); }
        target.Lines.Add(key + ":" + value);
    }

    public static uint[] Colours(MapDocument document)
    {
        var colours = new SortedDictionary<int, uint>();
        foreach (var line in document.OriginalSections.Where(s => s.Name.Equals("Colours", StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Lines))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2 || !ComboKey(parts[0], out int index)) continue;
            var rgb = parts[1].Split("//", 2)[0].Split(',');
            if (rgb.Length == 3 && byte.TryParse(rgb[0].Trim(), out byte r) && byte.TryParse(rgb[1].Trim(), out byte g)
                && byte.TryParse(rgb[2].Trim(), out byte b)) colours[index] = (uint)(r << 16 | g << 8 | b);
        }
        return colours.Values.ToArray();
    }

    private static bool ComboKey(string key, out int index)
    {
        key = key.Trim(); index = 0;
        return key.StartsWith("Combo", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[5..], out index) && index > 0;
    }

    public static void SetColours(MapDocument document, IReadOnlyList<uint> colours)
    {
        if (Colours(document).SequenceEqual(colours)) return;
        foreach (var section in document.OriginalSections.Where(s => s.Name.Equals("Colours", StringComparison.OrdinalIgnoreCase)))
            section.Lines.RemoveAll(line => ComboKey(line.Split(':', 2)[0], out _));
        for (int i = 0; i < colours.Count; i++)
        {
            uint colour = colours[i];
            Set(document, "Colours", "Combo" + (i + 1), string.Create(CultureInfo.InvariantCulture,
                $"{colour >> 16 & 255},{colour >> 8 & 255},{colour & 255}"));
        }
    }
}
