using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class EditorPerformance
{
    public static int RunAnchorDrag(string path)
    {
        var document = ProjectSerializer.ReadFile(path);
        if (document.Tracks.Count == 0) ImportedSliderEditing.ConvertAll(document, derandomizeDroplets: false);
        var view = new EditorView(false); view.LoadDocument(document);
        var canvas = new CountCanvas(); view.Render(canvas, 1440, 900);
        var track = view.Document.Tracks.First(t => t.Nodes.Count > 2 && t.SpanCount == 1);
        var node = track.Nodes[1];
        var before = view.Document.DeepClone();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var kind = typeof(EditorView).GetNestedType("DragKind", System.Reflection.BindingFlags.NonPublic)!;
        typeof(EditorView).GetMethod("BeginNodeDrag", flags)!.Invoke(view,
            [track, node, Enum.Parse(kind, "Anchor"), 500f, 500f]);
        var samples = new List<double>();
        for (int i = 0; i < 24; i++)
        {
            var watch = Stopwatch.StartNew();
            view.PointerMove(500f + (i % 2 == 0 ? 6 : -6), 500f, false, false);
            view.Render(canvas, 1440, 900);
            if (i >= 4) samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        if (view.Document.ContentEquals(before)) throw new Exception("Benchmark did not move the anchor.");
        samples.Sort();
        Console.WriteLine($"Anchor drag CPU: tracks={document.Tracks.Count}, nodes={document.Tracks.Sum(t => t.Nodes.Count)}, median={samples[10]:F2} ms, p95={samples[18]:F2} ms");
        view.CancelInteraction(); view.Render(canvas, 1440, 900);
        if (!view.Document.ContentEquals(before)) throw new Exception("Cancelled anchor drag changed content.");
        view.NewProject();
        return 0;
    }

    public static int RunSliderDrag(string path)
    {
        var document = OsuBeatmapReader.ReadFile(path);
        Console.WriteLine($"Slider drag CPU benchmark: sliders={document.ImportedSliders.Count}, fruits={document.Fruits.Count}");
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var begin = typeof(EditorView).GetMethod("BeginSliderObjectDrag", flags)!;
        var move = typeof(EditorView).GetMethod("MoveSliderObject", flags)!;
        foreach (bool tail in new[] { false, true })
        {
            var view = new EditorView(false); view.LoadDocument(document);
            var canvas = new CountCanvas(); view.Render(canvas, 1440, 900);
            var source = document.ImportedSliders.First(s => s.TimeMs >= 30000 && s.SpanCount == 1);
            var edges = view.Conversion.Objects.Where(o => o.SourceId == source.Id && o.Kind == CatchObjectKind.Fruit).ToArray();
            var target = tail ? edges[^1] : edges[0];
            Console.WriteLine($"{(tail ? "Tail" : "Head")} at {target.TimeMs:F2} ms, x={target.X:F2}");
            begin.Invoke(view, [target, 500f, 500f]);
            for (int i = 0; i < 8; i++)
            {
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                move.Invoke(view, [500f + (i % 2 == 0 ? 6 : -6)]);
                double moveMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart(); view.Render(canvas, 1440, 900);
                Console.WriteLine($"  move={moveMs:F2} ms, render={watch.Elapsed.TotalMilliseconds:F2} ms, allocated={(GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024} KiB");
            }
            view.CancelInteraction(); view.NewProject();
        }
        return 0;
    }

    public static int RunMap(string path)
    {
        var watch = Stopwatch.StartNew();
        var document = OsuBeatmapReader.ReadFile(path);
        Console.WriteLine($"Read: {watch.Elapsed.TotalMilliseconds:F1} ms; sliders={document.ImportedSliders.Count}; timing={document.TimingPoints.Count}");
        var view = new EditorView();
        watch.Restart(); view.LoadDocument(document);
        Console.WriteLine($"LoadDocument: {watch.Elapsed.TotalMilliseconds:F1} ms");
        watch.Restart(); _ = view.Conversion;
        Console.WriteLine($"Conversion: {watch.Elapsed.TotalMilliseconds:F1} ms");
        var canvas = new CountCanvas();
        view.Render(canvas, 1440, 900);
        view.Wheel(view.CanvasPlotBounds.X, view.CanvasPlotBounds.Bottom,
            (float)(120 * Math.Log(.32 / view.CanvasZoom) / Math.Log(1.16)), true, true);
        var snap = view.SnapSliderBounds;
        view.PointerDown(snap.Right - 31, snap.Y + snap.Height / 2, 0, false, false);
        view.PointerUp(snap.Right - 31, snap.Y + snap.Height / 2, 0);
        view.RequestScheduleHitsound = (_, _) => { };
        view.RequestPrepareHitsound = _ => { };
        view.HitsoundLookaheadMs = 250;
        view.StartHitsounds(89038);
        var frames = new List<double>();
        var transport = new List<double>();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            watch.Restart();
            view.UpdateTransport(89038 + i * 1000d / 120, document.DurationMs, true, true, false, null, document.AudioPath);
            if (i >= 20) transport.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart(); view.Render(canvas, 1440, 900);
            if (i >= 20) frames.Add(watch.Elapsed.TotalMilliseconds);
        }
        frames.Sort();
        transport.Sort();
        Console.WriteLine($"Transport + hitsound scheduling CPU: median={transport[50]:F3} p95={transport[95]:F3} ms (silent callbacks)");
        Console.WriteLine($"Playback CPU render: median={frames[50]:F3} p95={frames[95]:F3} ms; allocations={(GC.GetAllocatedBytesForCurrentThread() - allocated) / 120 / 1024} KiB/frame");
        foreach (string phase in new[] { "EnsureConversion", "DrawChrome", "DrawInspector", "DrawCanvas", "DrawObjectTimeline", "DrawTransport", "DrawStatus" })
        {
            var method = typeof(EditorView).GetMethod(phase, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Action run = phase == "EnsureConversion" ? method.CreateDelegate<Action>(view) : () => method.Invoke(view, [canvas]);
            var samples = new List<double>();
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++) { watch.Restart(); run(); samples.Add(watch.Elapsed.TotalMilliseconds); }
            samples.Sort();
            Console.WriteLine($"  {phase}: median={samples[20]:F3} p95={samples[38]:F3} ms; allocated={(GC.GetAllocatedBytesForCurrentThread() - bytes) / 40 / 1024} KiB/call");
        }
        view.NewProject();
        return 0;
    }
    public static int Run()
    {
        foreach (var (count, sliders) in new[] { (1000, false), (10000, false), (1000, true) })
        {
            var doc = new MapDocument { IsDemo = false, DurationMs = count * 500 + 10000 };
            for (int i = 0; i < count; i++)
            {
                if (!sliders) doc.Fruits.Add(new Fruit { X = 100 + i % 4 * 70, TimeMs = 1000 + i * 500 });
                else
                {
                    var track = new CurveTrack { Kind = CurveKind.Linear };
                    track.Nodes.Add(new Anchor { TimeMs = 1000 + i * 500, X = 100 });
                    track.Nodes.Add(new Anchor { TimeMs = 1250 + i * 500, X = 180 });
                    doc.Tracks.Add(track);
                }
            }
            var view = new EditorView(); var canvas = new CountCanvas(); view.LoadDocument(doc);
            void Render() => view.Render(canvas, 1440, 900);
            Render(); view.UpdateTransport(1000, doc.DurationMs, true, false, false, null, "fixture.ogg"); Render();
            var p = view.PlayfieldBounds;
            float X(double x) => p.X + (float)(x / 512 * p.Width);
            float Y(double t) => p.Bottom - (float)((t - view.ViewStartMs) * view.PixelsPerMs);
            for (int i = 0; i < 3000 && view.StarRatingsRefreshing; i++) { _ = view.CurrentStarRating; Thread.Sleep(10); }
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew(); view.PointerDown(X(100), Y(1000), 0, false, false); double down = watch.Elapsed.TotalMilliseconds;
            double conversionMs = 0, drawMs = 0, moveMs = 0;
            for (int i = 0; i < 20; i++)
            {
                watch.Restart(); view.PointerMove(X(120 + i % 8), Y(1000), false, false); moveMs += watch.Elapsed.TotalMilliseconds;
                watch.Restart(); _ = view.Conversion; conversionMs += watch.Elapsed.TotalMilliseconds;
                watch.Restart(); Render(); double draw = watch.Elapsed.TotalMilliseconds; drawMs += draw;
            }
            view.PointerUp(X(120), Y(1000), 0);
            double movedX = sliders ? view.Document.Tracks[0].Nodes[0].X : view.Document.Fruits[0].X;
            if (Math.Abs(movedX - 120) > 0.01) throw new Exception("Benchmark did not drag the target object.");
            Console.WriteLine($"{count} {(sliders ? "sliders" : "fruits")}: down={down:F2} ms, drag move={moveMs / 20:F2} conversion={conversionMs / 20:F2} draw={drawMs / 20:F2} ms/frame; draw commands={canvas.Commands}; allocated={(GC.GetAllocatedBytesForCurrentThread()-bytes)/20/1024:F0} KiB/frame");
            view.KeyDown(70, false, false);
            view.Performance.Enabled = true;
            watch.Restart(); view.PointerDown(X(490), Y(1100), 0, false, false); view.PointerUp(X(490), Y(1100), 0); Render();
            Console.WriteLine($"  Add + render={watch.Elapsed.TotalMilliseconds:F2} ms");
            if (view.Performance.Drain() is { } report) Console.WriteLine($"  {report}");
            view.Performance.Enabled = false;
            if (view.Document.Fruits.Count != (sliders ? 1 : count + 1)) throw new Exception("Benchmark did not add a fruit.");
            view.KeyDown(90, true, false);
            if (view.Document.Fruits.Count != (sliders ? 0 : count)) throw new Exception("Large-map add undo failed.");
            view.KeyDown(90, true, false);
            double restoredX = sliders ? view.Document.Tracks[0].Nodes[0].X : view.Document.Fruits[0].X;
            if (Math.Abs(restoredX - 100) > 0.01) throw new Exception("Large-map drag undo failed.");
            view.NewProject();
        }
        return 0;
    }
    private sealed class CountCanvas : ICanvas
    {
        public int Commands;
        public void Fill(Rect r,uint c,float radius=0, float opacity = 1) { if(r.X==0 && r.Y==0) Commands=0; Commands++; }
        public void Stroke(Rect r,uint c,float width=1,float radius=0)=>Commands++;
        public void Line(float x,float y,float x2,float y2,uint c,float width=1,float opacity=1)=>Commands++;
        public void Circle(float x,float y,float radius,uint c,bool filled=true,float width=1,float opacity=1)=>Commands++;
        public void Text(string t,float x,float y,float size,uint c,float maxWidth=10000,bool bold=false)=>Commands++;
        public bool Image(string p,Rect r,uint tint=0xFFFFFF,Rect? source=null,float opacity=1) { Commands++;return false; }
        public void Clip(Rect r) { }
        public void Unclip() { }
    }
}
