using System.Diagnostics;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class CollaborationMappingUiTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var a = Difficulty("A", 250); var b = Difficulty("B", 1250);
                var c = Difficulty("C", 2200); var d = Difficulty("D", 2400);
                c.Document.CollaborationReferences = [new(Guid.NewGuid(), a.Id, 0, 1000), new(Guid.NewGuid(), b.Id, 1000, 2000)];
                d.Document.CollaborationReferences = [new(Guid.NewGuid(), a.Id, 0, 1000)];
                var project = new BeatmapProject { Name = "Collaboration", Difficulties = [a, b, c, d] };
                var ui = new Ui(); ui.View.LoadProject(project); ui.Paint();
                ui.ClickText(L.Get("dock.layout"));
                ui.ClickText(L.Get("collab.title"));
                Check(ui.View.CollaborationPanelVisible, "Layout menu opens the Collab Mapping panel tab");
                foreach (var size in new[] { (980f, 620f), (1440f, 900f) })
                {
                    ui.Resize(size.Item1, size.Item2);
                    Check(ui.Canvas.Texts.Any(t => t.Value == "C") && ui.Canvas.Texts.Any(t => t.Value == "D"), "Every reverse target is visible");
                    var before = ui.View.Document.DeepClone();
                    ui.Key('F'); ui.Key(46); ui.View.Wheel(400, 300, 120, false); ui.Paint();
                    Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Viewing references and ordinary canvas keys are content neutral");
                }
                Check(ui.View.SwitchDifficulty(2), "Switch to merge target"); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == "A") && ui.Canvas.Texts.Any(t => t.Value == "B"), "Merge target lists its sources");
                var input = ui.View.CollaborationStartBounds; float x = input.X + 10;
                string copied = "", clipboard = "00:00:050 (1,2) - ";
                ui.View.RequestCopyText = text => copied = text;
                ui.View.RequestPasteField = () => ui.View.PasteFieldText(clipboard, ui.View.ActiveTextInputField);
                ui.Click(x, input.Y + 10); ui.Key('C', ctrl: true);
                Check(copied == "00:00:000", "Timestamp copy uses osu format");
                ui.Key('V', ctrl: true); ui.Key(13);
                Check(ui.View.Document.CollaborationReferences![0].StartMs == 50 && ui.View.IsDirty, "Range input commits and becomes dirty");
                ui.Click(x, input.Y + 10); ui.Key('A', ctrl: true); ui.Type("50.5"); ui.Key(13);
                Check(ui.View.Document.CollaborationReferences![0].StartMs == 50 && ui.View.IsEditingText, "Fractional milliseconds are rejected");
                ui.Key(27);
                ui.Key('Z', ctrl: true); Check(ui.View.Document.CollaborationReferences![0].StartMs == 0, "Reference edit undo");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.CollaborationReferences![0].StartMs == 50, "Reference edit redo");
                ui.View.UpdateTransport(1234, 5000, true, false, false, null, null);
                double playhead = ui.View.PlayheadMs, viewStart = ui.View.ViewStartMs;
                ui.View.SynchronizeCollaboration();
                Check(ui.View.CollaborationBusy, "Synchronization is submitted to a worker");
                var watch = Stopwatch.StartNew();
                while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
                Check(!ui.View.CollaborationBusy && ui.View.Document.Fruits.Count == 3, "Sources merged and outside target object retained");
                Check(ui.View.PlayheadMs == playhead && ui.View.ViewStartMs == viewStart && ui.View.AudioReady, "Sync preserves transport and viewport");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits.Count == 1, "Synchronization undo");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.Fruits.Count == 3, "Synchronization redo");
                var loaded = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(ui.View.CaptureProject()));
                ui.View.LoadProject(loaded); ui.View.SwitchDifficulty(2); ui.View.ShowCollaborationPanel(); ui.Paint();
                Check(ui.View.Document.CollaborationReferences!.Count == 2, "Restart retains references");
                ui.ClickText(L.Get("collab.add")); Check(ui.View.Document.CollaborationReferences!.Count == 3, "Multiple references can be added");
                Check(ui.View.Document.CollaborationReferences![2].DifficultyId == Guid.Empty, "New references start unassigned");
                Check(ui.View.SwitchDifficulty(0), "Source navigation"); ui.Paint();
                Check(ui.Canvas.Texts.Count(t => t.Value == "C") >= 1 && ui.Canvas.Texts.Count(t => t.Value == "D") >= 1, "Reverse graph updates after restart");
                Console.WriteLine("PASS Collab Mapping UI " + locale);
                BookmarkSections();
            }
        }
        finally { L.SetLanguage(language); }
        foreach (var (text, expected) in new[] { ("84771", 84771d), ("01:24:771", 84771d), ("00:00:000", 0d), ("35791:23:647", 2147483647d) })
            Check(FruitsAtelier.App.Editor.EditorView.TryParseCollaborationTime(text, out double result) && result == expected, "Timestamp parser " + text);
        foreach (string text in new[] { "84771.521739", "1e3", "-1", "00:60:000", "00:00:1000", "2147483648" })
            Check(!FruitsAtelier.App.Editor.EditorView.TryParseCollaborationTime(text, out _), "Invalid timestamp " + text);
        ResourceUndo();
    }

    private static void BookmarkSections()
    {
        var source = Difficulty("Source", 250); var target = Difficulty("Target", 1250);
        SongSetup.Set(target.Document, "Editor", "Bookmarks", "500,1500");
        target.Document.CollaborationReferences = [new(Guid.NewGuid(), source.Id, 0, 1000)];
        var ui = new Ui(); ui.View.LoadProject(new() { Name = "sections", Difficulties = [source, target] });
        ui.View.SwitchDifficulty(1); ui.View.ShowCollaborationPanel(); ui.Resize(1440, 900);
        ui.View.UpdateTransport(0, 6000, true, false, false, null, null); ui.Paint();
        var before = ui.View.Document.DeepClone();
        ui.ClickText(L.Get("collab.fromBookmarks"));
        var sections = ui.View.Document.CollaborationReferences!;
        Check(sections.Count == 3 && sections.All(r => r.DifficultyId == Guid.Empty), "Bookmark generation replaces references with empty sources");
        Check(sections[0].StartMs == 0 && sections[0].EndMs == 500 && sections[1].EndMs == 1500 && sections[2].EndMs == 6000,
            "Generated sections cover the whole audio timeline");
        var generated = ui.View.Document.DeepClone();
        ui.View.SynchronizeCollaboration();
        Check(!ui.View.CollaborationBusy && ui.View.Document.ContentEquals(generated), "Empty sources skip synchronization");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "Generation undo restores assigned references");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(generated), "Generation redo restores empty sections");
        ui.Paint();
        void OpenSource()
        {
            var input = ui.View.CollaborationStartBounds;
            ui.Click(ui.View.CollaborationPanelViewport.X + 10, input.Y + 10);
        }
        OpenSource(); ui.ClickObjectAction("Source");
        Check(ui.View.Document.CollaborationReferences![0].DifficultyId == source.Id, "User assigns a generated section source");
        OpenSource(); ui.ClickObjectAction(L.Get("collab.unassigned"));
        Check(ui.View.Document.CollaborationReferences![0].DifficultyId == Guid.Empty, "User clears the source without deleting the section");
        var loaded = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(ui.View.CaptureProject()));
        Check(loaded.Difficulties[1].Document.CollaborationReferences!.All(r => r.DifficultyId == Guid.Empty), "Empty UI sources survive restart");
        var first = ui.View.CollaborationStartBounds;
        ui.Click(first.X + 10, first.Y + 10); ui.Key('A', ctrl: true); ui.Type("501"); ui.Key(13);
        Check(ui.View.Document.CollaborationReferences![0].StartMs == 0, "Empty section rejects an invalid range"); ui.Key(27);
        var second = ui.View.CollaborationStartBounds;
        ui.Click(second.X + 10, second.Y + 50); ui.Key('A', ctrl: true); ui.Type("499"); ui.Key(13);
        Check(ui.View.Document.CollaborationReferences![1].StartMs == 500, "Empty sections reject overlap in the UI"); ui.Key(27);
    }
    private static void ResourceUndo()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/collaboration", "ui-resources-" + Guid.NewGuid().ToString("N")));
        string sourceRoot = Path.Combine(root, "source"), targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot); Directory.CreateDirectory(targetRoot);
        File.WriteAllBytes(Path.Combine(sourceRoot, "tap.wav"), [1, 2, 3]);
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read(
            "osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n180,192,500,1,0,1:1:0:65:tap.wav\n", Path.Combine(sourceRoot, "source.osu")) };
        var target = Difficulty("target", 2000); target.Document.SourcePath = Path.Combine(targetRoot, "target.osu");
        target.Document.CollaborationReferences = [new(Guid.NewGuid(), source.Id, 0, 1000)];
        var ui = new Ui(); ui.View.LoadProject(new() { Name = "resources", Difficulties = [source, target] });
        ui.View.SwitchDifficulty(1); ui.View.ShowCollaborationPanel(); ui.Paint(); ui.View.SynchronizeCollaboration();
        var watch = Stopwatch.StartNew();
        while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
        Check(ui.View.Document.Fruits.Count == 2, "Cross-folder sync commits content");
        string copied = Directory.EnumerateFiles(targetRoot, "tap.wav", SearchOption.AllDirectories).Single();
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 1 && !File.Exists(copied), "Resource copies follow synchronization undo");
        ui.Key('Y', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 2 && File.Exists(copied), "Resource copies follow synchronization redo");
    }
    private static ProjectDifficulty Difficulty(string name, double time)
    {
        var document = new MapDocument { Name = name, DurationMs = 5000, IsDemo = false };
        document.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        document.Fruits.Add(new() { TimeMs = time, X = 180 });
        SongSetup.Set(document, "Metadata", "Version", name);
        return new() { Name = name, Document = document };
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
