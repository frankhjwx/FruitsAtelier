using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace FruitsAtelier.Core;

public sealed record StoryboardAudio(string SourcePath)
{
    [JsonIgnore] public byte[]? Data { get; private set; }
    [JsonIgnore] public string? HistoryPath { get; internal init; }
    public string? ContentHash { get; init; }
    public void Cache(string savedPath, long remainingBytes = 16 * 1024 * 1024)
    {
        lock (this)
            Data ??= Capture(File.Exists(savedPath) ? savedPath
                : HistoryPath is { } history && File.Exists(history) ? history : SourcePath, remainingBytes).Data;
    }
    public static StoryboardAudio Capture(string path, long remainingBytes = 16 * 1024 * 1024)
    {
        byte[] data;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > Math.Min(16 * 1024 * 1024, remainingBytes)) data = [];
            else
            {
                data = new byte[(int)stream.Length];
                stream.ReadExactly(data);
                if (stream.ReadByte() != -1) data = [];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { data = []; }
        return new(Path.GetFullPath(path)) { Data = data, ContentHash = Convert.ToHexString(SHA256.HashData(data)) };
    }
    public static bool ValidRelativePath(string path)
    {
        var parts = path.Split('/');
        bool managed = parts.Length == 2 && parts[0] == "SBHitsounds"
            || parts.Length == 3 && parts[0] == "fa-storyboard" && parts[1].Length == 64 && parts[1].All(char.IsAsciiHexDigit);
        return managed && parts[^1].Length > 0
            && parts[^1].IndexOfAny(['\\', '"', '\r', '\n', ':']) < 0
            && Path.GetExtension(parts[^1]).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3";
    }
    [JsonIgnore] public string RelativePath => "SBHitsounds/" + Path.GetFileName(SourcePath);

    public string ChooseRelativePath(string root, IEnumerable<KeyValuePair<string, StoryboardAudio>> registered)
    {
        var known = registered.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Value).ToArray(), StringComparer.OrdinalIgnoreCase);
        string relative = RelativePath;
        for (int suffix = 2; ; suffix++)
        {
            bool conflict = known.TryGetValue(relative, out var entries) && entries.Any(a =>
                !ReferenceEquals(a, this) && (Data is null || (a.Data is not null
                    ? !a.Data.AsSpan().SequenceEqual(Data)
                    : ContentHash is null || a.ContentHash != ContentHash)));
            string path = Path.Combine(root, relative);
            if (!conflict && File.Exists(path))
                conflict = Data is null || new FileInfo(path).Length != Data.Length || !File.ReadAllBytes(path).AsSpan().SequenceEqual(Data);
            if (!conflict && !Directory.Exists(path)) return relative;
            relative = "SBHitsounds/" + Path.GetFileNameWithoutExtension(SourcePath) + "-"
                + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + Path.GetExtension(SourcePath);
        }
    }
}
