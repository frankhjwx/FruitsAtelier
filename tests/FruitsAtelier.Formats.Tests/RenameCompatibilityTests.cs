using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class RenameCompatibilityTests
{
    public static void Run()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vibecatch-schema1.catchproj"));
        var document = ProjectSerializer.Read(text);
        Check(document.Name == "VibeCatchEditor compatibility fixture", "Opening renamed old project changed its title");
        Check(document.Tracks[0].Name == "VCE Slider preserved user name", "User-authored track name was rewritten");
        Check(document.Tracks[0].Nodes[0].HandleOut == new MapPoint(350, 10), "Old curve handles were lost");
        string serialized = ProjectSerializer.Serialize(document);
        Check(document.ContentEquals(ProjectSerializer.Read(serialized)), "Old project did not round-trip");
        var converted = OsuBeatmapWriter.Serialize(document);
        Check(converted.ObjectSequenceMatches, "Renaming changed the generated beatmap sequence");
        string previous = L.Language;
        try
        {
            foreach (string language in L.AvailableLanguages)
            {
                L.SetLanguage(language);
                Check(L.Get("app.name") == (language == "zh-CN" ? "水果工坊" : "FruitsAtelier"), "Application title is not renamed");
                Check(L.Get("ui.sliderTool") == "FSlider  B", "Slider tool is not renamed");
                Check(L.Get("core.names.importedSlider", 1, 0).StartsWith("FSlider "), "New converted sliders retain the old name");
                Check(document.Tracks[0].Name == "VCE Slider preserved user name", "Language change rewrote old object data");
            }
        }
        finally { L.SetLanguage(previous); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
