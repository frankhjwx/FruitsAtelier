using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class IndependentSliderSvTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                OpenBesideTimingBoundary();
                var ui = new Ui(false);
                ui.LoadDocument(OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n"));
                ui.View.ToggleOsuCanvas(); ui.View.UpdateTransport(900, 5000, true, false, false, null, null); ui.Paint();
                Click(100, 192); ui.View.ShowSliderInspector(); ui.Paint();
                var before = ui.View.Document.DeepClone();
                var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("legacySv.label"));
                var viewport = ui.View.SliderInspectorViewport;
                while (label.Y + 32 > viewport.Bottom)
                {
                    ui.View.Wheel(viewport.X + 10, viewport.Y + 10, -120, false, false); ui.Paint();
                    label = ui.Canvas.Texts.Single(t => t.Value == L.Get("legacySv.label"));
                }
                ui.Click(label.X + 125, label.Y + 5); Check(ui.View.IsEditingText, "Inspector SV input did not focus."); ui.Key('A', ctrl: true); ui.Type("2"); ui.Key(13); Wait();
                Check(ui.View.Document.ImportedSliders.Single().SliderVelocityMultiplier == 2, "Inspector did not edit independent SV: " + ui.View.StatusMessage + "; value=" + ui.View.Document.ImportedSliders.Single().SliderVelocityMultiplier);
                Check(ui.View.Document.TimingPoints.Count == before.TimingPoints.Count, "Inspector SV added a green point.");
                const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(FruitsAtelier.App.Editor.EditorView).GetMethod("EnsureOsuDistanceReferences", hidden)!.Invoke(ui.View, null);
                var references = ((ValueTuple<double, double, double, double, double>[])typeof(FruitsAtelier.App.Editor.EditorView)
                    .GetField("osuDistanceReferences", hidden)!.GetValue(ui.View)!);
                Check(references.Single().Item5 == 2, "Distance Snap retained the green-line SV instead of the slider's own SV.");
                ui.Key('Z', ctrl: true); ui.Paint();
                Check(before.ContentEquals(ui.View.Document), "Inspector SV did not undo.");
                ui.Key('Y', ctrl: true); Wait();
                Check(ui.View.Document.ImportedSliders.Single().SliderVelocityMultiplier == 2, "Inspector SV did not redo.");
                ui.View.ShowSliderInspector(); ui.Paint();
                ui.View.LoadDocument(before); ui.View.UpdateTransport(900, 5000, true, false, false, null, null); ui.Paint();
                Click(100, 192);
                var from = ui.View.OsuCanvasScreen(300, 192); var to = ui.View.OsuCanvasScreen(400, 192);
                ui.View.PointerDown(from.X, from.Y, 0, false, false);
                ui.View.PointerMove(to.X, to.Y, true, false);
                ui.View.PointerUp(to.X, to.Y, 0, true); Wait();
                var slider = ui.View.Document.ImportedSliders.Single();
                Check(Math.Abs(ImportedSliderConverter.DurationMs(ui.View.Document, slider)
                    - ImportedSliderConverter.DurationMs(before, before.ImportedSliders.Single())) < .01,
                    "Shift control dragging changed duration.");
                Check(slider.SliderVelocityMultiplier > 1 && ui.View.Document.TimingPoints.Count == before.TimingPoints.Count,
                    "Shift dragging did not change only the object's independent SV.");
                ui.Key('Z', ctrl: true); ui.Paint(); Check(before.ContentEquals(ui.View.Document), "Shift gesture did not undo atomically.");
                Click(100, 192); from = ui.View.OsuCanvasScreen(300, 192); to = ui.View.OsuCanvasScreen(400, 192);
                ui.View.PointerDown(from.X, from.Y, 0, false, false); ui.View.PointerMove(to.X, to.Y, false, false);
                ui.View.PointerUp(to.X, to.Y, 0); Wait();
                Check(ui.View.Document.ImportedSliders.Single().SliderVelocityMultiplier == 1
                    && ui.View.Document.TimingPoints.Count == before.TimingPoints.Count, "Ordinary dragging changed SV or added green timing.");
                var processed = before.DeepClone(); processed.DerandomizeFSliderDroplets = true;
                Check(SliderRepresentationEditing.RefreshLegacyDroplets(processed).Success, "Processed Inspector fixture failed.");
                ui.LoadDocument(processed); ui.View.UpdateTransport(900, 5000, true, false, false, null, null); ui.Paint();
                Click(100, 192); ui.View.ShowSliderInspector(); ui.Paint();
                for (int i = 0; i < 20 && ui.View.LegacySliderSvBounds.Width == 0; i++)
                { viewport = ui.View.SliderInspectorViewport; ui.View.Wheel(viewport.X + 10, viewport.Y + 10, -120, false, false); ui.Paint(); }
                var box = ui.View.LegacySliderSvBounds;
                Check(box.Width > 0 && ui.Canvas.Texts.Any(t => t.Value == "1" && t.X >= box.X && t.X < box.Right && t.Y >= box.Y && t.Y < box.Bottom)
                    && ui.View.Document.ImportedSliders.Single().ProcessedSliderVelocityMultiplier > 1,
                    "Inspector showed the processed SV in the authored input.");
                ui.View.SetOsuSliderDisplay(true); ui.LoadDocument(processed);
                ui.View.UpdateTransport(900, 5000, true, false, false, null, null); ui.Paint();
                from = ui.View.OsuCanvasScreen(100, 192); to = ui.View.OsuCanvasScreen(116, 208);
                ui.View.PointerDown(from.X, from.Y, 0, false, false); ui.View.PointerMove(to.X, to.Y, false, false);
                ui.View.PointerUp(to.X, to.Y, 0); Wait();
                slider = ui.View.Document.ImportedSliders.Single();
                Check(slider.SliderVelocityMultiplier == 1 && Math.Abs(ImportedSliderConverter.DurationMs(ui.View.Document, slider)
                    - ImportedSliderConverter.DurationMs(processed, processed.ImportedSliders.Single())) < .01
                    && ui.View.Document.TimingPoints.Count == processed.TimingPoints.Count,
                    "Moving a processed Legacy slider changed authored SV, duration or green timing.");
                ui.LoadDocument(before); ui.Paint(); if (ui.View.IsOsuCanvas) ui.View.ToggleOsuCanvas(); ui.Paint();
                if (ui.View.DistanceSnapEnabled) ui.Key('Y');
                ui.Key('B'); ui.ClickMap(1000, 0); ui.ClickMap(1125, 500, ctrl: true); ui.Key(13); Wait();
                Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.ImportedSliders.Single().SliderVelocityMultiplier == 1,
                    "An impossible simultaneous FSlider was committed or changed the Legacy SV.");

                void Click(double x, double y) { var p = ui.View.OsuCanvasScreen(x, y); ui.Click(p.X, p.Y); }
                void Wait()
                {
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    do { ui.Paint(); Thread.Sleep(2); } while (ui.View.LegacyDropletRefreshBusy && DateTime.UtcNow < deadline);
                    Check(!ui.View.LegacyDropletRefreshBusy, "SV validation did not finish.");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void OpenBesideTimingBoundary()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/slider-head-sync", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Timing\nArtist:Fixture\nCreator:Mapper\nVersion:Catch\n[TimingPoints]\n0,500,4,1,0,80,1,0\n234071,400,4,2,3,60,1,1\n[HitObjects]\n100,192,234070,2,0,L|300:192,2,200\n");
        var project = BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]);
        var session = WorkspaceProject.Create(Path.Combine(root, "Workspace"), project, songs);
        var document = session.Project.Difficulties[0].Document;
        session.Manifest.Difficulties[0].Sync = WorkspaceSynchronization.Capture(source, document, session.Directory);
        session.Manifest.Difficulties[0].ExportConfirmed = true;
        document.ImportedSliders.Single().SliderVelocityMultiplier = 10;
        WorkspaceProject.Save(session, session.Project);
        var ui = new Ui(false);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        try
        {
            ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory), checkAdditionalDifficulties: true);
            SynchronizationUiTests.Wait(ui);
            Check(!ui.View.SynchronizationVisible && !ui.View.LibraryVisible, "Opening a slider before a BPM boundary left a sync error or returned to Library.");
            var exported = OsuBeatmapReader.ReadFile(source);
            Check(exported.ImportedSliders.Single().TimeMs == 234070
                && exported.ImportedSliders.Single().SliderVelocityMultiplier == 10,
                "Opening did not synchronize the independent Legacy SV at the unchanged head.");
            Check(ui.View.Document.ImportedSliders.Single().TimeMs == 234070,
                "Synchronization moved the authored slider head.");
        }
        finally { ui.View.StopFileMonitoring(); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
