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
            OffsetFixes();
            KiaiFix();

            Snapshot();
            AutoRefresh();
            PanelControls();
            CopyFeedback();
            Console.WriteLine("PASS Mapset Verifier timestamps, difficulty identity, snapshots and task retirement");
        }
        finally { L.SetLanguage(language); }
    }

    private static void OffsetFixes()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(locale);
            var map = new MapDocument { IsDemo = false, BeatLengthMs = 500, DurationMs = 5000 };
            map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
                new() { TimeMs = 2001, BeatLengthMs = -100, Uninherited = false, Volume = 40 }]);
            map.Fruits.Add(new() { TimeMs = 1001, X = 100 });
            var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900);
            var before = ui.View.Document.DeepClone();
            ui.View.VerifierAnalyze = (inputs, _, _) =>
            {
                var input = inputs[0];
                return Task.FromResult(new MapsetVerifierReport(1, [], [new(input.Id, input.Name, "Hard", 3,
                    [.. input.Document.Fruits.Where(f => f.TimeMs == 1001).Select(_ => new MapsetVerifierIssue(
                        "MapsetVerifier.Checks.AllModes.Timing.CheckUnsnaps", "Unsnapped hit objects.", "Minor",
                        "00:01:001 - Circle unsnapped by 1 ms.")),
                     .. MapsetVerifierClient.TimingOffsetIssues(input)])]));
            };
            ui.View.ShowMapsetVerifier(); Settle(ui);
            Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Checking offsets preserves original content");
            Check(ui.View.VerifierFixBounds.Count == 2, "Timing line group offers its own fix");
            ClickFix();
            Check(ui.View.VerifierFixPromptVisible, "Other sections offer a combined fix");
            ui.ClickText(L.Get("mv.fixThisSection")); Settle(ui);
            Check(ui.View.Document.Fruits[0].TimeMs == 1000 && ui.View.Document.TimingPoints[1].TimeMs == 2001,
                "Object fix leaves timing lines unchanged");
            Check(ui.View.VerifierFixBounds.Count == 1, "Fixed objects disappear after refresh");
            ui.Key(90, true); ui.Paint();
            Check(ui.View.Document.ContentEquals(before), "Object fix is one undo transaction");
            ui.Key(89, true); ui.Paint();
            Check(ui.View.Document.Fruits[0].TimeMs == 1000, "Object fix supports redo");
            ui.View.RefreshMapsetVerifier(); Settle(ui);
            ClickFix(); Settle(ui);
            Check(!ui.View.VerifierFixPromptVisible, "A single affected section fixes without asking");
            Check(ui.View.Document.TimingPoints[1].TimeMs == 2000 && ui.View.Document.Fruits[0].TimeMs == 1000,
                "Timing fix leaves objects unchanged");
            Check(MapsetVerifierClient.TimingOffsetIssues(new(Guid.NewGuid(), "map", ui.View.Document)).Length == 0,
                "Timing fix clears the detected offsets");
            ui.Key(90, true); ui.Paint();
            Check(ui.View.Document.TimingPoints[1].TimeMs == 2001 && ui.View.Document.Fruits[0].TimeMs == 1000,
                "Timing fix is independently undoable");
            ui.Key(90, true); ui.Paint();
            Check(ui.View.Document.ContentEquals(before), "Both separate fixes can be undone");
            ui.View.RefreshMapsetVerifier(); Settle(ui); ClickFix();
            Check(ui.View.VerifierFixPromptVisible, "Combined choice uses the current document");
            ui.Key(27); ui.Paint();
            Check(!ui.View.VerifierFixPromptVisible && ui.View.Document.ContentEquals(before), "Escape cancels without edits");
            ClickFix(); ui.ClickText(L.Get("mv.fixAllSections")); Settle(ui);
            Check(ui.View.Document.Fruits[0].TimeMs == 1000 && ui.View.Document.TimingPoints[1].TimeMs == 2000,
                "Combined choice fixes object and timing offsets");
            ui.Key(90, true); ui.Paint();
            Check(ui.View.Document.ContentEquals(before), "Combined fix is one undo step");
            ui.View.StopMapsetVerifier();

            void ClickFix()
            {
                var button = ui.View.VerifierFixBounds.First();
                var copy = ui.View.VerifierCopyBounds.First();
                Check(button.Y == copy.Y && button.Right < copy.X, "Fix for me sits left of Copy all on the title row");
                ui.Click(button.X + 4, button.Y + 5);
            }
        }
    }

    private static void KiaiFix()
    {
        var map = new MapDocument { IsDemo = false, BeatLengthMs = 500, DurationMs = 5000 };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 1001, BeatLengthMs = -100, Uninherited = false, Effects = 1 },
            new() { TimeMs = 2001, BeatLengthMs = -100, Uninherited = false }]);
        map.Fruits.Add(new() { TimeMs = 1001, X = 100 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900);
        var before = ui.View.Document.DeepClone();
        ui.View.VerifierAnalyze = (inputs, _, _) => Task.FromResult(new MapsetVerifierReport(1, [],
            [new(inputs[0].Id, inputs[0].Name, "Hard", 3,
                [new("MapsetVerifier.Checks.AllModes.Timing.CheckKiaiUnsnap", "Unsnapped kiai.", "Minor",
                    "00:01:001 - Kiai is unsnapped by 1 ms."),
                 new("MapsetVerifier.Checks.AllModes.Timing.CheckUnusedLines", "Unused timing lines.", "Minor",
                    "00:02:001 - Inherited line changes sample settings, but affects nothing.")])]));
        ui.View.ShowMapsetVerifier(); Settle(ui);
        Check(ui.View.VerifierFixBounds.Count == 1 && ui.View.VerifierCopyBounds.Count == 2,
            "Kiai offers a fix while unused timing remains read-only");
        var button = ui.View.VerifierFixBounds.Single();
        ui.Click(button.X + 4, button.Y + 5);
        Check(ui.View.VerifierFixPromptVisible, "Kiai offers the other timing offsets");
        ui.ClickText(L.Get("mv.fixThisSection")); Settle(ui);
        Check((TimingEditing.Current(ui.View.Document, 1000)!.Effects & 1) != 0
            && (TimingEditing.Current(ui.View.Document, 2000)!.Effects & 1) == 0,
            "Kiai button aligns both transitions");
        Check(ui.View.Document.Fruits[0].TimeMs == 1001, "Current-section Kiai fix preserves object offsets");
        ui.Key(90, true); ui.Paint();
        Check(ui.View.Document.ContentEquals(before), "Kiai fix is one undo step");
        ui.Key(89, true); ui.Paint();
        Check((TimingEditing.Current(ui.View.Document, 1000)!.Effects & 1) != 0, "Kiai fix supports redo");
        ui.View.RefreshMapsetVerifier(); Settle(ui);
        button = ui.View.VerifierFixBounds.Single();
        ui.Click(button.X + 4, button.Y + 5);
        if (ui.View.VerifierFixPromptVisible) ui.View.AnswerVerifierFix(false);
        Settle(ui);
        Check(ui.View.StatusMessage == L.Get("mv.noOffsets"), "Repeated Kiai fix makes no changes");
        ui.Key(90, true); ui.Paint();
        Check(ui.View.Document.ContentEquals(before), "Kiai-only fix can return to the complete baseline");
        ui.View.RefreshMapsetVerifier(); Settle(ui);
        button = ui.View.VerifierFixBounds.Single();
        ui.Click(button.X + 4, button.Y + 5);
        Check(ui.View.VerifierFixPromptVisible, "All three offset categories offer a combined choice");
        ui.ClickText(L.Get("mv.fixAllSections")); Settle(ui);
        Check(ui.View.Document.Fruits[0].TimeMs == 1000
            && (TimingEditing.Current(ui.View.Document, 1000)!.Effects & 1) != 0
            && (TimingEditing.Current(ui.View.Document, 2000)!.Effects & 1) == 0
            && !TimingAlignment.Analyze(ui.View.Document).HasChanges,
            "Combined fix includes objects, timing and Kiai");
        ui.Key(90, true); ui.Paint();
        Check(ui.View.Document.ContentEquals(before), "All three categories undo together");
        ui.View.StopMapsetVerifier();
    }

    private static void PanelControls()
    {
        var longMap = new MapDocument();
        longMap.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 3662001, BeatLengthMs = -100, Uninherited = false, Volume = 40 }]);
        var longIssue = MapsetVerifierClient.TimingOffsetIssues(new(Guid.NewGuid(), "Long", longMap)).Single();
        Check(MapsetVerifierClient.Tokenize(longIssue.Message).Single(t => t.TimeMs is not null).TimeMs == 3662001,
            "Timing line timestamps retain total minutes beyond one hour");
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
        var copyBounds = ui.View.VerifierCopyBounds.Single();
        Check(ui.Canvas.Fills.Any(f => f.Bounds == copyBounds), "Copy all retains its button background without hover");
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
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "video-with-audio.flv"), Path.Combine(original, "shared.flv"));
            File.Copy(Path.Combine(original, "shared.flv"), Path.Combine(original, "clip.mpeg"));
            File.Copy(Path.Combine(original, "shared.flv"), Path.Combine(original, "clip.custom"));
            File.WriteAllText(Path.Combine(original, "shared.osb"), "[Events]\nVideo,0,shared.flv\nVideo,0,missing.wmv\nVideo,0,broken.mp4\nVideo,0,clip.mpeg\nVideo,0,clip.custom\n");
            File.WriteAllBytes(Path.Combine(original, "broken.mp4"), [0, 1, 2]);
            var document = new MapDocument { IsDemo = false, SourcePath = Path.Combine(original, "input.osu") };
            document.Fruits.Add(new() { TimeMs = 1234, X = 128 });
            var request = MapsetVerifierClient.PrepareSnapshot([new(Guid.NewGuid(), "Edited", document)], true, snapshot, default);
            Check(!File.Exists(Path.Combine(snapshot, "private.txt")) && File.Exists(Path.Combine(snapshot, "bg.jpg")), "Snapshot copies allowlisted mapset resources only");
            Check(File.ReadAllText(Path.Combine(snapshot, request.Difficulties[0].File)).Contains(",1234,"), "Snapshot exports current unsaved content");
            var video = request.Videos.Single(v => v.File == "shared.flv");
            Check(video is { Width: 1296, Height: 24, HasAudio: true, Error: null }, "Snapshot probes shared storyboard FLV metadata through the packaged decoder");
            Check(new[] { "clip.mpeg", "clip.custom" }.All(name => File.Exists(Path.Combine(snapshot, name))
                && request.Videos.Single(v => v.File == name) is { Width: 1296, Height: 24, HasAudio: true, Error: null }),
                "Referenced videos are copied and probed independently of filename extension");
            Check(request.Videos.Single(v => v.File == "missing.wmv").Error == "Missing"
                && request.Videos.Single(v => v.File == "broken.mp4").Error == "Exception", "Snapshot preserves missing and unreadable video results");
            var metadata = FruitsAtelier.App.Media.VideoMetadata.Read(Path.Combine(original, "shared.flv"));
            Check(metadata == new AimodVideoMetadata(1296, 24, true), "Silent audio stream remains present in FLV metadata");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool cancelled = false;
            try { FruitsAtelier.App.Media.VideoMetadata.Read(Path.Combine(original, "shared.flv"), cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Cancelled video metadata reads cannot publish a result");
            Check(Directory.GetFiles(original).Length == 7, "Snapshot leaves source directory untouched");
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
        VideoEngine(root.FullName);
        Console.WriteLine("PASS actual pinned Mapset Verifier engine, exported snapshot and multiple difficulty identities");
    }

    private static void VideoEngine(string repository)
    {
        string root = Path.Combine(repository, "artifacts", "tests", "mapset-verifier", "media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "video-with-audio.flv"), Path.Combine(root, "shared.flv"));
            File.Copy(Path.Combine(root, "shared.flv"), Path.Combine(root, "clip.custom"));
            File.WriteAllBytes(Path.Combine(root, "broken.mp4"), [0, 1, 2]);
            File.WriteAllText(Path.Combine(root, "shared.osb"), "[Events]\nVideo,0,shared.flv\nVideo,0,missing.wmv\nVideo,0,broken.mp4\nVideo,0,clip.custom\n");
            var map = new MapDocument { IsDemo = false, SourcePath = Path.Combine(root, "map.osu"),
                AudioPath = Path.Combine(repository, "tests", "FruitsAtelier.Audio.Tests", "Fixtures", "quiet-tone.ogg") };
            SongSetup.Set(map, "General", "Mode", "2");
            map.Fruits.Add(new() { TimeMs = 1000 });
            var before = map.DeepClone();
            var result = MapsetVerifierClient.AnalyzeAsync([new(Guid.NewGuid(), "Media", map)], true, default).GetAwaiter().GetResult();
            Check(result.General.Any(i => i.Check.EndsWith("CheckAudioInVideo") && i.Level == "Problem" && i.Message.Contains("shared.flv")),
                "Actual engine flags the silent audio stream of a shared storyboard FLV");
            Check(result.General.Any(i => i.Check.EndsWith("CheckVideoResolution") && i.Level == "Problem" && i.Message.Contains("1296 x 24")),
                "Actual engine uses upstream's either-axis video resolution threshold");
            Check(result.General.Any(i => i.Check.EndsWith("CheckAudioInVideo") && i.Level == "Problem" && i.Message.Contains("clip.custom")),
                "Actual engine checks referenced video containers without an allowlisted extension");
            foreach (string check in new[] { "CheckAudioInVideo", "CheckVideoResolution" })
            {
                Check(result.General.Any(i => i.Check.EndsWith(check) && i.Level == "Warning" && i.Message.Contains("missing.wmv")),
                    "Actual video checks retain missing resource warnings");
                Check(result.General.Any(i => i.Check.EndsWith(check) && i.Level == "Error" && i.Message.Contains("broken.mp4")),
                    "Actual video checks retain metadata failure errors");
            }
            Check(map.ContentEquals(before), "Actual media verification preserves source content");
        }
        finally { Directory.Delete(root, true); }
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
