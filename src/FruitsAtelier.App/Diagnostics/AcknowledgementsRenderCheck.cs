using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class AcknowledgementsRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/acknowledgements-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (820, 740), (760, 580), (640, 440) })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var view = new EditorView(false);
                int continued = 0, closed = 0;
                string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
                view.RequestClose = () => closed++;
                view.BeginAcknowledgements(() => continued++, path);
                Paint("unchecked");
                Click(view.AcknowledgementsConfirmBounds);
                if (continued != 0 || view.AcknowledgementsAgreed) throw new InvalidOperationException("Native unchecked confirmation was accepted.");
                view.KeyDown(35, false, false); Paint("scrolled");
                Click(view.AcknowledgementsCheckboxBounds); Paint("checked");
                Click(view.AcknowledgementsConfirmBounds);
                if (continued != 1 || closed != 0 || view.AcknowledgementsVisible || !FruitsAtelier.Core.AcknowledgementsPreference.IsAccepted(path))
                    throw new InvalidOperationException("Native acknowledgement confirmation did not save and continue.");
                File.Delete(path);

                void Click(Rect r) { view.PointerDown(r.X + 8, r.Y + 8, 0, false, false); view.PointerUp(r.X + 8, r.Y + 8, 0); }
                void Paint(string stage)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{stage}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Acknowledgements rendering and consent passed at all sizes, interface languages and DPI values.");
    }
}
