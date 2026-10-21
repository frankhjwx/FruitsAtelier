using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class NewDifficultyTests
{
    public static void Run()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (var size in new[] { (980, 620), (1440, 900) })
        {
            L.SetLanguage(locale);
            var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
            var map = DemoMap.Create(); map.IsDemo = false;
            SongSetup.Set(map, "Difficulty", "HPDrainRate", "6.3");
            SongSetup.Set(map, "Difficulty", "OverallDifficulty", "7.2");
            map.SliderTickRate = 3; map.SliderMultiplierOverride = 2.4;
            ui.LoadDocument(map); ui.Paint();
            var original = ui.View.Document.DeepClone();
            Open(); ui.Key('A', ctrl: true); ui.Type("Copied"); ui.Paint();
            Click("difficulty.useSource"); Confirm();
            Check(ui.View.DifficultyCount == 2 && ui.View.CurrentDifficultyName == "Copied", "Named copy is created");
            var expected = original.DeepClone();
            var metadata = expected.OriginalSections.FirstOrDefault(s => s.Name == "Metadata");
            if (metadata is null) { metadata = new OsuSection { Name = "Metadata" }; expected.OriginalSections.Add(metadata); }
            metadata.Lines.RemoveAll(line => line.Split(':', 2)[0].Trim() is "Version" or "BeatmapID");
            metadata.Lines.Add("Version:Copied"); metadata.Lines.Add("BeatmapID:0");
            Check(expected.ContentEquals(ui.View.Document), "Copy retains all source content apart from new difficulty metadata");
            ui.View.Document.Fruits[0].X += 10;
            ui.View.SwitchDifficulty(0); ui.Paint();
            Check(original.ContentEquals(ui.View.Document), "Copied objects are independent");

            Open(); Click("difficulty.templateTab"); Click("template.manage"); Click("template.fromCurrent"); ui.Key(13);
            Check(original.ContentEquals(ui.View.Document) && ui.View.DifficultyCount == 2, "Saving a template preserves map content and difficulty count");
            string root = Path.GetFullPath("artifacts/tests/new-difficulty/" + locale + size.Item1);
            Directory.CreateDirectory(root);
            string config = Path.Combine(root, "settings.json"); ui.View.LibrarySettings.Save(config);
            var template = LibrarySettings.Load(config).MapTemplates.Single();
            Check(template.SliderMultiplier == 2.4 && template.HP == "6.3" && template.OD == "7.2", "Template settings survive restart");
            ui.Key(27); Check(original.ContentEquals(ui.View.Document), "Cancel and template save preserve source content");
            ui.View.Document.ApproachRate = 4; ui.View.Document.CircleSize = 3;
            ui.View.Document.SliderMultiplierOverride = 1.1; ui.View.Document.SliderTickRate = 1;
            var blankSource = ui.View.Document.DeepClone();
            Open(); Click("difficulty.templateTab"); Click("difficulty.useSource"); Confirm();
            Check(ui.View.Document.Fruits.Count == 0 && ui.View.Document.Tracks.Count == 0
                && ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.BananaShowers.Count == 0, "Template creates an empty difficulty");
            Check(ui.View.Document.ApproachRate == template.ApproachRate && ui.View.Document.CircleSize == template.CircleSize
                && ui.View.Document.EffectiveSliderMultiplier == 2.4 && ui.View.Document.SliderTickRate == 3, "Template applies stored parameters");
            Check(ui.View.Document.TimingPoints.Count == blankSource.TimingPoints.Count, "Template keeps blank creation timing");
            ui.View.SwitchDifficulty(0); ui.Paint();
            Check(blankSource.ContentEquals(ui.View.Document), "Template creation leaves its source unchanged");

            Open(); Click("difficulty.templateTab"); Confirm();
            Check(ui.View.Document.ApproachRate == 4 && ui.View.Document.EffectiveSliderMultiplier == 1.1
                && ui.View.Document.SliderTickRate == 1 && ui.View.Document.Fruits.Count == 0, "Disabled source toggle retains blank behavior");
            Open(); ui.Key('A', ctrl: true); ui.Key(8); ui.Key(13);
            Check(ui.View.DiscardConfirmationVisible, "Empty difficulty name cannot submit"); ui.Key(27);

            ui.View.OpenMapTemplates(); ui.Paint();
            Check(!ui.View.AddDifficulty(), "Template manager blocks difficulty content operations");
            Set("template.name", "Renamed template"); Set("AR", "6.5", false); Set("SV", "1.8", false);
            var iconLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("template.icon"));
            ui.Click(iconLabel.X + 10 * 36 + 16, iconLabel.Y + 40);
            ui.Key(13);
            Check(ui.View.LibrarySettings.MapTemplates.Single().Name == "Renamed template"
                && ui.View.LibrarySettings.MapTemplates[0].ApproachRate == 6.5
                && ui.View.LibrarySettings.MapTemplates[0].Icon == MapTemplateIcon.Indigo, "Manager edits template name, parameters and icon");
            ui.View.LibrarySettings.Save(config);
            Check(LibrarySettings.Load(config).MapTemplates.Single().Icon == MapTemplateIcon.Indigo, "Template icon survives restart");
            ui.View.OpenSettings(); ui.Paint(); ui.View.ApplySettings(config, force: true); ui.Key(27);
            Check(ui.View.LibrarySettings.MapTemplates.Single().Icon == MapTemplateIcon.Indigo, "Applying settings retains templates and icons");
            ui.View.OpenMapTemplates(); ui.Paint(); Set("AR", "NaN", false); ui.Key(13);
            Check(ui.View.MapTemplatesVisible, "Invalid parameters cannot save"); ui.Key(27);
            Check(ui.View.LibrarySettings.MapTemplates[0].ApproachRate == 6.5, "Cancel retains saved parameters");
            ui.View.OpenMapTemplates(); ui.Paint(); Click("template.delete"); ui.Key(27);
            Check(ui.View.LibrarySettings.MapTemplates.Count == 1, "Cancel discards deletion");
            ui.View.OpenMapTemplates(); ui.Paint(); Click("template.delete"); ui.Key(13);
            Check(ui.View.LibrarySettings.MapTemplates.Count == 0, "Save commits deletion");

            ui.View.SwitchDifficulty(0); ui.Paint(); Open(); Click("difficulty.useSource");
            var nextSource = ui.Canvas.Texts.Last(t => t.Value == "›"); ui.Click(nextSource.X + 4, nextSource.Y + 5); Confirm();
            Check(ui.View.Document.Fruits[0].X == original.Fruits[0].X + 10, "Copy selector uses the chosen inactive difficulty");

            ui.View.OpenMapTemplates(); ui.Paint(); Click("template.new");
            Set("template.name", "Precise"); Set("SV", "1.23456789123", false); ui.Key(13);
            ui.View.OpenMapTemplates(); ui.Paint(); ui.Key(13);
            Check(ui.View.LibrarySettings.MapTemplates.Single().SliderMultiplier == 1.23456789123, "Opening and saving a template preserves numeric precision");

            ui.View.InitializeLibrary(true, new LibrarySettings { FirstRunSetupCompleted = true, Workspace = Path.Combine(root, "library") }); ui.Paint();
            Click("difficulty.templateTab");
            Check(ui.View.MapTemplatesVisible && ui.View.LibraryVisible, "Library opens standalone template manager");
            Click("template.new"); Set("template.name", "Library template"); Set("CS", "4.2", false); ui.Key(13);
            Check(ui.View.LibrarySettings.MapTemplates.Single().CircleSize == 4.2, "Library creates templates without a source difficulty");

            void Set(string label, string value, bool localized = true)
            {
                var text = ui.Canvas.Texts.Single(t => t.Value == (localized ? L.Get(label) : label));
                ui.Click(text.X + 10, text.Y + 34); ui.Key('A', ctrl: true); ui.Type(value); ui.Paint();
            }

            void Click(string key) => ui.ClickText(L.Get(key));
            void Open()
            {
                var plus = ui.Canvas.Texts.Single(t => t.Value == "+" && t.Y < 84);
                ui.Click(plus.X + 4, plus.Y + 4); Click("project.add");
            }
            void Confirm()
            {
                var label = ui.Canvas.Texts.Last(t => t.Value == L.Get("project.add"));
                ui.Click(label.X + 4, label.Y + 5);
            }
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
