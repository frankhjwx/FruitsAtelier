using System.Text.Json;
using FruitsAtelier.Core;

namespace FruitsAtelier.Localization;

public static class LanguagePreference
{
    private sealed record Preference(string Language);
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FruitsAtelier", "language.json");

    public static string ReadLanguage(string? path = null)
    {
        try
        {
            var preference = JsonSerializer.Deserialize<Preference>(File.ReadAllText(path ?? DefaultPath));
            return preference is not null && Strings.AvailableLanguages.Contains(preference.Language) ? preference.Language : "en";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return "en"; }
    }

    public static void SaveLanguage(string language, string? path = null)
    {
        if (!Strings.AvailableLanguages.Contains(language)) throw new ArgumentOutOfRangeException(nameof(language));
        path = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, JsonSerializer.Serialize(new Preference(language)));
    }
}
