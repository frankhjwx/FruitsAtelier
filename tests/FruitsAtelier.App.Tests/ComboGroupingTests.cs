using FruitsAtelier.Core;

static class ComboGroupingTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.AddRange([
            new Fruit { TimeMs = 1000, X = 256 },
            new Fruit { TimeMs = 1250, X = 256 },
            new Fruit { TimeMs = 1500, X = 256 },
            new Fruit { TimeMs = 1750, X = 256 }
        ]);
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        ui.LoadDocument(map);
        ui.OpenPreview();
        Seek(1300);
        var conversion = ui.View.Conversion;
        Check(!PriorComboExploding(), "The preceding group ends only after its last fruit.");
        Check(TimelineNumber(1500) == "3" && TimelineNumber(1750) == "4", "Initial timeline numbering is sequential.");

        ui.Key('1');
        ui.ClickFruit(map.Fruits[2].Id);
        ui.Key('Q');
        Check(ReferenceEquals(conversion, ui.View.Conversion), "New Combo need not rebuild the catch stream.");
        Verify(true);
        Check(ObjectFlags.NewCombo(ui.View.Document, map.Fruits[2].Id), "New Combo flag was not saved.");
        var readBack = OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack;
        Check(ObjectFlags.NewCombo(readBack, readBack.Fruits.Single(f => f.TimeMs == 1500).Id),
            "The exported beatmap lost New Combo.");
        VerifyTestplayGrouping();

        ui.Key('Z', ctrl: true);
        Verify(false);
        ui.Key('Y', ctrl: true);
        Verify(true);

        void Seek(double time)
        {
            ui.View.UpdateTransport(time, 5000, true, false, false, null, null);
            ui.Paint();
        }

        void Verify(bool newCombo)
        {
            Seek(1300);
            Check(PriorComboExploding() == newCombo, "Preview grouping did not update immediately.");
            Check(TimelineNumber(1500) == (newCombo ? "1" : "3")
                && TimelineNumber(1750) == (newCombo ? "2" : "4"),
                "Object timeline numbering did not update immediately.");
            Check(ui.Canvas.Texts.Any(t => t.Value == "NC" && t.Color == 0xF2C66D) == newCombo,
                "Canvas New Combo label did not update immediately.");
        }

        void VerifyTestplayGrouping()
        {
            Seek(0);
            ui.View.UpdateTransport(0, 5000, false, false, false, null, null);
            ui.View.StartTestplay();
            clock.Advance(1300);
            ui.Paint();
            Check(ui.View.TestplayCombo == 2, "Testplay did not catch the preceding fruits.");
            Check(ui.Canvas.Circles.Any(c => c.Color == 0xFFFFFF && c.Opacity < 1),
                "Testplay did not end the preceding combo after the first New Combo edit.");
            ui.View.StopTestplay();
        }

        bool PriorComboExploding() => ui.Canvas.Circles.Any(c => c.X > ui.Plot.Right
            && c.Color == 0xFFFFFF && c.Opacity < 1);

        string? TimelineNumber(double time)
        {
            var timeline = ui.View.ObjectTimelineBounds;
            float x = timeline.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
            return ui.Canvas.Texts.SingleOrDefault(t => Math.Abs(t.X - (x - 4)) < 9
                && t.Y >= timeline.Y && t.Y < timeline.Bottom).Value;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class ManualTime : TimeProvider
    {
        private double milliseconds;
        public override long TimestampFrequency => 1_000_000;
        public override long GetTimestamp() => (long)(milliseconds * 1000);
        public void Advance(double elapsedMs) => milliseconds += elapsedMs;
    }
}
