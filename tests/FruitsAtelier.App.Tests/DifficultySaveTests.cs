using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class DifficultySaveTests
{
    private static EditorHistory History(Ui ui) => (EditorHistory)typeof(EditorView)
        .GetProperty("history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
    private static FieldInfo SaveTask => typeof(EditorView).GetField("difficultySaveTask", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void Run()
    {
        foreach (bool linked in new[] { false, true })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/difficulty-save", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set");
            Directory.CreateDirectory(set);
            var project = new BeatmapProject { Name = "Save fixture" };
            for (int i = 0; i < 6; i++)
            {
                string path = Path.Combine(set, $"{i}.osu");
                File.WriteAllText(path, $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Save fixture\nArtist:Fixture\nCreator:Test\nVersion:Diff {i}\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
                var document = OsuBeatmapReader.ReadFile(path);
                if (!linked) document.SourcePath = null;
                project.Difficulties.Add(new() { Name = $"Diff {i}", Document = document });
            }
            var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), project, linked ? songs : "");
            foreach (var entry in session.Manifest.Difficulties) entry.ExportConfirmed = linked;
            var ui = new Ui(false);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace");
            ui.View.LibrarySettings.Songs = linked ? songs : "";
            ui.View.LoadWorkspace(session);
            ui.View.SwitchDifficulty(1);
            History(ui).Begin("other difficulty"); ui.View.Document.Fruits[0].X = 200; History(ui).Commit();
            ui.View.SwitchDifficulty(0);
            ui.PaintUntilReady();
            History(ui).Begin("saved edit"); ui.View.Document.Fruits[0].X = 300; History(ui).Commit();
            var untouched = session.Manifest.Difficulties.Skip(1).SelectMany(e => new[] { e.File, e.SyncFile })
                .OfType<string>().ToDictionary(f => f, f => (Text: File.ReadAllText(Path.Combine(session.Directory, f)), Time: File.GetLastWriteTimeUtc(Path.Combine(session.Directory, f))));
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            ui.View.SaveCurrentDifficulty();
            double dispatch = timer.Elapsed.TotalMilliseconds;
            allocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
            var task = (Task?)SaveTask.GetValue(ui.View);
            Check(task is not null, "Ordinary save uses the current-difficulty worker");
            Check(ui.View.SwitchDifficulty(1) && ui.View.Document.Fruits[0].X == 200, "Difficulty navigation and unsaved edits remain available during saving");
            Check(ui.View.SwitchDifficulty(0), "Return to the saving difficulty");
            ui.PaintUntilReady();
            History(ui).Begin("newer edit"); ui.View.Document.Fruits[0].X = 400; History(ui).Commit();
            Check(task!.Wait(TimeSpan.FromSeconds(30)), "Save finishes without pumping UI");
            ui.Paint();
            Check(ui.View.Document.Fruits[0].X == 400 && ui.View.IsDirty, "Completion preserves newer edits and their dirty state");
            var saved = WorkspaceProject.Open(session.Directory);
            Check(saved.Project.Difficulties[0].Document.Fruits[0].X == 300 && saved.Project.Difficulties[1].Document.Fruits[0].X == 100,
                "Only the requested difficulty snapshot is published");
            foreach (var (file, before) in untouched)
                Check(File.ReadAllText(Path.Combine(session.Directory, file)) == before.Text && File.GetLastWriteTimeUtc(Path.Combine(session.Directory, file)) == before.Time,
                    "Untouched difficulty and sync files retain their bytes and timestamps");
            if (linked)
                Check(OsuBeatmapReader.ReadFile(saved.Manifest.Difficulties[0].Source!).Fruits[0].X == 300
                    && OsuBeatmapReader.ReadFile(saved.Manifest.Difficulties[1].Source!).Fruits[0].X == 100, "Only the requested external difficulty is written");
            var versions = WorkspaceVersionHistory.List(saved);
            Check(versions.Any(v => !v.WorkingCopy), "Save retains its previous version");
            var compression = WorkspaceStorage.CompressLegacyHistoryAsync(Path.Combine(root, "Workspace"), interval: TimeSpan.Zero).GetAwaiter().GetResult();
            Check(compression.Error is null, "Deferred snapshots support background compression");
            foreach (var version in versions)
            {
                var historical = WorkspaceVersionHistory.Read(saved, version);
                Check(historical.Difficulties.Count == 6 && historical.Difficulties[0].Document.Fruits[0].X == (version.WorkingCopy ? 300 : 100),
                    "Deferred history reconstructs complete saved and working versions");
            }
            ui.Key('Z', ctrl: true);
            Check(!History(ui).IsDirty && ui.View.Document.Fruits[0].X == 300, "Undo reaches the saved snapshot");
            Check(ui.View.SwitchDifficulty(1) && ui.View.IsDirty && ui.View.Document.Fruits[0].X == 200, "Other difficulty edits remain dirty");
            if (linked)
            {
                ui.View.EnableFileMonitoring();
                var monitor = (WorkspaceFileMonitor)typeof(EditorView).GetField("fileMonitor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
                monitor.Record(saved.Manifest.Difficulties[0].Source!);
                monitor.Record(Path.Combine(saved.Directory, saved.Manifest.Difficulties[0].File));
                typeof(WorkspaceFileMonitor).GetField("changedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(monitor, DateTime.UtcNow.AddSeconds(-2));
                ui.Paint();
                var acceptedCheck = (Task?)typeof(EditorView).GetField("acceptedFileCheck", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View);
                Check(acceptedCheck is not null && acceptedCheck.Wait(TimeSpan.FromSeconds(5)), "Saved output notifications are checked off-thread");
                ui.Paint();
                Check(!ui.View.SynchronizationBusy && OsuBeatmapReader.ReadFile(saved.Manifest.Difficulties[1].Source!).Fruits[0].X == 100,
                    "Accepted save notifications do not trigger set-wide publication of other edits");
            }
            Console.WriteLine($"Save linked={linked}, difficulties=6: dispatch={dispatch:F2}ms, UI allocation={allocation}, total={timer.Elapsed.TotalMilliseconds:F2}ms");
            ui.View.StopFileMonitoring();
        }
        Recovery();
        ReceiptRecovery();
        EditingDuringSave();
    }

    private static void EditingDuringSave()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/save-editing", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Editing fixture\nArtist:Fixture\nCreator:Test\nVersion:Catch\n[Difficulty]\nSliderMultiplier:1.4\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,500,1,0,0:0:0:0:\n100,192,1000,2,0,L|200:192,1,100\n");
        var ui = new Ui(false);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace");
        ui.View.LibrarySettings.Songs = songs;
        var session = WorkspaceProject.Create(ui.View.LibrarySettings.Workspace,
            BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs);
        session.Manifest.Difficulties[0].ExportConfirmed = true;
        ui.View.LoadWorkspace(session);
        ui.View.EnableFileMonitoring();
        ui.View.RequestSave = ui.View.SaveCurrentDifficulty;
        try
        {
            ui.PaintUntilReady();
            for (int i = 0; i < 5; i++)
            {
                ui.Key('2');
                int count = ui.View.Document.Fruits.Count;
                ui.ClickMap(750, 200);
                Check(ui.View.Document.Fruits.Count == count + 1, "Note placement remains available");
                NoModal();
                ui.Key('S', ctrl: true);
                ui.Key('S', ctrl: true);
                NoModal();
                ui.Key(46);
                Check(ui.View.Document.Fruits.Count == count, "Note deletion remains available during background save");
                NoModal();
                WaitQuiet();
                ui.View.CheckFilesOnActivation();
                WaitQuiet();
            }
        }
        finally { ui.View.StopFileMonitoring(); }

        void WaitQuiet()
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            do
            {
                ui.Paint(); NoModal();
                if (!ui.View.SynchronizationBusy) return;
                Thread.Sleep(1);
            } while (DateTime.UtcNow < deadline);
            throw new Exception("Background publication did not finish");
        }

        void NoModal() => Check(!ui.View.SynchronizationWaitVisible && !ui.View.SynchronizationVisible
            && !ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("sync.waitHelp")
                || t.Value == FruitsAtelier.Localization.Strings.Get("sync.title")),
            "Placing, deleting and saving notes must not flash synchronization windows");
    }

    private static void Recovery()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/difficulty-save-recovery", Guid.NewGuid().ToString("N")));
        var map = new MapDocument { Name = "Recovery" };
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        var session = WorkspaceProject.Create(root, BeatmapProject.FromDocuments([map]), "");
        string file = session.Manifest.Difficulties[0].File;
        string journal = session.Directory + ".updating";
        Directory.CreateDirectory(Path.Combine(journal, "before"));
        File.Copy(Path.Combine(session.Directory, file), Path.Combine(journal, "before", file));
        File.WriteAllText(Path.Combine(journal, "ready.json"), System.Text.Json.JsonSerializer.Serialize(new { Files = new[] { file }, Existing = new[] { file } }));
        File.WriteAllText(Path.Combine(session.Directory, file), "interrupted replacement");
        Check(WorkspaceProject.Open(session.Directory).Project.Difficulties[0].Document.Fruits[0].X == 100 && !Directory.Exists(journal),
            "Interrupted difficulty publication restores the previous snapshot before opening");
    }

    private static void ReceiptRecovery()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/difficulty-receipt-recovery", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
        var first = new MapDocument { Name = "Recovery" };
        first.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        var project = BeatmapProject.FromDocuments([first, first.DeepClone()]);
        project.Difficulties[1].Name = "Other";
        var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), project, songs);
        var initial = WorkspaceExport.Plan(session, project.Difficulties[0], songs, false, project.Difficulties[0].Name, false);
        Directory.CreateDirectory(Path.GetDirectoryName(initial.Target)!);
        WorkspaceExport.Commit(session, initial);
        WorkspaceProject.Save(session, project);
        session = WorkspaceProject.Open(session.Directory);
        var changed = session.Project.Difficulties[0];
        changed.Document.Fruits[0].X = 300;
        var plan = WorkspaceExport.Plan(session, changed, songs, true, changed.Name, false);
        string other = Path.Combine(session.Directory, session.Manifest.Difficulties[1].File);
        DateTime timestamp = File.GetLastWriteTimeUtc(other);
        WorkspaceExportRecovery.Prepare(session, session.Project, plan, changed.Id, difficultyOnly: true);
        WorkspaceExport.Commit(session, plan);
        string newerExternal = File.ReadAllText(plan.Target) + "200,192,2000,1,0,0:0:0:0:\n";
        File.WriteAllText(plan.Target, newerExternal);
        var restored = WorkspaceProject.Open(session.Directory);
        Check(restored.Project.Difficulties.Count == 2 && restored.Project.Difficulties[0].Document.Fruits[0].X == 300
            && File.GetLastWriteTimeUtc(other) == timestamp && File.ReadAllText(plan.Target) == newerExternal,
            "Scoped export recovery preserves other difficulty files and newer external content");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
