using System.Reflection;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using FruitsAtelier.Localization;

internal static class PatternSourceTests
{
    internal static void Run()
    {
        string directory = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "fa-pattern-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Strings.SetLanguage("en");
            var source = new MapDocument { Name = "Source song", IsDemo = false, DurationMs = 8000 };
            foreach (var (key, value) in new[] { ("Title", "Source song"), ("Artist", "Source artist"), ("Creator", "Source mapper"), ("Version", "Expert") })
                SongSetup.Set(source, "Metadata", key, value);
            SongSetup.Set(source, "General", "Mode", "2");
            source.Fruits.Add(new() { TimeMs = 2000, X = 180 });
            var pattern = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "Source fixture");
            string gallery = Path.Combine(directory, "Patterns");
            var store = new PatternGalleryStore(gallery); store.Add(pattern);
            string sourcePath = Path.Combine(directory, "source.osu");
            File.WriteAllText(sourcePath, OsuBeatmapWriter.Serialize(source).Text);
            store.RememberSource(pattern.Id, sourcePath);

            var unsavedSource = new MapDocument { Name = "Unsaved source name", DurationMs = 8000 };
            unsavedSource.Fruits.Add(new() { TimeMs = 1500, X = 100 });
            var unsavedPattern = PatternSnapshot.Capture(unsavedSource, unsavedSource.Fruits.Select(f => f.Id), "Unsaved source");
            var unsaved = CreateUi("Unsaved"); unsaved.LoadDocument(unsavedSource);
            unsaved.View.RequestPatternSource!(unsavedPattern);
            Check(unsaved.View.PlayheadMs == 1500 && !unsaved.View.LibraryOpening,
                "An unsaved source map resolves by its document name without raw Metadata");

            var same = CreateUi("Same"); same.LoadDocument(source);
            var sameHistory = History(same);
            sameHistory.Begin("unsaved source change"); same.View.Document.Fruits[0].X = 220; sameHistory.Commit();
            same.View.RequestPatternSource!(pattern); same.Paint();
            Check(same.View.DiscardConfirmationVisible, "Normal gallery source jump asks about the current dirty map");
            same.ClickText(Strings.Get("mac.cancel"));
            Check(ReferenceEquals(History(same), sameHistory) && same.View.Document.Fruits[0].X == 220,
                "Cancel leaves the current map and unsaved history untouched");
            same.View.RequestPatternSource!(pattern); same.Paint(); same.ClickText(Strings.Get("mac.discard"));
            Check(same.View.PlayheadMs == 2000 && ReferenceEquals(History(same), sameHistory), "Existing source difficulty jump seeks to original capture time");

            var ui = CreateUi("External");
            var original = new MapDocument { Name = "Unsaved original", DurationMs = 12000 };
            original.Fruits.Add(new() { TimeMs = 500, X = 100 }); ui.LoadDocument(original);
            var originalHistory = History(ui);
            originalHistory.Begin("original draft"); ui.View.Document.Fruits[0].X = 300; originalHistory.Commit();
            Check(ui.View.BeginPatternEditing(pattern), "Enter pattern with unrelated unsaved original");
            ui.View.RequestPatternSource!(pattern); ui.Paint();
            Check(ui.View.DiscardConfirmationVisible, "Source jump offers Save/Discard even for an unchanged pattern draft");
            ui.ClickText(Strings.Get("mac.discard"));
            Wait(() => ui.View.WorkspaceSession is not null && !ui.View.LibraryOpening, ui);
            Check(!ui.View.PatternEditingActive && ui.View.HasRetainedPatternProject && SongSetup.Get(ui.View.Document, "Metadata", "Title") == "Source song",
                "Outside source opens while original draft remains retained");
            Check(ui.View.PlayheadMs == 2000 && !ui.View.DiscardConfirmationVisible, "Source opens at capture time without requiring original-map save");
            Check(ui.View.RestoreRetainedPatternProject() && ReferenceEquals(History(ui), originalHistory)
                && ui.View.Document.Fruits[0].X == 300 && ui.View.IsDirty, "Return recovers original unsaved history after a real source load");
            originalHistory.Undo(); Check(ui.View.Document.Fruits[0].X == 100, "Original undo works after source navigation"); originalHistory.Redo();

            var absentSource = source.DeepClone();
            SongSetup.Set(absentSource, "Metadata", "Title", "Missing unique source");
            var absent = PatternSnapshot.Capture(absentSource, absentSource.Fruits.Select(f => f.Id), "Missing source"); store.Add(absent);
            Check(ui.View.BeginPatternEditing(absent), "Enter missing-source pattern");
            ui.View.RequestPatternSource!(absent); ui.Paint(); ui.ClickText(Strings.Get("mac.discard"));
            Wait(() => ui.View.ErrorVisible, ui, allowError: true);
            Check(!ui.View.PatternEditingActive && ReferenceEquals(History(ui), originalHistory)
                && ui.View.Document.Fruits[0].X == 300 && ui.View.IsDirty, "Failed source lookup leaves original unsaved map recovered");

            Ui CreateUi(string suffix)
            {
                var result = new Ui();
                result.View.LibrarySettings.Workspace = Path.Combine(directory, "Workspace" + suffix);
                Directory.CreateDirectory(result.View.LibrarySettings.Workspace);
                result.View.ConfigurePatternGallery(gallery);
                return result;
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static EditorHistory History(Ui ui) => (EditorHistory)ui.View.GetType()
        .GetProperty("history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
    private static void Wait(Func<bool> ready, Ui ui, bool allowError = false)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            ui.Paint();
            if (!allowError && ui.View.ErrorVisible) throw new Exception("Source navigation error: " + string.Join(" ", ui.Canvas.Texts.Select(t => t.Value)));
            if (timer.Elapsed.TotalSeconds > 20) throw new Exception("Source navigation did not settle: " + ui.View.StatusMessage);
            Thread.Sleep(5);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
