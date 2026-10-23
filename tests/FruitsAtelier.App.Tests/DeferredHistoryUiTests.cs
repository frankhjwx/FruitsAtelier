using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class DeferredHistoryUiTests
{
    public static void Run()
    {
        string root = Path.GetFullPath("artifacts/tests/history-ui-" + Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "Workspace"), songs = Path.Combine(root, "Songs");
        Directory.CreateDirectory(songs);
        string source = Path.Combine(songs, "map.osu");
        string original = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:History\nArtist:Test\nCreator:Test\nVersion:Catch\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n";
        File.WriteAllText(source, original);
        var project = BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]);
        project.Difficulties[0].Name = "Catch";
        var session = WorkspaceProject.Create(workspace, project, songs);
        string history = Path.Combine(workspace, ".sync-history", session.Manifest.Id.ToString("N"));
        for (int i = 0; i < 105; i++)
        {
            string round = Path.Combine(history, DateTime.UtcNow.AddMinutes(i - 200).ToString("yyyyMMddTHHmmssfffffff") + "-save");
            Directory.CreateDirectory(round); File.WriteAllText(Path.Combine(round, "saved.catchproj"), "{}");
        }
        string broken = Path.Combine(workspace, "unrelated.catchdiff"); File.WriteAllText(broken, "{invalid");
        var ui = new Ui(false); ui.View.LibrarySettings.Workspace = workspace; ui.View.LibrarySettings.Songs = songs;
        ui.View.LoadWorkspace(session);
        File.WriteAllText(Path.Combine(songs, "added.osu"), original.Replace("Version:Catch", "Version:Added"));
        ui.View.RefreshSynchronization();
        Wait(() => !ui.View.SynchronizationBusy);
        Check(!ui.View.SynchronizationVisible && ui.View.CaptureProject().Difficulties.Count == 2, "New difficulty synchronization commits without unrelated reference maintenance");
        Check(Directory.GetDirectories(history).Length > 100 && WorkspaceStorage.HistoryMaintenancePending(workspace), "Open projects defer requested retention");
        File.Delete(broken);
        ui.View.ShowLibrary(); ui.View.EnableFileMonitoring();
        Set("nextStorageMaintenance", DateTime.UtcNow.AddDays(1));
        Wait(() => !WorkspaceStorage.HistoryMaintenancePending(workspace));
        Check(Directory.GetDirectories(history).Length <= 100, "Queued requests run in the idle Library before the daily deadline");
        ui.View.StopFileMonitoring();
        Console.WriteLine("PASS automatic sync defers history pruning and idle Library processes queued requests");

        void Set(string name, object value) => typeof(EditorView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ui.View, value);
        void Wait(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            do { ui.Paint(); if (condition()) return; Thread.Sleep(5); } while (timer.Elapsed < TimeSpan.FromSeconds(15));
            throw new Exception("Deferred history UI did not settle");
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
