using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SongSetupTests
{
    public static void Run()
    {
        string previous = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(language);
                var map = new MapDocument { Name = "Song", IsDemo = false };
                foreach (var pair in new[] { ("Title", "Song"), ("TitleUnicode", "Song"), ("Artist", "Artist"), ("ArtistUnicode", "Artist"), ("Version", "Easy") })
                    SongSetup.Set(map, "Metadata", pair.Item1, pair.Item2);
                SongSetup.Set(map, "General", "Countdown", "0");
                SongSetup.Set(map, "General", "AudioLeadIn", "1200");
                SongSetup.Set(map, "Colours", "SliderBorder", "1,2,3");
                var second = map.DeepClone(); SongSetup.Set(second, "Metadata", "Version", "Hard");
                SongSetup.Set(second, "Metadata", "Source", "Original source");
                var ui = new Ui(false); ui.Resize(980, 620);
                ui.View.LoadProject(BeatmapProject.FromDocuments([map, second])); ui.Paint();
                ui.ClickText(L.Get("song.title"));
                ui.Key('F'); ui.Key(116); ui.Key('S', ctrl: true);
                Check(ui.View.SongSetupVisible && !ui.View.IsTestplaying && ui.View.Document.ContentEquals(map), "Modal isolates editor input");
                ui.ClickText(L.Get("song.ok"));
                Check(!ui.View.IsDirty, "Confirming unchanged settings is a no-op");
                ui.View.SwitchDifficulty(1); ui.Paint();
                ui.Key('F'); ui.ClickMap(100, 250); ui.Key('1');
                Check(ui.View.Document.Fruits.Count == 1, "Create an independent difficulty undo step");
                ui.View.SwitchDifficulty(0); ui.Paint();
                ui.ClickText(L.Get("song.title"));
                Set(ui, "ArtistUnicode", "艺术家"); Set(ui, "Artist", "Artist Romanised");
                Set(ui, "TitleUnicode", "歌曲"); Set(ui, "Title", "Song Romanised");
                Set(ui, "Creator", "Mapper"); Set(ui, "Source", "Source"); Set(ui, "Tags", "tag one"); Set(ui, "Version", "Normal");
                ui.ClickText(L.Get("song.difficulty"));
                Set(ui, "HPDrainRate", "6.2"); Set(ui, "CircleSize", "4.5"); Set(ui, "ApproachRate", "9.3"); Set(ui, "OverallDifficulty", "7.1");
                ui.ClickText(L.Get("song.colours")); ui.ClickText(L.Get("song.customColours"));
                Set(ui, "Hex", "#123ABC"); ui.ClickText(L.Get("song.addColour")); Set(ui, "Hex", "#FA1234");
                ui.ClickText(L.Get("song.design")); ui.ClickText(L.Get("song.countdownOff"));
                Set(ui, "CountdownOffset", "3"); ui.ClickText("□ " + L.Get("song.WidescreenStoryboard"));
                ui.ClickText("□ " + L.Get("song.LetterboxInBreaks")); ui.ClickText("□ " + L.Get("song.EpilepsyWarning"));
                ui.ClickText(L.Get("song.ok"));
                Check(!ui.View.SongSetupVisible && ui.View.IsDirty && ui.View.CurrentDifficultyName == "Normal", "Confirm changes and difficulty name");
                var edited = ui.View.Document.DeepClone();
                Check(edited.ApproachRate == 9.3 && edited.CircleSize == 4.5, "Difficulty updates model");
                Check(SongSetup.Colours(edited).SequenceEqual(new uint[] { 0x123ABC, 0x00CA00, 0x127CFF, 0xF21839, 0xFA1234 }), "HEX and added colours commit");
                var exported = OsuBeatmapWriter.Serialize(edited).ReadBack;
                foreach (string key in new[] { "HPDrainRate", "OverallDifficulty" }) Check(SongSetup.Get(exported, "Difficulty", key) == SongSetup.Get(edited, "Difficulty", key), "Difficulty exports");
                foreach (string key in new[] { "Countdown", "CountdownOffset", "WidescreenStoryboard", "LetterboxInBreaks", "EpilepsyWarning", "AudioLeadIn" })
                    Check(SongSetup.Get(exported, "General", key) == SongSetup.Get(edited, "General", key), "Design exports and preserves unrelated options");
                Check(SongSetup.Colours(exported).SequenceEqual(SongSetup.Colours(edited)), "Colours export");
                Check(ProjectSerializer.Read(ProjectSerializer.Serialize(edited)).ContentEquals(edited), "Project persistence retains setup");
                ui.View.SwitchDifficulty(1); ui.Paint();
                foreach (string key in SongSetup.SharedKeys) Check(SongSetup.Get(ui.View.Document, "Metadata", key) == SongSetup.Get(edited, "Metadata", key), "Shared metadata reaches siblings");
                Check(ui.View.CurrentDifficultyName == "Hard" && ui.View.Document.ApproachRate == 8 && SongSetup.Colours(ui.View.Document).Length == 0, "Local settings stay on current difficulty");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.Fruits.Count == 0 && SongSetup.Get(ui.View.Document, "Metadata", "Title") == "Song Romanised", "Sibling undo preserves shared metadata");
                ui.View.SwitchDifficulty(0); ui.Paint(); ui.Key('Z', ctrl: true);
                Check(ui.View.Document.ContentEquals(map), "Setup is one undo transaction");
                Check(SongSetup.Get(ui.View.CaptureProject().Difficulties[1].Document, "Metadata", "Title") == "Song", "Undo restores shared metadata");
                Check(SongSetup.Get(ui.View.CaptureProject().Difficulties[1].Document, "Metadata", "Source") == "Original source", "Undo restores each sibling's original value");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(edited), "Redo restores setup");
                ui.ClickText(L.Get("song.title")); ui.ClickText(L.Get("song.difficulty")); Set(ui, "CircleSize", "NaN"); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.SongSetupVisible && ui.View.Document.ContentEquals(edited), "Invalid draft cannot partly apply");
                ui.Key(27); Check(!ui.View.SongSetupVisible && ui.View.Document.ContentEquals(edited), "Cancel discards all drafts");
                ui.ClickText(L.Get("song.title")); Set(ui, "TitleUnicode", "English title"); ui.ClickText(L.Get("song.ok"));
                Check(SongSetup.Get(ui.View.Document, "Metadata", "Title") == "English title", "English title keeps romanised value aligned");
                ui.View.SwitchDifficulty(1); ui.Paint();
                ui.ClickText(L.Get("song.title")); Set(ui, "TitleUnicode", "Latest title"); ui.ClickText(L.Get("song.ok"));
                ui.View.SwitchDifficulty(0); ui.Paint(); ui.Key('Z', ctrl: true);
                Check(ui.View.CaptureProject().Difficulties.All(d => SongSetup.Get(d.Document, "Metadata", "Title") == "Latest title"),
                    "Undoing an older setup cannot split shared metadata after another difficulty edits it");
                ui.View.SwitchDifficulty(1); ui.Paint(); ui.Key('Z', ctrl: true);
                Check(ui.View.CaptureProject().Difficulties.All(d => SongSetup.Get(d.Document, "Metadata", "Title") == "English title"), "Latest shared edit undoes coherently");
                ui.ClickText(L.Get("song.title")); ui.ClickText(L.Get("song.colours")); ui.ClickText(L.Get("song.customColours"));
                Set(ui, "Hex", "bad hex"); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.SongSetupVisible && SongSetup.Colours(ui.View.Document).Length == 0, "Invalid HEX cannot apply");
                ui.Key(27);
            }
        }
        finally { L.SetLanguage(previous); }
    }

    private static void Set(Ui ui, string key, string value)
    {
        var box = ui.View.SongSetupFieldBounds[key]; ui.Click(box.X + 5, box.Y + 5); ui.Key('A', ctrl: true);
        ui.View.PasteSongSetupText(value, ui.View.SongSetupInputSession); ui.Paint();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
