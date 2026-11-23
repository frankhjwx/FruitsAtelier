using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ProjectSaveTests
{
    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            string previous = L.Language;
            try
            {
                L.SetLanguage(language);
                SaveGroup();
                SaveAllEdits();
                SaveFromCleanDifficulty();
                ProjectNotesAndObjects();
                WorkspaceOnlySave();
                FirstExport();
                UndoAndRedo();
                ExternalConflict();
                if (OperatingSystem.IsWindows()) FailedWriteRetry();
                LocalAndFaOnly();
            }
            finally { L.SetLanguage(previous); }
        }
    }

    private static void SaveGroup()
    {
        var f = new Fixture();
        f.Ui.View.SwitchDifficulty(1);
        Edit(f.Ui, () => f.Ui.View.Document.Fruits[0].X = 250);
        f.Ui.View.SwitchDifficulty(0); f.Sync();
        f.Ui.View.SwitchDifficulty(2);
        Edit(f.Ui, () => SongSetup.Set(f.Ui.View.Document, "Metadata", "Creator", "Later creator"));
        f.Ui.View.SwitchDifficulty(0);
        f.Save(beforeCompletion: () =>
        {
            Edit(f.Ui, () => SongSetup.Set(f.Ui.View.Document, "Metadata", "Tags", "newer edit"));
        });
        Check(f.Tags().All(t => t == "synchronized tags"), "One save publishes Tags to every affected osu file");
        Check(f.Read(1).Fruits[0].X == 250, "Sibling independent objects are saved with the project");
        Check(SongSetup.Get(f.Read(2), "Metadata", "Creator") == "Later creator", "All sibling metadata edits are saved");
        Check(f.Ui.View.IsDirty && History(f.Ui).IsDirty, "Newer source edit remains dirty after completion");
        f.Ui.View.SwitchDifficulty(1);
        Check(!History(f.Ui).IsDirty && f.Ui.View.Document.Fruits[0].X == 250, "Saved sibling object edit is clean");
        f.Ui.View.SwitchDifficulty(2);
        Check(!History(f.Ui).IsDirty, "Saved sibling metadata is clean");
        var reopened = WorkspaceProject.Open(f.Directory);
        Check(reopened.Project.Difficulties[1].Document.Fruits[0].X == 250, "All object edits persist in the workspace");
        Check(reopened.Manifest.Difficulties.All(d => !d.PendingExport), "Successful group save clears persisted pending flags");
        Console.WriteLine("PASS project-wide Tags and object saves preserve only newer dirty state");
    }

    private static void SaveAllEdits()
    {
        var f = new Fixture();
        for (int i = 0; i < 3; i++)
        {
            f.Ui.View.SwitchDifficulty(i);
            Edit(f.Ui, () => f.Ui.View.Document.Fruits[0].X = 200 + i);
        }
        f.Ui.View.SwitchDifficulty(0); f.Save();
        Check(Enumerable.Range(0, 3).All(i => f.Read(i).Fruits[0].X == 200 + i), "Ctrl+S publishes all dirty difficulties without a metadata sync");
        Check(!f.Ui.View.IsDirty, "All saved edits become clean");
        Console.WriteLine("PASS Ctrl+S saves all dirty difficulties");
    }

    private static void SaveFromCleanDifficulty()
    {
        var f = new Fixture();
        string clean = f.Ui.View.WorkspaceSession!.Manifest.Difficulties[0].Source!;
        DateTime before = File.GetLastWriteTimeUtc(clean);
        f.Ui.View.SwitchDifficulty(1);
        Edit(f.Ui, () => f.Ui.View.Document.Fruits[0].X = 222);
        f.Ui.View.SwitchDifficulty(0); f.Save();
        Check(f.Read(1).Fruits[0].X == 222 && File.GetLastWriteTimeUtc(clean) == before,
            "Save from a clean difficulty publishes other dirty difficulties without rewriting the clean file");
    }

    private static void ProjectNotesAndObjects()
    {
        var f = new Fixture();
        Edit(f.Ui, () => f.Ui.View.Document.Fruits[0].X = 321);
        typeof(EditorView).GetField("projectNotes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Ui.View, "Saved notes");
        f.Ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(f.Ui);
        var saved = WorkspaceProject.Open(f.Directory);
        Check(saved.Project.Notes == "Saved notes" && f.Read(0).Fruits[0].X == 321 && !f.Ui.View.IsDirty,
            "A single save persists project notes and all pending linked content");
    }

    private static void WorkspaceOnlySave()
    {
        var f = new Fixture(); f.Sync();
        Check(f.Ui.View.SaveWorkspace(), "Workspace save succeeds");
        Check(f.Ui.View.IsDirty, "Workspace-only save retains pending osu publication dirty state");
        f.Ui.Paint();
        Check(f.Ui.Canvas.Circles.Count(c => c.Y == 62 && c.Radius == 2.5f) == 3, "Every pending difficulty retains its visible dirty dot");
        Check(f.Tags().All(t => t == "old tags"), "Workspace-only save does not publish Songs");
        var reopened = WorkspaceProject.Open(f.Directory);
        Check(reopened.Manifest.Difficulties.All(d => d.PendingExport), "Pending sync survives manifest serialization");
        f.Ui.View.LoadWorkspace(reopened); f.Ui.PaintUntilReady();
        Check(f.Ui.View.IsDirty, "Reopened workspace keeps pending sync dirty");
        Check(f.Tags().All(t => t == "old tags"), "Reopening does not publish pending sync");
        f.Ui.View.SwitchDifficulty(1); f.Save();
        Check(f.Tags().All(t => t == "synchronized tags") && !f.Ui.View.IsDirty, "Saving any synchronized difficulty writes all targets and clears only completed dirty state");
        Console.WriteLine("PASS workspace-only save and reopen retain pending publication");
    }

    private static void FirstExport()
    {
        var f = new Fixture(confirmed: false); f.Sync();
        f.Ui.View.SaveProject(); f.Ui.Paint();
        Check(f.Ui.View.ExportVisible, "First full export still requires its normal choice");
        Check(f.Ui.View.SaveWorkspace(), "Host saves workspace before overwrite export");
        Check(f.Ui.View.IsDirty, "Saving workspace before export keeps sibling dirty state");
        var session = f.Ui.View.WorkspaceSession!;
        var project = f.Ui.View.CaptureProject();
        var plan = WorkspaceExport.Plan(session, project.Difficulties[0], f.Songs, true, project.Difficulties[0].Name, false);
        LibraryOperations.Export(session, project, plan);
        f.Ui.View.LibraryExportFinished(plan);
        SynchronizationUiTests.Wait(f.Ui);
        Check(f.Tags().All(t => t == "synchronized tags"), "Confirmed first export also finishes pending metadata sync");
        Check(!f.Ui.View.IsDirty, "Dirty clears after all synchronized files have been written");
        Check(f.Ui.View.WorkspaceSession!.Manifest.Difficulties.All(d => d.ExportConfirmed), "Project save confirms successful linked publications");
        Console.WriteLine("PASS first export publishes the sync group without losing sibling dirty state");
    }

    private static void UndoAndRedo()
    {
        var f = new Fixture(); f.Sync(); f.Save();
        f.Ui.Key('Z', ctrl: true);
        Check(f.Ui.View.IsDirty, "Undo of saved synchronization becomes dirty");
        f.Save();
        Check(f.Tags()[0] == "synchronized tags" && f.Tags().Skip(1).All(t => t == "old tags"), "Saving undone sync restores sibling osu metadata");
        f.Ui.Key('Y', ctrl: true); f.Save();
        Check(f.Tags().All(t => t == "synchronized tags"), "Saving redone sync republishes siblings");
        Console.WriteLine("PASS synchronization undo and redo publish their affected difficulties");
    }

    private static void ExternalConflict()
    {
        var f = new Fixture(); f.Sync();
        var entry = f.Ui.View.WorkspaceSession!.Manifest.Difficulties[1];
        File.WriteAllText(entry.Source!, File.ReadAllText(entry.Source!).Replace("Tags:old tags", "Tags:external tags"));
        f.Ui.View.SaveProject();
        var task = SaveTask(f.Ui);
        Check(task is not null && task.Wait(TimeSpan.FromSeconds(30)), "Conflicting save finishes preflight");
        Check(f.Tags()[0] == "old tags" && f.Tags()[1] == "external tags" && f.Tags()[2] == "old tags", "Conflicting target prevents any group publication");
        Check(f.Ui.View.IsDirty, "Failed save retains dirty state");
        SynchronizationUiTests.Wait(f.Ui);
        Check(f.Ui.View.SynchronizationVisible, "Conflicting metadata opens review");
        var row = f.Ui.Canvas.Texts.Single(t => t.Value.StartsWith("Tags · "));
        var heading = f.Ui.Canvas.Texts.Single(t => t.Value == "FA");
        f.Ui.Click(heading.X + 20, row.Y + 40);
        f.Ui.ClickText(L.Get("sync.applyChoices"));
        SynchronizationUiTests.Wait(f.Ui);
        Check(f.Tags().All(t => t == "synchronized tags"), "Accepting local Tags finishes the pending save group");
        Check(!f.Ui.View.IsDirty, "Resolved group clears dirty only after publication");
        Console.WriteLine("PASS external conflict review preserves files and resumes the pending group");
    }

    private static void FailedWriteRetry()
    {
        var f = new Fixture(); f.Sync();
        string locked = f.Ui.View.WorkspaceSession!.Manifest.Difficulties[1].Source!;
        using (var handle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            f.Ui.View.SaveProject();
            var task = SaveTask(f.Ui);
            Check(task is not null && task.Wait(TimeSpan.FromSeconds(30)), "Failed publication returns");
            f.Ui.Paint();
            Check(f.Ui.View.ErrorVisible && f.Ui.View.IsDirty, "Failed target retains an error and dirty state");
            Check(f.Tags()[0] == "synchronized tags" && f.Tags().Skip(1).All(t => t == "old tags"),
                "Partial failure acknowledges only the successfully written difficulty");
        }
        f.Ui.ClickText(L.Get("mac.ok")); f.Save();
        Check(f.Tags().All(t => t == "synchronized tags") && !f.Ui.View.IsDirty, "Retry saves unfinished difficulties and clears dirty state");
        Console.WriteLine("PASS failed write retains unfinished dirty state and retries successfully");
    }

    private static void LocalAndFaOnly()
    {
        var f = new Fixture();
        var session = f.Ui.View.WorkspaceSession!;
        session.Manifest.Difficulties[1].FaOnly = true;
        session.Project.Difficulties[1].FaOnly = true;
        var local = session.Manifest.Difficulties[2];
        string untouched = local.Source!;
        local.Source = local.ExportTarget = null; local.Sync = null;
        session.Project.Difficulties[2].Document.SourcePath = null;
        f.Ui.View.LoadWorkspace(session); f.Ui.PaintUntilReady(); f.Sync(); f.Save();
        Check(SongSetup.Get(OsuBeatmapReader.ReadFile(untouched), "Metadata", "Tags") == "old tags", "Unlinked source is untouched");
        Check(SongSetup.Get(f.Read(1), "Metadata", "Tags") == "old tags", "FA-only source is untouched");
        var saved = WorkspaceProject.Open(f.Directory);
        Check(saved.Project.Difficulties.All(d => SongSetup.Get(d.Document, "Metadata", "Tags") == "synchronized tags"), "FA-only and local targets save synchronized fields to workspace");
        Check(!f.Ui.View.IsDirty, "Local and FA-only synchronization clears dirty after workspace publication");
        Console.WriteLine("PASS local and FA-only synchronized fields save without exporting");
    }

    private sealed class Fixture
    {
        public Ui Ui { get; } = new(false);
        public string Directory { get; }
        public string Songs { get; }
        public Fixture(bool confirmed = true)
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/project-save", Guid.NewGuid().ToString("N")));
            Songs = Path.Combine(root, "Songs");
            string set = Path.Combine(Songs, "set"); System.IO.Directory.CreateDirectory(set);
            var project = new BeatmapProject { Name = "Tags fixture" };
            for (int i = 0; i < 3; i++)
            {
                string path = Path.Combine(set, $"{i}.osu");
                File.WriteAllText(path, $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Tags fixture\nArtist:Fixture\nCreator:Test\nVersion:Diff {i}\nTags:old tags\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
                project.Difficulties.Add(new() { Name = $"Diff {i}", Document = OsuBeatmapReader.ReadFile(path) });
            }
            Ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace");
            Ui.View.LibrarySettings.Songs = Songs;
            var session = WorkspaceProject.Create(Ui.View.LibrarySettings.Workspace, project, Songs);
            foreach (var entry in session.Manifest.Difficulties) entry.ExportConfirmed = confirmed;
            Directory = session.Directory;
            Ui.View.LoadWorkspace(session); Ui.PaintUntilReady();
            Ui.View.RequestSave = Ui.View.SaveProject;
        }
        public void Sync()
        {
            Ui.View.ShowSongDock(); Ui.Paint();
            var viewport = Ui.View.SongDockViewport;
            Ui.View.Wheel(viewport.X + 10, viewport.Y + 80, -12000, false, false); Ui.Paint();
            var tags = Ui.View.SongSetupFieldBounds["Tags"];
            Ui.Click(tags.X + 8, tags.Y + 12); Ui.Key('A', ctrl: true);
            Ui.View.PasteSongSetupText("synchronized tags", Ui.View.SongSetupInputSession); Ui.Paint();
            var label = L.Get("song.syncGeneral");
            var text = Ui.Canvas.Texts.First(t => t.Value.Length > 5 && label.StartsWith(t.Value, StringComparison.Ordinal));
            Ui.Click(text.X + 4, text.Y + 4);
            Check(Ui.View.CaptureProject().Difficulties.All(d => SongSetup.Get(d.Document, "Metadata", "Tags") == "synchronized tags"), "Sync button updates all in-memory Tags");
        }
        public MapDocument Read(int index) => OsuBeatmapReader.ReadFile(Ui.View.WorkspaceSession!.Manifest.Difficulties[index].Source!);
        public string[] Tags() => Enumerable.Range(0, 3).Select(i => SongSetup.Get(Read(i), "Metadata", "Tags")).ToArray();
        public void Save(Action? beforeCompletion = null) { Ui.Key('S', ctrl: true); Wait(beforeCompletion); }
        public void Wait(Action? beforeCompletion = null)
        {
            var task = SaveTask(Ui);
            Check(task is not null && task.Wait(TimeSpan.FromSeconds(30)), "Background save completes");
            beforeCompletion?.Invoke(); Ui.Paint();
            Check(!Ui.View.ErrorVisible && !Ui.View.SynchronizationVisible, "Save completes without errors or conflicts");
        }
    }
    private static Task? SaveTask(Ui ui) => (Task?)typeof(EditorView).GetField("difficultySaveTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View);
    private static EditorHistory History(Ui ui) => (EditorHistory)typeof(EditorView).GetProperty("history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
    private static void Edit(Ui ui, Action edit) { var history = History(ui); history.Begin("Local edit"); edit(); history.Commit(); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
