using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class VersionHistoryUiTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                string workspace = Path.GetFullPath(Path.Combine("artifacts/tests/version-history-ui", Guid.NewGuid().ToString("N")));
                var first = new MapDocument { IsDemo = false, DurationMs = 10000 };
                first.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
                first.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500 });
                SongSetup.Set(first, "Metadata", "Version", "Kept");
                var second = first.DeepClone(); SongSetup.Set(second, "Metadata", "Version", "Removed");
                var project = BeatmapProject.FromDocuments([first, second]);
                var session = WorkspaceProject.Create(workspace, project, "");
                project.Difficulties[0].Document.Fruits[0].X = 300;
                WorkspaceProject.Save(session, project);
                Guid removedId = project.Difficulties[1].Id;
                WorkspaceAssociations.DeleteDifficulty(session, project, removedId);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = workspace;
                ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory)); ui.Paint();
                int saves = 0; ui.View.RequestSave = () => saves++;
                ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("history.menu")); Wait(ui);
                Check(ui.View.VersionHistoryVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("history.title")), "Edit opens localized history");
                var before = ui.View.Document.DeepClone();
                ui.Key('S', ctrl: true); ui.Key(46); ui.Key('Z', ctrl: true);
                Check(saves == 0 && !ui.View.IsDirty && ui.View.Document.ContentEquals(before), "history browsing isolates editor commands");
                ui.ClickText(L.Get("history.deletedName", "Removed")); Wait(ui);
                double Span() => (double)ui.View.GetType().GetField("syncViewSpan", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
                double normalSpan = Span();
                Check(normalSpan < first.DurationMs / 2, "history shows an AR-sized time window rather than the entire map");
                ui.View.Document.ApproachRate = 10; ui.Paint();
                Check(Span() < normalSpan, "both previews follow the active editor AR even for a deleted difficulty");
                ui.View.Document.ApproachRate = before.ApproachRate; ui.Paint();
                ui.View.Wheel(size.Item1 - 100, 300, 120, true); ui.Paint();
                Check(Span() < normalSpan, "history zoom changes the AR-sized viewport");
                Check(ui.View.Document.ContentEquals(before), "preview navigation does not edit authoring");
                ui.ClickText(L.Get("history.restore")); Wait(ui);
                Check(!ui.View.VersionHistoryVisible && ui.View.DifficultyCount == 2 && ui.View.CurrentDifficultyName == "Removed" && ui.View.IsDirty,
                    "deleted difficulty is restored as a pending project addition");
                Check(ui.View.Document.SourcePath is null && ui.View.CaptureProject().Difficulties.Single(d => d.Id != removedId).Document.Fruits[0].X == 300,
                    "restoration keeps other edits and unlinks deleted sources");
                ui.View.SaveWorkspace();
                Check(WorkspaceProject.Open(session.Directory).Project.Difficulties.Any(d => d.Id == removedId), "restored difficulty survives save and restart");
                ui.View.SwitchDifficulty(0); ui.Paint();
                string missingSource = Path.Combine(workspace, "missing.osu");
                Guid keptId = ui.View.CaptureProject().Difficulties[0].Id;
                ui.View.WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == keptId).Source = missingSource;
                ui.View.Document.SourcePath = missingSource;
                ui.View.ShowVersionHistory(); Wait(ui);
                var versions = WorkspaceVersionHistory.List(ui.View.WorkspaceSession!);
                int target = versions.ToList().FindIndex(v => WorkspaceVersionHistory.Read(ui.View.WorkspaceSession!, v).Difficulties[0].Document.Fruits[0].X == 100);
                Check(target >= 0, "initial saved version remains available");
                for (int i = 0; i < target; i++) ui.Key(40);
                Wait(ui);
                var keptRow = ui.Canvas.Texts.Single(t => t.Value == "Kept" && t.Y >= 88 && t.Y < 176);
                ui.View.PointerDown(keptRow.X + 4, keptRow.Y + 4, 0, false, false); ui.View.PointerUp(keptRow.X + 4, keptRow.Y + 4, 0);
                Wait(ui);
                ui.ClickText(L.Get("history.restore")); Wait(ui);
                Check(ui.View.Document.Fruits[0].X == 100 && ui.View.DifficultyCount == 2, "existing restore changes only the selected difficulty");
                Check(ui.View.Document.SourcePath is null && WorkspaceSynchronization.Target(ui.View.WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == keptId)) is null,
                    "a restored missing source becomes a local difficulty");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits[0].X == 300, "restoration has one undo step");
                Check(ui.View.Document.SourcePath == missingSource && ui.View.WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == keptId).Source == missingSource,
                    "undo restores the previous source association");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.Fruits[0].X == 100, "restoration supports redo");
                ui.View.SaveWorkspace();
                var reopened = WorkspaceProject.Open(session.Directory);
                Check(reopened.Project.Difficulties.Single(d => d.Id == keptId).Document.SourcePath is null
                    && WorkspaceSynchronization.Target(reopened.Manifest.Difficulties.Single(d => d.Id == keptId)) is null,
                    "a recovered missing source saves and restarts as local");
                ui.Key('Z', ctrl: true);
                Check(ui.View.WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == keptId).Source == missingSource,
                    "undo retains the association after save replaces manifest entries");
                ui.Key('Y', ctrl: true);
                ui.View.ShowVersionHistory(); Wait(ui); ui.Key(27);
                Check(!ui.View.VersionHistoryVisible && ui.View.Document.Fruits[0].X == 100, "Escape dismisses history without changing content");
                ui.View.StopFileMonitoring();
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (ui.View.VersionHistoryBusy && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(10); }
        ui.Paint();
        Check(!ui.View.VersionHistoryBusy, "history worker completes");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
