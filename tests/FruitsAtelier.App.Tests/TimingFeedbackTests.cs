using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimingFeedbackTests
{
    private sealed class Clock : TimeProvider
    {
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => 0;
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                foreach (bool red in new[] { true, false })
                {
                    var map = Map(); var ui = new Ui(false, new Clock()); ui.LoadDocument(map);
                    ui.View.UpdateTransport(750.9, 20000, true, false, false, null, null); ui.Paint();
                    ui.Key(117); if (red) ui.Key(36);
                    ui.ClickText(L.Get("timing.useCurrent"));
                    Check(ui.View.TimingFields.Single(f => f.Key == "offset").Value == "750"
                        && ui.View.Document.ContentEquals(map) && ui.View.PlayheadMs == 750.9,
                        "Use Current Time must show an integer offset in the draft without moving playback.");
                    ui.Key(13);
                    Check(ui.View.Document.TimingPoints.Single(p => p.Uninherited == red).TimeMs == 750,
                        "Use Current Time did not commit integer milliseconds.");
                    ui.Key('Z', ctrl: true);
                    Check(ui.View.Document.ContentEquals(map), "Use Current Time did not undo atomically.");
                }

                var precise = new Ui(false, new Clock()); var original = Map(); precise.LoadDocument(original);
                precise.Key(117);
                Set("bpm", "137.696"); Set("offset", "7822.261429");
                Check(precise.View.TimingFields.Single(f => f.Key == "bpm").Value == "137.7"
                    && precise.View.TimingFields.Single(f => f.Key == "offset").Value == "7822"
                    && precise.View.Document.ContentEquals(original), "F6 drafts must use two-decimal BPM and integer offsets.");
                precise.Key(13); CheckTiming(precise, 7822, 137.7);
                precise.Key('Z', ctrl: true); Check(precise.View.Document.ContentEquals(original), "Precision-limited F6 edits did not undo together.");

                void Set(string key, string value)
                {
                    var bounds = precise.View.TimingFields.Single(f => f.Key == key).Bounds;
                    precise.Click(bounds.X + 8, bounds.Y + 8); precise.Key('A', ctrl: true); precise.Type(value); precise.Key(13);
                }

                var tappedMap = Map(); var tap = new Ui(false, new Clock()); tap.LoadDocument(tappedMap); tap.Key(114);
                for (int i = 0; i < 10; i++)
                {
                    tap.View.UpdateTransport(1000.6 + i * 500.4, 20000, true, true, false, null, null);
                    tap.Key('T'); tap.Key('T'); tap.View.KeyUp('T');
                    if (i < 9) Check(tap.View.Document.ContentEquals(tappedMap), "Taps applied before ten distinct presses.");
                }
                CheckTiming(tap, 1001, 119.90);
                var firstResult = tap.View.Document.DeepClone();
                tap.View.UpdateTransport(6014.6, 20000, true, true, false, null, null); tap.Key('T'); tap.View.KeyUp('T');
                CheckTiming(tap, 999, 119.80);
                var refined = tap.View.Document.DeepClone();
                tap.Key('Z', ctrl: true); Check(tap.View.Document.ContentEquals(firstResult), "A refined tap result did not undo to the previous result.");
                tap.Key('Z', ctrl: true); Check(tap.View.Document.ContentEquals(tappedMap), "Automatic timing did not undo to the original section.");
                tap.Key('Y', ctrl: true); tap.Key('Y', ctrl: true);
                Check(tap.View.Document.ContentEquals(refined), "Automatic timing did not redo.");
                tap.View.UpdateTransport(10000, 20000, true, true, false, null, null); tap.Key('T'); tap.View.KeyUp('T');
                Check(tap.View.Document.ContentEquals(refined), "A long gap did not restart tap collection before applying.");
                tap.View.UpdateTransport(10200, 20000, true, false, false, null, null); tap.Key('T'); tap.View.KeyUp('T');
                Check(tap.View.Document.ContentEquals(refined), "Paused taps changed timing.");

                var rolling = new Ui(false, new Clock()); rolling.LoadDocument(Map()); rolling.Key(114);
                for (int i = 0; i < 40; i++)
                {
                    rolling.View.UpdateTransport(1000.6 + i * 500.4, 30000, true, true, false, null, null);
                    rolling.Key('T'); rolling.View.KeyUp('T');
                }
                CheckTiming(rolling, 1001, 119.90);
                rolling.ClickText(L.Get("timing.tapReset"));
                for (int i = 0; i < 10; i++)
                {
                    rolling.View.UpdateTransport(23000.6 + i * 500.4, 30000, true, true, false, null, null);
                    rolling.Key('T'); rolling.View.KeyUp('T');
                }
                CheckTiming(rolling, 23001, 119.90);

                var paused = new Ui(false, new Clock()); paused.LoadDocument(Map());
                var seeks = new List<double>(); paused.View.RequestSeek = seeks.Add;
                paused.View.RequestTogglePlayback = () => { };
                paused.View.UpdateTransport(550, 20000, true, true, false, null, null); paused.Key(32);
                paused.Key(114);
                paused.View.UpdateTransport(571.25, 20000, true, false, false, null, null);
                Check(seeks.Count == 0 && paused.View.PlayheadMs == 571.25,
                    "Entering Timing before a delayed pause confirmation must retain the confirmed time.");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static MapDocument Map()
    {
        var map = new MapDocument { DurationMs = 20000, IsDemo = false };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true },
            new() { TimeMs = 500, BeatLengthMs = -100, Uninherited = false }]);
        map.Fruits.Add(new() { TimeMs = 6000, X = 200 });
        return map;
    }

    private static void CheckTiming(Ui ui, double offset, double bpm)
    {
        var point = ui.View.Document.TimingPoints.Single(p => p.Uninherited);
        Check(point.TimeMs == offset && Math.Abs(60000 / point.BeatLengthMs - bpm) < 1e-8,
            "Automatic taps did not jointly fit BPM and integer offset.");
        Check(ui.View.Document.Fruits.Single().TimeMs == 6000,
            "Automatic taps moved notes while movement was disabled.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
