using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckUiScale(bool timelineLayout = false)
    {
        string folder = Path.Combine(Artifacts, "tests", timelineLayout ? "timeline-layout-native" : "ui-scale-native");
        Directory.CreateDirectory(folder);
        string language = L.Language;
        var preference = view.RequestViewPreference;
        nint style = Native.GetWindowLongPtr(hwnd, -16);
        view.RequestViewPreference = null;
        try
        {
            Native.SetWindowLongPtr(hwnd, -16, unchecked((nint)0x80000000u));
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1920, 1080), (1280, 800) })
            foreach (int percent in Enumerable.Range(2, 7).Select(step => step * 25))
            {
                L.SetLanguage(locale);
                var document = DemoMap.Create();
                view.LoadDocument(document); view.CloseLibrary();
                view.LibrarySettings.UiScalePercent = 150;
                view.LibrarySettings.ObjectTimelineHeight = 64;
                view.LibrarySettings.OverviewTimelineHeight = 40;
                Native.SetWindowPos(hwnd, 0, 0, 0, size.Item1, size.Item2, 0x0004 | 0x0010 | 0x0020);
                Paint();
                view.OpenSettings(); Paint();
                var r = view.SettingsBounds;
                Click(r.X + 40, r.Y + 192); Paint();
                Click(r.X + 250, r.Y + 322); Paint();
                float menuTop = Math.Clamp(r.Y + 342, 0, Math.Max(0, size.Item2 * 96 / uiDpi - 236));
                Click(r.X + 250, menuTop + 6 + (percent - 50) / 25 * 32 + 16);
                if (view.LibrarySettings.UiScalePercent != 150)
                    throw new InvalidOperationException("Native scale selection applied before Apply.");
                view.ApplySettings(Path.Combine(folder, "settings.json"), force: true); Paint();
                float expected = ClientUiDpi(size.Item1, size.Item2) * percent / 150f;
                if (failed || Math.Abs(uiDpi - expected) > .001)
                    throw new InvalidOperationException($"Native UI scale selection failed at {percent}%.");
                canvas!.Begin(); view.Render(canvas, size.Item1 * 96 / uiDpi, size.Item2 * 96 / uiDpi);
                canvas.End(capture: () =>
                {
                    var frame = canvas.CapturePixels();
                    SaveFrame(frame.Width, frame.Height, frame.Pixels, Path.Combine(folder, $"{locale}-{size.Item1}-{percent}.bmp"));
                });
                view.KeyDown(27, false, false); Paint();
                if (timelineLayout)
                {
                    var upper = view.ObjectTimelineResizeBounds;
                    Drag(upper.X + 10, upper.Y + 4, -32); Paint();
                    var lower = view.OverviewTimelineResizeBounds;
                    Drag(20, lower.Y + 4, 20); Paint();
                    if (Math.Abs(view.ObjectTimelineBounds.Height - 32) > 2 || Math.Abs(view.LibrarySettings.OverviewTimelineHeight - 20) > 2
                        || view.WantsCapture || view.TimeDisplayBounds.Right >= view.TransportButtonBounds(0).X
                        || view.PlaybackSpeedSelectorBounds.Right >= view.VolumeButtonBounds.X)
                        throw new InvalidOperationException("Native timeline resize or transport layout failed.");
                    Capture("resized");
                    var speed = view.PlaybackSpeedSelectorBounds;
                    Click(speed.X + 2 * 50 + 20, speed.Y + 12); Paint(); Capture("speed");
                    if (view.PlaybackSpeed != .5) throw new InvalidOperationException("Native speed selector did not select 50%.");
                    upper = view.ObjectTimelineResizeBounds;
                    Drag(upper.X + 10, upper.Y + 4, -100); Paint();
                    lower = view.OverviewTimelineResizeBounds;
                    Drag(20, lower.Y + 4, 100); Paint();
                    if (view.ObjectTimelineBounds.Height != 32 || view.LibrarySettings.OverviewTimelineHeight != 20)
                        throw new InvalidOperationException("Native timeline half-height limits failed.");
                    Capture("minimum");
                }
                var bounds = view.CanvasScaleLockBounds;
                bool uniform = view.UniformCanvasScale;
                Click(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2); Paint();
                if (view.UniformCanvasScale == uniform) throw new InvalidOperationException("Scaled native click missed.");
                Click(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2); Paint();
                var slider = view.ZoomSliderBounds;
                Send(0x0201, slider.X + slider.Width / 2, slider.Y + slider.Height / 2);
                Send(0x0200, slider.Right + 2, slider.Y + slider.Height / 2);
                Send(0x0202, slider.Right + 2, slider.Y + slider.Height / 2); Paint();
                if (Math.Abs(view.CanvasZoom - 1) > .001 || view.WantsCapture)
                    throw new InvalidOperationException($"Scaled native drag failed at {size}, {percent}%: zoom {view.CanvasZoom}, capture {view.WantsCapture}.");
                var plot = view.CanvasPlotBounds;
                var point = new Native.Point { X = (int)((plot.X + plot.Width / 2) * uiDpi / 96), Y = (int)((plot.Y + plot.Height / 2) * uiDpi / 96) };
                Native.GetWindowRect(hwnd, out var windowBounds);
                point.X += windowBounds.Left; point.Y += windowBounds.Top;
                int snap = view.SnapDivisor;
                short delta = snap >= 16 ? (short)-120 : (short)120;
                HandleMessage(hwnd, 0x020A, (nuint)((uint)(ushort)delta << 16 | 0x0008), (nint)((long)(ushort)point.Y << 16 | (ushort)point.X));
                if (view.SnapDivisor == snap || !document.ContentEquals(view.Document))
                    throw new InvalidOperationException($"Scaled wheel/content check failed at {percent}%: snap {snap} -> {view.SnapDivisor}, content equal {document.ContentEquals(view.Document)}.");
            }
            AppLog.Write("UI scale native checks passed at every supported step in English and Chinese: settings, rendering, click, drag and wheel.");
            if (timelineLayout) AppLog.Write("Resizable timeline and compact transport native checks passed at every supported UI scale.");
        }
        finally
        {
            view.LibrarySettings.UiScalePercent = 150;
            view.RequestViewPreference = preference;
            L.SetLanguage(language);
            Native.SetWindowLongPtr(hwnd, -16, style);
        }
        void Paint() => WndProc(hwnd, 0x000F, 0, 0);
        void Click(float x, float y) { Send(0x0201, x, y); Send(0x0202, x, y); }
        void Drag(float x, float y, float delta)
        {
            Send(0x0201, x, y); Send(0x0200, x, y + delta); Send(0x0202, x, y + delta);
        }
        void Capture(string name)
        {
            canvas!.Begin();
            Native.GetClientRect(hwnd, out var size);
            view.Render(canvas, size.Right * 96 / uiDpi, size.Bottom * 96 / uiDpi);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                SaveFrame(frame.Width, frame.Height, frame.Pixels, Path.Combine(folder, $"{name}-{L.Language}-{size.Right}-{view.LibrarySettings.UiScalePercent}.bmp"));
            });
        }
        void Send(uint message, float x, float y)
        {
            int px = (int)Math.Round(x * uiDpi / 96), py = (int)Math.Round(y * uiDpi / 96);
            HandleMessage(hwnd, message, 0, (nint)((long)(ushort)py << 16 | (ushort)px));
        }
    }
}
