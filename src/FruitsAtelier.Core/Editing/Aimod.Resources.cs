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
        string? background = OsuTimeline.BackgroundFilename(document);
        AddReference(SongSetup.Get(document, "General", "AudioFilename"));
        foreach (var line in document.OriginalSections.Where(s => s.Name == "Events").SelectMany(s => s.Lines)) ReadEvent(line);
        foreach (var line in document.Fruits.Select(f => f.OriginalLine).Concat(document.ImportedSliders.Select(s => s.OriginalLine)))
        {
            if (line?.Split(',') is not { Length: >= 6 } parts) continue;
            string sample = parts[^1];
            if (sample.Split(':', 5) is { Length: 5 } fields) AddReference(fields[4]);
        }
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
        return issues;

        void AddReference(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            OsuBeatmapReader.ValidateOsuResourceReference(name);
            references.Add(name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        }
        void ReadEvent(string line)
        {
            var fields = WorkspaceProject.Csv(line.Trim());
            if (fields.Length < 3 || line.TrimStart().StartsWith("//", StringComparison.Ordinal)) return;
            switch (fields[0])
            {
                case "0": case "Background": case "1": case "Video": AddReference(fields[2]); break;
                case "Sprite": case "Sample": case "5":
                    if (fields.Length > 3) AddReference(fields[3]); break;
                case "Animation":
                    if (fields.Length > 6 && int.TryParse(fields[6], out int count))
                        for (int i = 0; i < Math.Clamp(count, 0, 10000); i++)
                            AddReference(Path.ChangeExtension(fields[3], null) + i + Path.GetExtension(fields[3]));
                    break;
            }
        }
    }

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
