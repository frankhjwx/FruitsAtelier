using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class CanvasOverscanTests
{
    public static void Run()
    {
        foreach (bool native in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 10000, CircleSize = 0, ApproachRate = 5 };
            map.Fruits.AddRange([new() { TimeMs = 1000, X = 0, Y = 192 }, new() { TimeMs = 1000, X = 512, Y = 192 }]);
            var ui = new Ui(false); ui.View.LibrarySettings.LazerEditing = false;
            ui.LoadDocument(map); ui.Resize(1100, 900); ui.OpenPreview();
            if (native) ui.View.ToggleOsuCanvas();
            else
            {
                ui.View.LibrarySettings.UniformCanvasScale = false;
                double zoom = ui.View.CanvasZoom * ui.View.CanvasPlotBounds.Width / ui.View.PlayfieldBounds.Width;
                typeof(EditorView).GetMethod("SetCanvasScales", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(ui.View, [zoom, ui.View.CanvasTimeZoom, ui.View.CanvasPlotBounds.Bottom]);
            }
            ui.View.UpdateTransport(500, 10000, true, false, false, null, null); ui.Paint();
            var plot = ui.View.CanvasPlotBounds;
            var field = native ? ui.View.OsuCanvasBounds : ui.View.PlayfieldBounds;
            var clip = ui.View.CanvasRenderBounds;
            var canvas = new OrderedCanvas();
            ui.View.Render(canvas, ui.Width, ui.Height);
            Check(plot == ui.View.CanvasPlotBounds && field == (native ? ui.View.OsuCanvasBounds : ui.View.PlayfieldBounds), "Overscan changed logical coordinates");
            Check(clip.X < plot.X && clip.Right > plot.Right && clip.Y == plot.Y && clip.Bottom == plot.Bottom,
                "Canvas overscan must widen only the horizontal display area");
            Check(ui.View.PreviewViewport.Width > 0 && clip.Right <= ui.View.PreviewViewport.X,
                "Canvas overscan paints into the preview dock");
            var dots = canvas.Dots.Where(d => d.Clip == clip && d.Dot.Opacity > 0).ToArray();
            Check(dots.Any(d => d.Dot.X - d.Dot.Radius < plot.X && d.Dot.X + d.Dot.Radius > plot.X), "Left-edge objects are clipped at the logical field");
            Check(dots.Any(d => d.Dot.X - d.Dot.Radius < plot.Right && d.Dot.X + d.Dot.Radius > plot.Right), "Right-edge objects are clipped at the logical field");
            Check(dots.Max(d => d.Order) < canvas.Tools.Min(), "Floating tool icons are painted below canvas objects");
            var before = ui.View.Document.DeepClone();
            foreach (var (index, name) in new[] { (1, "Fruit"), (2, "Slider"), (3, "Banana"), (0, "Select") })
            {
                var button = ui.View.ToolButtonBounds[index];
                ui.Click(button.X + button.Width / 2, button.Y + button.Height / 2);
                Check(ui.View.ActiveTool == name && ui.View.Document.ContentEquals(before), "Canvas objects intercepted a floating tool button");
            }
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class OrderedCanvas : ICanvas
    {
        private readonly RecordingCanvas canvas = new();
        private readonly Stack<Rect> clips = new();
        private int order;
        public List<(int Order, Rect? Clip, RecordingCanvas.Dot Dot)> Dots { get; } = [];
        public List<int> Tools { get; } = [];
        public void Fill(Rect r, uint color, float radius = 0, float opacity = 1) { order++; canvas.Fill(r, color, radius, opacity); }
        public void Stroke(Rect r, uint color, float width = 1, float radius = 0) { order++; canvas.Stroke(r, color, width, radius); }
        public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1)
        { order++; canvas.Line(x1, y1, x2, y2, color, width, opacity); }
        public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1)
        {
            Dots.Add((order++, clips.TryPeek(out var clip) ? clip : null, new(x, y, radius, filled, color, opacity)));
            canvas.Circle(x, y, radius, color, filled, width, opacity);
        }
        public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false)
        { order++; canvas.Text(text, x, y, size, color, maxWidth, bold); }
        public bool Image(string path, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1)
        {
            if (path.Contains(Path.Combine("icons", "tools"))) Tools.Add(order);
            order++;
            return canvas.Image(path, destination, tint, source, opacity);
        }
        public void Clip(Rect r) { clips.Push(r); canvas.Clip(r); }
        public void Unclip() { clips.Pop(); canvas.Unclip(); }
    }
}
