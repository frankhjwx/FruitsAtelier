using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryOpeningTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-open-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var project = BeatmapProject.FromDocuments([new MapDocument { Name = "Open fixture", IsDemo = false }, new MapDocument { Name = "Second", IsDemo = false }]);
            var session = WorkspaceProject.Create(root, project, "");
            var map = new LibraryMap("", "", "", "", "", "", "", "", "", "", "", "", session.Directory);
            var gate = typeof(WorkspaceProject).GetField("Gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var view = new EditorView(false);
            var before = view.Document;
            var canvas = new RecordingCanvas();
            int completed = 0;
            lock (gate)
            {
                var timer = Stopwatch.StartNew();
                view.BeginLibraryOpen(map, () => completed++);
                view.BeginLibraryOpen(map, () => completed += 100);
                view.Render(canvas, 980, 620);
                Check(timer.Elapsed < TimeSpan.FromSeconds(2), "UI returns while the workspace gate is held");
                Check(view.LibraryOpening && ReferenceEquals(before, view.Document), "pending load retains the current document");
                Check(view.SynchronizationNeedsRedraw, "pending load keeps the loading overlay animated");
                Check(!view.PrepareFileOperation(), "pending load blocks competing file operations");
                Check(canvas.Texts.Any(t => t.Value == L.Get("library.opening")), "pending load draws localized feedback");
                view.ShowLibrary();
                Check(completed == 0, "completion waits for the worker and UI publication");
            }
            Pump(view, canvas);
            Check(completed == 1 && view.WorkspaceSession?.Directory == session.Directory, "one result publishes and completes once");

            var second = session.Manifest.Difficulties[1];
            var selectedMap = map with { Path = Path.Combine(session.Directory, second.File) };
            view.BeginLibraryOpen(selectedMap, selectDifficulty: true);
            Pump(view, canvas);
            Check(view.CaptureProject().Difficulties[view.ActiveDifficultyIndex].Id == second.Id,
                "opening a difficulty selects its identity instead of the first tab");
            view.TryResumeLibraryProject(map with { Path = Path.Combine(session.Directory, session.Manifest.Difficulties[0].File) }, true);
            Check(view.ActiveDifficultyIndex == 0, "resuming a project selects the requested difficulty");

            string songs = Path.Combine(root, "Songs");
            Directory.CreateDirectory(songs);
            string sourceText = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Source\nArtist:Artist\nCreator:Mapper\nVersion:{0}\n[Difficulty]\nCircleSize:5\nOverallDifficulty:5\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,1,100,1,0\n[HitObjects]\n123,192,1000,1,0,0:0:0:0:\n";
            foreach (string name in new[] { "First", "Second" })
                File.WriteAllText(Path.Combine(songs, name + ".osu"), string.Format(sourceText, name));
            view = new EditorView(false);
            view.InitializeLibrary(false, new LibrarySettings { Workspace = Path.Combine(root, "Imported"), Songs = songs });
            view.BeginLibraryOpen(LibraryDatabase.ReadMetadata(Path.Combine(songs, "Second.osu"))!, selectDifficulty: true);
            Pump(view, canvas);
            Check(view.CurrentDifficultyName == "Second", "first source import selects the clicked source file");
            string thirdPath = Path.Combine(songs, "Third.osu");
            File.WriteAllText(thirdPath, string.Format(sourceText, "Third"));
            var thirdMap = LibraryDatabase.ReadMetadata(thirdPath)! with { ProjectPath = view.WorkspaceSession!.Directory };
            view.BeginLibraryOpen(thirdMap, selectDifficulty: true);
            Pump(view, canvas);
            var deadline = Stopwatch.StartNew();
            while (view.SynchronizationBusy && deadline.Elapsed < TimeSpan.FromSeconds(15))
            { view.Render(canvas, 980, 620); Thread.Sleep(10); }
            Check(view.CurrentDifficultyName == "Third", "new source difficulty is selected after synchronization imports it");

            view = new EditorView(false); before = view.Document;
            view.BeginLibraryOpen(map with { ProjectPath = Path.Combine(root, "missing") }, () => completed++);
            Pump(view, canvas);
            Check(view.ErrorVisible && ReferenceEquals(before, view.Document) && completed == 1, "failed open retains the document and skips completion");

            view = new EditorView(false);
            Task retired;
            lock (gate)
            {
                view.BeginLibraryOpen(map, () => completed++);
                retired = (Task)typeof(EditorView).GetField("libraryOpenTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                view.LoadProject(project);
            }
            Check(retired.Wait(TimeSpan.FromSeconds(10)), "retired worker finishes");
            view.Render(canvas, 980, 620);
            Check(!view.LibraryOpening && view.WorkspaceSession is null && completed == 1, "project replacement retires a pending open");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Pump(EditorView view, RecordingCanvas canvas)
    {
        var timer = Stopwatch.StartNew();
        while (view.LibraryOpening && timer.Elapsed < TimeSpan.FromSeconds(10))
        { view.Render(canvas, 980, 620); Thread.Sleep(10); }
        Check(!view.LibraryOpening, "open completes");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
}
