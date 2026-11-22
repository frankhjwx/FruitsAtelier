using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class VersionHistoryUiTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            StoryboardAudioRestore();
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                string workspace = Path.GetFullPath(Path.Combine("artifacts/tests/version-history-ui", Guid.NewGuid().ToString("N")));
                var first = new MapDocument { IsDemo = false, DurationMs = 10000 };
                first.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
                first.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500 });
                SongSetup.Set(first, "Metadata", "Version", "Kept");
                SongSetup.Set(first, "Metadata", "Title", "Original title");
                SongSetup.Set(first, "General", "SampleSet", "Soft");
                var second = first.DeepClone(); SongSetup.Set(second, "Metadata", "Version", "Removed");
                var project = BeatmapProject.FromDocuments([first, second]);
                var session = WorkspaceProject.Create(workspace, project, "");
                project.Difficulties[0].Document.Fruits[0].X = 300;
                SongSetup.Set(project.Difficulties[0].Document, "Metadata", "Title", "Edited title");
                SongSetup.Set(project.Difficulties[0].Document, "General", "SampleSet", "Normal");
                project.Difficulties[0].Document.TimingPoints[0].BeatLengthMs = 400;
                WorkspaceProject.Save(session, project);
                Guid removedId = project.Difficulties[1].Id;
                WorkspaceAssociations.DeleteDifficulty(session, project, removedId);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = workspace;
                ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory)); ui.PaintUntilReady();
                int saves = 0; ui.View.RequestSave = () => saves++;
                ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("history.menu")); Wait(ui);
                Check(ui.View.VersionHistoryVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("history.title")), "Edit opens localized history");
                PerformanceSchedulingTests.History(ui);
                void Tab(string name)
                {
                    var label = ui.Canvas.Texts.Single(t => t.Value == name && t.Y == 191);
                    ui.Click(label.X + 4, label.Y + 5);
                }
                var before = ui.View.Document.DeepClone();
                ui.Key('S', ctrl: true); ui.Key(46); ui.Key('Z', ctrl: true);
                Check(saves == 0 && !ui.View.IsDirty && ui.View.Document.ContentEquals(before), "history browsing isolates editor commands");
                ui.ClickText(L.Get("history.deletedName", "Removed")); Wait(ui);
                foreach (string tab in new[] { "General", "Editor", "Metadata", "Difficulty", "Events", "Timing", "Colours", "Objects" })
                    Tab(tab);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("history.difference", 1, 1)), "deleted difficulty exposes an object addition");
                double Span() => (double)ui.View.GetType().GetField("syncViewSpan", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
                double normalSpan = Span();
                Check(normalSpan < first.DurationMs / 2, "history shows an AR-sized time window rather than the entire map");
                ui.View.Document.ApproachRate = 10; ui.Paint();
                Check(Span() < normalSpan, "both previews follow the active editor AR even for a deleted difficulty");
                ui.View.Document.ApproachRate = before.ApproachRate; ui.Paint();
                double Start() => (double)ui.View.GetType().GetField("syncViewStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
                foreach (bool reverse in new[] { false, true })
                {
                    ui.View.LibrarySettings.ReverseCanvasScroll = reverse;
                    double start = Start();
                    ui.View.Wheel(size.Item1 - 100, size.Item2 - 160, 120, false); ui.Paint();
                    Check(Start() > start, "history wheel up views later times independently of canvas settings");
                    ui.View.Wheel(size.Item1 - 100, size.Item2 - 160, -120, false); ui.Paint();
                    Check(Math.Abs(Start() - start) < .001, "history wheel down restores the previous view");
                }
                ui.View.Wheel(size.Item1 - 100, size.Item2 - 160, 120, true); ui.Paint();
                Check(Span() < normalSpan, "history zoom changes the AR-sized viewport");
                Check(ui.View.Document.ContentEquals(before), "preview navigation does not edit authoring");
                ui.ClickText(L.Get("history.restore")); Wait(ui);
                Check(!ui.View.VersionHistoryVisible && ui.View.DifficultyCount == 2 && ui.View.CurrentDifficultyName == "Removed" && ui.View.IsDirty,
                    "deleted difficulty is restored as a pending project addition");
                Check(ui.View.Document.SourcePath is null && ui.View.CaptureProject().Difficulties.Single(d => d.Id != removedId).Document.Fruits[0].X == 300,
                    "restoration keeps other edits and unlinks deleted sources");
                ui.View.SaveWorkspace();
                Check(WorkspaceProject.Open(session.Directory).Project.Difficulties.Any(d => d.Id == removedId), "restored difficulty survives save and restart");
                ui.View.SwitchDifficulty(0); ui.PaintUntilReady();
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
                Tab("Metadata");
                Check(ui.Canvas.Texts.Any(t => t.Value == "Edited title") && ui.Canvas.Texts.Any(t => t.Value == "Original title"),
                    "history shows complete current and historical metadata values");
                Tab("Timing");
                Check(ui.Canvas.Texts.Any(t => t.Value.Contains(",400,")) && ui.Canvas.Texts.Any(t => t.Value.Contains(",500,")),
                    "history shows timing changes as text");
                Tab("Objects");
                Check(ui.Canvas.Texts.Any(t => t.Value == "300") && ui.Canvas.Texts.Any(t => t.Value == "100"), "history describes current and historical object positions");
                Check(ui.Canvas.Texts.Any(t => t.Value == "Objects" && t.Color == 0xED737B), "history marks changed categories");
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

    private static void StoryboardAudioRestore()
    {
        foreach (bool deleted in new[] { false, true })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/version-history-ui-audio", Guid.NewGuid().ToString("N")));
            string workspace = Path.Combine(root, "workspace"), media = Path.Combine(root, "media");
            Directory.CreateDirectory(media);
            byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "quiet-tone.ogg"));
            string source = Path.Combine(root, "source.ogg"); File.WriteAllBytes(source, bytes);
            var audio = StoryboardAudio.Capture(source);
            var document = new MapDocument { SourcePath = Path.Combine(media, WorkspaceProject.ResourceRootMarker), DurationMs = 10000 };
            SongSetup.Set(document, "Metadata", "Version", "Audio");
            document.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
            var events = new OsuSection { Name = "Events" };
            events.Lines.Add($"Sample,1000,0,\"{audio.RelativePath}\",100");
            document.OriginalSections.Add(events); document.StoryboardAudio[audio.RelativePath] = audio;
            var project = BeatmapProject.FromDocuments([new MapDocument(), document]);
            var session = WorkspaceProject.Create(workspace, project, "");
            Guid id = project.Difficulties[1].Id;
            if (deleted) WorkspaceAssociations.DeleteDifficulty(session, project, id);
            else WorkspaceVersionHistory.ArchiveCurrent(session, project);
            File.Delete(source); File.Delete(Path.Combine(media, audio.RelativePath));
            session = WorkspaceProject.Open(session.Directory);
            if (!deleted)
            {
                string missing = Path.Combine(root, "missing.osu");
                session.Manifest.Difficulties.Single(d => d.Id == id).Source = missing;
                session.Project.Difficulties.Single(d => d.Id == id).Document.SourcePath = missing;
            }
            var ui = new Ui(false); ui.Resize(1440, 900); ui.View.LibrarySettings.Workspace = workspace;
            ui.View.LoadWorkspace(session); ui.PaintUntilReady();
            ui.View.ShowVersionHistory(); Wait(ui);
            string label = deleted ? L.Get("history.deletedName", "Audio") : "Audio";
            var row = ui.Canvas.Texts.Single(t => t.Value == label && t.Y >= 88 && t.Y < 176);
            ui.Click(row.X + 4, row.Y + 4); Wait(ui);
            ui.ClickText(L.Get("history.restore")); Wait(ui);
            Check(ui.View.Document.AudioPath is null && Path.GetFileName(ui.View.Document.SourcePath) == WorkspaceProject.ResourceRootMarker,
                "storyboard-only history restores with a persistent local resource root");
            Check(WorkspaceSynchronization.Target(ui.View.WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == id)) is null,
                "historical audio resources do not restore a deleted source association");
            if (!deleted)
            {
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.SourcePath == Path.Combine(root, "missing.osu"), "undo restores the missing source context");
                ui.Key('Y', ctrl: true);
                Check(Path.GetFileName(ui.View.Document.SourcePath) == WorkspaceProject.ResourceRootMarker,
                    "redo restores the local storyboard resource root");
            }
            Check(ui.View.SaveWorkspace(), "restored storyboard difficulty saves");
            var reopened = WorkspaceProject.Open(session.Directory);
            var restored = reopened.Project.Difficulties.Single(d => d.Id == id).Document;
            string managed = Path.Combine(Path.GetDirectoryName(restored.SourcePath)!, audio.RelativePath);
            Check(File.ReadAllBytes(managed).SequenceEqual(bytes), "restored storyboard bytes survive save and reopen without music or original sources");
            restored.StoryboardAudio[audio.RelativePath].Cache(managed);
            Check(restored.StoryboardAudio[audio.RelativePath].Data!.SequenceEqual(bytes), "reopened restored audio loads from its new managed root");
            ui.View.StopFileMonitoring();
        }
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
