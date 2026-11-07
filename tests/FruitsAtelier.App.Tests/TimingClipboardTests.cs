using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimingClipboardTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ticks;
    }

    public static void Run()
    {
        string previous = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (bool timingPage in new[] { false, true })
            foreach (bool halfSize in new[] { false, true })
            {
                L.SetLanguage(locale);
                var clock = new Clock(); var ui = new Ui(false, clock); ui.Resize(1440, 900);
                var map = new MapDocument { IsDemo = false, DurationMs = 10000 };
                map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true },
                    new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, Volume = 80 },
                    new() { TimeMs = 2000, BeatLengthMs = -100, Uninherited = false, Effects = 1 }]);
                ui.LoadDocument(map); if (timingPage) ui.Key(114); ui.Key(117);
                float scale = halfSize ? .5f : 1;
                if (halfSize)
                {
                    var window = ui.View.TimingSetupBounds;
                    ui.View.PointerDown(window.Right - 2, window.Bottom - 2, 0, false, false);
                    ui.View.PointerMove(window.X, window.Y, false, false);
                    ui.View.PointerUp(window.X, window.Y, 0); ui.Paint();
                }
                string clipboard = "invalid";
                ui.View.RequestCopyText = text => clipboard = text;
                ui.View.RequestTimingClipboardMenu = request => ui.View.ShowTimingClipboardMenu(clipboard, request);
                var list = ui.View.TimingListViewport;
                ClickRow(0); ClickRow(1, ctrl: true); ui.Key('C', ctrl: true);
                Check(TimingEditing.Parse(clipboard).Length == 2, "Copies every selected line");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.copied")) && ui.View.TimingCopyOpacity == 1, "Copy shows feedback");
                clock.Ticks = 850; ui.Paint();
                Check(ui.View.TimingCopyOpacity > 0 && ui.View.TimingCopyOpacity < 1 && ui.View.TimingFeedbackNeedsRedraw, "Feedback fades");
                clock.Ticks = 1000; ui.Paint();
                Check(ui.View.TimingCopyOpacity == 0 && !ui.View.TimingFeedbackNeedsRedraw && !ui.Canvas.Texts.Any(t => t.Value == L.Get("timing.copied")), "Feedback expires at one second without idle redraws");
                RightRow(0); Check(Menu("copy") && Menu("paste"), "Selected row shows copy and valid paste");
                ui.ClickText(L.Get("editor.command.copy"));
                Check(TimingEditing.Parse(clipboard).Length == 2, "Context copy retains multiselection");
                RightRow(1); Check(Menu("copy"), "Any selected row offers copy");
                ui.Key(27); Check(ui.View.TimingSetupVisible, "Escape dismisses menu without closing Timing Setup");
                RightRow(2); Check(!Menu("copy") && Menu("paste"), "Unselected row only offers paste"); ui.Key(27);
                Right(list.X + 30, list.Bottom - 10); Check(!Menu("copy") && Menu("paste"), "Empty list area offers paste only");
                ui.ClickText(L.Get("editor.command.paste")); ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                Check(TimingEditing.Parse(clipboard).Length == 5 && ui.View.Document.ContentEquals(map), "Context paste updates only the draft");
                ui.Key('Z', ctrl: true); ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                Check(TimingEditing.Parse(clipboard).Length == 3, "Paste is draft-undoable");
                clipboard = "not timing data"; Right(list.X + 30, list.Bottom - 10);
                Check(!Menu("copy") && !Menu("paste"), "Invalid clipboard has no empty-area menu");
                ClickRow(0); RightRow(0); Check(Menu("copy") && !Menu("paste"), "Invalid clipboard still permits selection copy"); ui.Key(27);
                int pending = -1; ui.View.RequestTimingClipboardMenu = request => pending = request;
                RightRow(0); ClickRow(1);
                ui.View.ShowTimingClipboardMenu(TimingEditing.Serialize(map.TimingPoints), pending); ui.Paint();
                Check(!Menu("copy") && !Menu("paste"), "Delayed clipboard read cannot reopen an obsolete menu");
                ui.Key(27); Check(ui.View.Document.ContentEquals(map), "Cancel retains document");

                bool Menu(string command) => ui.Canvas.Texts.Any(t => t.Value == L.Get("editor.command." + command));
                void ClickRow(int row, bool ctrl = false)
                {
                    float x = list.X + 30, y = list.Y + (46 + row * 28) * scale;
                    ui.View.PointerDown(x, y, 0, false, ctrl); ui.View.PointerUp(x, y, 0); ui.Paint();
                }
                void RightRow(int row) => Right(list.X + 30, list.Y + (46 + row * 28) * scale);
                void Right(float x, float y) { ui.View.PointerDown(x, y, 2, false, false); ui.View.PointerUp(x, y, 2); ui.Paint(); }
            }
        }
        finally { L.SetLanguage(previous); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
