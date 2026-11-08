using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class TimingClipboardRenderCheck
{
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/timing-clipboard-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var clock = new Clock();
                var map = new MapDocument { IsDemo = false, DurationMs = 10000 };
                map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true },
                    new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, Effects = 1 }]);
                var editor = new EditorView(false, clock); editor.LoadDocument(map);
                string clipboard = "";
                editor.RequestCopyText = text => clipboard = text;
                editor.RequestTimingClipboardMenu = request => editor.ShowTimingClipboardMenu(clipboard, request);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint(); editor.OpenTimingSetup(); Paint();
                editor.KeyDown('A', true, false); editor.KeyDown('C', true, false); Paint("copied");
                if (TimingEditing.Parse(clipboard).Length != 2) throw new InvalidOperationException("Timing copy lost selected lines.");
                clock.Milliseconds = 850; Paint("fade");
                if (Math.Abs(editor.TimingCopyOpacity - .5f) > .01f) throw new InvalidOperationException("Timing feedback did not fade.");
                clock.Milliseconds = 1000; Paint("expired");
                if (editor.TimingFeedbackNeedsRedraw) throw new InvalidOperationException("Expired feedback keeps redrawing.");
                var list = editor.TimingListViewport;
                double? seek = null;
                editor.RequestSeek = time => seek = time;
                editor.PointerDown(list.X + 30, list.Y + 74, 0, false, false);
                editor.PointerUp(list.X + 30, list.Y + 74, 0); Paint();
                if (seek is not null) throw new InvalidOperationException("Timing single-click sought playback.");
                editor.PointerDoubleClick(list.X + 30, list.Y + 74, false, false); Paint();
                if (seek != 1000 || editor.PlayheadMs != 1000 || !editor.TimingSetupVisible)
                    throw new InvalidOperationException("Timing double-click did not seek to the row.");
                editor.KeyDown('A', true, false); Paint();
                editor.PointerDown(list.X + 30, list.Y + 46, 2, false, false);
                editor.PointerUp(list.X + 30, list.Y + 46, 2); Paint("selected-menu");
                editor.KeyDown(27, false, false); Paint();
                editor.PointerDown(list.X + 30, list.Bottom - 10, 2, false, false);
                editor.PointerUp(list.X + 30, list.Bottom - 10, 2); Paint("empty-menu");
                if (editor.IsDirty) throw new InvalidOperationException("Timing copy changed content.");
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
        AppLog.Write("Timing clipboard native checks passed.");
    }
}
