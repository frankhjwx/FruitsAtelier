using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class NewDifficultyTests
{
    public static void Run()
    {
        SourceControls();
        TemplateReordering();
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

            Open(); Click("difficulty.useSource"); Click("difficulty.templateTab"); Click("template.manage"); Click("template.fromCurrent"); ui.Key(13);
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
            Open(); Click("difficulty.useSource"); Click("difficulty.templateTab"); Confirm();
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
            var selector = ui.Canvas.Texts.Last(t => t.Value == "▾"); ui.Click(selector.X + 4, selector.Y + 5);
            ui.Key(36); ui.Key(40); ui.Key(13); Confirm();
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
    private static void SourceControls()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(locale);
            var ui = new Ui(false); ui.Resize(980, 620);
            var project = BeatmapProject.FromDocuments(Enumerable.Range(0, 10).Select(i =>
            {
                var map = new MapDocument { IsDemo = false }; map.Fruits.Add(new() { TimeMs = 1000, X = 64 + i }); return map;
            }));
            for (int i = 0; i < 10; i++) project.Difficulties[i].Name = "Source " + i;
            ui.View.LoadProject(project); ui.Paint();
            ui.View.LibrarySettings.MapTemplates = Enumerable.Range(0, 10).Select(i => new MapTemplate
                { Name = "Preset " + i, HP = "5", CircleSize = 3, ApproachRate = 4 + i * .1, OD = "8" }).ToList();
            var plus = ui.Canvas.Texts.Single(t => t.Value == "+" && t.Y < 84);
            ui.Click(plus.X + 4, plus.Y + 4); ui.ClickText(L.Get("project.add"));
            Check(ui.Canvas.Texts.Last(t => t.Value == L.Get("difficulty.copyTab")).Color == 0x5B6777,
                "Source tabs start disabled");
            ui.ClickText(L.Get("difficulty.templateTab"));
            Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("template.manage")), "Disabled tabs cannot change source mode");
            Dropdown();
            Check(!ui.Canvas.Texts.Any(t => t.Value == "Source 1" && t.Y > 260), "Disabled dropdown does not open");
            ui.ClickText(L.Get("difficulty.useSource")); Dropdown();
            Check(ui.Canvas.Texts.Any(t => t.Value == "Source 1" && t.Y > 260), "Difficulty dropdown exposes source choices");
            ui.View.Wheel(400, 320, -480, false); ui.Paint();
            var last = ui.Canvas.Texts.Last(t => t.Value == "Source 9"); ui.Click(last.X + 4, last.Y + 5);
            Check(ui.Canvas.Texts.Any(t => t.Value == "Source 9" && t.Y > 220 && t.Y < 300), "Scrolled dropdown selects an inactive difficulty");
            Dropdown(); ui.Key(27);
            Check(ui.View.DiscardConfirmationVisible, "Escape closes the dropdown before the dialog");
            ui.ClickText(L.Get("difficulty.templateTab")); Dropdown(); ui.Key(35); ui.Key(13);
            Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith("HP 5 · CS 3 · AR 4.9 · OD 8")), "Template preview follows HP/CS/AR/OD order");
            ui.ClickText(L.Get("difficulty.useSource"));
            ui.ClickText(L.Get("template.manage")); Check(!ui.View.MapTemplatesVisible, "Disabled source region blocks template management");
            ui.ClickText(L.Get("difficulty.useSource"));
            var confirm = ui.Canvas.Texts.Last(t => t.Value == L.Get("project.add")); ui.Click(confirm.X + 4, confirm.Y + 5);
            Check(ui.View.Document.ApproachRate == 4.9 && ui.View.Document.CircleSize == 3, "Template dropdown creates from the chosen preset");
            ui.View.OpenMapTemplates(); ui.Paint();
            var hp = ui.Canvas.Texts.Single(t => t.Value == "HP"); var cs = ui.Canvas.Texts.Single(t => t.Value == "CS");
            var ar = ui.Canvas.Texts.Single(t => t.Value == "AR"); var od = ui.Canvas.Texts.Single(t => t.Value == "OD");
            Check(hp.Y == cs.Y && ar.Y == od.Y && hp.Y < ar.Y && hp.X < cs.X && ar.X < od.X, "Template fields follow HP/CS/AR/OD layout");
            ui.Key(9); ui.Key('A', ctrl: true); ui.Type("9"); ui.Key(13);
            Check(ui.View.LibrarySettings.MapTemplates[0].HP == "9" && ui.View.LibrarySettings.MapTemplates[0].ApproachRate == 4,
                "Tab navigation edits HP first without swapping stored values");
            void Dropdown()
            {
                var arrow = ui.Canvas.Texts.Last(t => t.Value == "▾"); ui.Click(arrow.X + 3, arrow.Y + 4);
            }
        }
    }
    private static void TemplateReordering()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (var size in new[] { (980, 620), (1440, 900) })
        {
            L.SetLanguage(locale);
            var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
            var original = ui.View.Document.DeepClone();
            ui.View.LibrarySettings.MapTemplates = new[] { "Order A", "Order B", "Order C" }
                .Select((name, i) => new MapTemplate { Name = name, ApproachRate = i + 3, Icon = (MapTemplateIcon)(i + 1) }).ToList();
            ui.View.OpenMapTemplates(); ui.Paint();
            Drag("Order A", "Order C", false);
            Check(Rows() == "Order B,Order C,Order A" && !ui.View.WantsCapture, "Downward template drag reorders the draft and releases capture");
            Check(Saved() == "Order A,Order B,Order C", "Dragging does not save settings before Save");
            ui.Key(13);
            Check(Saved() == "Order B,Order C,Order A" && ui.View.LibrarySettings.MapTemplates[2].ApproachRate == 3
                && ui.View.LibrarySettings.MapTemplates[2].Icon == MapTemplateIcon.Blue, "Save preserves reordered template values and icon");
            string config = Path.GetFullPath($"artifacts/tests/template-order/{locale}-{size.Item1}/settings.json");
            ui.View.LibrarySettings.Save(config);
            Check(string.Join(',', LibrarySettings.Load(config).MapTemplates.Select(t => t.Name)) == Saved(), "Template order survives restart");
            ui.View.OpenMapTemplates(); ui.Paint(); Drag("Order A", "Order B", true); ui.Key(27);
            Check(Saved() == "Order B,Order C,Order A", "Cancel discards reordered draft");
            ui.View.OpenMapTemplates(); ui.Paint();
            var from = Row("Order B"); var to = Row("Order A");
            ui.View.PointerDown(from.X + 4, from.Y + 5, 0, false, false);
            ui.View.PointerMove(to.X + 4, to.Y + 20, false, false); ui.Paint(); ui.Key(27);
            Check(!ui.View.WantsCapture && ui.View.MapTemplatesVisible && Rows() == Saved(), "Escape cancels only the active drag");
            ui.View.PointerUp(to.X + 4, to.Y + 20, 0); ui.Paint();
            ui.View.PointerDown(from.X + 4, from.Y + 5, 0, false, false);
            ui.View.PointerMove(to.X + 4, to.Y + 20, false, false); ui.View.CancelInteraction(); ui.Paint();
            Check(!ui.View.WantsCapture && Rows() == Saved(), "Lost capture leaves template order unchanged");
            ui.View.PointerDown(from.X + 4, from.Y + 5, 0, false, false);
            ui.View.PointerMove(from.X + 300, to.Y + 20, false, false); ui.View.PointerUp(from.X + 300, to.Y + 20, 0); ui.Paint();
            Check(Rows() == Saved(), "Dropping outside the template list cancels the drag");
            Drag("Order A", "Order B", true); ui.Key(13);
            Check(Saved() == "Order A,Order B,Order C" && original.ContentEquals(ui.View.Document), "Upward drag saves without editing beatmap content");

            ui.View.LibrarySettings.MapTemplates = Enumerable.Range(0, 10).Select(i => new MapTemplate { Name = "Order " + i, ApproachRate = i }).ToList();
            ui.View.OpenMapTemplates(); ui.Paint(); from = Row("Order 0");
            ui.View.PointerDown(from.X + 4, from.Y + 5, 0, false, false);
            ui.View.PointerMove(from.X + 4, from.Y + 12, false, false);
            ui.View.Wheel(from.X + 4, from.Y + 12, -120, false); ui.Paint();
            ui.View.PointerMove(from.X + 4, from.Y + 100, false, false); ui.View.PointerUp(from.X + 4, from.Y + 100, 0); ui.Paint(); ui.Key(13);
            Check(ui.View.LibrarySettings.MapTemplates[0].Name == "Order 1" && ui.View.LibrarySettings.MapTemplates[^1].Name == "Order 0", "Dragging with wheel paging moves templates between pages");
            var plus = ui.Canvas.Texts.Single(t => t.Value == "+" && t.Y < 84); ui.Click(plus.X + 4, plus.Y + 4);
            ui.ClickText(L.Get("project.add")); ui.ClickText(L.Get("difficulty.useSource")); ui.ClickText(L.Get("difficulty.templateTab"));
            var arrow = ui.Canvas.Texts.Last(t => t.Value == "▾"); ui.Click(arrow.X + 3, arrow.Y + 4); ui.Key(36); ui.Key(13);
            var confirm = ui.Canvas.Texts.Last(t => t.Value == L.Get("project.add")); ui.Click(confirm.X + 3, confirm.Y + 4);
            Check(ui.View.Document.ApproachRate == 1, "Creation dropdown follows the saved template order");

            RecordingCanvas.Label Row(string name) => ui.Canvas.Texts.First(t => t.Value == name);
            string Saved() => string.Join(',', ui.View.LibrarySettings.MapTemplates.Select(t => t.Name));
            string Rows() => string.Join(',', ui.Canvas.Texts.Where(t => t.Value.StartsWith("Order ") && t.X < (size.Item1 - 860) / 2 + 230).OrderBy(t => t.Y).Select(t => t.Value));
            void Drag(string source, string destination, bool before)
            {
                var a = Row(source); var b = Row(destination);
                ui.View.PointerDown(a.X + 4, a.Y + 5, 0, false, false);
                Check(ui.View.WantsCapture, "Template row captures the pointer");
                ui.View.PointerMove(b.X + 4, b.Y + (before ? -8 : 20), false, false); ui.Paint();
                ui.View.PointerUp(b.X + 4, b.Y + (before ? -8 : 20), 0); ui.Paint();
            }
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
