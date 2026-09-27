namespace FruitsAtelier.Core;

internal sealed class OsuSliderParseCache
{
    private readonly Dictionary<(string Line, bool Lazer), ImportedSlider> entries = new();
    private readonly HashSet<(string Line, bool Lazer)> seen = new();

    public void Begin() => seen.Clear();
    public void End()
    {
        foreach (var key in entries.Keys.Where(key => !seen.Contains(key)).ToArray()) entries.Remove(key);
    }

    public void Parse(MapDocument document, string line, int order, bool lazer = false)
    {
        var key = (line, lazer);
        if (entries.TryGetValue(key, out var cached))
        {
            seen.Add(key);
            var slider = cached.DeepClone();
            slider.Id = Guid.NewGuid(); slider.SourceOrder = order;
            document.ImportedSliders.Add(slider);
            return;
        }
        int count = document.ImportedSliders.Count;
        OsuBeatmapReader.ParseObject(document, line, order, lazer);
        if (document.ImportedSliders.Count > count)
        {
            seen.Add(key);
            entries[key] = document.ImportedSliders[^1].DeepClone();
        }
    }
}
