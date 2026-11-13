using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class MediaPanelRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/media-panel/native");
        Directory.CreateDirectory(folder);
        string image = Path.Combine(folder, "palette-source.bmp");
        using (var writer = new BinaryWriter(File.Create(image)))
        {
            const int w = 128, h = 96, bytes = w * h * 4;
            writer.Write((ushort)0x4D42); writer.Write(54 + bytes); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(w); writer.Write(-h); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(bytes); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                writer.Write((byte)(x * 255 / (w - 1)));
                writer.Write((byte)(y * 255 / (h - 1)));
                writer.Write((byte)(x * 255 / (w - 1))); writer.Write((byte)255);
            }
        }
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { IsDemo = false, SourcePath = Path.Combine(folder, "fixture.osu") };
                var view = new EditorView(false);
                view.LibrarySettings.Workspace = folder;
                view.LoadDocument(map); view.CloseLibrary(); view.DecodePaletteImage = D2DCanvas.DecodeStoryboardImage;
                view.RequestMediaFile = (_, answer) => answer(image);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Wait(); view.ShowMediaPanel(); Paint("unbound");
                view.DropLibraryFiles([image]); Wait(); Paint("background");
                var panel = view.MediaPanelViewport;
                if (view.MediaLibraryBounds.Width > 0)
                {
                    view.Wheel(panel.X + 10, panel.Y + 60, -2400, false); Paint();
                    Click(view.MediaLibraryBounds.X + 12, view.MediaLibraryBounds.Y + 12);
                    var thumb = view.MediaScrollThumb;
                    var track = view.MediaScrollTrack;
                    if (thumb.Height > 0)
                    {
                        view.PointerDown(thumb.X + 5, thumb.Y + 2, 0, false, false);
                        view.PointerMove(track.X + 5, track.Bottom, false, false); Paint();
                        view.PointerUp(track.X + 5, track.Bottom, 0); Paint("media-scroll-bottom");
                        if (Math.Abs(view.MediaScrollThumb.Bottom - track.Bottom) > 1)
                            throw new InvalidOperationException("Native media scrollbar drag did not reach bottom");
                        Click(track.X + 5, track.Y + 1);
                    }
                    view.Wheel(panel.X + 10, panel.Y + 60, -240, false); Paint("existing-media");
                }
                Click(panel.Right - panel.Width / 4, panel.Y + 16); Paint("palette-empty");
                Click(view.MediaGenerateBounds.X + 12, view.MediaGenerateBounds.Y + 12); Wait(); Paint("palette");
                Click(view.MediaRefineBounds.X + 12, view.MediaRefineBounds.Y + 12); Paint("refinement");
                if (!view.MediaRefinementVisible) throw new InvalidOperationException("Native refinement did not open");
                var pick = view.MediaRefinementImageBounds;
                view.PointerDown(pick.X + pick.Width * .6f, pick.Y + pick.Height * .5f, 0, false, false); Paint("refinement-magnifier");
                view.PointerMove(pick.Right - 1, pick.Bottom - 1, false, false); Paint("refinement-magnifier-edge");
                view.PointerUp(pick.Right - 1, pick.Bottom - 1, 0); Paint("refinement-picked");
                float modalRight = (size.Item1 + Math.Min(1000, size.Item1 - 32)) / 2f;
                float modalBottom = (size.Item2 + Math.Min(660, size.Item2 - 32)) / 2f;
                Click(modalRight - 112, modalBottom - 40); Paint("refinement-confirmation");
                if (!view.MediaPaletteConfirmationVisible) throw new InvalidOperationException("Bright palette did not require confirmation");
                view.KeyDown(27, false, false); Paint();
                view.KeyDown(27, false, false); Paint();
                if (view.MediaApplyBounds.Width <= 0) throw new InvalidOperationException("Native palette did not finish generating");
                if (view.MediaApplyBounds.Bottom > panel.Bottom)
                {
                    view.Wheel(panel.X + 10, panel.Y + 60, -120 * (view.MediaApplyBounds.Bottom - panel.Bottom + 16) / 36, false);
                    Paint("palette-scrolled");
                }
                Click(view.MediaApplyBounds.X + 12, view.MediaApplyBounds.Y + 12);
                if (SongSetup.Colours(view.Document).Length != 4) throw new InvalidOperationException("Native palette application failed");
                var resize = view.DockResizeBounds(false);
                view.PointerDown(resize.X + 4, resize.Y + 60, 0, false, false);
                view.PointerMove(size.Item1 - 180, resize.Y + 60, false, false);
                view.PointerUp(size.Item1 - 180, resize.Y + 60, 0); Paint("narrow");
                void Click(float x, float y) { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint(); }
                void Wait()
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    do { Paint(); Thread.Sleep(5); if (timer.Elapsed.TotalSeconds > 20) throw new TimeoutException("Media panel native preparation timed out"); }
                    while (view.MediaEditingBusy || view.MediaPaletteBusy || view.MediaFilesBusy || view.LibraryOpening);
                }
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
        AppLog.Write("Media panel native file binding, palette and narrow layout checks passed.");
    }
}
