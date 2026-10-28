using System.IO.Compression;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

/// <summary>A bounded, versioned authoring payload encoded as visible Base64 text.</summary>
public static class PatternClipboard
{
    private const string Separator = "：";
    public static string Encode(PatternSnapshot pattern)
    {
        var raw = pattern.ClipboardGeometry;
        using var stream = new MemoryStream();
        stream.WriteByte(3);
        using (var compressor = new BrotliStream(stream, CompressionLevel.SmallestSize, leaveOpen: true)) compressor.Write(raw);
        byte[] bytes = stream.Length < raw.Length ? stream.ToArray() : raw;
        if (bytes.LongLength > PatternArchive.MaximumArchiveBytes) throw Invalid();
        return pattern.Name + Separator + Convert.ToBase64String(bytes);
    }

    public static bool IsPatternText(string text)
    {
        text = text.Trim();
        if (text.StartsWith('[') && text.EndsWith(']') && (text.Contains("]：[", StringComparison.Ordinal) || text.Contains("]:[", StringComparison.Ordinal))
            || text.StartsWith('【') && text.EndsWith('】') && text.Contains("】：【", StringComparison.Ordinal)) return true;
        int separator = text.LastIndexOfAny(['：', ':']);
        if (separator < 1 || text.Length - separator < 5) return false;
        Span<byte> header = stackalloc byte[3];
        return Convert.TryFromBase64Chars(text.AsSpan(separator + 1, 4), header, out int count)
            && count > 0 && header[0] is 2 or 3;
    }

    public static PatternSnapshot Decode(string text)
    {
        text = text.Trim();
        bool legacy = text.StartsWith('【') && text.EndsWith('】');
        bool boxed = text.StartsWith('[') && text.EndsWith(']');
        string delimiter = legacy ? "】：【" : boxed ? text.Contains("]：[", StringComparison.Ordinal) ? "]：[" : "]:[" : "";
        int separator = legacy || boxed ? text.LastIndexOf(delimiter, StringComparison.Ordinal) : text.LastIndexOfAny(['：', ':']);
        if (separator < (legacy || boxed ? 2 : 1) || text.Length > (PatternArchive.MaximumArchiveBytes + 2) / 3 * 4 + 1024) throw Invalid();
        string name = text[(legacy || boxed ? 1 : 0)..separator];
        int contentStart = separator + (legacy || boxed ? delimiter.Length : 1);
        int contentEnd = text.Length - (legacy || boxed ? 1 : 0);
        try
        {
            var payload = text.AsSpan(contentStart, contentEnd - contentStart);
            if (payload.IndexOfAny(" \r\n\t") >= 0) throw new FormatException();
            byte[] bytes = Convert.FromBase64String(payload.ToString());
            if (bytes.LongLength > PatternArchive.MaximumArchiveBytes) throw Invalid();
            if (bytes.Length > 0 && bytes[0] == 3)
            {
                using var compressed = new MemoryStream(bytes, 1, bytes.Length - 1, writable: false);
                using var decompressor = new BrotliStream(compressed, CompressionMode.Decompress);
                bytes = Expand(decompressor);
            }
            if (bytes.Length > 0 && bytes[0] == 2) return PatternGeometryCodec.Decode(bytes, name);
            using var stream = new MemoryStream(bytes, writable: false);
            PatternSnapshot pattern;
            if (stream.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual("FPC1"u8))
            {
                stream.Position = 4;
                using var decompressor = new BrotliStream(stream, CompressionMode.Decompress);
                using var expanded = new MemoryStream(Expand(decompressor), writable: false);
                var data = JsonNode.Parse(expanded)?.AsArray() ?? throw Invalid();
                if (data.Count != 6 || data[0]!.GetValue<int>() != 1 || data[2]!.GetValue<string>() != name) throw Invalid();
                var source = data[3]!.AsArray();
                if (source.Count != 7) throw Invalid();
                var project = data[5] ?? throw Invalid();
                UnpackFruits(project);
                RestoreIds(project);
                pattern = new(Guid.ParseExact(data[1]!.GetValue<string>(), "N"), name,
                    new(source[0]!.GetValue<string>(), source[1]!.GetValue<string>(), source[2]!.GetValue<string>(), source[3]!.GetValue<string>(),
                        source[4]!.GetValue<double>(), source[5]!.GetValue<long>(), source[6]!.GetValue<long>()),
                    ProjectSerializer.Read(project.ToJsonString()), data[4]!.AsArray().Select(t =>
                    {
                        var tag = t!.AsArray();
                        if (tag.Count != 2) throw Invalid();
                        return new PatternTag(tag[0]!.GetValue<string>(), tag[1]!.GetValue<uint>());
                    }));
            }
            else
            {
                var patterns = PatternArchive.Read(stream);
                if (patterns.Count != 1) throw Invalid();
                pattern = patterns[0];
            }
            if (pattern.Name != name) throw Invalid();
            return pattern;
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException or ArgumentException or OverflowException or NullReferenceException or EndOfStreamException)
        { throw new InvalidDataException(L.Get("core.pattern.invalid"), error); }
    }

    private static void UnpackFruits(JsonNode project)
    {
        if (project["Document"]?["Fruits"] is not JsonArray fruits) return;
        for (int i = 0; i < fruits.Count; i++)
        {
            var row = fruits[i]!.AsArray();
            if (row.Count != 6) throw Invalid();
            var fruit = new JsonObject { ["Id"] = row[0]?.DeepClone(), ["TimeMs"] = row[1]?.DeepClone(),
                ["X"] = row[2]?.DeepClone(), ["SourceOrder"] = row[3]?.DeepClone() };
            if (row[5] is { } y) fruit["Y"] = y.DeepClone();
            if (row[4] is JsonArray original)
            {
                if (original.Count != 1) throw Invalid();
                fruit["OriginalLine"] = original[0]!.GetValue<string>();
            }
            else if (row[4] is { } tail) fruit["OriginalLine"] = FruitPrefix(fruit) + tail.GetValue<string>();
            fruits[i] = fruit;
        }
    }

    private static string FruitPrefix(JsonObject fruit) =>
        (fruit["X"]?.GetValue<double>() ?? 0).ToString("R", CultureInfo.InvariantCulture) + ","
        + (fruit["Y"]?.GetValue<double>() ?? 192).ToString("R", CultureInfo.InvariantCulture) + ","
        + (fruit["TimeMs"]?.GetValue<double>() ?? 0).ToString("R", CultureInfo.InvariantCulture) + ",";

    private static byte[] Expand(Stream decompressor)
    {
        using var expanded = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = decompressor.Read(buffer)) > 0)
        {
            if (expanded.Length + count > ProjectSerializer.MaximumFileBytes) throw Invalid();
            expanded.Write(buffer, 0, count);
        }
        return expanded.ToArray();
    }

    private static void RestoreIds(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
            {
                if (property.Key is "Id" or "SourceId" && property.Value is JsonValue value && value.TryGetValue<int>(out int index))
                {
                    if (index < 0 || index > 10000000) throw Invalid();
                    obj[property.Key] = new Guid(index, 0, 0, new byte[8]).ToString();
                }
                else if (property.Value is { } child) RestoreIds(child);
            }
        else if (node is JsonArray array)
            foreach (var child in array) if (child is not null) RestoreIds(child);
    }

    private static InvalidDataException Invalid() => new(L.Get("core.pattern.invalid"));
}
