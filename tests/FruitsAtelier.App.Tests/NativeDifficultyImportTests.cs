using System.Diagnostics;
using System.Text;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class NativeDifficultyImportTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "atelier-native-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ui = new Ui(false);
        try
        {
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
            ui.View.LibrarySettings.Songs = "";
            var local = new MapDocument { Name = "Destination", IsDemo = false, DurationMs = 5000 };
            SongSetup.Set(local, "Metadata", "Version", "Guest");
            local.Fruits.Add(new() { TimeMs = 1000, X = 50 });
            var existing = BeatmapProject.FromDocuments([local]);
            var incoming = local.DeepClone();
            incoming.Name = "Other beatmap";
            incoming.AudioPath = Path.Combine(root, "foreign.ogg");
            incoming.SourcePath = Path.Combine(root, "foreign.osu");
            incoming.Fruits[0].X = 300;
            var curve = new CurveTrack();
            curve.Nodes.AddRange([new() { TimeMs = 2000, X = 100, HandleOut = new(200, 20) },
                new() { TimeMs = 3000, X = 200, HandleIn = new(-200, -20) }]);
            incoming.Tracks.Add(curve);
            var unavailable = new CollaborationReference(Guid.NewGuid(), Guid.NewGuid(), 0, 1500);
            incoming.CollaborationReferences = [unavailable];
            string raw = Path.Combine(root, "Guest.catchdiff");
            string backup = raw + WorkspaceHistoryFile.Extension;
            WorkspaceHistoryFile.Write(raw, Encoding.UTF8.GetBytes(ProjectSerializer.Serialize(incoming, raw)));
            incoming.Fruits[0].X = 400;
            File.WriteAllText(raw, ProjectSerializer.Serialize(incoming, raw));

            ui.View.LoadProject(existing);
            ui.View.CloseLibrary();
            ui.PaintUntilReady();
            int pickerRequests = 0;
            ui.View.RequestImportDifficulty = () => pickerRequests++;
            ui.ClickText(L.Get("ui.file"));
            ui.ClickText(L.Get("project.import"));
            Check(pickerRequests == 1, "the File menu opens the shared difficulty picker");
            Import(backup);
            ui.Key(27);
            Check(ui.View.Document.Fruits[0].X == 50 && ui.View.DifficultyCount == 1, "cancel preserves the target");

            Import(backup);
            ui.ClickText(L.Get("project.importDifficultyOverwrite"));
            Check(ui.View.Document.Fruits[0].X == 300 && ui.View.DifficultyCount == 1, "backup overwrites the matching difficulty");
            Check(ui.View.CaptureProject().Difficulties.Single().Id == existing.Difficulties.Single().Id,
                "overwrite preserves difficulty identity");
            Check(ui.View.Document.Tracks.Single().Nodes[0].HandleOut == curve.Nodes[0].HandleOut
                && ui.View.Document.CollaborationReferences!.Single() == unavailable with { DifficultyId = Guid.Empty },
                "native handles survive and unavailable collaboration references become unassigned");
            Check(ui.View.Document.AudioPath is null && ui.View.Document.SourcePath is null, "foreign resource paths are not imported");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.Fruits[0].X == 50, "overwrite is undoable");

            Import(raw);
            ui.ClickText(L.Get("project.importDifficultyCreate"));
            Check(ui.View.Document.Fruits[0].X == 400 && ui.View.CurrentDifficultyName == "Guest (2)"
                && ui.View.DifficultyCount == 2, "direct catchdiff import reads the selected file and creates a unique name");
            Check(ui.View.SaveWorkspace(), "two difficulties save as a workspace");
            var saved = WorkspaceProject.Open(ui.View.WorkspaceSession!.Directory);
            Check(saved.Project.Difficulties.Single(d => d.Name == "Guest (2)").Document.Fruits[0].X == 400
                && saved.Manifest.Difficulties.Single(d => d.Name == "Guest (2)").Source is null,
                "reopening retains the imported content without a borrowed source association");

            byte[] corrupt = File.ReadAllBytes(backup);
            corrupt[12] ^= 1;
            File.WriteAllBytes(backup, corrupt);
            Import(backup, expectError: true);
            Check(ui.View.DifficultyCount == 2 && !ui.View.IsDirty, "corrupt backup leaves the project untouched");
            ui.Key(27);
            SongSetup.Set(incoming, "Metadata", "Version", "Independent");
            File.WriteAllText(raw, ProjectSerializer.Serialize(incoming, raw));
            Import(raw, expectedName: "Independent");
            Check(ui.View.DifficultyCount == 3 && ui.View.CurrentDifficultyName == "Independent",
                "a unique name adds a difficulty without confirmation");

            void Import(string path, bool expectError = false, string? expectedName = null)
            {
                ui.View.ImportDifficultyFile(path);
                var timer = Stopwatch.StartNew();
                do { ui.Paint(); Thread.Sleep(5); }
                while (!ui.View.ErrorVisible
                    && (expectedName is null
                        ? !ui.Canvas.Texts.Any(t => t.Value == L.Get("project.importDifficultyOverwrite"))
                        : ui.View.CurrentDifficultyName != expectedName)
                    && timer.Elapsed.TotalSeconds < 10);
                Check(ui.View.ErrorVisible == expectError && timer.Elapsed.TotalSeconds < 10,
                    "the import completes with the expected result");
            }
        }
        finally
        {
            ui.View.StopFileMonitoring();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
