using System.Text.Json;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class MapImportSettingsTests
{
    public static void Run()
    {
        var defaults = JsonSerializer.Deserialize<LibrarySettings>("{}")!;
        Check(defaults.ImportSliderConversion == ImportSliderConversionMode.Ask && defaults.ConfirmCompensatedImports, "Older settings retain both confirmation defaults");
        Check(JsonSerializer.Deserialize<LibrarySettings>("{\"ImportSliderConversion\":99}")!.ImportSliderConversion == ImportSliderConversionMode.Ask,
            "Unknown import modes fall back to asking");
        string language = L.Language;
        string root = Path.GetFullPath("artifacts/tests/map-import-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (760,580), (980,620), (1440,900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1,size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
                ui.View.SupportsDisplayMode = ui.View.SupportsFullscreen = true;
                var before = ui.View.Document.DeepClone();
                ui.View.OpenSettings(); ui.Paint(); Scroll();
                var mode = ui.View.ImportSliderModeBounds;
                Check(mode.Y > ui.View.SettingsBounds.Y + 116 && ui.View.ConfirmFaImportBounds.Bottom < ui.View.SettingsBounds.Bottom - 116,
                    "Map Import controls scroll into the General viewport");
                ui.Click(mode.X+12,mode.Y+12); ui.ClickText(L.Get("settings.importSliderAlways"));
                ui.ClickText(L.Get("settings.importConfirmFa"));
                Check(ui.View.LibrarySettings.ImportSliderConversion == ImportSliderConversionMode.Ask && ui.View.LibrarySettings.ConfirmCompensatedImports,
                    "Settings drafts wait for Apply");
                string path = Path.Combine(root, Guid.NewGuid()+".json"); ui.View.ApplySettings(path); ui.Paint();
                var saved = LibrarySettings.Load(path);
                Check(saved.ImportSliderConversion == ImportSliderConversionMode.ConvertToFSlider && !saved.ConfirmCompensatedImports,
                    "Both import settings persist and reload");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint(); Scroll(); mode = ui.View.ImportSliderModeBounds;
                ui.Click(mode.X+12,mode.Y+12); ui.ClickText(L.Get("settings.importSliderKeep"));
                ui.Key(27); ui.View.OpenSettings(); ui.Paint(); Scroll();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.importSliderAlways")+" ▾"), "Closing without Apply discards the import draft");
                ui.Key(27); Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Import preferences never edit the current map");
                void Scroll()
                {
                    var r = ui.View.SettingsBounds;
                    ui.View.Wheel(r.Right - 60, r.Y + 200, -12000, false); ui.Paint();
                    for (int i = 0; i < 20; i++)
                    {
                        if (ui.View.ImportSliderModeBounds.Y > r.Y + 116 && ui.View.ConfirmFaImportBounds.Bottom < r.Bottom - 116) break;
                        ui.View.Wheel(r.Right - 60, r.Y + 200, ui.View.ImportSliderModeBounds.Y <= r.Y + 116 ? 120 : -120, false);
                        ui.Paint();
                    }
                }
            }
            L.SetLanguage("en");
            foreach (var mode in new[] { ImportSliderConversionMode.KeepLegacy, ImportSliderConversionMode.ConvertToFSlider })
            {
                var ui = new Ui(false); ui.View.LibrarySettings.Workspace = root;
                string path = Path.Combine(root, Guid.NewGuid()+"-remember.json"); int saves=0;
                ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
                ui.View.NewProject(); ui.View.AddDifficulty(Candidate(0,1)); Wait(ui);
                Check(ui.View.SliderImportPromptVisible, "Default import asks about conversion");
                ui.ClickText(L.Get("sliderImport.remember")); ui.View.AnswerSliderImport(mode == ImportSliderConversionMode.ConvertToFSlider); Wait(ui);
                Check(saves == 1 && LibrarySettings.Load(path).ImportSliderConversion == mode, "Remember stores either Keep or Convert as the global preference");
                ui.View.NewProject(); ui.View.AddDifficulty(Candidate(0,1)); Wait(ui);
                Check(!ui.View.SliderImportPromptVisible && !ui.View.SliderImportSourcePromptVisible && saves == 1,
                    "Future imports use the remembered choice without another prompt or save");
                Check((ui.View.Document.Tracks.Count > 0) == (mode == ImportSliderConversionMode.ConvertToFSlider), "Remembered representation is applied");
                if (mode == ImportSliderConversionMode.KeepLegacy)
                {
                    ui.View.ConvertAllSliders(); ui.Paint();
                    Check(ui.View.SliderImportPromptVisible && !ui.Canvas.Texts.Any(t => t.Value == L.Get("sliderImport.remember")),
                        "Manual conversion asks without offering to remember an import choice");
                    ui.View.AnswerSliderImport(false);
                }
            }
            foreach (var (matching,total,prompt) in new[] { (19,20,false),(18,20,true),(0,1,false) })
            {
                var ui = new Ui(false); ui.View.LibrarySettings.ConfirmCompensatedImports=false;
                ui.View.LibrarySettings.ImportSliderConversion=ImportSliderConversionMode.KeepLegacy;
                ui.View.NewProject(); ui.View.AddDifficulty(Candidate(matching,total)); Wait(ui);
                Check(ui.View.SliderImportSourcePromptVisible == prompt, "95% auto-accepts, 90% forces a choice, ordinary imports skip the source question");
                if (prompt) { ui.View.AnswerSliderImportSource(false); Wait(ui); }
                Check(!ui.View.SliderImportPromptVisible && ui.View.Document.SliderImportState == (matching == 19 ? SliderImportState.Compensated : SliderImportState.Native),
                    "Automatic source decisions and Keep Legacy apply independently");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static MapDocument Candidate(int matching, int total)
    {
        var document = new MapDocument { BeatLengthMs=500, SliderMultiplier=1, SliderTickRate=1, DurationMs=total*1000+2000, IsDemo=false };
        for (int i=0;i<total;i++)
        {
            var slider = new ImportedSlider { X=100, Y=64, TimeMs=1000+i*1000, PixelLength=40, PathType='L' };
            slider.ControlPoints.AddRange(i<matching ? [new(100,64),new(100,80),new(100,104)] : [new(100,64),new(100,104)]);
            document.ImportedSliders.Add(slider);
        }
        return document;
    }

    private static void Wait(Ui ui)
    {
        var deadline=DateTime.UtcNow.AddSeconds(30);
        while(ui.View.SliderConversionBusy && DateTime.UtcNow<deadline) { ui.Paint(); Thread.Sleep(5); }
        ui.Paint(); Check(!ui.View.SliderConversionBusy, "Import worker finishes");
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
}
