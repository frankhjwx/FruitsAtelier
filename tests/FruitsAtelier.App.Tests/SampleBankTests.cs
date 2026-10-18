using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class SampleBankTests
{
    public static void Run()
    {
        var map = new MapDocument();
        var fruit = new Fruit { X = 200, TimeMs = 1000, OriginalLine = "200,192,1000,21,8,0:3:2:65:custom.wav" };
        map.Fruits.Add(fruit);
        map.TimingPoints.Add(new() { SampleSet = 2, Volume = 30 });
        ObjectFlags.SetSampleBank(map, fruit.Id, false, 1);
        Check(fruit.OriginalLine == "200,192,1000,21,8,1:3:2:65:custom.wav", "Preserve flags, additions, index, volume and filename");
        ObjectFlags.SetSampleBank(map, fruit.Id, true, 0);
        Check(ObjectFlags.SampleBanks(map, fruit.Id, true).Single() == 0, "Display authored Auto");
        var converted = CatchStreamConverter.Convert(map);
        Check(new HitsoundResolver(map, converted.Objects).Describe(converted.Objects.Single()).AdditionSet == 1, "Addition Auto follows explicit normal bank");
        var slider = new ImportedSlider { X = 100, Y = 192, PathType = 'L', TimeMs = 2000, SpanCount = 2, PixelLength = 140,
            OriginalLine = "100,192,2000,2,8,L|240:192,2,140,8|4|2,1:2|2:3|3:1,2:1:4:60:" };
        slider.ControlPoints.AddRange([new(100,192), new(240,192)]);
        map.ImportedSliders.Add(slider);
        ObjectFlags.SetSampleBank(map, slider.Id, true, 0, 1);
        Check(slider.OriginalLine!.Contains("1:2|2:0|3:1,2:1:4:60:"), "Only selected edge changes");
        map.HitsoundOverrides.Add(new(fruit.Id, 0, new(3, 2, 5, 42, 8), InheritedFields: InheritedSampleFields.None));
        ObjectFlags.SetSampleBank(map, fruit.Id, false, 0);
        var copied = map.HitsoundOverrides.Single();
        Check(copied.InheritedFields == InheritedSampleFields.NormalSet && copied.Sample.Volume == 42 && copied.Sample.AdditionSet == 2, "Copied sample inheritance edits preserve other fields");
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.ConvertSelectedSliderRepresentation(slider.Id); ui.Paint();
            ui.Key('1'); ui.Key('A', ctrl: true);
            var before = ui.View.Document.DeepClone();
            var bounds = ui.View.SamplesetBounds;
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("samples.auto") + " ▾"), "Mixed selection displays Auto");
            ui.Click(bounds.X + 20, bounds.Y + 12);
            ui.ClickText("✓ " + L.Get("samples.auto"));
            Check(ObjectFlags.SampleBanks(ui.View.Document, slider.Id, false).All(v => v == 0)
                && ObjectFlags.SampleBanks(ui.View.Document, fruit.Id, false).Single() == 0, "Choosing Auto resets mixed selection inheritance");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(before), "Undo Auto restores distinct original banks");
            ui.Key('A', ctrl: true);
            bounds = ui.View.SamplesetBounds;
            ui.Click(bounds.X + 20, bounds.Y + 12);
            ui.ClickText(L.Get("timing.drum"));
            Check(ObjectFlags.SampleBanks(ui.View.Document, fruit.Id, false).All(v => v == 3), "UI applies to selection");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(before), "Sample edit undo preserves document");
            ui.Key('Y', ctrl: true);
            Check(ObjectFlags.SampleBanks(ui.View.Document, slider.Id, false).All(v => v == 3), "Redo includes slider edges");
            var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
            Check(saved.ContentEquals(ui.View.Document), "Sample banks survive project persistence");
            var exported = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(saved).Text);
            Check(ObjectFlags.SampleBanks(exported, exported.ImportedSliders.Single().Id, false).All(v => v == 3), "Slider banks survive osu export");
            var unchanged = ui.View.Document.DeepClone();
            ui.Key('2'); bounds = ui.View.SamplesetBounds;
            ui.Click(bounds.X + 20, bounds.Y + 12); ui.ClickText(L.Get("timing.soft"));
            Check(ui.View.Document.ContentEquals(unchanged), "Placement defaults do not change existing objects");
            int count = ui.View.Document.Fruits.Count;
            ui.ClickMap(1500, 300);
            Check(ui.View.Document.Fruits.Count == count + 1 && ObjectFlags.SampleBanks(ui.View.Document, ui.View.Document.Fruits.Last().Id, false).Single() == 2,
                "New fruit uses placement bank");
            ui.Resize(1100, 720);
            Check(ui.View.AdditionsBounds.Bottom <= ui.View.ToolButtonBounds[0].Y, "Selectors remain above tools");
            ui.Key(27);
        }
        ShortcutTests(map);
        L.SetLanguage("zh-CN");
    }

    private static void ShortcutTests(MapDocument map)
    {
        foreach (bool lazer in new[] { false, true })
        foreach (bool osuCanvas in new[] { false, true })
        {
            var ui = new Ui();
            ui.LoadDocument(map.DeepClone());
            if (ui.View.SliderImportPromptVisible) ui.View.AnswerSliderImport(false);
            ui.View.LibrarySettings.LazerEditing = lazer;
            if (ui.View.IsOsuCanvas != osuCanvas) ui.View.ToggleOsuCanvas();
            if (!osuCanvas) ui.View.ConvertSelectedSliderRepresentation(map.ImportedSliders.Single().Id);
            ui.Paint();
            ui.Key('1'); ui.Key('A', ctrl: true);
            Check(ui.View.SelectedObjectIds.Count == 2, $"Shortcut fixture selects both parents: {ui.View.SelectedObjectIds.Count}, {ui.View.StatusMessage}");
            foreach (bool additions in new[] { false, true })
            foreach (var (key, bank) in new[] { ('W', 1), ('E', 2), ('R', 3), ('Q', 0) })
            {
                ui.Key('A', ctrl: true);
                var before = ui.View.Document.DeepClone();
                var expected = before.DeepClone();
                foreach (var id in expected.Fruits.Select(f => f.Id).Concat(expected.ImportedSliders.Select(s => s.Id)).Concat(expected.Tracks.Select(t => t.Id)))
                    ObjectFlags.SetSampleBank(expected, id, additions, bank);
                ui.Key(key, ctrl: additions, shift: !additions);
                Check(ui.View.Document.ContentEquals(expected), $"Sample shortcut changes only its bank across the selection and slider edges (lazer={lazer}, osu={osuCanvas}, additions={additions}, key={key}); {ui.View.StatusMessage}; {ui.View.ActiveTool}");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.ContentEquals(before), "Sample shortcut is one undo step");
                ui.Key('Y', ctrl: true);
                Check(ui.View.Document.ContentEquals(expected), "Sample shortcut redo restores sample banks");
            }
            var unchanged = ui.View.Document.DeepClone();
            foreach (char key in "QWER") ui.Key(key, ctrl: true, shift: true);
            Check(ui.View.Document.ContentEquals(unchanged), "Ctrl+Shift does not change sample banks or flags");

            ui.Key('2');
            ui.Key('E', shift: true);
            ui.Key('R', ctrl: true);
            Check(ui.View.Document.ContentEquals(unchanged), "Placement sample shortcuts leave existing content unchanged");
            if (osuCanvas) ui.View.ToggleOsuCanvas();
            ui.Paint();
            ui.ClickMap(1500, 300);
            var placed = ui.View.Document.Fruits.Last();
            Check(ObjectFlags.SampleBanks(ui.View.Document, placed.Id, false).Single() == 2
                && ObjectFlags.SampleBanks(ui.View.Document, placed.Id, true).Single() == 3,
                "New fruit uses both shortcut placement banks");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
