using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DifficultyTabTests
{
    public static void SortAscending()
    {
        var hard = new MapDocument { IsDemo = false, DurationMs = 20000 };
        for (int i = 0; i < 100; i++) hard.Fruits.Add(new() { TimeMs = 1000 + i * 100, X = i % 2 == 0 ? 20 : 490 });
        var project = BeatmapProject.FromDocuments([hard, new() { IsDemo = false }, new() { IsDemo = false }]);
        project.Difficulties[0].Name = "Hard";
        project.Difficulties[1].Name = "Blank A";
        project.Difficulties[2].Name = "Blank B";
        var view = new EditorView(false); view.LoadProject(project);
        var before = view.CaptureProject();
        var canvas = new RecordingCanvas();
        view.Render(canvas, 1440, 900);
        var hardTab = canvas.Texts.Single(t => t.Value == "Hard" && t.Y == 54);
        view.PointerDown(hardTab.X + 2, hardTab.Y + 2, 2, false, false);
        view.PointerUp(hardTab.X + 2, hardTab.Y + 2, 2);
        canvas.Clear(); view.Render(canvas, 1440, 900);
        var sort = canvas.Texts.Single(t => t.Value == L.Get("project.sortStarsAscending"));
        view.PointerDown(sort.X + 2, sort.Y + 2, 0, false, false);
        view.PointerUp(sort.X + 2, sort.Y + 2, 0);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do
        {
            canvas.Clear(); view.Render(canvas, 1440, 900);
            if (!view.StarRatingsRefreshing) break;
            Thread.Sleep(10);
        } while (DateTime.UtcNow < deadline);
        Check(!view.StarRatingsRefreshing, "Sort waits for completed SR calculations");
        var names = canvas.Texts.Where(t => t.Y == 54 && new[] { "Hard", "Blank A", "Blank B" }.Contains(t.Value))
            .OrderBy(t => t.X).Select(t => t.Value).ToArray();
        Check(names.SequenceEqual(new[] { "Blank A", "Blank B", "Hard" }), "Ascending SR keeps ties stable");
        Check(view.ActiveDifficultyIndex == 0 && view.CurrentDifficultyName == "Hard", "Sorting retains active difficulty");
        var after = view.CaptureProject();
        Check(before.Difficulties.Zip(after.Difficulties).All(pair => pair.First.Id == pair.Second.Id
            && pair.First.Document.ContentEquals(pair.Second.Document)) && !view.IsDirty, "Sorting preserves project content and storage order");
        view.KeyDown(9, true, false); view.KeyUp(9);
        Check(view.CurrentDifficultyName == "Blank A", "Ctrl Tab traverses displayed order");
        view.KeyDown(9, true, true); view.KeyUp(9);
        Check(view.CurrentDifficultyName == "Hard", "Ctrl Shift Tab traverses displayed order backwards");
        var blank = canvas.Texts.Single(t => t.Value == "Blank B");
        view.PointerDown(blank.X + 2, blank.Y + 2, 0, false, false);
        view.PointerUp(blank.X + 2, blank.Y + 2, 0);
        Check(view.CurrentDifficultyName == "Blank B", "Sorted tab clicks target the original difficulty");
        view.LoadProject(project); canvas.Clear(); view.Render(canvas, 1440, 900);
        Check(canvas.Texts.Single(t => t.Value == "Hard" && t.Y == 54).X
            < canvas.Texts.Single(t => t.Value == "Blank A" && t.Y == 54).X, "New project clears presentation sorting");
    }

    public static void Layout()
    {
        var project = BeatmapProject.FromDocuments(Enumerable.Range(0, 3).Select(_ => new MapDocument { IsDemo = false }));
        project.Difficulties[0].Name = "A";
        project.Difficulties[1].Name = "12345678901234567890";
        project.Difficulties[2].Name = string.Concat(Enumerable.Repeat("🍎", 17));
        var view = new EditorView(); view.LoadProject(project);
        var canvas = new RecordingCanvas(); view.Render(canvas, 1440, 900);
        var a = canvas.Texts.Single(t => t.Value == "A");
        var b = canvas.Texts.Single(t => t.Value == project.Difficulties[1].Name);
        var emoji = canvas.Texts.Single(t => t.Value == project.Difficulties[2].Name);
        Check(a.Y >= 40 && a.Y < 84 && a.Y == b.Y, "Tabs must be below the toolbar");
        Check(emoji.X - b.X > b.X - a.X, "Tab widths must follow name width");
        Check(canvas.Texts.Single(t => t.Value == "+" && t.Y < 128).X < 1100, "Tabs must not stretch across the row");
        Check(view.CaptureProject().Difficulties[1].Name.Length == 20, "Truncation must not alter project names");
        canvas.Clear(); view.Render(canvas, 600, 620);
        b = canvas.Texts.Single(t => t.Y == 54 && t.Value.StartsWith("123") && t.Value.EndsWith("…"));
        view.PointerMove(b.X + 2, b.Y + 2, false, false);
        canvas.Clear(); view.Render(canvas, 600, 620);
        var tip = canvas.Texts.Single(t => t.Value == project.Difficulties[1].Name);
        view.PointerMove(b.X + 12, b.Y + 2, false, false);
        canvas.Clear(); view.Render(canvas, 600, 620);
        Check(canvas.Texts.Single(t => t.Value == project.Difficulties[1].Name).X == tip.X + 10, "Full-name tooltip follows the pointer");
        view.PointerMove(800, 300, false, false);
        canvas.Clear(); view.Render(canvas, 600, 620);
        Check(!canvas.Texts.Any(t => t.Value == project.Difficulties[1].Name), "Full-name tooltip disappears outside the tab");
    }

    public static void Editing()
    {
        var ui = new Ui();
        double initial = Rating(ui.View);
        ui.Key('F'); ui.ClickMap(1250, 480);
        var exported = OsuBeatmapWriter.Serialize(ui.View.Document);
        double expected = CatchDifficultyCalculator.Calculate(exported.ObjectSequenceMatches ? exported.PlayableObjects : ui.View.Conversion.Objects,
            ui.View.Document.CircleSize).StarRating;
        double displayed = ui.View.CurrentStarRating ?? 0;
        Check(displayed == initial || Math.Abs(displayed - expected) < 1e-12,
            "Edits show the cached rating while pending or the correct completed result");
        double changed = Rating(ui.View);
        Check(Math.Abs(changed - expected) < 1e-12, "Completed stars match independently exported gameplay events");
        Check(initial != changed, "Object edits must recalculate difficulty");
        ui.Key('Z', ctrl: true);
        Check(Math.Abs(Rating(ui.View) - initial) < 1e-12, "Undo restores rating");
        ui.Key('B'); ui.ClickMap(1750, 240);
        Check(ui.View.CurrentStarRating == initial && ui.View.CurrentStarRatingRefreshing, "Unfinished drafts retain cached stars and indicate pending refresh");
        ui.Key(27);
        double originalCs = ui.View.Document.CircleSize;
        ui.SetCs("8");
        Check(Rating(ui.View) != initial, "CS must affect rating");
        ui.SetCs(originalCs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ui.View.AddDifficulty(); ui.Paint();
        Check(Rating(ui.View) == 0, "Blank difficulty is zero stars");
        ui.Key(9, ctrl: true, shift: true);
        Check(ui.View.ActiveDifficultyIndex == 0 && Rating(ui.View) == initial, "Ctrl Shift Tab restores previous difficulty");
    }

    public static void PreviewRatings()
    {
        string originalLanguage = L.Language;
        var view = new EditorView();
        var canvas = new RecordingCanvas();
        view.Render(canvas, 1440, 900);
        view.PointerDown(view.PreviewToggleBounds.X + 5, view.PreviewToggleBounds.Y + 5, 0, false, false);
        view.PointerUp(view.PreviewToggleBounds.X + 5, view.PreviewToggleBounds.Y + 5, 0);
        Rating(view);
        foreach (string language in L.AvailableLanguages)
        {
            L.SetLanguage(language);
            foreach (int mod in new[] { 0, 1, 2 })
            {
                canvas.Clear(); view.Render(canvas, 1440, 900);
                string key = mod switch { 1 => "preview.easy", 2 => "preview.hardRock", _ => "preview.normal" };
                var button = canvas.Texts.Single(t => t.Value == L.Get(key));
                view.PointerDown(button.X + 2, button.Y + 2, 0, false, false);
                view.PointerUp(button.X + 2, button.Y + 2, 0);
                canvas.Clear(); view.Render(canvas, 1440, 900);
                var exported = OsuBeatmapWriter.Serialize(view.Document);
                var objects = mod == 2 ? exported.PlayableHardRockObjects : exported.PlayableObjects;
                double expected = CatchDifficultyCalculator.Calculate(objects, view.PreviewCircleSize).StarRating;
                Check(Math.Abs(view.PreviewStarRating - expected) < 1e-12, "Preview SR follows mod CS and HR positions");
                Check(!view.CurrentStarRatingRefreshing, "Switching preview mode reuses background results");
                var stats = canvas.Texts.Single(t => t.Value == L.Get("ui.previewStats",
                    view.PreviewApproachRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), view.PreviewCircleSize.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                    mod switch { 1 => "EZ", 2 => "HR", _ => "NM" }));
                var stars = canvas.Texts.Single(t => t.Value == L.Get("preview.stars", expected));
                Check(stars.X > stats.X && stars.Y == stats.Y, "SR appears at the right of the stats row");
            }
        }
        L.SetLanguage(originalLanguage);
    }

    private static double Rating(EditorView view)
    {
        double value = view.CurrentStarRating ?? 0;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (view.CurrentStarRatingRefreshing && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10); value = view.CurrentStarRating ?? 0;
        }
        Check(!view.CurrentStarRatingRefreshing, "Rating calculation completes");
        return value;
    }

    public static void AsyncRatings()
    {
        var view = new EditorView(); view.NewProject();
        Check(view.CurrentStarRating == 0 && view.CurrentStarRatingRefreshing, "First calculation shows a zero placeholder and refresh state");
        Check(Rating(view) == 0 && !view.CurrentStarRatingFailed, "A calculated zero is a completed result");
        var document = view.Document;
        for (int i = 0; i < 400; i++) document.Fruits.Add(new Fruit { X = i % 2 == 0 ? 20 : 490, TimeMs = i * 100 });
        Check(view.CurrentStarRating == 0 && view.CurrentStarRatingRefreshing, "Large edits retain the last result");
        document.Fruits.Clear();
        Check(Rating(view) == 0, "An outdated nonzero calculation cannot overwrite a newer blank map");
        document.CircleSize = -1;
        _ = view.CurrentStarRating;
        Check(Rating(view) == 0 && view.CurrentStarRatingFailed, "Failure keeps cached value and stops the spinner");
        document.CircleSize = 5;
        Check(Rating(view) == 0 && !view.CurrentStarRatingFailed, "A corrected map retries and clears failure");
        document.Fruits.Add(new Fruit { X = 20, TimeMs = 100 });
        _ = view.CurrentStarRating;
        view.NewProject();
        Check(Rating(view) == 0, "Replacing a project cannot accept the previous project's pending result");
    }

    public static void Overflow()
    {
        var project = BeatmapProject.FromDocuments(Enumerable.Range(0, 12).Select(_ => new MapDocument { IsDemo = false }));
        for (int i = 0; i < 12; i++) project.Difficulties[i].Name = "Diff " + i;
        var view = new EditorView(); view.LoadProject(project);
        var canvas = new RecordingCanvas(); view.Render(canvas, 980, 620);
        Check(canvas.Texts.Any(t => t.Value == "Diff 0") && !canvas.Texts.Any(t => t.Value == "Diff 11"), "Overflow clips hidden tabs");
        for (int i = 0; i < 12; i++) view.Wheel(400, 62, -120, false);
        canvas.Clear(); view.Render(canvas, 980, 620);
        var last = canvas.Texts.Single(t => t.Value == "Diff 11");
        view.PointerDown(last.X + 2, last.Y + 2, 0, false, false);
        view.PointerUp(last.X + 2, last.Y + 2, 0);
        Check(view.ActiveDifficultyIndex == 11 && !view.IsDirty, "Scrolled tabs remain clickable and clean");
        view.Render(canvas, 1440, 900);
        canvas.Clear(); view.Render(canvas, 980, 620);
        Check(canvas.Texts.Any(t => t.Value == "Diff 11"), "Resize keeps selected tab visible");
        view.KeyDown(9, true, false); canvas.Clear(); view.Render(canvas, 980, 620);
        Check(view.ActiveDifficultyIndex == 0 && canvas.Texts.Any(t => t.Value == "Diff 0"), "Keyboard wraps and reveals current tab");
        var first = canvas.Texts.Single(t => t.Value == "Diff 0");
        view.PointerDown(first.X + 60, first.Y + 2, 0, false, false);
        view.PointerMove(first.X - 140, first.Y + 2, false, false);
        canvas.Clear(); view.Render(canvas, 980, 620);
        view.PointerUp(first.X - 140, first.Y + 2, 0);
        Check(view.ActiveDifficultyIndex == 0 && !view.IsDirty && !canvas.Texts.Any(t => t.Value == "Diff 0"), "Dragging scrolls tabs without switching or editing difficulties");
        var plus = canvas.Texts.Single(t => t.Value == "+" && t.Y < 128);
        view.PointerDown(plus.X + 2, plus.Y + 2, 0, false, false); canvas.Clear(); view.Render(canvas, 980, 620);
        var add = canvas.Texts.Single(t => t.Value == L.Get("project.add"));
        view.PointerDown(add.X + 2, add.Y + 2, 0, false, false);
        Check(view.DifficultyCount == 13 && view.ActiveDifficultyIndex == 12, "Plus menu adds and selects a difficulty");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
