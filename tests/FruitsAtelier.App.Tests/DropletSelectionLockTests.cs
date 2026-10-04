using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.Localization;

internal static class DropletSelectionLockTests
{
    public static void Selection()
    {
        foreach (bool imported in new[] { false, true })
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var map = new MapDocument { DurationMs = 5000, SliderTickRate = 1, IsDemo = false };
            var slider = new ImportedSlider { TimeMs = 1000, X = 120, Y = 192, PathType = 'L', PixelLength = 280 };
            slider.ControlPoints.AddRange([new(120, 192), new(400, 192)]);
            map.ImportedSliders.Add(slider);
            if (!imported) ImportedSliderEditing.ConvertToTrack(map, slider.Id);
            var ui = new Ui(lockDropletSelection: true); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Paint();
            Check(ui.View.DropletSelectionLocked, "Droplet selection must be locked by default.");
            Toggle(ui);
            ui.ClickText(Strings.Get("ui.sliderPathCurves"));
            var baseline = ui.View.Document.DeepClone();
            var target = OsuBeatmapWriter.Serialize(map).PlayableObjects
                .First(o => o.Kind == kind && o.TimeMs > 1400 && o.TimeMs < 1600);
            ui.ClickMap(target.TimeMs, target.X);
            string readout = Strings.Get("coordinate.readout", target.X.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            Check(ui.Canvas.Texts.Any(t => t.Value == readout), "Clicked droplet did not display X while its parent was selected.");
            Check(ui.View.XCoordinateFieldBounds is null && ui.View.SelectedObjectIds.SequenceEqual(new[] { slider.Id }),
                "Coordinate readout changed the first-click parent selection.");
            Check(baseline.ContentEquals(ui.View.Document), "Coordinate readout changed slider content.");
            if (kind == CatchObjectKind.TinyDroplet) CheckTinyReadout();
            ui.ClickMap(target.TimeMs, target.X);
            Check(ui.View.XCoordinateFieldBounds is not null, "Unlocked droplet could not be selected.");
            if (kind == CatchObjectKind.TinyDroplet) CheckTinyReadout();

            void CheckTinyReadout()
            {
                Check(ui.View.MovementReadout.Previous is not null && ui.View.MovementReadout.Next is not null,
                    "Tiny droplet movement readout is missing a neighbour");
                Check(ui.View.DistanceReadout.Previous is not null && ui.View.DistanceReadout.Next is not null,
                    "Tiny droplet distance readout is missing a neighbour");
                Check(ui.View.PreviousDistanceFieldBounds is null, "Reading tiny droplet distances enabled DS editing");
                var prior = ui.View.DistanceReadout;
                ui.Paint(); ui.Paint();
                Check(prior == ui.View.DistanceReadout && baseline.ContentEquals(ui.View.Document),
                    "Repainting changed tiny droplet distances or content");
            }
            Toggle(ui);
            Check(ui.View.DropletSelectionLocked && !ui.View.NotesLocked, "Droplet lock is not independent.");
            Check(ui.View.XCoordinateFieldBounds is null, "Lock retained the active droplet child selection.");
            ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X);
            Check(ui.View.XCoordinateFieldBounds is null, "Locked droplet was selected through its slider path.");
            Check(!ui.Canvas.Texts.Any(t => t.Value == readout), "Locked droplet exposed an X readout.");
            ui.Key('1'); ui.ClickMap(3500, 50);
            ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X, ctrl: true);
            Check(ui.View.SelectedObjectIds.Count == 0, "Hidden path still selected through a locked droplet.");
            var p = ui.ScreenAt(target.TimeMs, target.X);
            ui.View.PointerDown(p.X - 40, p.Y - 2, 0, false, false);
            ui.View.PointerMove(p.X + 40, p.Y + 2, false, false); ui.Paint();
            ui.View.PointerUp(p.X + 40, p.Y + 2, 0); ui.Paint();
            Check(ui.View.SelectedObjectIds.Count == 0, "Box selection included locked droplets.");
            ui.ClickMap(1000, 120);
            Check(ui.View.SelectedObjectIds.Contains(slider.Id), $"Droplet lock blocked a slider fruit (imported={imported}, mode={mode}, kind={kind}, viewStart={ui.View.ViewStartMs}, status={ui.View.StatusMessage}).");
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Droplet lock changed content/history.");
            Toggle(ui);
            ui.ClickMap(target.TimeMs, target.X); ui.ClickMap(target.TimeMs, target.X);
            Check(ui.View.XCoordinateFieldBounds is not null, "Unlocking did not restore droplet selection.");
        }
    }

    public static void Tooltips()
    {
        string original = Strings.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (1000, 720) })
            {
                Strings.SetLanguage(language);
                var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 5000 }); ui.Resize(size.Item1, size.Item2);
                foreach (var mode in Enum.GetValues<SliderEditingMode>())
                {
                    ui.View.SetSliderEditingMode(mode); ui.Paint();
                    for (int i = 0; i < 4; i++)
                    {
                        var b = ui.View.ToolButtonBounds[i];
                        ui.View.PointerMove(b.X + b.Width / 2, b.Y + b.Height / 2, false, false); ui.Paint();
                        string key = i switch { 0 => "tools.hint.select", 1 => "tools.hint.fruit",
                            2 => mode == SliderEditingMode.OsuLegacy ? "tools.hint.sliderLegacy" : "tools.hint.sliderPen", _ => "tools.hint.banana" };
                        Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get(key).Split('\n')[0]), "Missing tool operation hint.");
                        if (i == 2) Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("ui.osuLegacyMode")), "Tooltip hid slider mode controls.");
                    }
                }
                var ds = ui.View.AssistButtonBounds[5];
                ui.View.PointerMove(ds.X + ds.Width / 2, ds.Y + 10, false, false); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == Strings.Get("assist.hint.5").Split('\n')[0]), "Missing distance snap hint.");
                ui.ClickText(Strings.Get("ds.configure"));
                Check(ui.View.DistanceSnapDialogVisible, "Distance snap tooltip blocked Configure DS.");
            }
        }
        finally { Strings.SetLanguage(original); }
    }

    private static void Toggle(Ui ui)
    {
        var b = ui.View.AssistButtonBounds[6];
        ui.View.PointerMove(b.X + b.Width / 2, b.Y + b.Height / 2, false, false); ui.Paint();
        ui.ClickText(Strings.Get("assist.lockDropletSelection"));
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
