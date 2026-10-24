using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class MapsetVerifierRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height, int dpi)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var view = new EditorView(false); view.LoadDocument(new() { IsDemo = false, DurationMs = 300000 }); view.CloseLibrary();
                view.VerifierAnalyze = (inputs, _, _) => Task.FromResult(new MapsetVerifierReport(1,
                    [new("General", "Missing background.", "Problem", "No background image was found.")],
                    [new(inputs[0].Id, inputs[0].Name, "Expert", 6.68,
                        [new("Combo", "Too high combo.", "Warning", "04:43:381 (1) - The amount of combo exceeds the guideline of 16 significantly, currently it has 26."),
                         new("Snap", "Wrongly or inconsistently snapped hit objects.", "Warning", "00:31:193 - 04:03:100 - 04:03:736 - 04:45:399 - 1/16 makes out 0.5% or less of snappings, ensure this makes sense.")])]) { UpstreamTag = "render fixture" });
                Paint(); view.ShowMapsetVerifier();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                do { Paint(); Thread.Sleep(1); } while (view.MapsetVerifierBusy && DateTime.UtcNow < deadline);
                if (view.VerifierReport is null) throw new Exception("Native verifier report did not settle");
                Paint(true);
                double seek = -1; view.RequestSeek = t => seek = t;
                var link = view.VerifierTimestampBounds.First();
                view.PointerDown(link.Bounds.X + 3, link.Bounds.Y + 5, 0, false, false);
                view.PointerUp(link.Bounds.X + 3, link.Bounds.Y + 5, 0); Paint();
                if (seek != link.TimeMs || view.IsDirty) throw new Exception("Native verifier timestamp click failed");
                view.StopMapsetVerifier();

                void Paint(bool capture = false)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: !capture ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/mapset-verifier/native"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"panel-{locale}-{width}x{height}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
