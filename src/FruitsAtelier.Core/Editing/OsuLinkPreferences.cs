using System.Text.Json;

namespace FruitsAtelier.Core;

public sealed class OsuLinkPreferences
{
    public string TargetId { get; set; } = "";
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FruitsAtelier", "osu-links.json");

    public static OsuLinkPreferences Load(string? path = null)
    {
        try
        {
            var result = JsonSerializer.Deserialize<OsuLinkPreferences>(File.ReadAllText(path ?? DefaultPath)) ?? new();
            result.TargetId ??= "";
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path, JsonSerializer.Serialize(this));
    }
}
