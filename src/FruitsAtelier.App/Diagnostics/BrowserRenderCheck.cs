using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class BrowserRenderCheck
{
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/browser/native"); Directory.CreateDirectory(folder);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int width in new[] { 980, 1440 })
            {
                L.SetLanguage(locale);
                var clock = new Clock();
                var editor = new EditorView(false, clock);
                var snapshot = OsuDiscussionClient.Parse(File.ReadAllText("tests/FruitsAtelier.App.Tests/Fixtures/discussions.json"), 777);
                editor.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
                var map = new MapDocument { Name = "Browser fixture", DurationMs = 10000 };
                SongSetup.Set(map, "Metadata", "BeatmapSetID", "777"); SongSetup.Set(map, "Metadata", "BeatmapID", "101");
                editor.LoadDocument(map);
                var layout = editor.CaptureDockLayouts();
                layout.Mapping!.Right!.Active = "OsuWeb"; layout.Mapping.Right.Collapsed = false;
                layout.Mapping.Right.Width = width == 980 ? 260 : 600;
                editor.LibrarySettings.DockLayouts = layout; editor.RestoreDockLayouts();
                editor.UpdateBrowserState(new(editor.BrowserUrl, true, true));
                editor.RequestCopyText = _ => { };
                canvas.Resize(width, 900, 96);
                Paint("toolbar");
                var address = editor.BrowserAddressBounds;
                if (address.Bottom >= editor.BrowserViewport.Y) throw new InvalidOperationException("Toolbar overlaps native webpage.");
                editor.PointerDown(address.X + 5, address.Y + 10, 0, false, false);
                editor.PointerUp(address.X + 5, address.Y + 10, 0); Paint("copied");
                clock.Milliseconds = 1800; Paint("fade");
                if (Math.Abs(editor.BrowserCopyOpacity - .5f) > .01) throw new InvalidOperationException("Copy feedback fade is not synchronized.");
                clock.Milliseconds = 2100; Paint("restored");
                if (editor.BrowserFeedbackNeedsRedraw) throw new InvalidOperationException("Expired copy feedback keeps redrawing.");
                editor.RequestUpdateCheck = () => { };
                editor.OpenSettings(); Paint("settings");
                if (editor.BrowserViewport.Width > 0) throw new InvalidOperationException("Browser covers settings.");
                editor.RequestClearBrowserData = () => Task.CompletedTask;
                var settings = editor.SettingsBounds;
                editor.PointerDown(settings.X + 40, settings.Y + 78 + 6 * 48 + 16, 0, false, false);
                editor.PointerUp(settings.X + 40, settings.Y + 78 + 6 * 48 + 16, 0); Paint("web-settings");
                editor.StopDiscussions();

                void Paint(string name)
                {
                    canvas.Begin(); editor.Render(canvas, width, 900);
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{width}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
                        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
