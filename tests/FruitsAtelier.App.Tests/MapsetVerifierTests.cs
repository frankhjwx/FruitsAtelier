using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class MapsetVerifierTests
{
    public static void Run()
    {
        const string message = "00:31:193 - 04:03:100 (1,2) - 104:45:399 - wrong 00:99:123";
        var tokens = MapsetVerifierClient.Tokenize(message);
        Check(string.Concat(tokens.Select(t => t.Text)) == message, "Timestamp tokenization preserves upstream text");
        Check(tokens.Where(t => t.TimeMs is not null).Select(t => t.TimeMs).SequenceEqual(new double?[] { 31193, 243100, 6285399 }), "Each valid timestamp has its own target");
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                var project = BeatmapProject.FromDocuments([new() { IsDemo = false, DurationMs = 30000 }, new() { IsDemo = false, DurationMs = 30000 }]);
                project.Difficulties[0].Name = "First"; project.Difficulties[1].Name = "Second";
                ui.View.LoadProject(project); ui.Paint();
                var ids = project.Difficulties.Select(d => d.Id).ToArray();
                MapsetVerifierInput[]? captured = null;
                ui.View.VerifierAnalyze = (inputs, _, _) =>
                {
                    captured = inputs;
                    return Task.FromResult(new MapsetVerifierReport(1,
                        [new("GeneralCheck", "General heading", "Problem", "General message")],
                        [new(ids[0], "First", "Hard", 3, [new("Snap", "Snap heading", "Warning", "00:01:001 - 00:02:002 - First message", DifficultyId: ids[0])]),
                         new(ids[1], "Second", "Expert", 6, [new("Other", "Other heading", "Warning", "00:03:003 - Second message", DifficultyId: ids[1])])]) { UpstreamTag = "fixture" });
                };
                var baseline = ui.View.Document.DeepClone();
                double seek = -1; ui.View.RequestSeek = t => seek = t;
                ui.View.ShowMapsetVerifier(); Settle(ui);
                Check(captured is { Length: 2 } && !ReferenceEquals(captured[0].Document, ui.View.Document), "Worker receives detached documents for the full set");
                Check(ui.View.MapsetVerifierVisible && ui.View.VerifierTimestampBounds.Count == 2, "Current difficulty issues have separate links");
                var link = ui.View.VerifierTimestampBounds[1];
                ui.Click(link.Bounds.X + 2, link.Bounds.Y + 5);
                Check(seek == 2002 && ui.View.ActiveDifficultyIndex == 0, "Second timestamp seeks its own position");
                ui.ClickText(L.Get("mv.general"));
                Check(ui.View.VerifierTimestampBounds.Count == 0 && ui.Canvas.Texts.Any(t => t.Value.Contains("General")), "General view excludes difficulty issues");
                ui.View.NavigateVerifierTimestamp(ids[1], 3003); ui.Paint();
                Check(ui.View.ActiveDifficultyIndex == 1 && seek == 3003, "Navigation switches the canvas before seeking");
                var panel = ui.View.VerifierPanelBounds;
                ui.Click(panel.X + 110, panel.Y + 80);
                Check(ui.View.VerifierTimestampBounds.Count == 1 && ui.View.VerifierTimestampBounds[0].TimeMs == 3003, "Report follows the active difficulty GUID");
                ui.View.SwitchDifficulty(0); ui.Paint();
                Check(ui.View.VerifierTimestampBounds.Count == 2 && ui.View.Document.ContentEquals(baseline) && !ui.View.IsDirty, "Navigation preserves documents and cached results");
                ui.Resize(980, 620); ui.Resize(1800, 1000);
                var pending = new TaskCompletionSource<MapsetVerifierReport>();
                CancellationToken retired = default;
                ui.View.VerifierAnalyze = (_, _, cancellation) => { retired = cancellation; return pending.Task; };
                ui.View.RefreshMapsetVerifier();
                SpinWait.SpinUntil(() => retired.CanBeCanceled, 2000);
                ui.View.VerifierAnalyze = (_, _, _) => new TaskCompletionSource<MapsetVerifierReport>().Task;
                ui.View.LoadProject(BeatmapProject.FromDocuments([new() { IsDemo = false }]));
                Check(retired.IsCancellationRequested, "Project replacement cancels the retired worker");
                pending.SetResult(new(1, [], [])); ui.Paint();
                Check(ui.View.VerifierReport is null, "Retired result cannot publish into a new project");
            }
            Snapshot();
            AutoRefresh();
            Console.WriteLine("PASS Mapset Verifier timestamps, difficulty identity, snapshots and task retirement");
        }
        finally { L.SetLanguage(language); }
    }

    private static void Snapshot()
    {
        string root = Path.Combine(Path.GetTempPath(), "fa-verifier-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "source"), snapshot = Path.Combine(root, "snapshot");
            Directory.CreateDirectory(original); Directory.CreateDirectory(snapshot);
            File.WriteAllText(Path.Combine(original, "private.txt"), "unrelated data");
            File.WriteAllBytes(Path.Combine(original, "bg.jpg"), [1, 2, 3]);
            var document = new MapDocument { IsDemo = false, SourcePath = Path.Combine(original, "input.osu") };
            document.Fruits.Add(new() { TimeMs = 1234, X = 128 });
            var request = MapsetVerifierClient.PrepareSnapshot([new(Guid.NewGuid(), "Edited", document)], true, snapshot, default);
            Check(!File.Exists(Path.Combine(snapshot, "private.txt")) && File.Exists(Path.Combine(snapshot, "bg.jpg")), "Snapshot copies allowlisted mapset resources only");
            Check(File.ReadAllText(Path.Combine(snapshot, request.Difficulties[0].File)).Contains(",1234,"), "Snapshot exports current unsaved content");
            Check(Directory.GetFiles(original).Length == 2, "Snapshot leaves source directory untouched");
        }
        finally { Directory.Delete(root, true); }
    }

    public static void Engine()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "eng/mapset-verifier.json"))) root = root.Parent;
        if (root is null) throw new Exception("Repository fixture root missing");
        var map = new MapDocument { IsDemo = false, DurationMs = 10000, AudioPath = Path.Combine(root.FullName, "tests/FruitsAtelier.Audio.Tests/Fixtures/quiet-tone.ogg") };
        SongSetup.Set(map, "General", "Mode", "2");
        SongSetup.Set(map, "Metadata", "Artist", "Fixture artist");
        SongSetup.Set(map, "Metadata", "Title", "Fixture title");
        SongSetup.Set(map, "Metadata", "Creator", "Fixture mapper");
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        map.Fruits.AddRange([new() { TimeMs = 1001, X = 100 }, new() { TimeMs = 1004, X = 500 }]);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var result = MapsetVerifierClient.AnalyzeAsync([new(first, "First", map), new(second, "Second", map.DeepClone())], true, default).GetAwaiter().GetResult();
        Check(result.Schema == 1 && result.UpstreamTag.Length > 0 && result.General.Length > 0, "Actual upstream produces General issues and pinned metadata");
        Check(result.Difficulties.Select(d => d.Id).ToHashSet().SetEquals([first, second]), "Actual engine maps unsubmitted difficulties by file identity");
        string evidence = Path.Combine(root.FullName, "artifacts/tests/mapset-verifier"); Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "engine-report.json"), System.Text.Json.JsonSerializer.Serialize(result));
        var issues = result.Difficulties.SelectMany(d => d.Issues).ToArray();
        Check(issues.Any(i => i.Check.StartsWith("MapsetVerifier.") && MapsetVerifierClient.Tokenize(i.Message).Any(t => t.TimeMs == 1004)), "Actual upstream reports navigable object timestamps");
        Console.WriteLine("PASS actual pinned Mapset Verifier engine, exported snapshot and multiple difficulty identities");
    }

    private static void Settle(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do { ui.Paint(); if (!ui.View.MapsetVerifierBusy) return; Thread.Sleep(1); } while (DateTime.UtcNow < deadline);
        throw new Exception("Verifier worker did not settle");
    }

    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int milliseconds) => ticks += milliseconds;
    }

    private static void AutoRefresh()
    {
        var clock = new Clock();
        var ui = new Ui(false, clock);
        ui.LoadDocument(new() { IsDemo = false, DurationMs = 10000 });
        int runs = 0, fruits = -1;
        ui.View.VerifierAnalyze = (inputs, _, _) =>
        {
            Interlocked.Increment(ref runs); fruits = inputs[0].Document.Fruits.Count;
            return Task.FromResult(new MapsetVerifierReport(1, [], [new(inputs[0].Id, inputs[0].Name, "Hard", 3, [])]));
        };
        ui.View.ShowMapsetVerifier(); Settle(ui);
        Check(runs == 1, "Opening checks once");
        ui.View.PointerMove(ui.Plot.X + 20, ui.Plot.Y + 20, false, false);
        clock.Advance(2000); ui.Paint();
        Check(runs == 1 && !ui.View.MapsetVerifierNeedsRedraw, "Pointer and idle frames do not rerun checks");
        ui.Key('F'); ui.ClickMap(1000, 100);
        clock.Advance(500); ui.ClickMap(2000, 200);
        clock.Advance(999); ui.Paint();
        Check(runs == 1, "Continuous edits debounce from the last commit");
        clock.Advance(1);
        Check(ui.View.MapsetVerifierNeedsRedraw, "Debounce completion requests a frame without perpetual rendering");
        Settle(ui);
        Check(runs == 2 && fruits == 2, "Automatic check captures the latest committed snapshot");
        ui.Key('Z', ctrl: true); clock.Advance(1000); Settle(ui);
        Check(runs == 3 && fruits == 1, "Undo invalidates the automatic report");
        ui.Key('Y', ctrl: true); clock.Advance(1000); Settle(ui);
        Check(runs == 4 && fruits == 2, "Redo invalidates the automatic report");
        var analyze = ui.View.VerifierAnalyze;
        var prior = ui.View.VerifierReport;
        var pending = new TaskCompletionSource<MapsetVerifierReport>();
        CancellationToken retired = default;
        ui.View.VerifierAnalyze = (_, _, cancellation) => { retired = cancellation; return pending.Task; };
        ui.View.RefreshMapsetVerifier();
        Check(SpinWait.SpinUntil(() => retired.CanBeCanceled, 2000), "Pending check started");
        ui.Key('F'); ui.ClickMap(3000, 300);
        Check(retired.IsCancellationRequested, "Content edits cancel an obsolete check");
        pending.SetResult(new(1, [new("Retired", "Retired", "Problem", "obsolete")], [])); ui.Paint();
        Check(ReferenceEquals(prior, ui.View.VerifierReport), "Obsolete completed results never replace the displayed snapshot");
        ui.View.VerifierAnalyze = analyze;
        clock.Advance(1000); Settle(ui);
        Check(runs == 5 && fruits == 3, "The replacement check uses the latest content");
        ui.View.StopMapsetVerifier();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
