using System.Buffers.Binary;

namespace FruitsAtelier.Core;

public static partial class Aimod
{
    // Run resource I/O on a worker owned by the report snapshot, never during drawing.
    public static IReadOnlyList<AimodIssue> AnalyzeResources(MapDocument document)
    {
        var issues = new List<AimodIssue>();
        string? root = Path.GetDirectoryName(document.SourcePath ?? document.AudioPath);
        if (root is null) return issues;
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? background = OsuTimeline.BackgroundFilename(document) is string bg ? ResourceName(bg) : null;
        string audio = ResourceName(SongSetup.Get(document, "General", "AudioFilename"));
        AddReference(audio);
        var events = document.OriginalSections.Where(s => s.Name == "Events").SelectMany(s => s.Lines).ToList();
        var variables = document.OriginalSections.Where(s => s.Name == "Variables").SelectMany(s => s.Lines).ToList();
        if (Directory.Exists(root))
            foreach (string storyboard in Directory.EnumerateFiles(root, "*.osb").Order(StringComparer.OrdinalIgnoreCase))
            {
                string section = "";
                foreach (string line in File.ReadLines(storyboard))
                {
                    if (line.StartsWith('[')) { section = line.Trim(); continue; }
                    if (section == "[Events]") events.Add(line);
                    else if (section == "[Variables]") variables.Add(line);
                }
            }
        var substitutions = variables.Select(v => v.Split('=', 2)).Where(v => v.Length == 2 && v[0].StartsWith('$'))
            .GroupBy(v => v[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
        string Expand(string line)
        {
            foreach (var pair in substitutions.OrderByDescending(p => p.Key.Length)) line = line.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
            return line;
        }
        events = events.Select(Expand).ToList();
        foreach (var line in events) ReadEvent(line);
        if (SongSetup.Get(document, "General", "EpilepsyWarning", "0") != "1" && ShortStoryboardEvents(events) > 5)
            issues.Add(new(AimodCategory.Design, AimodSeverity.Warning, "epilepsy"));
        foreach (var line in document.Fruits.Select(f => f.OriginalLine).Concat(document.ImportedSliders.Select(s => s.OriginalLine)))
        {
            if (line?.Split(',') is not { Length: >= 6 } parts) continue;
            string sample = parts[^1];
            if (sample.Split(':', 5) is { Length: 5 } fields) AddReference(fields[4]);
        }
        foreach (string video in videos.Where(v => File.Exists(Path.Combine(root, v))))
            ReadMedia(video, properties =>
            {
                if (VideoDimensionIssue(properties.VideoWidth, properties.VideoHeight) is string code)
                    issues.Add(new(AimodCategory.Design, AimodSeverity.Warning, code));
            });
        if (!string.IsNullOrEmpty(audio) && File.Exists(Path.Combine(root, audio)))
            ReadMedia(audio, properties =>
            {
                var exported = OsuBeatmapWriter.Serialize(document).ReadBack;
                var timing = new TimingMap.Lookup(exported);
                var objects = exported.Fruits.Select(f => (Start: f.TimeMs, End: f.TimeMs))
                    .Concat(exported.BananaShowers.Select(b => (b.TimeMs, b.EndTimeMs)))
                    .Concat(exported.ImportedSliders.Select(s => (s.TimeMs, s.TimeMs + s.PixelLength * s.SpanCount * timing.At(s.TimeMs).BeatLengthMs
                        / (100 * exported.EffectiveSliderMultiplier * timing.At(s.TimeMs).SliderVelocityMultiplier))))
                    .OrderBy(o => o.Item1).ToArray();
                if (objects.Length > 0 && properties.Duration.TotalMilliseconds > objects[0].Item1
                    && (objects[^1].Item2 - objects[0].Item1) / (properties.Duration.TotalMilliseconds - objects[0].Item1) < .8)
                    issues.Add(new(AimodCategory.Timing, AimodSeverity.Warning, "longAudio"));
            });
        foreach (string reference in references)
        {
            string path = Path.Combine(root, reference);
            if (!File.Exists(path))
                issues.Add(new(AimodCategory.Design, AimodSeverity.Error, "missingFile", Arguments: [reference]));
        }
        if (background is not null && File.Exists(Path.Combine(root, background)))
        {
            try
            {
                var size = ReadImageSize(Path.Combine(root, background));
                if (size.Width > 2560 || size.Height > 1440)
                    issues.Add(new(AimodCategory.Design, AimodSeverity.Warning, "largeBackground"));
            }
            catch (IOException) { issues.Add(new(AimodCategory.Design, AimodSeverity.Warning, "unreadableImage", Arguments: [background])); }
            catch (UnauthorizedAccessException) { issues.Add(new(AimodCategory.Design, AimodSeverity.Warning, "unreadableImage", Arguments: [background])); }
        }
        if (Directory.Exists(root))
            foreach (string path in Directory.EnumerateFiles(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                int expected = name.EndsWith("@2x", StringComparison.Ordinal) ? 256 : 128;
                if (expected == 256) name = name[..^3];
                if (name is not ("hitcircle" or "hitcircleoverlay" or "approachcircle" or "reversearrow")) continue;
                try
                {
                    var size = ReadImageSize(path);
                    if (size != default && (size.Width != expected || size.Height != expected))
                        issues.Add(new(AimodCategory.Design, AimodSeverity.Error, "skinDimensions", Arguments: [Path.GetFileName(path), expected]));
                }
                catch (IOException) { issues.Add(new(AimodCategory.Design, AimodSeverity.Warning, "unreadableImage", Arguments: [Path.GetFileName(path)])); }
            }
        return issues;

        void ReadMedia(string name, Action<TagLib.Properties> inspect)
        {
            try { using var media = TagLib.File.Create(Path.Combine(root, name)); inspect(media.Properties); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
            { issues.Add(new(AimodCategory.Design, AimodSeverity.Warning, "unreadableMedia", Arguments: [name])); }
        }

        void AddReference(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            OsuBeatmapReader.ValidateOsuResourceReference(name);
            references.Add(ResourceName(name));
        }
        void ReadEvent(string line)
        {
            var fields = WorkspaceProject.Csv(line.Trim());
            if (fields.Length < 3 || line.TrimStart().StartsWith("//", StringComparison.Ordinal)) return;
            switch (fields[0])
            {
                case "0": case "Background": AddReference(fields[2]); break;
                case "1": case "Video": AddReference(fields[2]); videos.Add(ResourceName(fields[2])); break;
                case "Sprite": case "4": case "Sample": case "5":
                    if (fields.Length > 3) AddReference(fields[3]); break;
                case "Animation": case "6":
                    if (fields.Length > 6 && int.TryParse(fields[6], out int count))
                        for (int i = 0; i < Math.Clamp(count, 0, 10000); i++)
                            AddReference(Path.ChangeExtension(fields[3], null) + i + Path.GetExtension(fields[3]));
                    break;
            }
        }
    }

    private static string ResourceName(string name) => name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    internal static string? VideoDimensionIssue(int width, int height) => width > 1280 && height > 720
        ? "largeVideoWide" : width > 1024 && height > 768 ? "largeVideoStandard" : null;

    private static (int Width, int Height) ReadImageSize(string path)
    {
        using var input = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        if (input.Read(header) < 24) return default;
        if (header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return (BinaryPrimitives.ReadInt32BigEndian(header[16..20]), BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
        if (header[0] == 'G' && header[1] == 'I' && header[2] == 'F')
            return (BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]));
        if (header[0] != 255 || header[1] != 216) return default;
        input.Position = 2;
        while (input.Position < Math.Min(input.Length, 1024 * 1024))
        {
            if (input.ReadByte() != 255) return default;
            int marker;
            do { marker = input.ReadByte(); } while (marker == 255);
            if (marker is < 0 or 217 or 218) return default;
            if (marker is 1 or >= 208 and <= 215) continue;
            int size = input.ReadByte() * 256 + input.ReadByte();
            if (size < 2) return default;
            if (marker is >= 192 and <= 195 or >= 197 and <= 199 or >= 201 and <= 203 or >= 205 and <= 207)
            {
                input.ReadByte();
                int height = input.ReadByte() * 256 + input.ReadByte();
                int width = input.ReadByte() * 256 + input.ReadByte();
                return (width, height);
            }
            input.Seek(size - 2, SeekOrigin.Current);
        }
        return default;
    }
}
