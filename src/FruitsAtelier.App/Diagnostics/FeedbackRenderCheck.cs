using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class FeedbackRenderCheck
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int ms) => ticks += ms;
    }

    internal static void Run(D2DCanvas canvas, int width, int height)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var clock = new Clock();
                var view = new EditorView(false, clock);
                var map = new MapDocument { IsDemo = false, DurationMs = 20000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 5000, X = 200 }, new() { TimeMs = 15000, X = 300 }]);
                OsuTimeline.AddBreak(map, 2000, 3500);
                OsuTimeline.AddBookmark(map, 2500);
                SongSetup.Set(map, "General", "PreviewTime", "2500");
                SongSetup.Set(map, "Metadata", "Tags", string.Join(' ', Enumerable.Range(0, 40).Select(i => "tag" + i)));
                view.LoadDocument(map); Paint();
                view.UpdateTransport(2500, 20000, true, false, false, null, null); Paint();
                var timeline = view.ObjectTimelineBounds;
                float x = timeline.X + (float)((1000 - view.ObjectTimelineStartMs) * view.ObjectTimelinePixelsPerMs);
                view.PointerDown(x, timeline.Y + 27, 0, false, false);
                view.PointerUp(x, timeline.Y + 27, 0);
                view.PointerDoubleClick(x, timeline.Y + 27, false, false); Paint();
                if (view.PlayheadMs != 1000 || !view.SelectedObjectIds.Contains(map.Fruits[0].Id) || !view.Document.ContentEquals(map))
                    throw new InvalidOperationException("Native upper timeline double-click navigation failed.");
                view.OpenSongSetup(); Paint();
                var tags = view.SongSetupFieldBounds["Tags"];
                if (tags.Height <= 32 || tags.Bottom > view.SongSetupBounds.Bottom - 80)
                    throw new InvalidOperationException("Native wrapped Tags field exceeds its dialog.");
                view.KeyDown(27, false, false); Paint();
                view.LibrarySettings.TestplayStartupDelaySeconds = 0;
                view.UpdateTransport(1000, 20000, false, false, false, null, null);
                view.StartTestplay(); clock.Advance(500); Paint();
                view.KeyDown(192, false, false); clock.Advance(299); Paint();
                if (view.PlayheadMs != 1799) throw new InvalidOperationException("Native quick retry fired early.");
                clock.Advance(1); Paint();
                if (view.PlayheadMs != 1000) throw new InvalidOperationException("Native quick retry did not reset the session.");
                view.KeyUp(192); view.StopTestplay();
                if (!view.Document.ContentEquals(map)) throw new InvalidOperationException("Native feedback controls changed content.");
                void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
