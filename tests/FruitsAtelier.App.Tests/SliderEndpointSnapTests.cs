using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderEndpointSnapTests
{
    public static void Performance()
    {
        var document = new MapDocument { DurationMs = 180000, BeatLengthMs = 700, DerandomizeFSliderDroplets = true };
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.Add(new() { TimeMs = 875, X = 100 });
        track.Nodes.Add(new() { TimeMs = 1750, X = 350 });
        document.Tracks.Add(track);
        for (int i = 0; i < 300; i++) document.Fruits.Add(new() { TimeMs = 4000 + i * 400, X = 256 });
        for (int i = 0; i < 50; i++)
        {
            var slider = new ImportedSlider { TimeMs = 5000 + i * 1400, X = 200, Y = 192, PathType = 'L', PixelLength = 100 };
            slider.ControlPoints.Add(new(200, 192)); slider.ControlPoints.Add(new(300, 192)); document.ImportedSliders.Add(slider);
        }
        for (int i = 0; i < 20; i++)
        {
            document.BananaShowers.Add(new() { TimeMs = 5000 + i * 6000, EndTimeMs = 5500 + i * 6000 });
            document.TimingPoints.Add(new() { TimeMs = 4000 + i * 6000, BeatLengthMs = i % 2 == 0 ? 700 : 600 });
        }
        foreach (bool own in new[] { false, true })
        {
            document.Tracks[0].EndpointSnap = own ? new() : null;
            var ui = new Ui(false); ui.LoadDocument(document); ui.SelectTrack(track.Id);
            var before = ui.View.Document.DeepClone();
            ui.DownMap(1750, 350);
            var inputs = new List<double>(); var paints = new List<double>();
            long inputBytes = 0, paintBytes = 0;
            for (int i = 0; i < 64; i++)
            {
                var p = ui.ScreenAt(i % 2 == 0 ? 1960 : 1800, 350);
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                ui.View.PointerMove(p.X, p.Y, false, false);
                double input = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                long allocation = GC.GetAllocatedBytesForCurrentThread() - bytes;
                if (i == 0) Console.WriteLine($"Endpoint own={own} cold input={input:F3}ms/{allocation}B");
                if (i >= 4) { inputs.Add(input); inputBytes += allocation; }
                bytes = GC.GetAllocatedBytesForCurrentThread(); started = System.Diagnostics.Stopwatch.GetTimestamp();
                ui.Paint();
                double paint = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocation = GC.GetAllocatedBytesForCurrentThread() - bytes;
                if (i == 0) Console.WriteLine($"Endpoint own={own} cold paint={paint:F3}ms/{allocation}B");
                if (i >= 4) { paints.Add(paint); paintBytes += allocation; }
            }
            inputs.Sort(); paints.Sort();
            Console.WriteLine($"Endpoint own={own} warm input median={inputs[30]:F3}ms p95={inputs[57]:F3}ms {inputBytes / 60}B; paint median={paints[30]:F3}ms p95={paints[57]:F3}ms {paintBytes / 60}B (recording canvas; native presentation excluded)");
            ui.View.CancelInteraction(); ui.Paint();
            Check(ui.View.Document.ContentEquals(before), "Benchmark cancellation restores all content");
        }
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                foreach (var mode in Enum.GetValues<FruitsAtelier.App.Editor.SliderEditingMode>()) CheckTrack(mode);
                CheckImported();
            }
            var document = new MapDocument { BeatLengthMs = 840 };
            document.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = 660 });
            var timing = new TimingMap.Lookup(document);
            var auto = new SliderEndpointSnap();
            foreach (int divisor in SliderEndpointSnap.Divisors)
            {
                double expected = 1000 + 660d / divisor;
                Near(auto.Snap(timing, expected + .01, true), expected, "Auto snap after red-line change");
            }
            Near(auto.Snap(timing, 999.99, false), 1000, "Timing boundary snap");
            var ui = new Ui(false); ui.LoadDocument(document);
            foreach (int divisor in new[] { 10, 11 })
            {
                var preference = new MapEditingPreferences { BeatSnapDivisor = divisor };
                Check(preference.BeatSnapDivisor == divisor, "Irregular global grid preference persists");
                new BananaShowerPattern(1, divisor, 1, divisor).Validate();
                ui.SetSnapDivisor(divisor); ui.Key('F'); ui.ClickMap(1400, 256);
                Near(ui.View.Document.Fruits.Last().TimeMs, timing.Snap(1400, divisor), "Global irregular placement");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void CheckTrack(FruitsAtelier.App.Editor.SliderEditingMode mode)
    {
        var document = new MapDocument { DurationMs = 10000, BeatLengthMs = 700, DerandomizeFSliderDroplets = true };
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.Add(new() { TimeMs = 875, X = 100 });
        track.Nodes.Add(new() { TimeMs = 1750, X = 350 });
        document.Tracks.Add(track);
        var other = track.DeepClone(); other.Id = Guid.NewGuid();
        foreach (var node in other.Nodes) { node.Id = Guid.NewGuid(); node.TimeMs += 2800; }
        document.Tracks.Add(other);
        var ui = new Ui(false); ui.LoadDocument(document); ui.View.SetSliderEditingMode(mode); ui.Paint();
        ui.SelectTrack(track.Id); ui.View.ShowSliderInspector(); ui.Paint();
        ClickInspector(ui, L.Get("sliderSnap.toggle", L.Get("settings.romanisedOff")));
        Check(Current().EndpointSnap == new SliderEndpointSnap(), "Inspector enables auto snap on selected slider");
        Check(ui.View.Document.Tracks.Single(t => t.Id == other.Id).EndpointSnap is null, "Other slider follows global grid");
        int global = ui.View.SnapDivisor;
        ClickInspector(ui, L.Get("sliderSnap.end", L.Get("sliderSnap.auto"))); ui.ClickObjectAction("1/7");
        Near(CurveMath.EndTimeMs(Current()), 1800, "Manual tail snap");
        Check(Current().EndpointSnap?.EndDivisor == 7 && ui.View.SnapDivisor == global, "Tail divisor stays local");
        ClickInspector(ui, L.Get("sliderSnap.start", L.Get("sliderSnap.auto"))); ui.ClickObjectAction("1/4");
        Near(Current().Nodes[0].TimeMs, 875, "Independent quarter head");
        Near(CurveMath.EndTimeMs(Current()), 1800, "Head snap preserves tail");
        var saved = ui.View.Document.DeepClone();
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(saved)).ContentEquals(saved), "Single project preserves snapping");
        var project = BeatmapProject.FromDocuments([saved]);
        Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project)).Difficulties.Single().Document.ContentEquals(saved), "Multi project preserves snapping");
        Check(OsuBeatmapWriter.Serialize(saved).ObjectSequenceMatches, "Irregular slider exports playable events");
        ui.Key('Z', ctrl: true); Check(Current().EndpointSnap?.StartDivisor is null, "Snap selection undoes");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(saved), "Snap selection redoes");
        ui.SelectTrack(track.Id); ui.View.ShowSliderInspector(); ui.Paint();
        ClickInspector(ui, L.Get("sliderSnap.end", "1/7")); ui.ClickObjectAction(L.Get("sliderSnap.auto"));
        var before = ui.View.Document.DeepClone();
        typeof(FruitsAtelier.App.Editor.EditorView).GetField("snap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(ui.View, false);
        // The visible tail can move to a different grid while the head stays at 1/4.
        ui.SelectTrack(track.Id); ui.DownMap(1800, 350); ui.MoveMap(1960, 350); ui.UpMap(1960, 350);
        Near(CurveMath.EndTimeMs(Current()), 1960, "Tail drag reaches 1/5 grid");
        Near(Current().Nodes[0].TimeMs, 875, "Tail drag preserves quarter head");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "Endpoint drag is one undo step");
        ui.Key('Y', ctrl: true); Near(CurveMath.EndTimeMs(Current()), 1960, "Endpoint drag redoes");
        CurveTrack Current() => ui.View.Document.Tracks.Single(t => t.Id == track.Id);
    }

    private static void CheckImported()
    {
        var document = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,700\n[HitObjects]\n100,192,875,2,0,L|350:192,2,125\n");
        var ui = new Ui(false); ui.LoadDocument(document);
        ui.View.ToggleOsuCanvas(); ui.Paint();
        var slider = ui.View.Document.ImportedSliders.Single();
        slider.EndpointSnap = new(4, 7); ui.View.UpdateTransport(1700, 10000, true, false, false, null, null); ui.Paint();
        // Timeline tail dragging changes duration, retaining the repeat count.
        var head = ui.View.OsuCanvasScreen(slider.X, slider.Y); ui.Click(head.X, head.Y); ui.Paint();
        var intervals = (System.Collections.IEnumerable)typeof(FruitsAtelier.App.Editor.EditorView)
            .GetField("timelineObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
        var item = intervals.Cast<(Guid Id, double Time, FruitsAtelier.App.Rendering.Rect Bounds)>().Single(t => t.Id == slider.Id);
        float x = item.Bounds.Right - item.Bounds.Height / 2, y = item.Bounds.Y + item.Bounds.Height / 2;
        double originalEnd = ImportedSliderConverter.EndTimeMs(ui.View.Document, slider);
        float to = x + (float)((1900 - originalEnd) * ui.View.ObjectTimelinePixelsPerMs);
        ui.View.PointerDown(x, y, 0, false, false);
        ui.View.PointerMove(to, y, false, false); ui.Paint();
        ui.View.PointerUp(to, y, 0); ui.Paint();
        slider = ui.View.Document.ImportedSliders.Single();
        Near(ImportedSliderConverter.EndTimeMs(ui.View.Document, slider), 1900, "Legacy custom duration");
        Check(slider.SpanCount == 2, "Legacy custom duration retains repeats");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document)).ContentEquals(ui.View.Document), "Legacy settings persist");
        var after = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); Near(ImportedSliderConverter.EndTimeMs(ui.View.Document, ui.View.Document.ImportedSliders.Single()), originalEnd, "Legacy timeline resize undo");
        ui.Key('Y', ctrl: true); Check(after.ContentEquals(ui.View.Document), "Legacy timeline resize redo");
        var converted = FruitsAtelier.Core.ImportedSliderEditing.ConvertToTrack(ui.View.Document, slider.Id);
        Check(converted.Track.Id == slider.Id && ui.View.Document.Tracks.Single().EndpointSnap == new SliderEndpointSnap(4, 7), "Legacy conversion retains snap settings");
        var back = SliderRepresentationEditing.ConvertToLegacy(ui.View.Document, slider.Id);
        Check(back.Success && ui.View.Document.ImportedSliders.Single().EndpointSnap == new SliderEndpointSnap(4, 7), "FSlider conversion retains snap settings");
    }

    internal static void ClickInspector(Ui ui, string text)
    {
        for (int i = 0; i < 30; i++)
        {
            var label = ui.Canvas.Texts.Last(t => t.Value == text);
            var viewport = ui.View.SliderInspectorViewport;
            if (label.Y >= viewport.Y && label.Y + 26 <= viewport.Bottom) { ui.Click(label.X + 4, label.Y + 5); return; }
            ui.View.Wheel(viewport.X + 10, viewport.Y + 10, label.Y < viewport.Y ? 120 : -120, false, false); ui.Paint();
        }
        throw new Exception("Inspector action outside viewport: " + text);
    }

    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < .001, $"{message}: {actual} != {expected}");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
