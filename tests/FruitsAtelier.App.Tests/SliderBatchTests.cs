using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderBatchTests
{
    public static void Run()
    {
        string language = L.Language;
        try { RunImports(); }
        finally { L.SetLanguage(language); }
    }

    private static void RunImports()
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
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (var size in new[] { (980, 620), (1440, 900) })
        foreach (bool global in new[] { false, true })
        foreach (bool source in new[] { false, true })
        {
            L.SetLanguage(locale);
            Check(!L.Get("preview.convertSlider").Contains("Ctrl"), "Conversion action advertises the duplicate shortcut.");
            var authored = new MapDocument { DurationMs = 10000, DerandomizeFSliderDroplets = true };
            var track = new CurveTrack { Kind = CurveKind.Linear, DropletMode = SliderDropletMode.Derandomize };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 128 }, new() { TimeMs = 2000, X = 384 }]);
            authored.Tracks.Add(track);
            var imported = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(authored).Text);
            var playback = OsuBeatmapWriter.Serialize(imported).PlayableObjects;
            view.LibrarySettings.DerandomizeDroplets = global;
            view.LibrarySettings.DerandomizeNewProjects = !global;
            view.NewProject(); Check(view.AddDifficulty(imported), "Difficulty import failed."); Wait();
            Check(view.SliderImportSourcePromptVisible && !view.PrepareFileOperation(), "Suspected compensation must ask before edits or saving.");
            canvas.Clear(); view.Render(canvas, size.Item1, size.Item2);
            Check(canvas.Texts.Any(t => t.Value == L.Get("sliderImport.question")), "Source question missing.");
            var beforeAnswer = view.Document.DeepClone();
            view.KeyDown(68, true, false);
            Check(view.Document.ContentEquals(beforeAnswer), "Source question permitted cloning.");
            // Exercise the actual localized Yes/No hit target at both window sizes.
            var answer = canvas.Texts.Single(t => t.Value == L.Get(source ? "sliderImport.yes" : "sliderImport.no"));
            view.PointerDown(answer.X + 1, answer.Y + 1, 0, false, false); Wait();
            Check(!view.SliderImportSourcePromptVisible && view.SliderImportPromptVisible, "Import did not reach representation choice.");
            Check(view.Document.DerandomizeFSliderDroplets == (source || global), "Import mode disagrees with source answer/global preference.");
            Check(view.Document.SliderImportState == (source ? SliderImportState.Compensated : SliderImportState.Native), "Source choice was not retained.");
            var prepared = view.Document.DeepClone();
            if (source) Same(playback, OsuBeatmapWriter.Serialize(prepared).PlayableObjects);
            view.AnswerSliderImport(false); Wait();
            view.KeyDown(65, true, false); view.KeyDown(68, true, false); Wait();
            Check(view.Document.ImportedSliders.Count == 2 && view.Document.Tracks.Count == 0, "Ctrl+D must preserve Legacy representation.");
            Check(view.Document.ImportedSliders.All(s => s.RandomizationTrack is not null || !view.Document.DerandomizeFSliderDroplets), "Clone lost processing baseline.");
            view.KeyDown(90, true, false); Wait();
            Check(view.Document.ContentEquals(prepared), "Clone undo lost source state.");
            view.KeyDown(89, true, false); Wait();
            Check(view.Document.ImportedSliders.Count == 2, "Clone redo failed.");
            view.KeyDown(90, true, false); Wait();
            view.ConvertAllSliders(); Click(L.Get("sliderBatch.convert")); Wait();
            Check(view.Document.Tracks.Count == 1 && view.Document.ImportedSliders.Count == 0, "Batch conversion failed.");
            Check(view.Document.Tracks[0].RandomizesDroplets(view.Document) == !(source || global), "Conversion used a second droplet policy.");
            Same(OsuBeatmapWriter.Serialize(prepared).PlayableObjects, OsuBeatmapWriter.Serialize(view.Document).PlayableObjects);
            view.KeyDown(90, true, false); Check(view.Document.ContentEquals(prepared), "Conversion undo lost imported mode/baseline.");
            var saved = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(view.CaptureProject()));
            view.LoadProject(saved); Render();
            Check(!view.SliderImportSourcePromptVisible && !view.SliderImportPromptVisible && !view.SliderConversionBusy, "Saved project repeated import setup.");
        }
        L.SetLanguage("en");
        view.LoadProject(BeatmapProject.FromDocuments([Fixture(), Fixture()]));
        var oldProject = view.CaptureProject();
        var oldOutput = oldProject.Difficulties.Select(d => OsuBeatmapWriter.Serialize(d.Document).Text).ToArray();
        foreach (bool global in new[] { true, false })
        {
            view.LibrarySettings.DerandomizeDroplets = global;
            view.LoadProject(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(oldProject))); Render();
            Check(!view.SliderImportSourcePromptVisible && !view.SliderImportPromptVisible && !view.SliderConversionBusy && !view.IsDirty,
                "Opening an existing project triggered import processing.");
            Check(view.CaptureProject().Difficulties.Select(d => OsuBeatmapWriter.Serialize(d.Document).Text).SequenceEqual(oldOutput),
                "Global preference changed existing project output.");
        }
        view.OfferSliderConversion(true); Render();
        Check(view.SliderImportPromptVisible && !view.PrepareFileOperation(), "Conversion prompt must block editing.");
        Click(L.Get("sliderBatch.keep")); Check(!view.IsDirty, "Declining conversion changed existing content.");
        view.OfferSliderConversion(true); view.AnswerSliderImport(true); Wait();
        Check(view.CaptureProject().Difficulties.All(d => d.Document.ImportedSliders.Count == 0), "Batch did not include all difficulties.");
        view.LoadDocument(Fixture()); view.ConvertAllSliders(); view.AnswerSliderImport(true); view.CancelSliderConversion(); Wait();
        Check(view.Document.ImportedSliders.Count == 1 && !view.IsDirty, "Cancelled batch changed the map.");
        view.ConvertAllSliders(); view.AnswerSliderImport(true); view.NewProject(); Render(); Thread.Sleep(20); Render();
        Check(view.Document.Tracks.Count == 0 && !view.IsDirty, "Retired worker changed replacement project.");

        string root = Path.Combine(Path.GetTempPath(), "atelier-slider-import-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "External"));
            string source = Path.Combine(root, "External", "source.osu");
            File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Test\nArtist:Test\nCreator:Test\nVersion:Rain\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,100,1000,2,0,L|200:100,1,100\n");
            var authored = new MapDocument { DerandomizeFSliderDroplets = true };
            var track = new CurveTrack { Kind = CurveKind.Linear, DropletMode = SliderDropletMode.Derandomize };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 128 }, new() { TimeMs = 2000, X = 384 }]);
            authored.Tracks.Add(track);
            var pending = Enumerable.Range(0, 3).Select(i =>
            {
                var doc = i == 1 ? Fixture() : OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(authored).Text);
                doc.SliderImportState = SliderImportState.Pending;
                SongSetup.Set(doc, "Metadata", "Version", "Import " + i);
                return doc;
            }).ToArray();
            var wizard = WorkspaceProject.Create(Path.Combine(root, "wizard"), BeatmapProject.FromDocuments(pending), "");
            view.LoadWorkspace(wizard); Wait();
            Check(canvas.Texts.Any(t => t.Value == L.Get("sliderImport.page", 1, 3))
                && canvas.Texts.Any(t => t.Value == "Import 0"), "First import page lacks its difficulty and stable page count.");
            view.AnswerSliderImportSource(true); Wait();
            Check(canvas.Texts.Any(t => t.Value == L.Get("sliderImport.page", 2, 3))
                && canvas.Texts.Any(t => t.Value == "Import 2"), "Second source page did not skip the ordinary difficulty.");
            view.AnswerSliderImportSource(false); Wait();
            Check(canvas.Texts.Any(t => t.Value == L.Get("sliderImport.page", 3, 3))
                && view.SliderImportPromptVisible, "Import wizard did not finish at one combined conversion page.");
            Check(!canvas.Texts.Any(t => t.Value == L.Get("sliderBatch.help") || t.Value == L.Get("sliderBatch.later")),
                "Conversion page repeats extended help.");
            var decisions = view.CaptureProject().Difficulties.Select(d => d.Document.SliderImportState).ToArray();
            Check(decisions.SequenceEqual(new[] { SliderImportState.Compensated, SliderImportState.Native, SliderImportState.Native }),
                "Source answers were applied to the wrong difficulty.");
            view.AnswerSliderImport(true); Wait();
            Check(view.CaptureProject().Difficulties.All(d => d.Document.ImportedSliders.Count == 0),
                "Final wizard conversion omitted a difficulty.");
            foreach (bool mode in new[] { true, false })
            {
                var settings = new LibrarySettings { Workspace = Path.Combine(root, "workspace-" + mode),
                    DerandomizeDroplets = mode, DerandomizeNewProjects = !mode };
                var imported = LibraryOperations.ImportPath(source, settings);
                Check(imported.IsNewImport && imported.Project.Difficulties.All(d => d.Document.DerandomizeFSliderDroplets == mode), "File import ignored global import mode.");
                view.LoadWorkspace(imported); Wait();
                Check(!view.SliderImportSourcePromptVisible && view.SliderImportPromptVisible, "Ordinary import asked about compensation.");
                view.AnswerSliderImport(false);
                var completed = view.CaptureProject();
                var old = WorkspaceProject.Create(Path.Combine(root, "existing-" + mode), oldProject, "");
                var reopened = WorkspaceProject.Open(old.Directory);
                view.LoadWorkspace(reopened); Render();
                Check(!view.IsDirty && !view.SliderImportPromptVisible && !view.SliderImportSourcePromptVisible,
                    "Existing workspace was reinitialized.");
                Check(view.CaptureProject().Difficulties.Select(d => OsuBeatmapWriter.Serialize(d.Document).Text).SequenceEqual(oldOutput),
                    "Existing workspace export changed.");
                var ready = WorkspaceProject.Create(Path.Combine(root, "ready-" + mode), completed, "");
                view.LoadWorkspace(WorkspaceProject.Open(ready.Directory)); Render();
                Check(!view.SliderImportPromptVisible && !view.SliderImportSourcePromptVisible && !view.SliderConversionBusy,
                    "Resolved import prompted again after workspace reload.");
                var card = LibraryDatabase.ReadMetadata(source)!;
                var cardSettings = new LibrarySettings { Workspace = Path.Combine(root, "card-" + mode), DerandomizeDroplets = mode };
                view.LoadWorkspace(LibraryOperations.Open(card, cardSettings)); Wait();
                Check(view.Document.DerandomizeFSliderDroplets == mode, "Library card import mode mismatch.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Same(IReadOnlyList<ConvertedCatchObject> a, IReadOnlyList<ConvertedCatchObject> b)
        => Check(a.Count == b.Count && a.Zip(b).All(p => p.First.Kind == p.Second.Kind
            && Math.Abs(p.First.TimeMs - p.Second.TimeMs) < 1e-6 && Math.Abs(p.First.X - p.Second.X) <= 1), "Conversion changed playback.");

    private static MapDocument Fixture()
    {
        var doc = new MapDocument { DurationMs = 10000, IsDemo = false };
        var slider = new ImportedSlider { X = 100, Y = 100, TimeMs = 1000, PathType = 'L', PixelLength = 100 };
        slider.ControlPoints.AddRange([new(100, 100), new(200, 100)]); doc.ImportedSliders.Add(slider); return doc;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
