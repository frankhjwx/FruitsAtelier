using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class EditorPerformance
{
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
            watch.Restart(); view.PointerDown(X(490), Y(1100), 0, false, false); view.PointerUp(X(490), Y(1100), 0); Render();
            Console.WriteLine($"  Add + render={watch.Elapsed.TotalMilliseconds:F2} ms");
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
        public void Fill(Rect r,uint c,float radius=0) { if(r.X==0 && r.Y==0) Commands=0; Commands++; }
        public void Stroke(Rect r,uint c,float width=1,float radius=0)=>Commands++;
        public void Line(float x,float y,float x2,float y2,uint c,float width=1,float opacity=1)=>Commands++;
        public void Circle(float x,float y,float radius,uint c,bool filled=true,float width=1)=>Commands++;
        public void Text(string t,float x,float y,float size,uint c,float maxWidth=10000,bool bold=false)=>Commands++;
        public bool Image(string p,Rect r,uint tint=0xFFFFFF,Rect? source=null) { Commands++;return false; }
        public void Clip(Rect r) { }
        public void Unclip() { }
    }
}
