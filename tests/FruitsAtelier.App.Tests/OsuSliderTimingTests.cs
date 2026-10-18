using System.Reflection;
using System.Runtime.CompilerServices;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class OsuSliderTimingTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            string original = L.Language;
            try
            {
                L.SetLanguage(language);
                var ui = CreateUi(); var before = ui.View.Document.DeepClone();
                var button = ui.View.AssistButtonBounds[5];
                ui.View.PointerMove(button.X + 5, button.Y + 5, false, false); ui.Paint();
                ui.ClickText(L.Get("ds.configure"));
                Check(ui.View.DistanceSnapDialogVisible && before.ContentEquals(ui.View.Document),
                    "Configure DS must receive the click above the osu canvas.");
            }
            finally { L.SetLanguage(original); }
        }
        foreach (var (divisor, beat) in new[] { (3, 500d), (4, 400d), (6, 333.3333333333333), (16, 750d) })
        {
            var ui = CreateUi(beat); ui.SetSnapDivisor(divisor); ui.Key('3');
            Click(ui, 64, 192);
            double previousEnd = 0;
            foreach (int x in new[] { 256, 400 })
            {
                var at = ui.View.OsuCanvasScreen(x, 192);
                ui.View.PointerMove(at.X, at.Y, false, false); ui.Paint();
                var preview = ((Guid Id, double Start, double End, uint Color))typeof(EditorView)
                    .GetMethod("TimelinePlacementPreview", Hidden)!.Invoke(ui.View, null)!;
                Check(preview.End > preview.Start && preview.End > previousEnd, "Draft length did not update the upper timeline.");
                NearInteger((preview.End - preview.Start) / (beat / divisor));
                previousEnd = preview.End;
            }
            ui.Key(13);
            var slider = ui.View.Document.ImportedSliders.Single();
            Check(Math.Abs(ImportedSliderConverter.EndTimeMs(ui.View.Document, slider) - previousEnd) < 1e-6,
                "Committed length differs from the timeline draft.");
            var baseline = ui.View.Document.DeepClone();
            var from = ui.View.OsuCanvasScreen(400, 192); var to = ui.View.OsuCanvasScreen(320, 192);
            ui.View.PointerDown(from.X, from.Y, 0, false, false);
            ui.View.PointerMove(to.X, to.Y, false, false); ui.Paint();
            slider = ui.View.Document.ImportedSliders.Single();
            double duration = ImportedSliderConverter.DurationMs(ui.View.Document, slider);
            NearInteger(duration / (beat / divisor));
            var sources = (Array)typeof(EditorView).GetField("timelineSources", Hidden)!.GetValue(ui.View)!;
            var source = sources.Cast<ITuple>().Single(item => (Guid)item[0]! == slider.Id);
            Check(Math.Abs((double)source[2]! - slider.TimeMs - duration) < 1e-6,
                "The upper timeline did not update before releasing the control drag.");
            Check(Math.Abs(duration - (previousEnd - slider.TimeMs)) > 1, "The drag did not change the fixture length.");
            ui.Key(27); ui.View.PointerUp(to.X, to.Y, 0); ui.Paint();
            Check(baseline.ContentEquals(ui.View.Document), "Canceling the snapped drag did not restore content.");
            var output = OsuBeatmapWriter.Serialize(ui.View.Document);
            NearInteger(ImportedSliderConverter.DurationMs(output.ReadBack, output.ReadBack.ImportedSliders.Single()) / (beat / divisor));
        }
        var changing = CreateUi(); changing.Key('3'); Click(changing, 64, 192);
        var pointer = changing.View.OsuCanvasScreen(315, 192);
        changing.View.PointerMove(pointer.X, pointer.Y, false, false); changing.Paint();
        var draftGeometry = typeof(EditorView).GetMethod("EnsureOsuDraftGeometry", Hidden)!;
        double initialLength = ((ImportedSlider)draftGeometry.Invoke(changing.View, null)!).PixelLength;
        changing.SetSnapDivisor(3);
        changing.View.PointerMove(pointer.X, pointer.Y, false, false); changing.Paint();
        var updatedDraft = (ImportedSlider)draftGeometry.Invoke(changing.View, null)!;
        Check(Math.Abs(updatedDraft.PixelLength - initialLength) > 1, "Changing Snap retained the previous draft length.");
        NearInteger(ImportedSliderConverter.DurationMs(changing.View.Document, updatedDraft) / (500d / 3));
        var free = CreateUi();
        typeof(EditorView).GetField("snap", Hidden)!.SetValue(free.View, false);
        free.Key('3'); Click(free, 64, 192); Click(free, 315, 192); free.Key(13);
        Check(Math.Abs(free.View.Document.ImportedSliders.Single().PixelLength - 251) < .01,
            "Disabling beat snapping must allow a free slider length.");
    }

    private static Ui CreateUi(double beat = 500)
    {
        var ui = new Ui(false);
        ui.View.LibrarySettings.LazerEditing = false;
        var map = new MapDocument { DurationMs = 5000, SliderMultiplier = 1.4 };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = beat },
            new() { TimeMs = 0, BeatLengthMs = -100 / 1.37, Uninherited = false }]);
        ui.LoadDocument(map); ui.View.UpdateTransport(1000, 5000, true, false, false, null, null);
        ui.View.ToggleOsuCanvas(); ui.Paint();
        if (ui.View.EditorGridSettings.Enabled) ui.Key('T');
        return ui;
    }

    private static void Click(Ui ui, int x, int y)
    { var at = ui.View.OsuCanvasScreen(x, y); ui.Click(at.X, at.Y); }
    private static void NearInteger(double value) => Check(Math.Abs(value - Math.Round(value)) < 1e-6,
        $"Slider length did not snap to the current beat division: {value:R}.");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
