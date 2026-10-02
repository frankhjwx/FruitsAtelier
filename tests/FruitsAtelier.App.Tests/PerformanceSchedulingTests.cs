using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class PerformanceSchedulingTests
{
    private static FieldInfo Field(string name) => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void Save()
    {
        var ui = new Ui(false);
        ui.View.NewProject();
        ui.View.LibrarySettings.Workspace = Path.GetFullPath(Path.Combine("artifacts/tests/background-save", Guid.NewGuid().ToString("N")));
        ui.View.LibrarySettings.Songs = "";
        var history = (EditorHistory)typeof(EditorView).GetProperty("history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
        history.Begin("fruit"); history.Document.Fruits.Add(new() { TimeMs = 1000, X = 100 }); history.Commit();
        bool completed = false;
        Check(ui.View.BeginWorkspaceSave(saved => completed = saved), "save starts");
        var actual = (Task<WorkspaceSession>)Field("workspaceSaveTask").GetValue(ui.View)!;
        var pending = new TaskCompletionSource<WorkspaceSession>();
        Field("workspaceSaveTask").SetValue(ui.View, pending.Task);
        var before = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); ui.Key(116);
        Check(!completed && ui.View.Document.ContentEquals(before) && !ui.View.IsTestplaying
            && !ui.View.PrepareFileOperation() && !ui.View.BeginWorkspaceSave(), "pending save draws while isolating edits and duplicate saves");
        Check(actual.Wait(TimeSpan.FromSeconds(15)), "save worker finishes without UI pumping");
        history.Begin("newer edit"); history.Document.Fruits[0].X = 200; history.Commit();
        pending.SetResult(actual.Result); ui.Paint();
        Check(completed && ui.View.IsDirty && ui.View.Document.Fruits[0].X == 200
            && WorkspaceProject.Open(actual.Result.Directory).Project.Difficulties[0].Document.Fruits[0].X == 100,
            "completion only acknowledges the published snapshot");
        ui.Key('Z', ctrl: true);
        Check(!ui.View.IsDirty && ui.View.Document.Fruits[0].X == 100, "save completion preserves undo");
        history.Begin("second save"); history.Document.Fruits[0].X = 300; history.Commit();
        ui.View.RefreshSynchronization(quiet: true);
        Check(ui.View.BeginWorkspaceSave(), "ordinary save starts");
        actual = (Task<WorkspaceSession>)Field("workspaceSaveTask").GetValue(ui.View)!;
        pending = new TaskCompletionSource<WorkspaceSession>();
        Field("workspaceSaveTask").SetValue(ui.View, pending.Task);
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits[0].X == 100 && !ui.View.DiscardConfirmationVisible,
            "ordinary save keeps editing and undo available");
        Check(actual.Wait(TimeSpan.FromSeconds(15)), "ordinary save completes");
        pending.SetResult(actual.Result); ui.Paint();
        Check(ui.View.IsDirty && ui.View.Document.Fruits[0].X == 100, "editing during ordinary save remains unsaved");
        ui.View.StopFileMonitoring();
    }

    public static void SynchronizationRetry()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-retry", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Retry\nArtist:Fixture\nCreator:Test\nVersion:One\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
        var ui = new Ui(false);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.LoadWorkspace(FruitsAtelier.App.Platform.LibraryOperations.ImportPath(source, ui.View.LibrarySettings));
        SynchronizationUiTests.Wait(ui);
        var history = (EditorHistory)typeof(EditorView).GetProperty("history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
        void Edit() { history.Begin("move"); history.Document.Fruits[0].X++; history.Commit(); }
        Edit(); ui.View.RefreshSynchronization(quiet: true);
        var scan = (Task)Field("syncTask").GetValue(ui.View)!;
        Check(scan.Wait(TimeSpan.FromSeconds(15)), "initial synchronization scan finishes");
        Edit(); ui.Paint();
        Check(Field("syncRetry").GetValue(ui.View) is not null && Field("syncTask").GetValue(ui.View) is null,
            "stale result defers its retry");
        for (int i = 0; i < 5; i++)
        {
            var retry = Field("syncRetry").GetValue(ui.View)!;
            retry.GetType().GetProperty("After")!.SetValue(retry, DateTime.MinValue);
            Edit(); ui.Paint();
            Check(Field("syncTask").GetValue(ui.View) is null && Field("syncRetry").GetValue(ui.View) is not null,
                "continued edits postpone expensive synchronization work");
        }
        SynchronizationUiTests.Wait(ui);
        source = ui.View.WorkspaceSession!.Manifest.Difficulties[0].Source!;
        Check(OsuBeatmapReader.ReadFile(source).Fruits[0].X == ui.View.Document.Fruits[0].X && !ui.View.IsDirty,
            "stable retry exports the latest edit");
        var versions = WorkspaceVersionHistory.List(ui.View.WorkspaceSession);
        Check(versions.Count == 2 && versions.All(v => v.Operation == "before-sync") && versions.Any(v => v.WorkingCopy),
            "automatic publication keeps saved and working authoring in one recovery round");
        ui.Key('Z', ctrl: true);
        Check(ui.View.IsDirty, "retry publication preserves undo");
        ui.View.StopFileMonitoring();
    }

    public static void History(Ui ui)
    {
        var pending = new TaskCompletionSource();
        Field("versionBackground").SetValue(ui.View, pending.Task);
        for (int i = 0; i < 100; i++) ui.Key(i % 2 == 0 ? 40 : 38);
        Check(Field("versionReadTask").GetValue(ui.View) is null
            && Field("versionPreviewTask").GetValue(ui.View) is null
            && ReferenceEquals(Field("versionBackground").GetValue(ui.View), pending.Task), "navigation queues no work behind a busy history worker");
        pending.SetResult();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (ui.View.VersionHistoryBusy && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(5); }
        Check(!ui.View.VersionHistoryBusy && Field("versionHistoricalPane").GetValue(ui.View) is not null,
            "latest history selection loads after the preceding worker finishes");
    }

    public static void NativeTestplayCapture()
    {
        var time = new ManualTime(); var ui = new Ui(false, time);
        var map = new MapDocument { IsDemo = false, DurationMs = 10000 };
        map.Fruits.Add(new() { TimeMs = 5000, X = 256 });
        ui.LoadDocument(map);
        CatchTestplaySession? session = null;
        ui.View.RequestRunTestplay = value => { session = value; return new Driver(); };
        ui.View.UpdateTransport(0, 10000, true, true, false, null, null, sampledAtMs: 0);
        ui.View.StartTestplay();
        Check(session is not null, "native driver starts");
        object? previous = Field("testplayFrame").GetValue(ui.View);
        time.Advance(10); session!.Tick();
        ui.View.UpdateTransport(10, 10000, true, true, false, null, null, sampledAtMs: 10);
        ui.View.KeyUp(65);
        Check(ReferenceEquals(previous, Field("testplayFrame").GetValue(ui.View)), "native transport and key release do not copy render snapshots");
        ui.Paint();
        Check(!ReferenceEquals(previous, Field("testplayFrame").GetValue(ui.View)) && ui.View.PlayheadMs >= 10,
            "render captures the current native session");
        ui.View.StopTestplay();
    }

    public static int Benchmark()
    {
        var samples = new List<object>();
        var map = new MapDocument { IsDemo = false, DurationMs = 1000000 };
        map.Fruits.Add(new() { TimeMs = 5000, X = 256 });
        for (int i = 0; i < 2000; i++) map.TimingPoints.Add(new() { TimeMs = i * 500, BeatLengthMs = i % 2 == 0 ? 500 : 400 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900); ui.Key(114);
        Measure("timing-frames", 500, ui.Paint);
        ui.Key(112);
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 0, X = 100 }, new() { TimeMs = 100000, X = 400 }]);
        ui.View.Document.Tracks.Add(track); ui.Paint();
        Field("streamTargets").SetValue(ui.View, new[] { track.Id });
        var initialize = typeof(EditorView).GetMethod("InitializeConversionPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Measure("stream-initialize", 100, () => initialize.Invoke(ui.View, null));
        var native = new Ui(false, new ManualTime());
        native.LoadDocument(new MapDocument { IsDemo = false });
        native.View.Document.Fruits.Add(new() { TimeMs = 5000, X = 256 }); native.Paint();
        native.View.RequestRunTestplay = _ => new Driver();
        native.View.UpdateTransport(0, 10000, true, true, false, null, null, sampledAtMs: 0);
        native.View.StartTestplay();
        Measure("native-transport-poll", 10000, () => native.View.UpdateTransport(0, 10000, true, true, false, null, null, sampledAtMs: 0));
        native.View.StopTestplay();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(samples, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;

        void Measure(string phase, int count, Action action)
        {
            for (int i = 0; i < 10; i++) action();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; i++) action();
            samples.Add(new { phase, count, msPerOperation = watch.Elapsed.TotalMilliseconds / count,
                bytesPerOperation = (GC.GetAllocatedBytesForCurrentThread() - allocated) / count });
        }
    }

    private sealed class Driver : IDisposable { public void Dispose() { } }
    private sealed class ManualTime : TimeProvider
    {
        private long elapsed;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => elapsed;
        public void Advance(int ms) => elapsed += ms;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
