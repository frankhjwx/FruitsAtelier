using System.Reflection;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;

internal static class PatternEditingTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fa-pattern-edit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ui = new Ui();
            ui.View.LibrarySettings.Workspace = Path.Combine(directory, "Workspace");
            var map = new MapDocument { Name = "Original draft", DurationMs = 12000 };
            map.Fruits.Add(new() { TimeMs = 1000, X = 200 });
            ui.LoadDocument(map);
            ui.Resize(980, 620);
            var palette = ui.View.ToolButtonBounds.Select(b => (b.Y, b.Width, b.Height)).ToArray();
            var originalHistory = History(ui);
            originalHistory.Begin("unsaved change"); ui.View.Document.Fruits[0].X = 240; originalHistory.Commit();
            var originalDocument = ui.View.Document;
            ui.View.UpdateTransport(2000, 12000, false, false, false, null, null);
            double playhead = ui.View.PlayheadMs, viewStart = ui.View.ViewStartMs, zoom = ui.View.CanvasZoom;
            var pattern = PatternSnapshot.Capture(map, [map.Fruits[0].Id], "Test pattern");
            var store = new PatternGalleryStore(directory); store.Add(pattern);
            ui.View.ConfigurePatternGallery(directory);
            Check(ui.View.BeginPatternEditing(pattern), "Enter pattern editing");
            ui.Paint();
            Check(ui.View.ToolButtonBounds.Select(b => (b.Y, b.Width, b.Height)).SequenceEqual(palette),
                "Pattern banner does not shrink or shift the separate tool palette in compact windows");
            Check(ui.View.PatternEditingActive && ui.View.DifficultyCount == 1 && ui.View.WorkspaceSession is null,
                "Pattern is an independent single-document session");
            ui.View.UpdateTransport(7000, 12000, true, true, false, null, "retired-original.wav");
            Check(ui.View.PlayheadMs == pattern.StartTimeMs && !ui.View.AudioPlaying,
                "Retiring original audio cannot overwrite the pattern playhead or restart playback");
            History(ui).Begin("edit pattern"); ui.View.Document.Fruits[0].X = 333; History(ui).Commit();
            Check(!ui.View.AddDifficulty() && !ui.View.AddDifficulty(new MapDocument()) && ui.View.DifficultyCount == 1,
                "Pattern mode rejects new and imported project difficulties");
            Check(!ui.View.SaveWorkspace() && !ui.View.BeginWorkspaceSave(), "Pattern draft cannot be saved as a workspace project");
            ui.View.MarkSaved();
            Check(ui.View.IsDirty, "Project save acknowledgement cannot clear pattern dirty state");
            ui.View.ShowWorkspaceExport();
            Check(!ui.View.ExportVisible, "Pattern mode rejects workspace export");
            ui.View.BeginFadImport(Path.Combine(directory, "missing.fad"));
            ui.View.BeginFadExport(Path.Combine(directory, "unexpected.fad"), 0);
            ui.View.BeginFazExport(Path.Combine(directory, "unexpected.faz"), false);
            Check(!File.Exists(Path.Combine(directory, "unexpected.fad")) && !File.Exists(Path.Combine(directory, "unexpected.faz"))
                && !ui.View.ErrorVisible, "Project package commands are rejected before reading or writing files");
            ui.View.GetType().GetMethod("SetNotes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui.View, ["Unsupported project note", true]);
            Check(ui.View.CaptureProject().Notes == "", "Pattern mode does not accept unsaved project-level notes");
            var pendingLeave = ui.View.ResolvePatternSessionsBeforeLeavingAsync();
            ui.Paint(); ui.ClickText(FruitsAtelier.Localization.Strings.Get("mac.cancel"));
            Check(pendingLeave.IsCompletedSuccessfully && !pendingLeave.Result && ui.View.PatternEditingActive,
                "Canceling a window or file transition retains the pattern draft");
            ui.View.DiscardPatternEditing();
            Check(!ui.View.PatternEditingActive && ReferenceEquals(History(ui), originalHistory)
                && ReferenceEquals(ui.View.Document, originalDocument) && ui.View.IsDirty,
                "Discard restores exact original history and unsaved document");
            Check(ui.View.PlayheadMs == playhead && ui.View.ViewStartMs == viewStart && ui.View.CanvasZoom == zoom,
                "Original playhead and viewport are restored");
            Check(store.Load().Single().CreateDocument().Fruits[0].X == 200, "Discard leaves gallery unchanged");
            originalHistory.Undo();
            Check(ui.View.Document.Fruits[0].X == 200, "Original undo history survives pattern mode");
            originalHistory.Redo();

            Check(ui.View.BeginPatternEditing(pattern), "Enter again for save");
            History(ui).Begin("edit pattern"); ui.View.Document.Fruits[0].X = 350; History(ui).Commit();
            Check(ui.View.SavePatternEditing(), "Start pattern save");
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (ui.View.PatternEditingBusy)
            {
                ui.View.PumpPatternEditing();
                if (deadline.Elapsed.TotalSeconds > 10) throw new Exception("Pattern save did not complete");
                Thread.Sleep(1);
            }
            Check(!ui.View.PatternEditingActive && ReferenceEquals(History(ui), originalHistory), "Save returns to original history");
            var saved = store.Load().Single();
            Check(saved.Id == pattern.Id && saved.CreateDocument().Fruits[0].X == 350
                && ui.View.Document.Fruits[0].X == 240 && ui.View.IsDirty, "Save replaces only gallery snapshot");

            Check(ui.View.BeginPatternEditing(saved), "Enter for source navigation");
            ui.View.RequestExitPatternEditing(() => ui.View.LoadDocument(new MapDocument { Name = "Source" }), retainOriginalForNavigation: true);
            ui.Paint(); ui.ClickText(FruitsAtelier.Localization.Strings.Get("mac.discard"));
            Check(ui.View.HasRetainedPatternProject && !ui.View.PatternEditingActive && ui.View.Document.Name == "Source",
                "Source navigation exits pattern mode while retaining original draft");
            Check(ui.View.RestoreRetainedPatternProject() && ReferenceEquals(History(ui), originalHistory)
                && ui.View.Document.Fruits[0].X == 240 && ui.View.IsDirty, "Original draft can be recovered after source navigation");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static EditorHistory History(Ui ui) => (EditorHistory)ui.View.GetType()
        .GetProperty("history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
