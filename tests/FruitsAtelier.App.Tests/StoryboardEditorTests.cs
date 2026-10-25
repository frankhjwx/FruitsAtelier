using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class StoryboardEditorTests
{
    public static int Run(string path)
    {
        var document = OsuBeatmapReader.ReadFile(path);
        var clock = new Clock(); var view = new EditorView(false, timeProvider: clock); var canvas = new RecordingCanvas();
        view.LibrarySettings.TestplayStartupDelaySeconds = 0;
        view.LibrarySettings.BackgroundDim = view.LibrarySettings.MappingCanvasBackgroundDim = 0;
        view.LoadDocument(document); view.CloseLibrary();
        var sounds = new List<Hitsound>();
        view.RequestHitsound = sounds.Add; view.RequestPrepareHitsound = _ => { };
        void Paint() { canvas.Clear(); view.Render(canvas, 1440, 900); }
        var warm = Stopwatch.StartNew();
        do { Paint(); Thread.Sleep(5); }
        while ((Pending(view, "storyboardLoad") || Pending(view, "storyboardGameplayLoad") || view.LegacyDropletRefreshBusy || view.LibraryOpening) && warm.Elapsed.TotalSeconds < 15);
        if (Pending(view, "storyboardLoad") || Pending(view, "storyboardGameplayLoad") || view.LibraryOpening) throw new TimeoutException("Storyboard gameplay metadata did not settle.");
        var source = view.Document.DeepClone();
        view.UpdateTransport(0, document.DurationMs, true, false, false, null, document.AudioPath); Paint();
        view.UpdateTransport(3340, document.DurationMs, true, false, false, null, document.AudioPath); Paint();
        if (HasFirstDot(view, 0x00FF00) || !HasFirstDot(view, 0xFF0000)) throw new Exception("Paused preview seek activated a HitSound trigger without emitting its sound.");
        view.UpdateTransport(0, document.DurationMs, true, false, false, null, document.AudioPath); Paint();
        view.StartHitsounds(0);
        for (int time = 0; time <= 3340; time += 20)
        { view.UpdateTransport(time, document.DurationMs, true, true, false, null, document.AudioPath); Paint(); }
        if (!HasFirstDot(view, 0x00FF00) || sounds.Count == 0) throw new Exception("Actual preview sound emission did not activate the matching HitSound trigger.");
        view.UpdateTransport(4000, document.DurationMs, true, false, false, null, document.AudioPath); Paint();
        if (!HasFirstDot(view, 0x00FF00)) throw new Exception("Seek discarded an already-emitted sound trigger.");
        view.UpdateTransport(3200, document.DurationMs, true, false, false, null, document.AudioPath); Paint();
        if (HasFirstDot(view, 0x00FF00)) throw new Exception("Preview backward seek retained a future hit trigger's state.");
        view.UpdateTransport(3340, document.DurationMs, true, false, false, null, document.AudioPath); Paint();
        if (!HasFirstDot(view, 0x00FF00)) throw new Exception("Seek failed to recover a recorded sound trigger at its original time.");
        Console.WriteLine("PASS EXXTRA preview requires actual sound emission and retains timestamped triggers across seeks");
        foreach (bool autoplay in new[] { true, false })
        {
            view.UpdateTransport(0, document.DurationMs, true, false, false, null, document.AudioPath); Paint();
            view.UpdateTransport(0, document.DurationMs, false, false, false, null, document.AudioPath);
            view.StartTestplay(); Paint();
            if (!view.IsTestplaying) throw new Exception("Actual Editor StartTestplay did not start.");
            if (autoplay) { view.KeyDown(9, false, false); view.KeyUp(9); }
            else view.KeyDown(view.LibrarySettings.TestplayRightKey, false, false);
            double start = view.PlayheadMs;
            while (view.IsTestplaying && view.PlayheadMs < 3340) { clock.Advance(5); Paint(); }
            if (!view.IsTestplaying) throw new Exception("Testplay ended before the first trigger window.");
            if (HasFirstDot(view, 0x00FF00) != autoplay) throw new Exception("Actual hit/miss judgement did not choose the matching EXXTRA dot colour.");
            if (!autoplay && !HasFirstDot(view, 0xFF0000)) throw new Exception("EXXTRA failed-catch red dot was not retained.");
            view.KeyUp(view.LibrarySettings.TestplayRightKey);
            double pausePosition = view.PlayheadMs;
            view.KeyDown(80, true, false); view.KeyUp(80); clock.Advance(100); Paint();
            if (Math.Abs(view.PlayheadMs - pausePosition) > 1) throw new Exception("Paused Editor testplay advanced its storyboard time.");
            view.StopTestplay(); Paint();
            Console.WriteLine($"PASS EXXTRA actual Editor testplay {(autoplay ? "caught" : "missed")} judgement→HitSound→visible colour and pause; start={start:F2}");
        }
        if (sounds.Count == 0) throw new Exception("Actual successful testplay produced no hitsound events.");
        if (!source.ContentEquals(view.Document)) throw new Exception("Storyboard preview or testplay changed source content.");
        view.StopFileMonitoring(); view.StopMapsetVerifier(); view.StopDiscussions(); view.CompleteLibraryCacheWrites();
        ScheduledPreview(path);
        return 0;
    }

    private static void ScheduledPreview(string path)
    {
        var map = OsuBeatmapReader.ReadFile(path); var view = new EditorView(false); var canvas = new RecordingCanvas();
        var queued = new List<double>(); view.RequestScheduleHitsound = (_, time) => queued.Add(time); view.RequestPrepareHitsound = _ => { };
        view.LoadDocument(map); view.CloseLibrary();
        void Paint() { canvas.Clear(); view.Render(canvas, 1440, 900); }
        var timer = Stopwatch.StartNew();
        do { Paint(); Thread.Sleep(5); } while ((view.LibraryOpening || Pending(view, "storyboardLoad") || Pending(view, "storyboardGameplayLoad")) && timer.Elapsed.TotalSeconds < 15);
        view.UpdateTransport(0, map.DurationMs, true, false, false, null, map.AudioPath); Paint();
        view.RequestSeek = time => view.UpdateTransport(time, map.DurationMs, true, false, false, null, map.AudioPath);
        void Seek(double time)
        {
            var bounds = view.TimeDisplayBounds;
            view.PointerDown(bounds.X + 10, bounds.Y + 10, 0, false, false); view.PointerUp(bounds.X + 10, bounds.Y + 10, 0); Paint();
            if (!view.TimeJumpVisible) throw new Exception("Timestamp control did not open for scheduled-trigger seek.");
            view.KeyDown('A', true, false); view.PasteTimeJumpText(time.ToString(System.Globalization.CultureInfo.InvariantCulture), view.TimeJumpSession);
            view.KeyDown(13, false, false); view.KeyUp(13); Paint();
        }
        Seek(3200); view.StartHitsounds(3200);
        view.UpdateTransport(3200, map.DurationMs, true, true, false, null, map.AudioPath); Paint();
        if (queued.Count == 0 || HasFirstDot(view, 0x00FF00)) throw new Exception("Scheduling either omitted the future sound or activated its trigger early.");
        view.UpdateTransport(3240, map.DurationMs, true, false, false, null, map.AudioPath); Paint();
        Seek(3340);
        if (HasFirstDot(view, 0x00FF00)) throw new Exception("A future scheduled sound cancelled by pause/seek activated a trigger.");
        Seek(3200); view.StartHitsounds(3200);
        view.UpdateTransport(3200, map.DurationMs, true, true, false, null, map.AudioPath); Paint();
        view.UpdateTransport(3340, map.DurationMs, true, true, false, null, map.AudioPath); Paint();
        view.UpdateTransport(3340, map.DurationMs, true, false, false, null, map.AudioPath); Paint();
        if (!HasFirstDot(view, 0x00FF00)) throw new Exception("A consumed scheduled sound did not record its trigger.");
        Seek(3200); if (HasFirstDot(view, 0x00FF00)) throw new Exception("Recorded trigger leaked before its timestamp.");
        Seek(3340); if (!HasFirstDot(view, 0x00FF00)) throw new Exception("Actual seeks discarded a consumed trigger record.");
        view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
        Console.WriteLine("PASS scheduled preview sound remains pending until consumed, pause/seek cancels future events and retains actual trigger history");
    }

    private static bool Pending(EditorView view, string name) => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) is Task;
    private static bool HasFirstDot(EditorView view, uint colour)
    {
        var states = (List<StoryboardSpriteState>)typeof(EditorView).GetField("storyboardFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        return states.Any(s => Path.GetFileName(s.Path) == "dot.png" && Math.Abs(s.X + 97) < .01 && Math.Abs(s.Y - 286) < .01 && s.Colour == colour && s.Opacity > 0);
    }
    private sealed class Clock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance(long amount) => milliseconds += amount;
    }
}
