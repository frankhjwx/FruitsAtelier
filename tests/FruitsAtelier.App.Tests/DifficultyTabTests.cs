using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DifficultyTabTests
{
    public static void Layout()
    {
        var project = BeatmapProject.FromDocuments(Enumerable.Range(0, 3).Select(_ => new MapDocument { IsDemo = false }));
        project.Difficulties[0].Name = "A";
        project.Difficulties[1].Name = "12345678901234567890";
        project.Difficulties[2].Name = string.Concat(Enumerable.Repeat("🍎", 17));
        var view = new EditorView(); view.LoadProject(project);
        var canvas = new RecordingCanvas(); view.Render(canvas, 1440, 900);
        var a = canvas.Texts.Single(t => t.Value == "A");
        var b = canvas.Texts.Single(t => t.Value == "1234567890123456…");
        var emoji = canvas.Texts.Single(t => t.Value == string.Concat(Enumerable.Repeat("🍎", 16)) + "…");
        Check(a.Y >= 84 && a.Y < 128 && a.Y == b.Y, "Tabs must be below the toolbar");
        Check(emoji.X - b.X > b.X - a.X, "Tab widths must follow name width");
        Check(canvas.Texts.Single(t => t.Value == "+").X < 1100, "Tabs must not stretch across the row");
        Check(view.CaptureProject().Difficulties[1].Name.Length == 20, "Truncation must not alter project names");
    }

    public static void Editing()
    {
        var ui = new Ui();
        double initial = ui.View.CurrentStarRating!.Value;
        ui.Key('F'); ui.ClickMap(1250, 480);
        double changed = ui.View.CurrentStarRating!.Value;
        Check(initial != changed, "Object edits must recalculate difficulty");
        ui.Key('Z', ctrl: true);
        Check(Math.Abs(ui.View.CurrentStarRating!.Value - initial) < 1e-12, "Undo restores rating");
        ui.Key('B'); ui.ClickMap(1750, 240);
        Check(ui.View.CurrentStarRating is null, "Unfinished drafts must not display partial stars");
        ui.Key(27);
        ui.SetCs("8");
        Check(ui.View.CurrentStarRating != initial, "CS must affect rating");
        ui.Key('Z', ctrl: true);
        ui.View.AddDifficulty(); ui.Paint();
        Check(ui.View.CurrentStarRating == 0, "Blank difficulty is zero stars");
        ui.Key(9, ctrl: true, shift: true);
        Check(ui.View.ActiveDifficultyIndex == 0 && ui.View.CurrentStarRating == initial, "Ctrl Shift Tab restores previous difficulty");
    }

    public static void Overflow()
    {
        var project = BeatmapProject.FromDocuments(Enumerable.Range(0, 12).Select(_ => new MapDocument { IsDemo = false }));
        for (int i = 0; i < 12; i++) project.Difficulties[i].Name = "Diff " + i;
        var view = new EditorView(); view.LoadProject(project);
        var canvas = new RecordingCanvas(); view.Render(canvas, 980, 620);
        Check(canvas.Texts.Any(t => t.Value == "Diff 0") && !canvas.Texts.Any(t => t.Value == "Diff 11"), "Overflow clips hidden tabs");
        for (int i = 0; i < 12; i++) view.Wheel(400, 106, -120, false);
        canvas.Clear(); view.Render(canvas, 980, 620);
        var last = canvas.Texts.Single(t => t.Value == "Diff 11");
        view.PointerDown(last.X + 2, last.Y + 2, 0, false, false);
        Check(view.ActiveDifficultyIndex == 11 && !view.IsDirty, "Scrolled tabs remain clickable and clean");
        view.Render(canvas, 1440, 900);
        canvas.Clear(); view.Render(canvas, 980, 620);
        Check(canvas.Texts.Any(t => t.Value == "Diff 11"), "Resize keeps selected tab visible");
        view.KeyDown(9, true, false); canvas.Clear(); view.Render(canvas, 980, 620);
        Check(view.ActiveDifficultyIndex == 0 && canvas.Texts.Any(t => t.Value == "Diff 0"), "Keyboard wraps and reveals current tab");
        var plus = canvas.Texts.Single(t => t.Value == "+");
        view.PointerDown(plus.X + 2, plus.Y + 2, 0, false, false); canvas.Clear(); view.Render(canvas, 980, 620);
        var add = canvas.Texts.Single(t => t.Value == L.Get("project.add"));
        view.PointerDown(add.X + 2, add.Y + 2, 0, false, false);
        Check(view.DifficultyCount == 13 && view.ActiveDifficultyIndex == 12, "Plus menu adds and selects a difficulty");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
