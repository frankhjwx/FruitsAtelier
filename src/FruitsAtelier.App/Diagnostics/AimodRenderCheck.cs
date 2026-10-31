using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class AimodRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height, int dpi = 96)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 20000 };
                for (int i = 0; i < 60; i++) map.Fruits.Add(new() { TimeMs = 1000 + i * 5, X = i % 2 == 0 ? 100 : 400 });
                string captureName = "panel";
                var view = new EditorView(false); view.LoadDocument(map); Paint();
                view.KeyDown(65, true, true); Paint(capture: true);
                view.KeyDown(46, false, false); view.KeyDown(116, false, false); Paint();
                if (!view.AimodVisible || view.AimodErrors.Count != 59 || view.IsTestplaying || !view.Document.ContentEquals(map))
                    throw new InvalidOperationException("Native AiMod check or input isolation failed.");
                var first = view.AimodRowBounds(0); var time = view.AimodHeaderBounds(0);
                Click(time.X + 12, first.Y + 10); Paint();
                if (view.SelectedObjectIds.Count != 2) throw new InvalidOperationException("Native AiMod timestamp did not select its objects.");
                view.KeyDown(46, false, false); Paint();
                Click(time.X + 12, first.Y + 10); Paint();
                Click(first.Right - 10, first.Y + 10); view.KeyDown(13, false, false); Paint();
                if (view.SelectedObjectIds.Count != 0 || view.PlayheadMs != 1003 || view.ErrorVisible)
                    throw new InvalidOperationException("Native retained AiMod row selected deleted objects or trapped input.");
                view.KeyDown('Z', true, false); Paint();
                if (!view.Document.ContentEquals(map) || view.IsDirty) throw new InvalidOperationException("Native AiMod navigation changed deletion undo.");
                view.ShowAimod(); Paint();
                var panel = view.AimodPanelBounds;
                var track = view.AimodVerticalTrack;
                view.Wheel(track.X - 15, track.Y + 15, -120, false); Paint();
                if (view.AimodVerticalOffset <= 0) throw new InvalidOperationException("AiMod wheel scroll failed.");
                view.PointerDown(track.X + 5, track.Bottom - 10, 0, false, false);
                view.PointerMove(track.X + 5, track.Bottom - 2, false, false);
                view.PointerUp(track.X + 5, track.Bottom - 2, 0); Paint();
                captureName = "scrolled"; Paint(capture: true);
                var header = view.AimodHeaderBounds(0);
                view.PointerDown(header.Right, header.Y + 10, 0, false, false);
                view.PointerMove(header.Right + 30, header.Y + 10, false, false);
                view.PointerUp(header.Right + 30, header.Y + 10, 0); Paint();
                if (Math.Abs(view.AimodHeaderBounds(0).Width - header.Width - 30) > 1)
                    throw new InvalidOperationException("Native column resize failed.");
                float buttonWidth = Math.Min(100, (panel.Width - 8) / 3);
                Click(panel.X + (buttonWidth + 4) * 2 + 12, panel.Y + 12); Paint();
                if (view.AimodVisible || !view.Document.ContentEquals(map)) throw new InvalidOperationException("Native AiMod close changed content.");
                view.NewProject();
                void Paint(bool capture = false)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: !capture ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/aimod-native");
                        Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{captureName}-{locale}-{width}x{height}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
                void Click(float x, float y) { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
