using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TimingExtentUiTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (double audioDuration in new[] { 0d, 5000d })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|250:192,1,150\n");
                var ui = new Ui(false);
                try
                {
                    ui.LoadDocument(map);
                    ui.Key(9);
                    Check(ui.View.IsOsuCanvas, "Tab activates the native slider editing canvas.");
                    ui.View.UpdateTransport(0, audioDuration, audioDuration > 0, false, false, null, null);
                    ui.Paint();
                    var before = ui.View.Document.DeepClone();
                    ui.ClickText(L.Get("timeline.timingMenu"));
                    ui.ClickText(L.Get("timing.move"));
                    var field = ui.View.TimingFields.Single(f => f.Key == "move").Bounds;
                    ui.Click(field.X + 8, field.Y + 8);
                    ui.Key('A', ctrl: true); ui.Type("10000"); ui.Key(13); ui.Key(13);
                    double tail = ImportedSliderConverter.EndTimeMs(ui.View.Document, ui.View.Document.ImportedSliders.Single());
                    Check(tail == 11750 && ui.View.TimelineDurationMs >= tail,
                        $"Moving the final Legacy slider expands the timeline: tail={tail}, timeline={ui.View.TimelineDurationMs}.");
                    var moved = ui.View.Document.DeepClone();
                    double? requested = null;
                    ui.View.RequestSeek = time => requested = time;
                    var timestamp = ui.View.TimeDisplayBounds;
                    ui.Click(timestamp.X + 10, timestamp.Y + 10);
                    ui.View.PasteTimeJumpText("00:11:750", ui.View.TimeJumpSession);
                    ui.Key(13);
                    Check(ui.View.PlayheadMs == tail && requested == tail, "The moved tail remains reachable through timestamp navigation.");
                    Check(ui.View.Document.ContentEquals(moved), "Seeking does not change authoring content.");
                    ui.Key('Z', ctrl: true);
                    Check(ui.View.Document.ContentEquals(before), "One undo restores the original timing and duration.");
                    ui.Key('Y', ctrl: true);
                    Check(ui.View.Document.ContentEquals(moved), "Redo restores the moved timing and duration.");
                    ui.LoadDocument(ProjectSerializer.Read(ProjectSerializer.Serialize(moved)));
                    Check(ui.View.TimelineDurationMs >= tail, "Reopening retains the expanded timeline.");
                    Console.WriteLine($"PASS Legacy tail navigation, history and reopen {locale}, audio={audioDuration}");
                }
                finally { ui.View.StopFileMonitoring(); ui.View.NewProject(); }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
