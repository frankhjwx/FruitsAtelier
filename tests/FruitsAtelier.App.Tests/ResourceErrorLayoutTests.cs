using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;

internal static class ResourceErrorLayoutTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        var view = new EditorView(loadDemo: false);
        var errors = new ErrorRows("Missing resource: " + string.Concat(Enumerable.Repeat("😀", 500000)));
        Field("resourcePage").SetValue(view, true);
        Field("resourceErrors").SetValue(view, errors);
        typeof(EditorView).GetProperty("LibraryVisible")!.SetValue(view, true);
        var canvas = new CountingCanvas();
        view.Render(canvas, 980, 620);
        if (errors.Reads != 1 || canvas.Measurements > 10000 || canvas.MaximumMeasuredLength > 8192
            || canvas.Texts.Count > 64 || !canvas.Texts.Any(text => text.Contains('…')) || canvas.MaximumEllipsisBottom > 600)
            throw new Exception("Resource errors measured an unbounded message or visited offscreen rows.");
        int measured = canvas.Measurements, reads = errors.Reads;
        var draw = typeof(EditorView).GetMethod("DrawResourceErrors", Private)!;
        for (int i = 0; i < 20; i++) draw.Invoke(view, [canvas]);
        if (canvas.Measurements != measured || errors.Reads != reads)
            throw new Exception("Unchanged resource diagnostics were wrapped again on repaint.");

        Field("height").SetValue(view, 630f);
        canvas.MaximumEllipsisBottom = 0;
        draw.Invoke(view, [canvas]);
        if (canvas.MaximumEllipsisBottom == 0 || canvas.MaximumEllipsisBottom > 610)
            throw new Exception("A partial final row clipped the resource diagnostic truncation marker.");
        reads = errors.Reads;

        Field("libraryScroll").SetValue(view, 100f);
        canvas.Texts.Clear(); draw.Invoke(view, [canvas]);
        if (!canvas.Texts.Any(text => text.Contains("Resource row 100")) || errors.Reads - reads > 24)
            throw new Exception("Resource diagnostic scrolling did not visit only the visible rows.");
        measured = canvas.Measurements;
        Field("width").SetValue(view, 620f);
        draw.Invoke(view, [canvas]);
        if (canvas.Measurements <= measured) throw new Exception("Resource diagnostic resizing reused stale wrapping.");
        var replacement = new ErrorRows("Replacement diagnostic");
        Field("resourceErrors").SetValue(view, replacement);
        Field("libraryScroll").SetValue(view, 0f);
        canvas.Texts.Clear(); draw.Invoke(view, [canvas]);
        if (!canvas.Texts.Contains("Replacement diagnostic"))
            throw new Exception("A new resource report reused old diagnostic text.");
        Console.WriteLine("PASS bounded resource diagnostics, viewport-only rows, cached repaint and invalidation");

        static FieldInfo Field(string name) => typeof(EditorView).GetField(name, Private)!;
    }

    private sealed class ErrorRows(string first) : IReadOnlyList<string>
    {
        public int Reads;
        public int Count => 100000;
        public string this[int index] { get { Reads++; return index == 0 ? first : "Resource row " + index; } }
        public IEnumerator<string> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CountingCanvas : ICanvas
    {
        public int Measurements, MaximumMeasuredLength;
        public float MaximumEllipsisBottom;
        public readonly List<string> Texts = [];
        public float MeasureText(string text, float size, bool bold = false)
        {
            Measurements++; MaximumMeasuredLength = Math.Max(MaximumMeasuredLength, text.Length);
            if (text.Length > 8192) throw new Exception("Resource diagnostic exceeded its measurement budget.");
            return text.Length * size * .6f;
        }
        public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false)
        {
            if (text.Length > 0 && (char.IsHighSurrogate(text[^1]) || char.IsLowSurrogate(text[0])))
                throw new Exception("Resource wrapping split a Unicode character.");
            if (text.Contains('…')) MaximumEllipsisBottom = Math.Max(MaximumEllipsisBottom, y + size);
            Texts.Add(text);
        }
        public void Fill(Rect r, uint color, float radius = 0, float opacity = 1) { }
        public void Stroke(Rect r, uint color, float width = 1, float radius = 0) { }
        public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1) { }
        public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1) { }
        public bool Image(string path, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1) => false;
        public void Clip(Rect r) { }
        public void Unclip() { }
    }
}
