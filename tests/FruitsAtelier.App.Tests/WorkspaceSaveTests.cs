using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using L = FruitsAtelier.Localization.Strings;

static class WorkspaceSaveTests
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
                ExportLocalDifficulty(size.Item1, size.Item2);
                SaveConvertedSlidersAsNewDifficulty(size.Item1, size.Item2);
                SaveConvertedSlidersAsNewDifficulty(size.Item1, size.Item2, initialConversion: true);
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/workspace-save", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace");
                ui.View.LibrarySettings.Songs = songs;
                ui.View.NewProject();
                int exports = 0;
                ui.View.RequestSave = ui.View.SaveCurrentDifficulty;
                ui.View.RequestWorkspaceExport = (_, _) => exports++;
                ui.Key('S', ctrl: true);
                SynchronizationUiTests.Wait(ui);
                Check(ui.View.DiscardConfirmationVisible && !ui.View.ExportVisible && !ui.View.IsDirty, "Workspace saves before the optional export prompt");
                string directory = ui.View.WorkspaceSession!.Directory;
                Check(WorkspaceProject.Open(directory).Project.Difficulties[0].Document.ContentEquals(ui.View.Document), "Workspace content is already persisted");
                var before = ui.View.Document.DeepClone();
                ui.Key(46); ui.Key('S', ctrl: true); ui.Key(116);
                Check(ui.View.Document.ContentEquals(before) && !ui.View.IsTestplaying && exports == 0, "Prompt blocks editor commands and repeated saves");
                ui.ClickText(L.Get("library.workspaceOnly"));
                Check(!ui.View.DiscardConfirmationVisible && !ui.View.ExportVisible && Directory.GetFiles(songs, "*", SearchOption.AllDirectories).Length == 0,
                    "Keeping the workspace completes save without touching Songs");
                ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(ui); ui.Key(27);
                Check(!ui.View.DiscardConfirmationVisible && !ui.View.IsDirty, "Escape keeps the completed save");
                ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(ui); ui.ClickText(L.Get("library.exportToSongs"));
                Check(!ui.View.ExportVisible && !ui.View.DiscardConfirmationVisible && exports == 1, "First Songs export proceeds directly with the current difficulty name");
                Check(!ui.View.IsDirty && ui.View.WorkspaceSession.Directory == directory, "First export retains the saved workspace");
                exports = 0;
                var entry = ui.View.WorkspaceSession.Manifest.Difficulties[0];
                entry.ExportTarget = Path.Combine(songs, "deleted.osu"); entry.ExportHash = "old";
                ui.Key('S', ctrl: true);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (ui.View.SynchronizationBusy && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(10); }
                Check(ui.View.SynchronizationVisible && exports == 0, "Missing linked exports require resolution before export");
                ui.Key(27);
                ui.View.NewProject(); ui.View.CloseLibrary();
                ui.View.LibrarySettings.Songs = "";
                ui.Key('S', ctrl: true);
                SynchronizationUiTests.Wait(ui);
                Check(!ui.View.DiscardConfirmationVisible && !ui.View.ExportVisible, "Unconfigured Songs saves to workspace directly");
                ui.Key('S', ctrl: true, shift: true);
                Check(ui.View.WorkspaceSession is not null && exports == 0, "Local save does not export");
                ui.ClickText(L.Get("ui.file"));
                Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("Shift + S")), "File menu exposes supported save actions");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void ExportLocalDifficulty(int width, int height)
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/local-songs-export", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
        var ui = new Ui(false); ui.Resize(width, height);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.NewProject();
        var initial = ui.View.Document;
        Check(!ui.View.IsDirty && SongSetup.Get(initial, "General", "Mode") == "2"
            && SongSetup.Get(initial, "Metadata", "Title") == initial.Name
            && SongSetup.Get(initial, "Metadata", "TitleUnicode") == initial.Name
            && SongSetup.Get(initial, "Metadata", "Version") == ui.View.CurrentDifficultyName
            && SongSetup.Get(initial, "General", "PreviewTime") == "-1"
            && SongSetup.Get(initial, "General", "Countdown") == "1"
            && SongSetup.Get(initial, "Difficulty", "HPDrainRate") == "5"
            && SongSetup.Get(initial, "Difficulty", "OverallDifficulty") == "5"
            && initial.TimingPoints.Single().TimeMs == 0 && initial.TimingPoints.Single().BeatLengthMs == 500,
            "New projects start clean with Catch metadata, song settings and an explicit red timing point");
        Check(!WorkspaceSynchronization.HasFieldDifferences(initial, OsuBeatmapWriter.Serialize(initial, false).ReadBack),
            "Initialized authoring fields agree with their exported defaults");
        ui.View.RequestSave = ui.View.SaveCurrentDifficulty;
        ui.View.RequestWorkspaceExport = (overwrite, name) =>
        {
            if (overwrite) ui.View.BeginWorkspaceSave(saved => { if (saved) Export(); });
            else Export();
            void Export()
            {
                var project = ui.View.CaptureProject();
                var plan = WorkspaceExport.Plan(ui.View.WorkspaceSession!, project.Difficulties[ui.View.ActiveDifficultyIndex], songs, overwrite, name, false);
                LibraryOperations.Export(ui.View.WorkspaceSession!, project, plan); ui.View.LibraryExportFinished(plan);
            }
        };
        ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(ui);
        var session = ui.View.WorkspaceSession!;
        Guid id = session.Manifest.Difficulties.Single().Id;
        string name = ui.View.CurrentDifficultyName;
        var localFiles = Directory.GetFiles(session.Directory, "*.catchdiff").Select(Path.GetFileName).Order().ToArray();
        ui.ClickText(L.Get("library.exportToSongs")); SynchronizationUiTests.Wait(ui);
        Check(ui.View.CaptureProject().Difficulties.Count == 1 && ui.View.CaptureProject().Difficulties.Single().Id == id
            && ui.View.CurrentDifficultyName == name && ui.View.CurrentDifficultyHasExport, "First Songs export binds the existing local difficulty");
        Check(localFiles.SequenceEqual(Directory.GetFiles(session.Directory, "*.catchdiff").Select(Path.GetFileName).Order()),
            "First Songs export retains the existing catchdiff files without a duplicate");
        var reopened = WorkspaceProject.Open(session.Directory);
        string target = reopened.Manifest.Difficulties.Single().ExportTarget!;
        Check(reopened.Project.Difficulties.Single().Id == id && File.Exists(target), "Songs association survives restart");
        ui.View.LoadWorkspace(reopened);
        var history = (EditorHistory)ui.View.GetType().GetProperty("history", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
        history.Begin("add local fruit"); ui.View.Document.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 }); history.Commit(); ui.Paint();
        ui.View.ShowWorkspaceExport(); ui.Paint();
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("library.newDifficultyName")), "Linked export defaults to Update");
        ui.ClickText(L.Get("library.exportUpdate", name)); SynchronizationUiTests.Wait(ui);
        Check(!ui.View.SynchronizationVisible, "Update completes synchronization without a default metadata conflict");
        Check(!ui.View.ExportVisible && !ui.View.IsDirty && OsuBeatmapReader.ReadFile(target).Fruits.Single().X == 100,
            "Update completes and writes current content into the linked osu file");
        history.Begin("move local fruit"); ui.View.Document.Fruits.Single().X = 200; history.Commit(); ui.Paint();
        ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(ui);
        Check(!ui.View.DiscardConfirmationVisible && !ui.View.ExportVisible && !ui.View.IsDirty
            && OsuBeatmapReader.ReadFile(target).Fruits.Single().X == 200, "Subsequent Save updates Songs without export choices");
        Check(WorkspaceProject.Open(session.Directory).Project.Difficulties.Single().Id == id
            && Directory.GetFiles(songs, "*.osu", SearchOption.AllDirectories).Length == 1
            && localFiles.SequenceEqual(Directory.GetFiles(session.Directory, "*.catchdiff").Select(Path.GetFileName).Order()),
            "Repeated exports retain one osu file and one local difficulty");
        ui.View.StopFileMonitoring();
    }

    private static void SaveConvertedSlidersAsNewDifficulty(int width, int height, bool initialConversion = false)
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/save-new-difficulty", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "18sai"); Directory.CreateDirectory(set);
        string source = Path.Combine(set, "Rain.osu");
        const string originalOsu = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:18sai\nArtist:Goose house\nCreator:Mapper\nVersion:Rain\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,100,1000,2,0,L|200:100,1,100\n";
        File.WriteAllText(source, originalOsu);
        var ui = new Ui(false); ui.Resize(width, height);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.LoadWorkspace(LibraryOperations.ImportPath(source, ui.View.LibrarySettings)); ui.View.AnswerSliderImport(initialConversion); ui.Paint();
        var session = ui.View.WorkspaceSession!;
        Guid originalId = session.Manifest.Difficulties.Single().Id;
        string catchdiff = Path.Combine(session.Directory, session.Manifest.Difficulties.Single().File);
        string originalCatchdiff = File.ReadAllText(catchdiff);
        if (!initialConversion) { ui.View.ConvertAllSliders(); ui.View.AnswerSliderImport(true); }
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (ui.View.SliderConversionBusy && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(10); }
        Check(ui.View.Document.Tracks.Count == 1 && ui.View.Document.ImportedSliders.Count == 0 && ui.View.IsDirty, "Slider conversion is an unsaved edit");
        ui.View.RequestSave = ui.View.SaveCurrentDifficulty;
        ui.View.RequestWorkspaceExport = (overwrite, name) =>
        {
            if (overwrite) Check(ui.View.SaveWorkspace(), "Overwrite saves the workspace before explicit export");
            var project = ui.View.CaptureProject();
            var plan = WorkspaceExport.Plan(ui.View.WorkspaceSession!, project.Difficulties[ui.View.ActiveDifficultyIndex], songs, overwrite, name, false);
            LibraryOperations.Export(ui.View.WorkspaceSession!, project, plan); ui.View.LibraryExportFinished(plan);
        };
        if (!initialConversion)
        {
            ui.View.RefreshSynchronization(quiet: true); SynchronizationUiTests.Wait(ui);
            Check(File.ReadAllText(source) == originalOsu && File.ReadAllText(catchdiff) == originalCatchdiff && ui.View.IsDirty,
                "Background synchronization preserves unconfirmed source and authoring files");
            var importedEntry = ui.View.WorkspaceSession!.Manifest.Difficulties.Single();
            importedEntry.ExportTarget = source; importedEntry.ExportHash = WorkspaceProject.Hash(source);
            ui.View.RefreshSynchronization(quiet: true); SynchronizationUiTests.Wait(ui);
            Check(!ui.View.CurrentDifficultyHasExport && File.ReadAllText(source) == originalOsu && ui.View.IsDirty,
                "An unconfirmed legacy export association cannot authorize background publication");
        }
        ui.Key('S', ctrl: true);
        Check(ui.View.ExportVisible && !ui.View.SynchronizationBusy,
            "Unconfirmed imported difficulty chooses its Songs target before starting a save or synchronization");
        SynchronizationUiTests.Wait(ui);
        Check(ui.View.ExportVisible && ui.View.IsDirty && File.ReadAllText(source) == originalOsu && File.ReadAllText(catchdiff) == originalCatchdiff,
            "First save opens export choices before writing the source difficulty");
        ui.Key(27);
        Check(ui.View.IsDirty && ui.View.Document.Tracks.Count == 1 && File.ReadAllText(source) == originalOsu
            && File.ReadAllText(catchdiff) == originalCatchdiff, "Cancelling export retains unsaved edits and original files");
        ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(ui);
        ui.ClickText(L.Get("library.exportCreate")); SynchronizationUiTests.Wait(ui);
        var reopened = WorkspaceProject.Open(session.Directory).Project;
        Check(reopened.Difficulties.Count == 2 && ui.View.CurrentDifficultyName == "Rain (FruitsAtelier)" && !ui.View.IsDirty, "New difficulty is persisted and selected");
        var original = reopened.Difficulties.Single(d => d.Id == originalId);
        var added = reopened.Difficulties.Single(d => d.Id != originalId);
        Check(original.Document.ImportedSliders.Count == 1 && original.Document.Tracks.Count == 0
            && added.Document.Tracks.Count == 1 && added.Document.ImportedSliders.Count == 0, "Only the new difficulty contains converted authoring");
        Check(File.ReadAllText(catchdiff) == originalCatchdiff && File.ReadAllText(source) == originalOsu, "Creating a difficulty preserves the imported source");
        var reopenedSession = WorkspaceProject.Open(session.Directory);
        Check(!reopenedSession.Manifest.Difficulties.Single(d => d.Id == originalId).ExportConfirmed
            && reopenedSession.Manifest.Difficulties.Single(d => d.Id == added.Id).ExportConfirmed,
            "Explicit new export persists consent only for its own difficulty");
        Check(ui.View.SwitchDifficulty(reopened.Difficulties.FindIndex(d => d.Id == originalId))
            && ui.View.Document.ImportedSliders.Count == 1 && ui.View.Document.Tracks.Count == 0, "Original editor tab retains saved imported sliders");
        ui.View.ConvertAllSliders(); ui.View.AnswerSliderImport(true);
        deadline = DateTime.UtcNow.AddSeconds(15);
        while (ui.View.SliderConversionBusy && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(10); }
        ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(ui);
        Check(ui.View.ExportVisible && File.ReadAllText(source) == originalOsu, "Another difficulty's export does not authorize this source");
        ui.ClickText(L.Get("library.exportOverride"));
        ui.ClickText(L.Get("library.exportUpdate", ui.View.CurrentDifficultyName)); SynchronizationUiTests.Wait(ui);
        Check(ui.View.CurrentDifficultyHasExport && !ui.View.IsDirty
            && WorkspaceProject.Open(session.Directory).Manifest.Difficulties.Single(d => d.Id == originalId).ExportConfirmed,
            "Explicit overwrite records consent across save and restart");
        ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory));
        ui.View.SwitchDifficulty(ui.View.CaptureProject().Difficulties.FindIndex(d => d.Id == originalId));
        string exported = File.ReadAllText(source);
        var history = (EditorHistory)ui.View.GetType().GetProperty("history", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
        history.Begin("move confirmed slider"); ui.View.Document.Tracks[0].Nodes[0].X += 10; history.Commit();
        ui.Key('S', ctrl: true); SynchronizationUiTests.Wait(ui);
        Check(!ui.View.ExportVisible && !ui.View.IsDirty && File.ReadAllText(source) != exported,
            "Confirmed source updates directly after restart");
        ui.View.StopFileMonitoring();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
