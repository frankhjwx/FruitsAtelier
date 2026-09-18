using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimeJumpTests
{
    public static void Run()
    {
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 240000 };
        map.Fruits.Add(new Fruit { TimeMs = 2000, X = 256 }); ui.LoadDocument(map);
        ui.View.UpdateTransport(1500, 240000, true, false, false, null, null); ui.Paint();
        foreach (var size in new[] { (980f, 620f), (1440f, 1200f) })
        {
            ui.Resize(size.Item1, size.Item2);
            var buttons = ui.View.ToolButtonBounds;
            Near(ui.View.CanvasPlotBounds.Y + ui.View.CanvasPlotBounds.Height / 2, (buttons[0].Y + buttons[^1].Bottom) / 2);
            Near(ui.Height - 120 + 46, ui.View.TimeDisplayBounds.Y + ui.View.TimeDisplayBounds.Height / 2);
        }
        string copied = "", clipboard = "03:03:311 (2,3) - "; double? seek = null;
        ui.View.RequestCopyText = text => copied = text;
        ui.View.RequestPasteTime = () => ui.View.PasteTimeJumpText(clipboard, ui.View.TimeJumpSession);
        ui.View.RequestSeek = time => seek = time;
        void Open() { var r = ui.View.TimeDisplayBounds; ui.Click(r.X + 10, r.Y + 10); Check(ui.View.TimeJumpVisible, "timestamp opens dialog"); }
        Open();
        Check(ui.View.IsEditingText, "dialog enables text input");
        ui.Key('C', ctrl: true); Check(copied == "00:01:500", "initial time is selected for copying");
        ui.Key('V', ctrl: true); ui.Key(13);
        Near(183311, ui.View.PlayheadMs); Near(183311, seek!.Value);
        Check(!ui.View.TimeJumpVisible, "Enter closes successful jump");
        Open(); ui.ClickText(L.Get("timeJump.copy")); Check(copied == "03:03:311", "Copy button uses current timestamp");
        clipboard = "00:99:123"; ui.ClickText(L.Get("timeJump.paste")); ui.Key(13);
        Check(ui.View.TimeJumpVisible, "invalid timestamp stays open"); Near(183311, ui.View.PlayheadMs);
        double start = ui.View.ViewStartMs;
        ui.View.Wheel(ui.Plot.X + 20, ui.Plot.Y + 20, 120, false); ui.Key('F'); ui.Key(46);
        Near(start, ui.View.ViewStartMs);
        Check(ui.View.Document.ContentEquals(map), "modal blocks editing and canvas navigation");
        ui.Key('A', ctrl: true); clipboard = "12345"; ui.Key('V', ctrl: true); ui.ClickText(L.Get("timeJump.go"));
        Near(12345, ui.View.PlayheadMs);
        Open(); int session = ui.View.TimeJumpSession; ui.Key(27); Open();
        ui.View.PasteTimeJumpText("99999", session); ui.Key('C', ctrl: true);
        Check(copied == "00:12:345", "late clipboard response cannot overwrite a new dialog");
        ui.Key(27); Check(!ui.View.LibraryVisible && !ui.View.TimeJumpVisible, "Escape dismisses only dialog");
        Check(!ui.View.IsDirty && ui.View.Document.ContentEquals(map), "time navigation does not edit beatmap");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .01, $"Expected {expected}, got {actual}");
}
