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
                    [],
                    [new(inputs[0].Id, inputs[0].Name, "Expert", 6.68,
                        [new("Overlap", "Concurrent hit objects.", "Problem", "00:01:001 (2,3) - Concurrent hit objects."),
                         new("Combo", "Too high combo.", "Warning", "04:43:381 (1) - The amount of combo exceeds the guideline of 16 significantly, currently it has 26."),
                         new("Snap", "Wrongly or inconsistently snapped hit objects.", "Warning", "00:31:193 - 04:03:100 - 04:03:736 - 04:45:399 - 1/16 makes out 0.5% or less of snappings, ensure this makes sense."),
                         .. Enumerable.Range(0, 32).Select(i => new MapsetVerifierIssue("Timing", "Concurrent or conflicting timing lines.", "Minor", $"00:03:{i:000} - Conflicting line settings. Green overrides red."))])]) { UpstreamTag = "v2.1.1" });
                string captureName = "panel";
                Paint(); view.ShowMapsetVerifier();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                do { Paint(); Thread.Sleep(1); } while (view.MapsetVerifierBusy && DateTime.UtcNow < deadline);
                if (view.VerifierReport is null) throw new Exception("Native verifier report did not settle");
                Paint(true);
                string? copied = null; view.RequestCopyText = value => copied = value;
                var copy = view.VerifierCopyBounds.First();
                view.PointerMove(copy.X + 4, copy.Y + 5, false, false); Paint();
                if (!view.MapsetVerifierHandCursor) throw new Exception("Native Copy all hand cursor missing");
                view.PointerDown(copy.X + 4, copy.Y + 5, 0, false, false);
                view.PointerUp(copy.X + 4, copy.Y + 5, 0);
                captureName = "copy"; Paint(true);
                if (copied is null || view.VerifierCopyOpacity <= 0) throw new Exception("Native Copy all confirmation missing");
                var filter = view.VerifierFilterBounds("Warning");
                view.PointerDown(filter.X + 8, filter.Y + 8, 0, false, false);
                view.PointerUp(filter.X + 8, filter.Y + 8, 0);
                captureName = "filtered"; Paint(true);
                if (view.VerifierTimestampBounds.Any(t => t.TimeMs == 1001)) throw new Exception("Native exclusive severity filter failed");
                view.PointerDown(filter.X + 8, filter.Y + 8, 0, false, false);
                view.PointerUp(filter.X + 8, filter.Y + 8, 0); Paint();
                double seek = -1; view.RequestSeek = t => seek = t;
                var link = view.VerifierTimestampBounds.First();
                view.PointerDown(link.Bounds.X + 3, link.Bounds.Y + 5, 0, false, false);
                view.PointerUp(link.Bounds.X + 3, link.Bounds.Y + 5, 0); Paint();
                if (seek != link.TimeMs || view.IsDirty) throw new Exception("Native verifier timestamp click failed");
                var thumb = view.VerifierScrollbarThumb;
                if (thumb.Height <= 0) throw new Exception("Native verifier scrollbar missing");
                view.PointerDown(thumb.X + 3, thumb.Y + 4, 0, false, false);
                view.PointerMove(thumb.X + 3, view.VerifierScrollbarTrack.Bottom, false, false); Paint();
                view.PointerUp(thumb.X + 3, view.VerifierScrollbarTrack.Bottom, 0);
                if (view.WantsCapture || view.VerifierScrollOffset <= 0) throw new Exception("Native verifier scrollbar drag failed");
                var general = view.VerifierGeneralTabBounds;
                captureName = "general";
                view.PointerDown(general.X + 20, general.Y + 12, 0, false, false);
                view.PointerUp(general.X + 20, general.Y + 12, 0); Paint(true);
                view.StopMapsetVerifier();

                void Paint(bool capture = false)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: !capture ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/mapset-verifier/native"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{captureName}-{locale}-{width}x{height}-{dpi}.bmp")));
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
