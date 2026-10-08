using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using L = FruitsAtelier.Localization.Strings;

internal static class FeedbackInteractionTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int ms) => ticks += ms;
    }

    public static void Retry()
    {
        var clock = new Clock();
        var ui = new Ui(false, clock);
        var map = new MapDocument { DurationMs = 20000, IsDemo = false };
        map.Fruits.AddRange([new() { TimeMs = 6000, X = 256 }, new() { TimeMs = 15000, X = 256 }]);
        ui.LoadDocument(map);
        ui.View.UpdateTransport(5000, 20000, true, false, false, null, null);
        ui.View.UpdateTransport(5000, 20000, false, false, false, null, null);
        var sounds = new List<string>();
        ui.View.RequestAuditionHitsound = sound => sounds.Add(sound.Name);
        ui.View.StartTestplay();
        clock.Advance(500); ui.Paint();
        ui.Key(192);
        clock.Advance(125); ui.Paint();
        Check(Math.Abs(RetryDim() - .5f) < .001 && sounds.Count == 0, "Half-held retry must dim the whole window without a click.");
        clock.Advance(124); ui.Key(192); ui.Paint();
        Check(RetryDim() > .99f && sounds.Count == 0, "Retry must approach black before its deadline without a click.");
        Check(ui.View.PlayheadMs == 5749, "Retry fired before 250 ms or key repeat reset its deadline.");
        clock.Advance(1); ui.Paint();
        Check(ui.View.IsTestplaying && ui.View.PlayheadMs == 5000 && ui.View.TestplayCombo == 0, "Held retry did not reset to the testplay start.");
        Check(RetryDim() == 0 && sounds.SequenceEqual(["pause-retry-click"]), "Retry must clear the dim and play the pause Retry click once.");
        clock.Advance(350); ui.Key(192);
        Check(ui.View.PlayheadMs == 5350, "A held retry restarted more than once.");
        Check(sounds.Count == 1, "A held retry repeated its click.");
        ui.View.KeyUp(192);
        ui.Key(192); clock.Advance(200); ui.View.KeyUp(192); clock.Advance(200); ui.Paint();
        Check(ui.View.PlayheadMs == 5750, "A released short press still retried.");
        Check(RetryDim() == 0 && sounds.Count == 1, "Releasing early must remove dim without a retry click.");
        ui.Key(27); ui.View.KeyUp(27);
        Check(ui.View.TestplayPaused, "Pause fixture failed.");
        sounds.Clear();
        ui.Key(192); clock.Advance(125); ui.Paint();
        Check(Math.Abs(RetryDim() - .5f) < .001, "Paused retry must dim the pause menu too.");
        clock.Advance(125); ui.Paint();
        Check(!ui.View.TestplayPaused && ui.View.PlayheadMs == 5000, "Held retry did not restart paused testplay.");
        Check(RetryDim() == 0 && sounds.SequenceEqual(["pause-retry-click"]), "Paused retry must clear dim and play its click once.");
        ui.View.KeyUp(192);
        ui.Key(192); ui.View.CancelInteraction(preserveTestplay: true); clock.Advance(350); ui.Paint();
        Check(ui.View.PlayheadMs == 5350, "Focus cancellation retained a pending retry.");
        Check(RetryDim() == 0 && sounds.Count == 1, "Focus cancellation retained dim or triggered a click.");
        ui.View.StopTestplay();

        float RetryDim() => ui.Canvas.PaintCalls
            .Where(call => call.Color == 0 && call.FillBounds == new FruitsAtelier.App.Rendering.Rect(0, 0, ui.Width, ui.Height))
            .Select(call => call.Opacity).DefaultIfEmpty(0).Last();
    }

    public static void PreviousSave()
    {
        string workspace = Path.GetFullPath(Path.Combine("artifacts/tests/previous-save", Guid.NewGuid().ToString("N")));
        var map = new MapDocument { DurationMs = 20000, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        var other = map.DeepClone();
        var project = BeatmapProject.FromDocuments([map, other]);
        var session = WorkspaceProject.Create(workspace, project, "");
        var ui = new Ui(false); ui.View.LibrarySettings.Workspace = workspace;
        ui.View.LoadWorkspace(session); ui.Paint();
        ui.Key('L', ctrl: true); Wait(ui);
        Check(!ui.View.DiscardConfirmationVisible && ui.View.Document.Fruits[0].X == 100, "First save offered an invalid rollback.");
        project.Difficulties[0].Document.Fruits[0].X = 300;
        WorkspaceProject.Save(session, project);
        ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory)); ui.Paint();
        ui.View.Document.Fruits[0].X = 400;
        WorkspaceVersionHistory.ArchiveCurrent(ui.View.WorkspaceSession!, ui.View.CaptureProject());
        ui.Key('L', ctrl: true); Wait(ui);
        Check(ui.View.DiscardConfirmationVisible && ui.View.Document.Fruits[0].X == 400, "Rollback changed content before confirmation.");
        ui.Key(27);
        Check(ui.View.Document.Fruits[0].X == 400, "Cancelling rollback lost edits.");
        ui.Key('L', ctrl: true); Wait(ui); ui.Key(13); Wait(ui);
        Check(ui.View.Document.Fruits[0].X == 100 && ui.View.IsDirty, "Rollback selected a working copy or latest save instead of the previous save.");
        Check(ui.View.CaptureProject().Difficulties[1].Document.Fruits[0].X == 100, "Rollback changed another difficulty.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits[0].X == 400, "Rollback cannot be undone.");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.Fruits[0].X == 100, "Rollback cannot be redone.");
        ui.View.StopFileMonitoring();
    }

    public static void Navigation()
    {
        var map = new MapDocument { DurationMs = 60000, IsDemo = false };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 5000, BeatLengthMs = 500 }]);
        map.Fruits.Add(new() { TimeMs = 1000, X = 256 });
        var ui = new Ui(false); ui.LoadDocument(map);
        ui.View.UpdateTransport(1000, 60000, true, false, false, null, null); ui.Paint();
        var r = ui.View.ObjectTimelineBounds;
        float x = r.X + (float)((1000 - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs), y = r.Y + 27;
        var baseline = ui.View.Document.DeepClone();
        ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerUp(x, y, 0);
        ui.View.PointerDoubleClick(x, y, false, false); ui.Paint();
        Check(ui.View.PlayheadMs == 1000 && ui.View.SelectedObjectIds.Contains(map.Fruits[0].Id)
            && ui.View.Document.ContentEquals(baseline), "Timeline double-click did not select and navigate without editing.");
        ui.Key(38); Check(ui.View.PlayheadMs == 5000, "Up did not navigate to the following timing point.");
        ui.Key(40); Check(ui.View.PlayheadMs == 0, "Down did not navigate to the preceding timing point.");
        ui.Key('1'); var plot = ui.View.CanvasPlotBounds;
        ui.View.Wheel(plot.X + 20, plot.Y + 20, -120, true, alt: true);
        Check(ui.View.ActiveTool == "Fruit", "Wheel down did not advance through the tools.");
        ui.View.Wheel(plot.X + 20, plot.Y + 20, 120, true, alt: true);
        Check(ui.View.ActiveTool == "Select", "Wheel up did not return to the preceding tool.");
        ui.View.UpdateTransport(1000, 60000, true, false, false, null, null); ui.Paint();
        ui.ClickFruit(map.Fruits[0].Id); ui.Key(46);
        ui.View.UpdateTransport(40000, 60000, true, false, false, null, null); ui.Paint();
        ui.Key('Z', ctrl: true);
        Check(ui.View.PlayheadMs == 1000, "Undo did not reveal the restored offscreen object.");
        double view = ui.View.ViewStartMs;
        ui.Key('Y', ctrl: true);
        Check(ui.View.ViewStartMs == view, "Redo moved the viewport although the deleted position was visible.");
        ui.View.UpdateTransport(40000, 60000, true, false, false, null, null); ui.Paint();
        ui.Key('Z', ctrl: true); ui.View.UpdateTransport(40000, 60000, true, false, false, null, null); ui.Paint();
        ui.Key('Y', ctrl: true);
        Check(ui.View.PlayheadMs == 1000, "Redo did not reveal an offscreen deletion.");
    }

    public static void TailNavigation()
    {
        foreach (var mode in new[] { SliderEditingMode.PenTool, SliderEditingMode.OsuLegacy })
        {
            var map = new MapDocument { DurationMs = 20000, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]);
            map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode);
            ui.EditTrack(track.Id);
            var baseline = ui.View.Document.DeepClone();
            ui.DownMap(2000, 300); ui.MoveMap(2125, 300);
            var p = ui.ScreenAt(2125, 300);
            int snap = ui.View.SnapDivisor;
            ui.View.Wheel(p.X, p.Y, 120, true); ui.Paint();
            Check(ui.View.SnapDivisor != snap && ui.View.WantsCapture, "Tail drag blocked Ctrl wheel or lost capture.");
            double zoom = ui.View.CanvasZoom;
            ui.View.Wheel(p.X, p.Y, 120, false, alt: true); ui.Paint();
            Check(ui.View.CanvasZoom > zoom && ui.View.WantsCapture, "Tail drag blocked Alt wheel.");
            double start = ui.View.ViewStartMs;
            ui.View.Wheel(p.X, p.Y, -120, false); ui.Paint();
            Check(ui.View.ViewStartMs > start && ui.View.WantsCapture, "Tail drag blocked scrolling.");
            ui.View.UpdateTransport(ui.View.PlayheadMs, 20000, true, false, false, null, null);
            ui.View.RequestTogglePlayback = () => ui.View.UpdateTransport(ui.View.PlayheadMs, 20000, true, !ui.View.AudioPlaying, false, null, null);
            ui.Key(32); Check(ui.View.AudioPlaying && ui.View.WantsCapture, "Tail drag blocked playback.");
            ui.Key('C'); Check(!ui.View.AudioPlaying && ui.View.WantsCapture, "Tail drag blocked pause.");
            ui.Key(27);
            Check(ui.View.Document.ContentEquals(baseline), "Cancelling a navigated tail drag did not restore all authored data.");
        }
    }

    public static void TagsAndPreference()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false };
        string tags = string.Join(' ', Enumerable.Range(0, 40).Select(i => "tag" + i));
        SongSetup.Set(map, "Metadata", "Tags", tags);
        var ui = new Ui(false); ui.Resize(980, 620); ui.LoadDocument(map);
        ui.View.OpenSongSetup(); ui.Paint();
        var box = ui.View.SongSetupFieldBounds["Tags"];
        var rows = ui.Canvas.Texts.Where(t => t.X > box.X && t.X < box.Right && t.Y >= box.Y && t.Y < box.Bottom).OrderBy(t => t.Y).ToArray();
        Check(rows.Length > 1 && string.Concat(rows.Select(t => t.Value)) == tags, "Tags are truncated instead of wrapping completely.");
        ui.Click(box.X + 10, rows[1].Y + 2); ui.Type("new "); ui.Paint(); ui.Key(13);
        string edited = OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Tags")!;
        Check(edited.Contains("new ") && edited != "new " + tags, "Wrapped row click did not place the caret on that row.");
        ui.Key('Z', ctrl: true); Check(OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Tags") == tags, "Tags edit did not undo.");
        string path = Path.GetFullPath("artifacts/tests/feedback-preferences.json");
        ui.View.LibrarySettings.Workspace = Path.GetFullPath("artifacts/tests/feedback-preferences");
        ui.View.RequestViewPreference = () => ui.View.LibrarySettings.Save(path);
        bool locked = ui.View.DropletSelectionLocked;
        var lockButton = ui.View.AssistButtonBounds[6];
        ui.View.PointerMove(lockButton.X + 5, lockButton.Y + 5, false, false); ui.Paint();
        ui.ClickText(L.Get("assist.lockDropletSelection"));
        Check(LibrarySettings.Load(path).LockDropletSelection != locked && !ui.View.IsDirty, "Droplet selection lock did not persist independently of content.");
    }

    public static void MarkersAndBreak()
    {
        var map = new MapDocument { DurationMs = 3600000, IsDemo = false };
        map.TimingPoints.Add(new() { TimeMs = 0, Effects = 1, BeatLengthMs = 500 });
        map.Fruits.Add(new() { TimeMs = 10000, X = 100 });
        OsuTimeline.AddBreak(map, 1700, 2200);
        OsuTimeline.AddBookmark(map, 2000);
        SongSetup.Set(map, "General", "PreviewTime", "2000");
        var ui = new Ui(false); ui.LoadDocument(map);
        ui.View.UpdateTransport(2000, map.DurationMs, false, false, false, null, null); ui.Paint();
        var overview = ui.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
        foreach (var bounds in new[] { overview, ui.View.ObjectTimelineBounds })
        {
            var calls = ui.Canvas.PaintCalls;
            int preview = calls.FindIndex(c => c.Line is { Color: 0xFFD34A, Y1: var y, Y2: var bottom }
                && y == bounds.Y - 3 && bottom == bounds.Bottom + 2);
            int bookmark = calls.FindIndex(c => c.Line is { Color: 0x4B9EF5, Y1: var y }
                && y == bounds.Y + bounds.Height / 2);
            Check(preview >= 0 && bookmark > preview && calls[preview].Line!.Value.X1 == calls[bookmark].Line!.Value.X1,
                "Coincident bookmarks must draw above the full-height preview line on both timelines.");
            Check(!ui.Canvas.Fills.Any(f => f.Color is 0xFFD34A or 0x4B9EF5),
                "Timeline location markers must retain their line shapes.");
        }
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xBCB1AE && f.Bounds.Y > overview.Y && f.Bounds.Y < overview.Bottom && f.Bounds.Width >= 2),
            "A short break vanished at full-song overview scale.");
        Check(ui.View.Document.ContentEquals(map), "Timeline marker drawing changed the beatmap.");
        map = new MapDocument { DurationMs = 20000, IsDemo = false, ApproachRate = 9 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 5000, X = 200 }, new() { TimeMs = 10000, X = 300 }]);
        ui.LoadDocument(map); ui.ClickFruit(map.Fruits[1].Id); ui.Key(46);
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 1, "Unmapping a section did not produce its break.");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xBCB1AE), "New gap break did not refresh the overview.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(map), "Section deletion and generated break did not undo together.");
    }

    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (ui.View.VersionHistoryBusy && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(10); }
        ui.Paint();
        Check(!ui.View.VersionHistoryBusy, "Previous save worker did not finish.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
