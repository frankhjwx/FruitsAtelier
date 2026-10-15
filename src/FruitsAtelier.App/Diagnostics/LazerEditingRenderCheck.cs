using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class LazerEditingRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/lazer-editing-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 480 };
                map.Fruits.AddRange([
                    new Fruit { TimeMs = 1200, X = 160 },
                    new Fruit { TimeMs = 1440, X = 280 },
                    new Fruit { TimeMs = 1920, X = 400 }
                ]);
                var editor = new EditorView(false);
                editor.LibrarySettings.Workspace = Path.Combine(folder, "workspace");
                editor.LibrarySettings.PlaybackLineFromBottom = .63;
                editor.LoadDocument(map);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint();
                editor.OpenSettings(); Paint();
                var settings = editor.SettingsBounds;
                Click(settings.X + 40, settings.Y + 96 + 6 * 48);
                Paint("settings-off");
                var toggle = editor.LazerEditingToggleBounds;
                Click(toggle.X + 10, toggle.Y + toggle.Height / 2); Paint("settings-on");
                editor.ApplySettings(persist: false);
                editor.KeyDown(27, false, false); Paint();
                if (!editor.IsLazerEditing || !editor.HitLightingEnabled || editor.LibrarySettings.PlaybackLineFromBottom != .63)
                    throw new InvalidOperationException("Native lazer settings did not apply or changed mapping preferences.");
                editor.UpdateTransport(1250, 10000, true, false, false, null, null);
                Paint("canvas");
                var zoom = editor.ZoomSliderBounds;
                Click(zoom.X + zoom.Width * (.6f - .25f) / 1.75f, zoom.Y + zoom.Height / 2);
                Thread.Sleep(220);
                Paint("game-screen-frame");
                zoom = editor.ZoomSliderBounds;
                Click(zoom.Right, zoom.Y + zoom.Height / 2);
                Thread.Sleep(220);
                Paint();
                var panPlot = editor.CanvasPlotBounds;
                float panX = panPlot.X + panPlot.Width / 2, panY = panPlot.Y + 20;
                editor.PointerDown(panX, panY, 1, false, false);
                editor.PointerMove(panX + 30, panY, false, false);
                editor.PointerUp(panX + 30, panY, 1);
                Paint("horizontal-pan");
                zoom = editor.ZoomSliderBounds;
                Click(zoom.X + zoom.Width * .75f / 1.75f, zoom.Y + zoom.Height / 2);
                Thread.Sleep(220);
                Paint();
                var preview = editor.PreviewToggleBounds;
                if (!editor.CatchPreviewVisible && preview.Width > 0) Click(preview.X + preview.Width / 2, preview.Y + preview.Height / 2);
                Paint("preview");
                foreach (var mode in Enum.GetValues<CatchHitLightingMode>())
                {
                    editor.LibrarySettings.CatchHitLighting = mode;
                    Paint("lighting-" + mode);
                }
                editor.LibrarySettings.CatchHitLighting = CatchHitLightingMode.FollowMode;
                if (!editor.DistanceSnapEnabled) editor.KeyDown('T', false, false);
                editor.KeyDown('2', false, false);
                editor.UpdateTransport(1250, 10000, true, false, false, null, null);
                Paint();
                var plot = editor.CanvasPlotBounds;
                var field = editor.PlayfieldBounds;
                float guideX = field.X + field.Width / 2;
                float guideY = plot.Bottom - (float)((1700 - editor.ViewStartMs) * editor.PixelsPerMs);
                editor.PointerMove(guideX, guideY, false, false);
                Paint("distance-snap");
                if (!editor.DistanceSnapEnabled || editor.PlayheadMs != 1250 || editor.LazerDistanceGuideCount == 0)
                    throw new InvalidOperationException("Native distance-snap capture must contain active guides at the requested map time.");
                editor.OpenSettings(); Paint();
                settings = editor.SettingsBounds;
                Click(settings.X + 40, settings.Y + 96 + 6 * 48);
                toggle = editor.LazerEditingToggleBounds;
                Click(toggle.X + 10, toggle.Y + toggle.Height / 2);
                editor.ApplySettings(persist: false);
                editor.KeyDown(27, false, false); Paint("mapping");
                if (editor.IsLazerEditing || editor.HitLightingEnabled || editor.LibrarySettings.PlaybackLineFromBottom != .63
                    || !map.ContentEquals(editor.Document) || editor.IsDirty || editor.WantsCapture)
                    throw new InvalidOperationException("Native mode roundtrip changed content, capture or retained mapping preferences.");

                void Click(float x, float y)
                { editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0); Paint(); }
                void Paint(string? name = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height);
                        output.Write((ushort)1); output.Write((ushort)32); output.Write(0); output.Write(frame.Pixels.Length);
                        output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Lazer editing native settings, mode retention and rendering checks passed.");
    }
}
