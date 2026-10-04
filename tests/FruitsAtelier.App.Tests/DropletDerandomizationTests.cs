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
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("randomize.enableAll")).Size == 13, "batch actions use larger text");
                ui.ClickText(L.Get("randomize.derandomizeOff"));
                var seed = ui.View.SongSetupFieldBounds["RandomizeDropletSeed"];
                ui.Click(seed.X + 10, seed.Y + 12); ui.Key('A', ctrl: true); ui.View.PasteSongSetupText("999", ui.View.SongSetupInputSession); ui.Paint();
                ui.ClickText(L.Get("randomize.resetStrength")); ui.ClickText(L.Get("randomize.disableAll"));
                Check(ui.View.Document.ContentEquals(map), "disabled randomization controls do not commit or mutate the document");
                Check(ui.Canvas.Texts.Any(t => t.Value == "37") && ui.Canvas.Texts.Any(t => t.Value == "-123"), "disabled fields retain their values");
                ui.ClickText(L.Get("randomize.derandomizeHrOff")); ui.Key(27);
                Check(ui.View.Document.ContentEquals(map), "Cancel discards map and HR derandomization drafts");
                Open(); ui.ClickText(L.Get("randomize.derandomizeOff")); ui.ClickText(L.Get("randomize.derandomizeHrOff")); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.Document.DerandomizeFSliderDroplets && ui.View.Document.DerandomizeDropletsForHardRock
                    && ui.View.Document.Tracks[0].DropletRandomization is { Enabled: true }, "Apply saves both gates while retaining FX state");
                Check(ui.View.Document.RandomizeDropletStrength == 37 && ui.View.Document.RandomizeDropletSeed == -123, "disabled parameters stay unchanged");
                var committed = ui.View.Document.DeepClone();
                ui.View.RandomizeAllDroplets(false);
                Check(ui.View.Document.ContentEquals(committed), "Edit batch commands respect the map gate");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "both preferences commit as one undo step");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(committed), "both preferences support redo");
                Open(); ui.ClickText(L.Get("randomize.derandomizeOn")); ui.ClickText(L.Get("song.ok"));
                Check(!ui.View.Document.DerandomizeFSliderDroplets && ui.View.Document.RandomizeNewSliders
                    && ui.View.Document.Tracks[0].DropletRandomization is { Enabled: true }, "turning the gate off restores FX and enables the new-slider default");
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
