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
                var tab = ui.View.VerifierDifficultyTabBounds;
                ui.Click(tab.X + tab.Width / 2, tab.Y + tab.Height / 2);
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
            PanelControls();
            CopyFeedback();
            Console.WriteLine("PASS Mapset Verifier timestamps, difficulty identity, snapshots and task retirement");
        }
        finally { L.SetLanguage(language); }
    }

    private static void PanelControls()
    {
        var ui = new Ui(false);
        var map = new MapDocument { IsDemo = false, DurationMs = 30000 };
        SongSetup.Set(map, "Metadata", "Title", "Toolbar song title");
        ui.LoadDocument(map); ui.Resize(1440, 900);
        int checks = 0;
        ui.View.VerifierAnalyze = (inputs, _, _) =>
        {
            checks++;
            MapsetVerifierIssue[] issues = [new("Problem", "Concurrent hit objects.", "Problem", "00:01:001 - First problem"),
                new("Warning", "Preview warning.", "Warning", "00:02:002 - Preview warning"),
                .. Enumerable.Range(0, 60).Select(i => new MapsetVerifierIssue("Minor", "Concurrent or conflicting timing lines.", "Minor", $"00:03:{i:000} - Minor detail {i}"))];
            return Task.FromResult(new MapsetVerifierReport(1, [], [new(inputs[0].Id, inputs[0].Name, "Expert", 6.68, issues)]) { UpstreamTag = "v2.1.1" });
        };
        ui.View.ShowMapsetVerifier(); Settle(ui);
        var general = ui.View.VerifierGeneralTabBounds;
        var difficulty = ui.View.VerifierDifficultyTabBounds;
        var refresh = ui.View.VerifierRefreshBounds;
        Check(general.Y == difficulty.Y && refresh.Y == general.Y && difficulty.Right < refresh.X, "Compact tabs and right-aligned refresh share a row");
        Check(ui.Canvas.Circles.Any(c => general.Contains(c.X, c.Y) && c.Color == 0x22D66C)
            && ui.Canvas.Circles.Any(c => difficulty.Contains(c.X, c.Y) && c.Color == 0xFF6067), "General and difficulty show independent clear/problem status icons");
        Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("Toolbar song title") && t.X >= ui.View.VerifierPanelBounds.X), "Verifier header omits the song title");
        Check(EditorView.VerifierHighestLevel([]) == "Check"
            && EditorView.VerifierHighestLevel([new("m", "m", "Minor", "m")]) == "Minor", "Clear and negligible result statuses remain distinct");
        var link = ui.View.VerifierTimestampBounds.First();
        ui.View.PointerMove(link.Bounds.X + 3, link.Bounds.Y + 5, false, false); ui.Paint();
        Check(ui.View.MapsetVerifierHandCursor, "Timestamp hover selects the hand cursor");
        ui.View.PointerLeave(); Check(!ui.View.MapsetVerifierHandCursor, "Pointer leave clears the hand cursor");
        var filter = ui.View.VerifierFilterBounds("Problem"); Click(filter);
        Check(ui.View.VerifierTimestampBounds.Count == 1 && ui.View.VerifierTimestampBounds[0].TimeMs == 1001 && checks == 1, "Selecting a pill shows only its severity without checking again");
        Click(ui.View.VerifierFilterBounds("Warning"));
        Check(ui.View.VerifierTimestampBounds.Count == 1 && ui.View.VerifierTimestampBounds[0].TimeMs == 2002, "Selecting another pill replaces the severity filter");
        Click(ui.View.VerifierFilterBounds("Warning"));
        Check(ui.View.VerifierTimestampBounds.Count == 62, "Clicking the selected pill restores unfiltered results");
        Click(filter);
        Click(filter);
        Check(ui.View.VerifierTimestampBounds.Any(t => t.TimeMs == 1001), "Clicking the selected severity returns to all issues");
        var heading = ui.Canvas.Texts.First(t => t.Value.Contains("Concurrent ") && t.Bold);
        ui.Click(ui.View.VerifierPanelBounds.X + 18, heading.Y + 7);
        Check(!ui.View.VerifierTimestampBounds.Any(t => t.TimeMs == 1001), "Chevron collapses the complete group");
        ui.Click(ui.View.VerifierPanelBounds.X + 18, heading.Y + 7);
        Check(ui.View.VerifierTimestampBounds.Any(t => t.TimeMs == 1001), "Chevron expands the complete group");
        var thumb = ui.View.VerifierScrollbarThumb;
        Check(thumb.Height > 0, "Overflow produces a scrollbar");
        ui.View.PointerDown(thumb.X + 3, thumb.Y + 4, 0, false, false);
        Check(ui.View.WantsCapture, "Scrollbar thumb captures the pointer");
        var track = ui.View.VerifierScrollbarTrack;
        ui.View.PointerMove(thumb.X + 3, track.Bottom + 50, false, false); ui.Paint();
        Check(ui.View.VerifierScrollOffset > 0, "Thumb drags outside the track and clamps to the end");
        ui.View.PointerUp(thumb.X + 3, track.Bottom + 50, 0); ui.Paint();
        Check(!ui.View.WantsCapture, "Thumb release clears capture");
        ui.View.PointerDown(track.X + 3, track.Y + 2, 0, false, false); ui.Paint();
        Check(ui.View.VerifierScrollOffset == 0, "Clicking the track reaches the beginning");
        ui.View.CancelInteraction(); ui.Paint();
        Check(!ui.View.WantsCapture && !ui.View.IsDirty, "Lost capture and scrolling preserve content");
        Click(ui.View.VerifierFilterBounds("Warning"));
        ui.View.PointerMove(link.Bounds.X + 3, link.Bounds.Y + 5, false, false); ui.Paint();
        Check(!ui.View.VerifierTimestampBounds.Any(t => t.TimeMs == 1001), "Filtering removes unrelated timestamp targets");
        ui.View.StopMapsetVerifier();
        void Click(FruitsAtelier.App.Rendering.Rect bounds) => ui.Click(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static void CopyFeedback()
    {
        var clock = new Clock();
        var ui = new Ui(false, clock);
        ui.LoadDocument(new() { IsDemo = false, DurationMs = 10000 }); ui.Resize(1440, 900);
        const string first = "00:01:001 - First", second = "00:02:002 - Second";
        ui.View.VerifierAnalyze = (inputs, _, _) => Task.FromResult(new MapsetVerifierReport(1, [],
            [new(inputs[0].Id, inputs[0].Name, "Hard", 3,
                [new("Copy", "Copy heading", "Warning", first), new("Copy", "Copy heading", "Warning", second)])]));
        ui.View.ShowMapsetVerifier(); Settle(ui);
        var copy = ui.Canvas.Texts.Single(t => t.Value == L.Get("mv.copyAll", 2));
        ui.View.PointerMove(copy.X + 4, copy.Y + 5, false, false); ui.Paint();
        Check(ui.View.MapsetVerifierHandCursor, "Copy all hover uses the hand cursor");
        ui.Click(copy.X + 4, copy.Y + 5);
        Check(ui.View.VerifierCopyOpacity == 0, "Unavailable clipboard does not confirm a copy");
        string? clipboard = null; ui.View.RequestCopyText = text => clipboard = text;
        ui.Click(copy.X + 4, copy.Y + 5);
        Check(clipboard == first + Environment.NewLine + second, "Copy all copies every complete group message");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("discussion.linkCopied")) && ui.View.VerifierCopyOpacity == 1,
            "Copy all shows the shared osu web confirmation");
        Check(ui.View.MapsetVerifierNeedsRedraw, "Copy feedback requests fade frames");
        clock.Advance(1800); ui.Paint();
        Check(Math.Abs(ui.View.VerifierCopyOpacity - .5f) < .01, "Copy confirmation shares the web toolbar fade duration");
        clock.Advance(200); ui.Paint();
        Check(!ui.View.MapsetVerifierNeedsRedraw && !ui.Canvas.Texts.Any(t => t.Value == L.Get("discussion.linkCopied")), "Expired feedback stops rendering");
        ui.Click(copy.X + 4, copy.Y + 5);
        Check(ui.View.VerifierCopyOpacity == 1, "Repeated copy restarts confirmation");
        var general = ui.View.VerifierGeneralTabBounds;
        ui.Click(general.X + 15, general.Y + 12);
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("discussion.linkCopied")) && !ui.View.MapsetVerifierNeedsRedraw,
            "Copy confirmation is scoped to its visible group");
        Check(!ui.View.IsDirty, "Copy feedback preserves content and undo");
        ui.View.StopMapsetVerifier();
        Check(ui.View.VerifierCopyOpacity == 0, "Project retirement clears copy feedback");
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
