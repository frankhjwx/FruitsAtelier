using System.Text.Json;

namespace FruitsAtelier.Core;

public sealed class AcknowledgementsPreference
{
    public const int CurrentVersion = 2;
    public int AcceptedVersion { get; set; }
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FruitsAtelier", "acknowledgements.json");

    public static bool IsAccepted(string? path = null)
    {
        try
        {
            return File.Exists(path ?? SettingsPath)
                && JsonSerializer.Deserialize<AcknowledgementsPreference>(File.ReadAllText(path ?? SettingsPath))?.AcceptedVersion == CurrentVersion;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static void Accept(string? path = null)
    {
        path = Path.GetFullPath(path ?? SettingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, JsonSerializer.Serialize(new AcknowledgementsPreference { AcceptedVersion = CurrentVersion }));
    }
}
