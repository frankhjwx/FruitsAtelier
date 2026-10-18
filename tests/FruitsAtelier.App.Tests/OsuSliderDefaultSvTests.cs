using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class OsuSliderDefaultSvTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            var defaults = new LibrarySettings();
            Check(defaults.OsuSliderDefaultSv == 1, "Missing preference must retain 1x SV.");
            defaults.OsuSliderDefaultSv = double.NaN; Check(defaults.OsuSliderDefaultSv == 1, "Nonfinite default SV was accepted.");
            defaults.OsuSliderDefaultSv = -1; Check(defaults.OsuSliderDefaultSv == .1, "Default SV lower bound failed.");
            defaults.OsuSliderDefaultSv = 99; Check(defaults.OsuSliderDefaultSv == 10, "Default SV upper bound failed.");
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int width in new[] { 980, 1440 })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(width, 900);
                ui.LoadDocument(new MapDocument { DurationMs = 5000 });
                ui.View.ToggleOsuCanvas(); ui.View.UpdateTransport(1000, 5000, true, false, false, null, null); ui.Paint();
                var before = ui.View.Document.DeepClone();
                var path = Path.GetFullPath($"artifacts/tests/osu-default-sv-{locale}-{width}.json");
                int saves = 0;
                ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
                var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("osu.sliderDefaultSv"));
                var ds = ui.View.OsuDistanceSpacingTrackBounds;
                Check(label.Y > ds.Bottom, "Default SV control must appear below DS.");
                var track = ui.View.OsuSliderDefaultSvTrackBounds;
                float target = track.X + (float)((2.5 - .1) / 9.9) * track.Width;
                ui.View.PointerDown(target, track.Y + 4, 0, false, false);
                ui.View.PointerMove(target, track.Y + 4, false, false); ui.Paint();
                Check(saves == 0 && ui.View.WantsCapture, "Default SV must defer persistence until release.");
                ui.View.PointerUp(target, track.Y + 4, 0); ui.Paint();
                Check(saves == 1 && LibrarySettings.Load(path).OsuSliderDefaultSv == 2.5, "Default SV did not save globally.");
                Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Default SV preference edited the map.");
                ui.View.PointerDown(track.Right, track.Y + 4, 0, false, false); ui.View.CancelInteraction(); ui.Paint();
                Check(ui.View.LibrarySettings.OsuSliderDefaultSv == 2.5 && saves == 1 && !ui.View.WantsCapture,
                    "Cancelled SV drag changed the stored preference.");
                var restored = new Ui(false);
                typeof(EditorView).GetProperty(nameof(EditorView.LibrarySettings))!.SetValue(restored.View, LibrarySettings.Load(path));
                restored.LoadDocument(before); restored.View.ToggleOsuCanvas();
                restored.View.UpdateTransport(1000, 5000, true, false, false, null, null); restored.Paint();
                restored.Key('3'); Click(64, 256); Click(192, 320); restored.Key(13); restored.Paint();
                Check(restored.View.Document.ImportedSliders.Single().SliderVelocityMultiplier == 2.5
                    && restored.View.Document.TimingPoints.Count == before.TimingPoints.Count, "New Legacy slider did not capture the restored default SV.");
                var existing = restored.View.Document.DeepClone();
                restored.View.LibrarySettings.OsuSliderDefaultSv = 4;
                Check(existing.ContentEquals(restored.View.Document), "Changing default SV modified an existing slider.");
                restored.Key('Z', ctrl: true);
                Check(restored.View.Document.ImportedSliders.Count == 0 && restored.View.LibrarySettings.OsuSliderDefaultSv == 4,
                    "Drawing undo changed the global default.");
                restored.Key('Y', ctrl: true); restored.Paint();
                Check(restored.View.Document.ImportedSliders.Single().SliderVelocityMultiplier == 2.5,
                    "Drawing redo replaced the captured SV with the current default.");
                void Click(double x, double y) { var p = restored.View.OsuCanvasScreen(x, y); restored.Click(p.X, p.Y); }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
