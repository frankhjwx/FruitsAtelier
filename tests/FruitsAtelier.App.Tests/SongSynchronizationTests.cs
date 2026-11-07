using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SongSynchronizationTests
{
    public static void Run()
    {
        string previous = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(language);
                var source = new MapDocument { Name = "Source song", IsDemo = false };
                foreach (string key in SongSetup.SyncMetadataKeys) SongSetup.Set(source, "Metadata", key, "Source " + key);
                SongSetup.Set(source, "Metadata", "Version", "Easy");
                foreach (string key in new[] { "Countdown", "CountdownOffset", "WidescreenStoryboard", "LetterboxInBreaks", "EpilepsyWarning" })
                    SongSetup.Set(source, "General", key, key == "CountdownOffset" ? "3" : "1");
                SongSetup.SetColours(source, [0x123456, 0xABCDEF]);
                var target = new MapDocument { Name = "Target song", IsDemo = false, CircleSize = 3, ApproachRate = 7 };
                SongSetup.Set(target, "Metadata", "Title", "Target song");
                SongSetup.Set(target, "Metadata", "TitleUnicode", "Target song");
                SongSetup.Set(target, "Metadata", "Version", "Hard");
                SongSetup.Set(target, "Metadata", "BeatmapID", "123");
                SongSetup.Set(target, "General", "AudioLeadIn", "1500");
                SongSetup.Set(target, "Colours", "SliderBorder", "7,8,9");
                var ui = new Ui(false); ui.Resize(1440, 900);
                ui.View.LoadProject(BeatmapProject.FromDocuments([source, target, target.DeepClone()])); ui.Paint();
                var before = ui.View.CaptureProject();
                ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("song.syncGeneral")); ui.Key(27);
                var synced = ui.View.CaptureProject();
                foreach (var difficulty in synced.Difficulties.Skip(1))
                {
                    foreach (string key in SongSetup.SyncMetadataKeys)
                        Check(SongSetup.Get(difficulty.Document, "Metadata", key) == SongSetup.Get(source, "Metadata", key), "All metadata synchronized");
                    foreach (string key in new[] { "Countdown", "CountdownOffset", "WidescreenStoryboard", "LetterboxInBreaks", "EpilepsyWarning" })
                        Check(SongSetup.Get(difficulty.Document, "General", key) == SongSetup.Get(source, "General", key), "Design synchronized");
                    Check(SongSetup.Colours(difficulty.Document).SequenceEqual(SongSetup.Colours(source)), "Palette synchronized");
                    Check(difficulty.Document.CircleSize == 3 && difficulty.Document.ApproachRate == 7
                        && SongSetup.Get(difficulty.Document, "Metadata", "Version") == "Hard"
                        && SongSetup.Get(difficulty.Document, "Metadata", "BeatmapID") == "123"
                        && SongSetup.Get(difficulty.Document, "General", "AudioLeadIn") == "1500"
                        && SongSetup.Get(difficulty.Document, "Colours", "SliderBorder") == "7,8,9", "Unlisted settings preserved");
                    Check(ProjectSerializer.Read(ProjectSerializer.Serialize(difficulty.Document)).ContentEquals(difficulty.Document), "Sync persists");
                    Check(SongSetup.Colours(OsuBeatmapWriter.Serialize(difficulty.Document).ReadBack).SequenceEqual(SongSetup.Colours(source)), "Sync exports");
                }
                Check(ui.View.IsDirty, "Sibling-only changes dirty project");
                ui.Key('Z', ctrl: true);
                Check(ui.View.CaptureProject().Difficulties.Zip(before.Difficulties).All(p => p.First.Document.ContentEquals(p.Second.Document)), "Undo restores every target exactly");
                ui.Key('Y', ctrl: true);
                Check(ui.View.CaptureProject().Difficulties.Zip(synced.Difficulties).All(p => p.First.Document.ContentEquals(p.Second.Document)), "Redo restores set");
                ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("song.syncGeneral")); ui.Key(27); ui.Key('Z', ctrl: true);
                Check(ui.View.CaptureProject().Difficulties.Zip(before.Difficulties).All(p => p.First.Document.ContentEquals(p.Second.Document)), "Repeated sync adds no undo step");
                ui.Key('Y', ctrl: true);
                ui.View.SwitchDifficulty(1); ui.Paint(); ui.View.OpenSongSetup(); ui.Paint();
                var creator = ui.View.SongSetupFieldBounds["Creator"];
                ui.Click(creator.X + 5, creator.Y + 5); ui.Key('A', ctrl: true);
                ui.View.PasteSongSetupText("Later local creator", ui.View.SongSetupInputSession); ui.ClickText(L.Get("song.ok"));
                ui.View.SwitchDifficulty(0); ui.Paint(); ui.Key('Z', ctrl: true);
                Check(SongSetup.Get(ui.View.CaptureProject().Difficulties[1].Document, "Metadata", "Creator") == "Later local creator",
                    "Undo of earlier sync preserves a later local field edit");
                ui.Key('Y', ctrl: true);
                Check(SongSetup.Get(ui.View.CaptureProject().Difficulties[1].Document, "Metadata", "Creator") == "Later local creator",
                    "Redo of earlier sync preserves a later local field edit");
                ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("song.colours")); ui.ClickText(L.Get("song.customColours"));
                ui.ClickText(L.Get("song.general")); ui.ClickText(L.Get("song.syncGeneral")); ui.Key(27);
                Check(ui.View.CaptureProject().Difficulties.All(d => SongSetup.Colours(d.Document).Length == 0), "Disabled custom colors remove every override");
                ui.Key('Z', ctrl: true);
                Check(ui.View.CaptureProject().Difficulties.Skip(1).All(d => SongSetup.Colours(d.Document).Length == 2), "Undo restores disabled target palettes");
            }
        }
        finally { L.SetLanguage(previous); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
