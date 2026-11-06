using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class BookmarkPanelRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/bookmark-panel/native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 120000 };
                map.Fruits.Add(new() { TimeMs = 15000, X = 256 });
                map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
                map.TimingPoints.Add(new() { TimeMs = 30000, BeatLengthMs = -100, Uninherited = false, Effects = 1 });
                map.TimingPoints.Add(new() { TimeMs = 60000, BeatLengthMs = -100, Uninherited = false });
                foreach (int time in new[] { 15000, 30000, 60000, 90000 }) OsuTimeline.AddBookmark(map, time);
                var view = new EditorView(false);
                view.LoadDocument(map); view.CloseLibrary();
                view.UpdateTransport(42500, 120000, true, false, false, null, null);
                view.ShowBookmarkPanel();
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint("default");
                var panel = view.BookmarkPanelViewport;
                var timeBox = view.BookmarkTimeBounds(15000);
                var toggle = view.BookmarkIntervalBounds(15000);
                Click(toggle.X + 10, toggle.Y + 10);
                if (!view.Document.BookmarkAppearanceAt(15000).ShowInterval)
                    throw new InvalidOperationException("Native bookmark interval toggle failed.");
                Paint("interval");
                var colour = view.BookmarkColourBounds(15000);
                view.RequestCopyText = _ => { };
                view.RequestPasteBookmarkColour = () => view.PasteBookmarkColour("#65C9B1", view.BookmarkColourPasteSession);
                view.PointerDown(colour.X + 10, colour.Y + 10, 2, false, false);
                view.PointerUp(colour.X + 10, colour.Y + 10, 2); Paint("colour-context");
                view.KeyDown(27, false, false); Paint();
                Click(colour.X + 10, colour.Y + 10); Paint("colour-picker");
                Click(panel.Right - 20, panel.Bottom - Math.Min(242, panel.Height) + 14);
                view.PointerDown(timeBox.X + 10, timeBox.Y + 10, 2, false, false);
                view.PointerUp(timeBox.X + 10, timeBox.Y + 10, 2); Paint("context");
                view.KeyDown(27, false, false); Paint();
                Click(timeBox.X + 10, timeBox.Y + 10);
                foreach (char value in "00:20:000") view.TextInput(value);
                view.KeyDown(13, false, false); Paint("edited");
                if (!OsuTimeline.Bookmarks(view.Document).Contains(20000)
                    || !view.Document.BookmarkAppearanceAt(20000).ShowInterval)
                    throw new InvalidOperationException("Native bookmark time edit lost its interval.");
                var resize = view.DockResizeBounds(false);
                view.PointerDown(resize.X + 4, resize.Y + 60, 0, false, false);
                view.PointerMove(size.Item1 - 180, resize.Y + 60, false, false);
                view.PointerUp(size.Item1 - 180, resize.Y + 60, 0); Paint("minimum-dock");

                void Click(float x, float y)
                { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint(); }
                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Bookmark panel native input and rendering passed.");
    }
}
