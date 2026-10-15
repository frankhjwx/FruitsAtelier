using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DropletDerandomizationTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (760, 580) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { IsDemo = false, DurationMs = 8000, RandomizeDropletStrength = 37, RandomizeDropletSeed = -123 };
                var track = new CurveTrack { Kind = CurveKind.Linear, DropletRandomization = new() { Enabled = true } };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 3000, X = 256 }]); map.Tracks.Add(track);
                var ui = new Ui(); ui.LoadDocument(map); ui.Resize(size.Item1, size.Item2);
                void Open() { ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("randomize.title")); }
                Open();
                Check(!ui.Canvas.Texts.Any(t => t.Value.StartsWith("Strength is")), "Randomize page omits the explanatory footer");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("randomize.enableAll")), "settings omit object batch commands");
                ui.ClickText(L.Get("randomize.derandomizeOff"));
                var seed = ui.View.SongSetupFieldBounds["RandomizeDropletSeed"];
                ui.Click(seed.X + 10, seed.Y + 12); ui.Key('A', ctrl: true); ui.View.PasteSongSetupText("999", ui.View.SongSetupInputSession); ui.Paint();
                ui.ClickText(L.Get("randomize.resetStrength"));
                Check(ui.View.Document.ContentEquals(map), "parameter drafts do not mutate the document");
                Check(ui.Canvas.Texts.Any(t => t.Value == "20") && ui.Canvas.Texts.Any(t => t.Value == "999"), "parameters remain editable under a derandomized default");
                ui.ClickText(L.Get("randomize.derandomizeHrOff")); ui.Key(27);
                Check(ui.View.Document.ContentEquals(map), "Cancel discards map and HR derandomization drafts");
                Open(); ui.ClickText(L.Get("randomize.derandomizeOff")); ui.ClickText(L.Get("randomize.derandomizeHrOff")); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.Document.DerandomizeFSliderDroplets && ui.View.Document.DerandomizeDropletsForHardRock
                    && ui.View.Document.Tracks[0].DropletRandomization is { Enabled: true }, "Apply saves the map default and compensation target while retaining FX state");
                Check(ui.View.Document.RandomizeDropletStrength == 37 && ui.View.Document.RandomizeDropletSeed == -123, "unedited parameters stay unchanged");
                var committed = ui.View.Document.DeepClone();
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "both preferences commit as one undo step");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(committed), "both preferences support redo");
                Open(); ui.ClickText(L.Get("randomize.derandomizeOn")); ui.ClickText(L.Get("song.ok"));
                Check(!ui.View.Document.DerandomizeFSliderDroplets && ui.View.Document.RandomizeNewSliders
                    && ui.View.Document.Tracks[0].DropletRandomization is { Enabled: true }, "randomized default restores inherited FX");
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
