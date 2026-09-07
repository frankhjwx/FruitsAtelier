using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderBatchTests
{
    public static void Run()
    {
        var view = new EditorView(); var canvas = new RecordingCanvas();
        void Render() { canvas.Clear(); view.Render(canvas, 1440, 900); }
        void Wait()
        {
            var timeout = DateTime.UtcNow.AddSeconds(30);
            while (view.SliderConversionBusy && DateTime.UtcNow < timeout) { Render(); Thread.Sleep(5); }
            Check(!view.SliderConversionBusy, "Batch conversion did not complete."); Render();
        }
        void Click(string text)
        {
            Render(); var label = canvas.Texts.Single(t => t.Value == text); view.PointerDown(label.X + 1, label.Y + 1, 0, false, false); Render();
        }
        view.LoadProject(BeatmapProject.FromDocuments([Fixture(), Fixture()]));
        view.OfferSliderConversion(true); Render();
        Check(view.SliderImportPromptVisible && !view.PrepareFileOperation(), "First-import prompt must block content operations.");
        var before = view.Document.DeepClone(); view.KeyDown(70, false, false); view.PointerDown(600, 450, 0, false, false);
        Check(view.Document.ContentEquals(before), "Prompt allowed background editing.");
        Click(L.Get("sliderBatch.keep")); Check(!view.SliderImportPromptVisible && !view.IsDirty, "Declining changed content.");
        view.PointerDown(180, 18, 0, false, false); Render();
        Check(canvas.Texts.Any(t => t.Value == L.Get("sliderBatch.menu")), "Edit menu is missing batch conversion.");
        Click(L.Get("sliderBatch.menu")); Wait();
        Check(view.Document.Tracks.Count == 1 && view.Document.ImportedSliders.Count == 0 && view.IsDirty, "Menu failed to convert current difficulty.");
        view.SwitchDifficulty(1); Check(view.Document.ImportedSliders.Count == 1, "Menu modified another difficulty.");
        view.SwitchDifficulty(0); view.KeyDown(90, true, false); Check(!view.IsDirty && view.Document.ImportedSliders.Count == 1, "Batch conversion is not one undo step.");
        view.OfferSliderConversion(true); view.AnswerSliderImport(true); Wait();
        Check(view.CaptureProject().Difficulties.All(d => d.Document.ImportedSliders.Count == 0), "First-import conversion did not include every difficulty.");
        view.LoadDocument(Fixture()); view.ConvertAllSliders(); view.CancelSliderConversion(); Wait();
        Check(view.Document.ImportedSliders.Count == 1 && !view.IsDirty, "Cancelled batch applied changes.");
        view.ConvertAllSliders(); view.NewProject(); Render(); Thread.Sleep(20); Render();
        Check(view.Document.Tracks.Count == 0 && !view.IsDirty, "Old batch changed a replacement project.");

        view.NewProject(); Check(view.AddDifficulty(Fixture()) && view.SliderImportPromptVisible, "Importing a difficulty did not offer conversion.");
        view.AnswerSliderImport(true); Wait();
        Check(view.CaptureProject().Difficulties[0].Document.Tracks.Count == 0 && view.Document.Tracks.Count == 1, "Difficulty import used the wrong scope.");
        var mixed = Fixture(); mixed.SliderMultiplier = 5; mixed.SliderTickRate = 2;
        var repeated = new ImportedSlider { TimeMs = 3000, X = 100, Y = 100, PathType = 'L', PixelLength = 300, SpanCount = 3 };
        repeated.ControlPoints.AddRange([new(100, 100), new(300, 100)]); mixed.ImportedSliders.Add(repeated);
        view.LoadDocument(mixed); view.ConvertAllSliders(); Wait();
        Check(view.Document.Tracks.Count == 1 && view.Document.ImportedSliders.Single().Id == repeated.Id, "Partial failure did not preserve the failed slider.");
        Check(canvas.Texts.Any(t => t.Value == L.Get("sliderBatch.preserved", 1)), "Partial failure was not explained.");
        Click(L.Get("sliderBatch.close")); view.KeyDown(90, true, false);
        Check(view.Document.ImportedSliders.Count == 2 && !view.IsDirty, "Partial batch cannot be undone in one step.");

        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-slider-import-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace") };
            Directory.CreateDirectory(Path.Combine(root, "External"));
            string source = Path.Combine(root, "External", "source.osu");
            File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Test\nArtist:Test\nCreator:Test\nVersion:Rain\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,100,1000,2,0,L|200:100,1,100\n");
            var staleCard = LibraryDatabase.ReadMetadata(source)!;
            var imported = LibraryOperations.ImportPath(source, settings);
            Check(imported.IsNewImport, "New source import was not identified.");
            view.LoadWorkspace(imported); Check(view.SliderImportPromptVisible, "Workspace import did not offer conversion.");
            view.AnswerSliderImport(false);
            var reopened = LibraryOperations.ImportPath(source, settings);
            Check(!reopened.IsNewImport, "Re-import treated an existing project as new.");
            var staleOpened = LibraryOperations.Open(staleCard, settings);
            Check(!staleOpened.IsNewImport && staleOpened.Directory == imported.Directory, "Stale library card repeated first import.");
            view.LoadWorkspace(reopened); Check(!view.SliderImportPromptVisible, "Prompt repeated after reopening.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static MapDocument Fixture()
    {
        var doc = new MapDocument { DurationMs = 10000, IsDemo = false };
        var slider = new ImportedSlider { X = 100, Y = 100, TimeMs = 1000, PathType = 'L', PixelLength = 100 };
        slider.ControlPoints.AddRange([new(100, 100), new(200, 100)]); doc.ImportedSliders.Add(slider); return doc;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
