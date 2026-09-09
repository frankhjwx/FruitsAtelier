namespace FruitsAtelier.Core;

public sealed record Hitsound(CatchObjectKind Kind, string? FilePath, float Volume, string Name = "hitnormal", int SampleSet = 1);

/// <summary>Resolves preserved osu! object/edge samples against timing and map resources.</summary>
public sealed class HitsoundResolver
{
    private readonly Dictionary<Guid, string?> lines;
    private readonly Dictionary<(Guid, int), int> edges = new();
    private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimingPoint[] timing;
    private readonly int defaultSet;
    public HitsoundResolver(MapDocument document, IReadOnlyList<ConvertedCatchObject> objects)
    {
        lines = document.Fruits.Select(f => (f.Id, f.OriginalLine))
            .Concat(document.Tracks.Select(f => (f.Id, f.OriginalLine)))
            .Concat(document.ImportedSliders.Select(f => (f.Id, f.OriginalLine)))
            .ToDictionary(f => f.Id, f => f.OriginalLine);
        foreach (var group in objects.Where(o => o.Kind == CatchObjectKind.Fruit && !o.IsStandalone).GroupBy(o => o.SourceId))
        {
            int index = 0;
            foreach (var item in group) edges[(item.SourceId, item.EventIndex)] = index++;
        }
        timing = document.TimingPoints.OrderBy(t => t.TimeMs).ThenBy(t => t.SourceOrder).ToArray();
        string? set = document.OriginalSections.Where(s => s.Name == "General").SelectMany(s => s.Lines)
            .Select(l => l.Split(':', 2)).Where(p => p.Length == 2 && p[0].Trim() == "SampleSet").Select(p => p[1].Trim()).LastOrDefault();
        defaultSet = set?.ToLowerInvariant() switch { "soft" => 2, "drum" => 3, _ => 1 };
        string? root = Path.GetDirectoryName(document.SourcePath ?? document.AudioPath);
        if (root is not null && Directory.Exists(root))
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
                foreach (string path in Directory.EnumerateFiles(root, "*", options).Take(20000))
                    if (Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3")
                        files.TryAdd(Path.GetRelativePath(root, path).Replace('\\', '/'), path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public IReadOnlyList<Hitsound> Resolve(ConvertedCatchObject item)
    {
        if (item.Kind == CatchObjectKind.TinyDroplet) return Array.Empty<Hitsound>();
        if (item.Kind == CatchObjectKind.Banana) return new[] { new Hitsound(item.Kind, HitsoundDefaults.Find(1, "catch-banana"), 1, "catch-banana") };
        int low = 0, high = timing.Length;
        while (low < high) { int mid = (low + high) / 2; if (timing[mid].TimeMs <= item.TimeMs) low = mid + 1; else high = mid; }
        var point = low > 0 ? timing[low - 1] : null;
        int bank = point?.SampleSet is >= 1 and <= 3 ? point.SampleSet : defaultSet;
        int index = point?.SampleIndex ?? 0;
        int volume = point?.Volume ?? 100;
        int additions = 0, additionBank = 0;
        string? custom = null;
        if (lines.TryGetValue(item.SourceId, out string? line) && line is not null)
        {
            string[] p = line.Split(',');
            bool slider = (Number(p, 3) & 2) != 0;
            string[] sample = At(p, slider ? 10 : 5).Split(':', 5);
            int normal = Number(sample, 0); if (normal is >= 1 and <= 3) bank = normal;
            additionBank = Number(sample, 1);
            // Legacy sliders carry banks here; their index and volume follow timing.
            if (!slider)
            {
                if (Number(sample, 2) > 0) index = Number(sample, 2);
                if (Number(sample, 3) > 0) volume = Number(sample, 3);
                custom = At(sample, 4);
            }
            additions = Number(p, 4);
            if (slider && edges.TryGetValue((item.SourceId, item.EventIndex), out int edge))
            {
                string[] sounds = At(p, 8).Split('|');
                if (edge < sounds.Length && sounds[edge].Length > 0) additions = Number(sounds, edge);
                string[] sets = At(p, 9).Split('|');
                if (edge < sets.Length && sets[edge].Length > 0)
                {
                    string[] pair = sets[edge].Split(':');
                    bank = Number(pair, 0) is >= 1 and <= 3 ? Number(pair, 0) : point?.SampleSet is >= 1 and <= 3 ? point.SampleSet : defaultSet;
                    additionBank = Number(pair, 1);
                }
            }
        }
        float gain = Math.Clamp(volume, 0, 100) / 100f;
        if (gain == 0) return Array.Empty<Hitsound>();
        int extra = additionBank is >= 1 and <= 3 ? additionBank : bank;
        if (item.Kind == CatchObjectKind.Droplet)
        {
            var ticks = new List<Hitsound> { Make("slidertick", bank) };
            if ((additions & 14) != 0 && extra != bank) ticks.Add(Make("slidertick", extra));
            return ticks;
        }
        // Lazer's legacy parser replaces the normal layer with an explicit file.
        var result = new List<Hitsound> {
            !string.IsNullOrWhiteSpace(custom) && files.TryGetValue(custom.Replace('\\', '/'), out string? path)
                ? new(item.Kind, path, gain, "custom", bank) : Make("hitnormal", bank)
        };
        if ((additions & 2) != 0) result.Add(Make("hitwhistle", extra));
        if ((additions & 4) != 0) result.Add(Make("hitfinish", extra));
        if ((additions & 8) != 0) result.Add(Make("hitclap", extra));
        return result;

        Hitsound Make(string name, int sampleSet)
        {
            string prefix = sampleSet switch { 2 => "soft", 3 => "drum", _ => "normal" };
            string basename = prefix + "-" + name + (index > 1 ? index.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            string? path = null;
            if (index > 0)
                foreach (string extension in new[] { ".wav", ".ogg", ".mp3" })
                    if (files.TryGetValue(basename + extension, out path)) break;
            return new(item.Kind, path ?? HitsoundDefaults.Find(sampleSet, name), gain, name, sampleSet);
        }
    }
    private static string At(string[] values, int i) => i < values.Length ? values[i] : "";
    private static int Number(string[] values, int i) => int.TryParse(At(values, i), out int result) ? result : 0;
}
