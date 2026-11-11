using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimingPresentationTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                MappingFlagEdges(size.Item1, size.Item2);
                var map = new MapDocument { DurationMs = 20000 };
                map.TimingPoints.AddRange([
                    new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 1, Volume = 60 },
                    new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, SampleIndex = 50, Volume = 80, Effects = 1 },
                    new() { TimeMs = 2000, BeatLengthMs = 400, SampleSet = 3, SampleIndex = 2, Volume = 40 }]);
                var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(size.Item1, size.Item2);
                ui.View.UpdateTransport(1200, 20000, true, false, false, null, null); ui.Paint();
                var original = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
                var timeline = ui.View.ObjectTimelineBounds;
                var flags = ui.Canvas.Texts.Where(t => t.Value == L.Get("timeline.bpmValue", 120d) || t.Value == L.Get("timeline.bpmValue", 150d))
                    .Where(t => t.Y >= timeline.Y && t.Y < timeline.Bottom).ToArray();
                Check(flags.Length > 0, "Mapping must show a BPM flag at visible red lines");
                Check(flags.All(t => Math.Abs(t.X - 5 - timeline.X - ((t.Value == L.Get("timeline.bpmValue", 120d) ? 0 : 2000) - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs) < .01),
                    "Mapping flags must remain anchored to red-line times");
                ui.View.ShowHitsoundingPage(); ui.Paint();
                var headings = new[] { "timing.bpm", "hitsounding.timingSet", "volume.title", "timeline.kiaiIndicator" }
                    .Select(key => ui.Canvas.Texts.Single(t => t.Value == L.Get(key) && Math.Abs(t.X - ui.View.SamplesetBounds.X - 2) < .01)).ToArray();
                Check(headings.Zip(headings.Skip(1)).All(pair => pair.Second.Y > pair.First.Y + 15), "Timing tracks must have distinct ordered rows");
                Check(ui.View.SamplesetBounds.Bottom < headings[0].Y && ui.View.AdditionsBounds.Bottom < headings[0].Y,
                    "Bank selectors must not overlap timing track headings");
                var bpm = ui.Canvas.Texts.Single(t => t.Value == "120" && t.Y == headings[0].Y);
                float x = bpm.X + 4, y = bpm.Y + 4;
                ui.View.PointerDown(x, y, 0, false, false);
                ui.View.PointerMove(x + 100, y, false, false); ui.View.PointerUp(x + 100, y, 0); ui.Paint();
                Check(original.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "BPM track dragging must not move red lines");
                Check(ReferenceEquals(conversion, ui.View.Conversion), "Timing presentation and selection must reuse conversion");
                ui.View.UpdateTransport(3500, 20000, true, false, false, null, null); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == "150" && t.Y == headings[0].Y), "BPM track must retain the preceding red value when its start is offscreen");
                L.SetLanguage(locale == "en" ? "zh-CN" : "en"); ui.Paint();
                Check(original.ContentEquals(ui.View.Document), "Language and viewport changes must preserve content");
                foreach (bool playing in new[] { false, true })
                {
                    ui.View.UpdateTransport(1200, 20000, true, playing, false, null, null); ui.Paint();
                    var seekConversion = ui.View.Conversion;
                    float zero = ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints[0]).X + 4;
                    float twoSeconds = ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints[2]).X + 4;
                    double requested = -1;
                    ui.View.RequestSeek = time => requested = time;
                    ui.Click(zero + (twoSeconds - zero) * .867f, headings[0].Y - 20);
                    Check(Math.Abs(ui.View.PlayheadMs - 1734) < .01 && Math.Abs(requested - 1734) < .01,
                        "Ruler clicks must seek to the exact pointer time without beat snapping");
                    Check(ui.View.AudioPlaying == playing && original.ContentEquals(ui.View.Document) && !ui.View.IsDirty
                        && ReferenceEquals(seekConversion, ui.View.Conversion), "Ruler seeking must preserve playback state, content and conversion");
                }
            }
        }
        finally { L.SetLanguage(language); }
        MergedTracks();
    }

    private static void MappingFlagEdges(int width, int height)
    {
        var map = new MapDocument { DurationMs = 30000 };
        map.TimingPoints.Add(new() { TimeMs = 10000, BeatLengthMs = 500 });
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(width, height); ui.Paint();
        var original = ui.View.Document.DeepClone();
        var conversion = ui.View.Conversion;
        var timeline = ui.View.ObjectTimelineBounds;
        double scale = ui.View.ObjectTimelinePixelsPerMs;
        string label = L.Get("timeline.bpmValue", 120d);
        float labelWidth = ((FruitsAtelier.App.Rendering.ICanvas)ui.Canvas).MeasureText(label, 10, true) + 10;
        foreach (float offset in new[] { -labelWidth - 1, -labelWidth + 1, -1, 0, 1,
                     timeline.Width - labelWidth - 1, timeline.Width - labelWidth + 1, timeline.Width - 1 })
        {
            ui.View.UpdateTransport(10000 + (timeline.Width / 2 - offset) / scale, 30000, true, false, false, null, null);
            ui.Paint();
            var flags = ui.Canvas.Texts.Where(t => t.Value == label && t.Y >= timeline.Y && t.Y < timeline.Bottom).ToArray();
            Check(flags.Length == (offset + labelWidth > 0 ? 1 : 0),
                "BPM flags must draw while partially visible at either viewport edge");
            if (flags.Length > 0)
                Check(Math.Abs(flags[0].X - timeline.X - offset - 5) < .01,
                    "Clipped BPM flags must keep their red-line anchor without jumping");
        }
        Check(original.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion),
            "BPM edge scrolling must preserve content and reuse conversion");
    }

    private static void MergedTracks()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.TimingPoints.AddRange([
            new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, Volume = 80 },
            new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, SampleSet = 3, SampleIndex = 1, Volume = 80 },
            new() { TimeMs = 1500, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, Volume = 80 },
            new() { TimeMs = 2000, BeatLengthMs = 500, SampleSet = 2, Volume = 80 },
            new() { TimeMs = 3000, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, Volume = 80, Effects = 1 }]);
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900);
        ui.View.ShowHitsoundingPage(); ui.View.UpdateTransport(1200, 10000, true, false, false, null, null); ui.Paint();
        var original = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        Check(Cells(0xE77785).Length == 1, "Equal BPM red points must merge visually");
        var green = Cells(0x72C686);
        Check(green.GroupBy(b => b.Y).OrderBy(g => g.Key).Select(g => g.Count()).SequenceEqual(new[] { 3, 1 }),
            "Sample bank changes must not split the unchanged volume track");
        Check(Cells(0x54728C).Length == 1 && Cells(0xD89532).Length == 1, "Kiai must merge Off and show On in its separate orange interval");
        Check(!ui.Canvas.Lines.Any(l => l.Color == 0xD89532), "Kiai state uses its track without a second indicator line");
        Check(map.TimingPoints.All(p => ui.View.HitsoundingTimingHandleBounds(ui.View.Document.TimingPoints.Single(t => t.TimeMs == p.TimeMs)).Height > 0),
            "Merged tracks must retain every original timing handle");
        ui.View.PointerMove(ui.View.SamplesetBounds.Right + 50, 260, false, false); ui.Paint();
        Check(original.ContentEquals(ui.View.Document) && ReferenceEquals(conversion, ui.View.Conversion), "Merged display and hover must preserve timing data and conversion");
        FruitsAtelier.App.Rendering.Rect[] Cells(uint colour) => ui.Canvas.Fills.Where(f => f.Color == colour && f.Bounds.Height == 22).Select(f => f.Bounds).ToArray();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
