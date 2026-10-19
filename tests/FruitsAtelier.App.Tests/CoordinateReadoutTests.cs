using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class CoordinateReadoutTests
{
    public static void Run()
    {
        string language = Strings.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                Strings.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.4, IsDemo = false };
                foreach (var (time, x) in new[] { (1000, 100), (1500, 240), (2000, 310), (2500, 380) })
                    map.Fruits.Add(new Fruit { TimeMs = time, X = x });
                var ui = new Ui(); ui.LoadDocument(map);
                var original = ui.View.Document.DeepClone();
                ui.MoveMap(3000, 184);
                Check(0, Strings.Get("coordinate.readout", "184"));
                if (Rows().Length != 1) throw new Exception("Pointer readout must only show X.");

                ui.ClickMap(1500, 240); ui.MoveMap(3000, 400);
                Check(0, Strings.Get("coordinate.readout", "240"));
                Check(1, Strings.Get("assist.previous", Strings.Get("assist.ratio", 1d)));
                Check(2, Strings.Get("assist.next", Strings.Get("assist.ratio", .5)));

                ui.Key('1');
                ui.ClickMap(2000, 310); ui.ClickMap(1500, 240, ctrl: true);
                Check(0, Strings.Get("coordinate.readout", "240"));
                Check(1, Strings.Get("assist.previous", Strings.Get("assist.ratio", 1d)));
                Check(2, Strings.Get("assist.next", Strings.Get("assist.ratio", .5)));
                ui.Key('A', ctrl: true);
                Check(0, Strings.Get("coordinate.readout", "100"));
                Check(1, Strings.Get("assist.previous", "—"));
                Check(2, Strings.Get("assist.next", "—"));
                ui.ClickMap(3000, 320); ui.MoveMap(3000, 320);
                Check(0, Strings.Get("coordinate.readout", "320"));
                if (!original.ContentEquals(ui.View.Document) || ui.View.IsDirty)
                    throw new Exception("Coordinate readout changed map content.");

                var osu = new Ui(false);
                osu.View.LoadDocument(new MapDocument
                {
                    DurationMs = 10000,
                    Fruits = { new Fruit { TimeMs = 1000, X = 100, Y = 64 } }
                });
                osu.Resize(1440, 900);
                osu.View.ToggleOsuCanvas(); osu.Paint();
                var osuOriginal = osu.View.Document.DeepClone();
                MoveOsu(184, 96);
                var head = osu.View.OsuCanvasScreen(100, 64);
                osu.View.UpdateTransport(900, 10000, true, false, false, null, null); osu.Paint();
                osu.Click(head.X, head.Y);
                MoveOsu(320, 240);
                osu.View.ZoomOsuCanvasAt(head.X, head.Y, 1.3); osu.Paint();
                MoveOsu(128, 192);
                var center = osu.View.OsuCanvasScreen(256, 192);
                osu.View.PointerDown(center.X, center.Y, 1, false, false);
                osu.View.PointerMove(center.X + 30, center.Y + 20, false, false);
                osu.View.PointerUp(center.X + 30, center.Y + 20, 1); osu.Paint();
                MoveOsu(96, 288);
                osu.View.ZoomOsuCanvasAt(center.X, center.Y, .6); osu.Paint();
                MoveOsu(-16, -24);
                osu.View.PointerMove(0, 0, false, false); osu.Paint();
                AssertOsu("—", "—");
                if (!osuOriginal.ContentEquals(osu.View.Document) || osu.View.IsDirty)
                    throw new Exception("Reading osu coordinates changed map content.");

                void MoveOsu(double x, double y)
                {
                    var point = osu.View.OsuCanvasScreen(x, y);
                    osu.View.PointerMove(point.X, point.Y, false, false); osu.Paint();
                    AssertOsu(x.ToString("0"), y.ToString("0"));
                }
                void AssertOsu(string x, string y)
                {
                    string expected = Strings.Get("coordinate.readoutXY", x, y);
                    var label = osu.Canvas.Texts.SingleOrDefault(t => t.Value == expected);
                    if (label.Value != expected) throw new Exception($"Missing osu coordinate readout: {expected}");
                    if (label.Y <= osu.View.OsuDistanceSpacingTrackBounds.Bottom)
                        throw new Exception("The coordinate readout overlaps the osu DS control.");
                }

                RecordingCanvas.Label[] Rows() => ui.Canvas.Texts.Where(t =>
                    t.X > ui.View.CanvasRenderBounds.Right - 320
                    && t.Y >= ui.View.CanvasPlotBounds.Y + 13
                    && t.Y <= ui.View.CanvasPlotBounds.Y + 49).ToArray();
                void Check(int index, string expected)
                {
                    var rows = Rows();
                    if (rows.Length == 3 && (rows[1].Y != rows[2].Y || rows[2].X <= rows[1].X))
                        throw new Exception("Previous and next distances must share one horizontal row.");
                    if (rows.Length <= index || rows[index].Value != expected)
                        throw new Exception($"Coordinate readout row {index}: expected {expected}, got {string.Join(", ", rows.Select(t => t.Value))}");
                }
            }
        }
        finally { Strings.SetLanguage(language); }
    }
}
