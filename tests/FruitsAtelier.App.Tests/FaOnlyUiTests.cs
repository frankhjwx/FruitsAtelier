using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class FaOnlyUiTests
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
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/fa-only-ui", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Title\nArtist:Artist\nCreator:Mapper\nVersion:One\n[Difficulty]\nCircleSize:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
                var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs);
                session.Manifest.Difficulties[0].ExportConfirmed = true;
                WorkspaceProject.Save(session, session.Project);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
                ui.View.LoadWorkspace(session); Wait(ui);
                int exports = 0;
                ui.View.RequestWorkspaceExport = (overwrite, name) =>
                {
                    exports++;
                    var project = ui.View.CaptureProject();
                    var active = project.Difficulties[ui.View.ActiveDifficultyIndex];
                    var current = ui.View.WorkspaceSession!;
                    var plan = WorkspaceExport.Plan(current, active, songs, overwrite, name, true);
                    LibraryOperations.Export(current, project, plan);
                    ui.View.LibraryExportFinished(plan);
                };
                try
                {
                    void New()
                    {
                        var add = (FruitsAtelier.App.Rendering.Rect)ui.View.GetType().GetField("difficultyAddButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
                        ui.Click(add.X + 4, add.Y + 4); ui.ClickText(L.Get("project.add"));
                    }
                    void Create() => ui.ClickObjectAction(L.Get("project.add"));
                    int count = ui.View.DifficultyCount;
                    New(); ui.Key(27);
                    Check(ui.View.DifficultyCount == count && exports == 0, "canceling creation creates neither difficulty nor osu file");
                    New(); Create(); Wait(ui);
                    Check(!ui.View.CurrentDifficultyFaOnly && exports == 1 && ui.View.WorkspaceSession!.Manifest.Difficulties[ui.View.ActiveDifficultyIndex].ExportConfirmed,
                        "default new difficulty synchronizes and generates its osu file");
                    New(); ui.ClickText(L.Get("song.faOnly")); Create(); Wait(ui);
                    Guid local = ui.View.CaptureProject().Difficulties[ui.View.ActiveDifficultyIndex].Id;
                    Check(ui.View.CurrentDifficultyFaOnly && exports == 1, "FA-only creation does not generate to Songs");
                    ui.View.SaveCurrentDifficulty(); Wait(ui);
                    Check(ui.View.WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == local).FaOnly && exports == 1, "saving FA-only writes workspace only");
                    ui.Key(115); ui.Paint();
                    ui.ClickText(L.Get("song.faSettings"));
                    ui.ClickText(L.Get("song.syncToOsu")); Wait(ui);
                    Check(!ui.View.CurrentDifficultyFaOnly && exports == 2, "enabling sync in Song Setup generates the previously local difficulty");
                    string external = WorkspaceSynchronization.Target(ui.View.WorkspaceSession!.Manifest.Difficulties.Single(d => d.Id == local))!;
                    string unchanged = File.ReadAllText(external);
                    void Settings()
                    {
                        ui.View.ShowSongDock(); ui.Paint(); ui.ClickText(L.Get("song.faSettings"));
                    }
                    Settings(); ui.ClickText(L.Get("song.syncToOsu"));
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("song.faOnlyPrompt")), "detaching a linked difficulty asks about its osu file");
                    ui.Key(27);
                    Check(!ui.View.CurrentDifficultyFaOnly && File.ReadAllText(external) == unchanged, "canceling detachment leaves sync and file unchanged");
                    Settings(); ui.ClickText(L.Get("song.syncToOsu")); ui.ClickText(L.Get("song.faOnlyKeepFile")); Wait(ui);
                    Check(ui.View.CurrentDifficultyFaOnly && File.ReadAllText(external) == unchanged, "keep-file branch detaches without deleting osu");
                    File.WriteAllText(external, unchanged.Replace("Title:Title", "Title:Outside title"));
                    ui.View.RefreshSynchronization(); Wait(ui);
                    Check(!ui.View.SynchronizationVisible && SongSetup.Get(ui.View.Document, "Metadata", "Title") == "Title"
                        && ui.View.DifficultyCount == count + 2, "external FA-only edits do not replace authoring or create duplicates");
                    File.WriteAllText(external, unchanged);
                    Settings(); ui.ClickText(L.Get("song.syncToOsu")); Wait(ui);
                    Check(!ui.View.CurrentDifficultyFaOnly, "kept association can resume synchronization");
                    Settings(); ui.ClickText(L.Get("song.syncToOsu")); ui.ClickText(L.Get("song.faOnlyDeleteFile")); Wait(ui);
                    Check(ui.View.CurrentDifficultyFaOnly && !File.Exists(external) && File.Exists(source), "explicit delete removes only this difficulty's osu file");
                    var reopened = WorkspaceProject.Open(ui.View.WorkspaceSession!.Directory);
                    Check(reopened.Project.Difficulties.Single(d => d.Id == local).FaOnly
                        && reopened.Project.Difficulties.Count(d => !d.FaOnly) == count + 1, "restart retains independent per-difficulty settings");
                    ui.View.LoadWorkspace(reopened); ui.View.SwitchDifficulty(reopened.Project.Difficulties.FindIndex(d => d.Id == local)); Wait(ui);
                    ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("song.faSettings"));
                    ui.ClickText(L.Get("song.syncToOsu")); ui.ClickText(L.Get("mac.cancel"));
                    Check(ui.View.CurrentDifficultyFaOnly, "modal Song Setup cancellation preserves the saved sync setting");
                    ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("song.faSettings"));
                    ui.ClickText(L.Get("song.syncToOsu")); ui.ClickText(L.Get("song.ok")); Wait(ui);
                    Check(!ui.View.CurrentDifficultyFaOnly && exports == 3, "modal Song Setup applies sync and recreates the deleted osu file");
                    Console.WriteLine($"PASS FA-only UI {locale} {size.Item1}x{size.Item2}: defaults, creation, settings, keep/delete/cancel and restart");
                }
                finally { ui.View.StopFileMonitoring(); }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do { ui.Paint(); if (!ui.View.SynchronizationBusy) { ui.Paint(); return; } Thread.Sleep(5); } while (DateTime.UtcNow < deadline);
        throw new TimeoutException("FA-only operation did not finish.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
