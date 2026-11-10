using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TestplayModTests
{
    public static void Run()
    {
        var ui = new Ui();
        var map = new MapDocument { ApproachRate = 8, CircleSize = 5, DurationMs = 20000 };
        map.Fruits.Add(new() { TimeMs = 10000, X = 128 });
        map.Fruits.Add(new() { TimeMs = 11000, X = 384 });
        ui.LoadDocument(map);
        ui.OpenPreview();
        var easy = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.mod.easy"));
        var halfTime = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.mod.halftime"));
        var hardRock = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.mod.hardrock"));
        var doubleTime = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.mod.doubletime"));
        var hidden = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.mod.hidden"));
        var flashlight = ui.Canvas.Texts.Single(t => t.Value == L.Get("testplay.mod.flashlight"));
        Check(easy.Y == halfTime.Y && easy.X < halfTime.X && hardRock.Y > easy.Y
            && hardRock.Y == doubleTime.Y && doubleTime.Y == hidden.Y && hidden.Y == flashlight.Y
            && hardRock.X < doubleTime.X && doubleTime.X < hidden.X && hidden.X < flashlight.X, "Selector uses the two/four osu category order.");
        var resolution = ui.Canvas.Texts.Single(t => t.Value == L.Get("preview.resolution"));
        var ratio = ui.Canvas.Texts.Single(t => t.Value == L.Get("preview.ratio43"));
        Check(resolution.Y < ratio.Y && Math.Abs(resolution.X - ratio.X) <= 12, "Resolution title and controls align at the left.");

        ui.ClickText(L.Get("testplay.mod.easy"));
        Check(ui.View.LibrarySettings.TestplayMods == CatchMods.Easy && ui.View.PreviewApproachRate == 4 && ui.View.PreviewCircleSize == 2.5, "Preview selects shared Easy.");
        ui.ClickText(L.Get("testplay.mod.hardrock"));
        ui.ClickText(L.Get("testplay.mod.hidden"));
        ui.ClickText(L.Get("testplay.mod.doubletime"));
        Check(ui.View.LibrarySettings.TestplayMods == (CatchMods.HardRock | CatchMods.Hidden | CatchMods.DoubleTime)
            && ui.View.PreviewCircleSize == 6.5 && ui.View.PlaybackSpeed == 1.5, "HR replaces EZ and combines with HD/DT.");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("testplay.sharedModsHint")), "Preview explains shared settings.");
        ui.View.OpenTestplayTab(); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("testplay.modsReduction"))
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("testplay.modsIncrease")), "Tab groups mods.");
        ui.ClickText(L.Get("testplay.mod.halftime"));
        ui.ClickText(L.Get("testplay.mod.flashlight"));
        Check(ui.View.LibrarySettings.TestplayMods == (CatchMods.HardRock | CatchMods.Hidden | CatchMods.HalfTime | CatchMods.Flashlight),
            "HT replaces DT and preserves compatible mods.");
        ui.View.SetPlaybackSpeed(.5);
        ui.View.StartTestplay(); ui.Paint();
        Check(ui.View.IsTestplaying && ui.View.PlaybackSpeed == .75 && ui.View.TestplayApproachRate == 10, "Gameplay applies mod speed and difficulty.");
        ui.View.StopTestplay();
        Check(ui.View.PlaybackSpeed == .5 && !ui.View.IsDirty && ui.View.Document.ContentEquals(map), "Exit restores editor speed without content edits.");
        string path = Path.GetFullPath("artifacts/tests/testplay-mods.json");
        ui.View.LibrarySettings.Save(path);
        var loaded = LibrarySettings.Load(path);
        Check(loaded.TestplayMods == ui.View.LibrarySettings.TestplayMods, "Mod combination persists.");
        ui.View.OpenSettings(); ui.Paint();
        ui.View.ApplySettings(path, force: true);
        Check(ui.View.LibrarySettings.TestplayMods == loaded.TestplayMods, "Applying other settings preserves mods.");
        Check(CatchModRules.Normalize((CatchMods)255) == (CatchMods.HardRock | CatchMods.Hidden | CatchMods.DoubleTime | CatchMods.Flashlight), "Invalid and incompatible saved flags normalize.");
        double preempt = CatchScrollTiming.PreemptMs(8);
        Check(CatchModRules.HiddenOpacity(preempt * .6, 8) > .999
            && Math.Abs(CatchModRules.HiddenOpacity(preempt * .52, 8) - .5) < .001
            && CatchModRules.HiddenOpacity(preempt * .44, 8) == 0
            && CatchModRules.HiddenOpacity(-1, 8) == 0, "Hidden fade follows Catch preempt.");
        Console.WriteLine("PASS: shared Testplay/Preview mods, categories, clock, persistence and Hidden fade.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
