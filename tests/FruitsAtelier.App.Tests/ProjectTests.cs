using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using System.IO.Compression;

internal static class ProjectTests
{
    public static void Run()
    {
        var a = new MapDocument { Name = "Song", IsDemo = false };
        a.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
        var b = a.DeepClone(); b.Fruits[0].X = 300;
        var view = new EditorView();
        view.LoadProject(BeatmapProject.FromDocuments([a, b]));
        Check(view.DifficultyCount == 2 && !view.IsDirty, "load");
        view.ChangeAudioPath("a.ogg");
        Check(view.SwitchDifficulty(1) && view.Document.Fruits[0].X == 300 && view.IsDirty, "inactive dirty");
        view.ChangeAudioPath("b.ogg");
        view.SwitchDifficulty(0);
        Check(view.Document.AudioPath == "a.ogg", "retained edits");
        view.KeyDown(90, true, false);
        Check(view.Document.AudioPath is null && view.IsDirty, "isolated undo");
        view.SwitchDifficulty(1);
        Check(view.Document.AudioPath == "b.ogg", "other undo unaffected");
        view.MarkSaved();
        Check(!view.IsDirty, "all baselines");
        view.KeyDown(90, true, false);
        Check(view.IsDirty && view.Document.AudioPath is null, "undo after save");
        view.KeyDown(89, true, false);
        Check(!view.IsDirty, "redo baseline");
        Check(view.AddDifficulty() && view.DifficultyCount == 3 && view.IsDirty, "add");
        Check(view.Document.Fruits.Count == 0 && view.Document.AudioPath == "b.ogg", "blank inherits resources");
        var project = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(view.CaptureProject()));
        view.LoadProject(project);
        Check(view.DifficultyCount == 3 && !view.IsDirty, "round trip");
        view.SwitchDifficulty(1);
        Check(view.Document.Fruits.Count == 1, "all diffs saved");
        view.NewProject();
        Check(view.DifficultyCount == 1 && view.Document.Fruits.Count == 0 && !view.IsDirty && !view.Document.IsDemo, "new project starts clean");
        Check(view.PrepareFileOperation() && !view.IsDirty, "untouched new project can be replaced without an unsaved prompt");
        view.ChangeAudioPath("new-song.ogg");
        Check(view.IsDirty, "new project resource edits require an unsaved prompt");
        view.KeyDown(90, true, false);
        Check(!view.IsDirty, "undo to the blank baseline clears unsaved changes");
        view.KeyDown(89, true, false);
        Check(view.IsDirty, "redo restores unsaved changes");
        view.NewProject();
        Check(view.AddDifficulty() && view.IsDirty, "adding a difficulty to a new project requires an unsaved prompt");
        view.NewProject();
        Check(view.AddDifficulty(a) && view.IsDirty, "importing a difficulty into a new project requires an unsaved prompt");
        Archive();
        view.LoadDocument(a);
        Check(view.DifficultyCount == 1 && !view.IsDirty, "legacy entry point resets project");
    }
    private static void Archive()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        string folder = Path.Combine(root!.FullName, "artifacts", "tests", "multi-osz-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string archive = Path.Combine(folder, "song.osz");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                for (int index = 0; index < 3; index++)
                {
                    using var writer = new StreamWriter(zip.CreateEntry($"diff{index}.osu").Open());
                    writer.Write($"osu file format v14\n[General]\nMode:{(index == 2 ? 0 : 2)}\n[Metadata]\nTitle:Song\nVersion:Diff{index}\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:");
                }
            }
            var project = BeatmapArchive.OpenProject(archive, Path.Combine(folder, "cache"));
            Check(project.Difficulties.Count == 2 && project.Difficulties.Select(d => d.Name).Distinct().Count() == 2, "all Catch difficulties imported");
            var view = new EditorView();
            view.LoadProject(project);
            var canvas = new RecordingCanvas();
            view.Render(canvas, 980, 620);
            var second = canvas.Texts.Single(t => t.Value == project.Difficulties[1].Name);
            view.PointerDown(second.X + 2, second.Y + 2, 0, false, false);
            Check(view.ActiveDifficultyIndex == 1 && !view.IsDirty, "tab switches without editing");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Project: " + name);
    }
}
