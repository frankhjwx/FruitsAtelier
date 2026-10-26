using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class FadTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/fad", Guid.NewGuid().ToString("N")));
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "workspace"), Songs = "" };
        Directory.CreateDirectory(settings.Workspace);
        string sender = Path.Combine(root, "sender"), receiver = Path.Combine(root, "receiver");
        Directory.CreateDirectory(sender); Directory.CreateDirectory(receiver);
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "quiet-tone.ogg");
        File.Copy(fixture, Path.Combine(sender, "audio.ogg")); File.Copy(fixture, Path.Combine(receiver, "local.ogg"));
        File.WriteAllText(Path.Combine(sender, "bg.jpg"), "background");
        File.Copy(fixture, Path.Combine(sender, "custom.ogg"));
        var original = new MapDocument { Name = "Song", IsDemo = false, DurationMs = 10000, SourcePath = Path.Combine(sender, "source.osu"), AudioPath = Path.Combine(sender, "audio.ogg") };
        SongSetup.Set(original, "General", "AudioFilename", "audio.ogg");
        SongSetup.Set(original, "Metadata", "Title", "Song"); SongSetup.Set(original, "Metadata", "Artist", "Artist");
        SongSetup.Set(original, "Metadata", "Version", "Guest"); SongSetup.Set(original, "Metadata", "BeatmapID", "0");
        original.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        original.Fruits.Add(new() { TimeMs = 1000, X = 120 });
        var curve = new CurveTrack { CompensateTinyDroplets = false };
        curve.Nodes.AddRange([new() { TimeMs = 3000, X = 120, HandleOut = new(250, 15) }, new() { TimeMs = 4000, X = 240, HandleIn = new(-250, -15) }]);
        original.Tracks.Add(curve);
        var incoming = new ProjectDifficulty { Name = "Guest", Document = original };
        string archive = Path.Combine(root, "guest.fad"), before = ProjectSerializer.Serialize(original);
        FadArchive.Export(incoming, archive, false, settings.Workspace);
        Check(ProjectSerializer.Serialize(original) == before, "export leaves live content untouched");
        using (var zip = ZipFile.OpenRead(archive))
        {
            using var reader = new StreamReader(zip.GetEntry("project.catchproj")!.Open());
            string portable = reader.ReadToEnd();
            Check(!portable.Contains(sender) && !portable.Contains(receiver), "package has no sender or receiver absolute paths");
        }
        Directory.Move(sender, sender + "-unavailable");
        var local = original.DeepClone(); local.SourcePath = Path.Combine(receiver, "local.osu"); local.AudioPath = Path.Combine(receiver, "local.ogg");
        SongSetup.Set(local, "General", "AudioFilename", "local.ogg");
        SongSetup.Set(local, "Metadata", "Version", "Base"); local.Tracks.Clear(); local.Fruits[0].X = 50;
        File.WriteAllText(local.SourcePath!, OsuBeatmapWriter.Serialize(local, false).Text);
        var host = new ProjectDifficulty { Name = "Base", Document = local };
        var project = new BeatmapProject { Name = "Song", Difficulties = [host] };
        using (var package = FadArchive.Prepare(archive, settings))
        {
            var plan = FadArchive.PlanImport(package, project);
            Check(plan.Difficulty.Id == incoming.Id, "new difficulty keeps identity");
            Check(plan.Difficulty.Document.AudioPath == local.AudioPath && plan.Difficulty.Document.SourcePath != local.SourcePath,
                "new difficulty uses local audio without inheriting another difficulty's osu association");
            plan.Resources.Apply();
            Check(File.Exists(Path.Combine(receiver, "custom.ogg")), "new resource is copied");
            plan.Resources.Restore();
            Check(!File.Exists(Path.Combine(receiver, "custom.ogg")), "resource plan can roll back");
            var different = local.DeepClone(); SongSetup.Set(different, "Metadata", "Title", "Another song");
            Throws(() => FadArchive.PlanImport(package, BeatmapProject.FromDocuments([different])), "unrelated project is rejected");
            File.WriteAllText(Path.Combine(receiver, "custom.ogg"), "conflict");
            Throws(() => FadArchive.PlanImport(package, project), "conflicting resource does not overwrite local file");
            Check(File.ReadAllText(Path.Combine(receiver, "custom.ogg")) == "conflict", "conflicting resource remains unchanged");
            File.Delete(Path.Combine(receiver, "custom.ogg"));
        }
        Throws(() => FazArchive.Prepare(archive, settings), "FAD is not accepted as FAZ");

        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.View.LibrarySettings.Workspace = Path.Combine(settings.Workspace, locale); ui.View.LibrarySettings.Songs = "";
                ui.View.LoadProject(project); ui.PaintUntilReady();
                ui.View.BeginFadImport(archive); Settle(ui); ui.PaintUntilReady();
                Check(!ui.View.ErrorVisible && ui.View.DifficultyCount == 2 && ui.View.CurrentDifficultyName == "Guest", "unknown difficulty is added");
                Check(ui.View.Document.Tracks[0].Nodes[0].HandleOut == curve.Nodes[0].HandleOut, "native curve handles survive import");
                var guest = ui.View.CaptureProject().Difficulties.Single(d => d.Id == incoming.Id);
                guest.Document.Fruits[0].X = 400; SongSetup.Set(guest.Document, "Metadata", "Creator", "Guest mapper");
                string update = Path.Combine(root, "update-" + locale + ".fad");
                FadArchive.Export(guest, update, false, settings.Workspace);
                ui.View.BeginFadImport(update); Settle(ui);
                Check(ui.View.SynchronizationVisible && ui.View.Document.Fruits[0].X == 120, "existing difficulty enters review before changing content");
                ui.ClickText(L.Get("mac.cancel"));
                Check(!ui.View.SynchronizationVisible && ui.View.Document.Fruits[0].X == 120, "cancel leaves content untouched");
                ui.View.BeginFadImport(update); Settle(ui); ui.ClickText(L.Get("fad.allExternal")); Settle(ui);
                Check(!ui.View.ErrorVisible && ui.View.Document.Fruits[0].X == 400 && ui.View.DifficultyCount == 2, "review applies incoming native difficulty");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits[0].X == 120, "update is undoable");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.Fruits[0].X == 400, "update can be redone");
                ui.View.BeginFadImport(archive); Settle(ui);
                ui.ClickText(L.Get("sync.allLocal")); Settle(ui);
                Check(ui.View.Document.Fruits[0].X == 400, "keep local preserves edited content");
                Check(ui.View.SaveWorkspace(), "imported difficulty can be saved");
                var saved = WorkspaceProject.Open(ui.View.WorkspaceSession!.Directory);
                Check(saved.Manifest.Difficulties.Single(d => d.Id == incoming.Id).Source is null,
                    "added difficulty has no borrowed Songs association after saving");
                Check(WorkspaceProject.MissingResources(saved.Project).Count == 0, "saved imported resources resolve");
                ui.View.StopFileMonitoring(); ui.View.ShowLibrary(); ui.Paint();
                string state = ProjectSerializer.Serialize(ui.View.CaptureProject());
                ui.View.BeginFadImport(update); ui.Paint();
                Check(ui.View.ErrorVisible && ui.Canvas.Texts.Any(t => t.Value.Contains(locale == "en" ? "corresponding project" : "对应谱面工程")), "library import shows project-required error");
                Check(ProjectSerializer.Serialize(ui.View.CaptureProject()) == state && ui.View.LibraryVisible, "library rejection preserves project and page");
                ui.Key(27); ui.View.DropLibraryFiles([archive]); ui.Paint();
                Check(ui.View.ErrorVisible, "library drag and drop shows the same error"); ui.Key(27);
                ui.View.LoadProject(project); ui.View.CloseLibrary(); ui.PaintUntilReady();
                int? exportedIndex = null; ui.View.RequestFadExport = index => exportedIndex = index;
                ui.ClickText(L.Get("ui.file"));
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("faz.export"))
                    && !ui.Canvas.Texts.Any(t => t.Value == L.Get("faz.exportComplete") || t.Value == L.Get("faz.exportProject")), "File menu has one FAZ export entry");
                ui.ClickText(L.Get("fad.export")); Check(exportedIndex == 0, "File exports the current difficulty");
                var tab = ui.Canvas.Texts.First(t => t.Value == "Base" && t.Y < 80);
                ui.View.PointerDown(tab.X + 3, tab.Y + 3, 2, false, false); ui.Paint();
                exportedIndex = null; ui.ClickText(L.Get("fad.export"));
                Check(exportedIndex == 0, "difficulty tab context menu exports its difficulty");
                bool? mode = null; ui.View.RequestFazExport = complete => mode = complete;
                ui.View.ShowFazExportChoice(); ui.Paint(); ui.ClickText(L.Get("faz.exportComplete"));
                Check(mode == true && !ui.View.DiscardConfirmationVisible, "complete export choice invokes save action");
                ui.View.ShowFazExportChoice(); ui.Paint(); ui.ClickText(L.Get("faz.exportProject"));
                Check(mode == false && !ui.View.DiscardConfirmationVisible, "native export choice invokes save action");
                ui.View.ShowFazExportChoice(); ui.Key(27); Check(!ui.View.DiscardConfirmationVisible, "export choice cancels with Escape");
                ui.View.StopFileMonitoring();
            }
        }
        finally { L.SetLanguage(language); }
        using (var package = FadArchive.Prepare(archive, settings))
        {
            var renamed = new ProjectDifficulty { Id = Guid.NewGuid(), Name = "Guest", Document = local.DeepClone() };
            SongSetup.Set(renamed.Document, "Metadata", "Version", "Guest");
            var plan = FadArchive.PlanImport(package, new BeatmapProject { Name = "Song", Difficulties = [renamed] });
            Check(plan.Difficulty.Id == renamed.Id && plan.Difficulty.Document.SourcePath == local.SourcePath, "difficulty-name fallback retains receiver identity and association");
            var merge = WorkspaceSynchronization.CompareNativeDifficulty(local, plan.Difficulty.Document, archive, false);
            var choices = merge.Conflicts.ToDictionary(c => c.Key, c => !c.Key.StartsWith('$'));
            var mixed = WorkspaceSynchronization.Resolve(merge, choices);
            Check(mixed.Fruits[0].X == 50 && mixed.Tracks.Count == 0 && OsuBeatmapReader.Setting(mixed, "Metadata", "Version") == "Guest", "metadata and native content choices are independent");
            choices = merge.Conflicts.ToDictionary(c => c.Key, c => c.Key.StartsWith('$'));
            mixed = WorkspaceSynchronization.Resolve(merge, choices);
            Check(mixed.Tracks[0].Nodes[0].HandleOut == curve.Nodes[0].HandleOut && mixed.Fruits[0].X == 120
                && OsuBeatmapReader.Setting(mixed, "Metadata", "Version") == "Base", "native object choices retain editable handles independently of metadata");
        }
        Console.WriteLine("PASS FAD portable package, project matching, new difficulty, merge review, native curves, undo and library rejection");
    }

    private static void Settle(Ui ui)
    {
        var field = typeof(EditorView).GetField("fazTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var timer = Stopwatch.StartNew();
        do { ui.Paint(); Thread.Sleep(5); } while (field.GetValue(ui.View) is not null && timer.Elapsed.TotalSeconds < 30);
        ui.Paint(); Check(field.GetValue(ui.View) is null, "background package operation completes");
        if (ui.View.ErrorVisible) throw new Exception(string.Join(" | ", ui.Canvas.Texts.Select(t => t.Value)));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws(Action action, string message)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException) { return; }
        throw new Exception(message);
    }
}
