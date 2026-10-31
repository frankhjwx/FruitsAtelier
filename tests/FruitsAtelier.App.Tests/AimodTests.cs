using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using System.Reflection;
using L = FruitsAtelier.Localization.Strings;

internal static class AimodTests
{
    public static void StaleNavigation()
    {
        string previous = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            foreach (bool osu in new[] { false, true })
            for (int kind = 0; kind < 4; kind++)
            {
                L.SetLanguage(language);
                var map = new MapDocument { DurationMs = 10000 };
                Guid target = Guid.NewGuid();
                if (kind == 0) map.Fruits.Add(new() { Id = target, TimeMs = 1003, X = 100 });
                if (kind == 1)
                {
                    var track = new CurveTrack { Id = target };
                    track.Nodes.AddRange([new() { TimeMs = 1003, X = 100 }, new() { TimeMs = 1503, X = 400 }]);
                    map.Tracks.Add(track);
                }
                if (kind == 2)
                {
                    var slider = new ImportedSlider { Id = target, TimeMs = 1003, X = 100, Y = 192, PathType = 'L', PixelLength = 300 };
                    slider.ControlPoints.AddRange([new(100, 192), new(400, 192)]); map.ImportedSliders.Add(slider);
                }
                if (kind == 3) map.BananaShowers.Add(new() { Id = target, TimeMs = 1003, EndTimeMs = 1503 });
                var remaining = new Fruit { TimeMs = 1008, X = 400 }; map.Fruits.Add(remaining);
                var ui = new Ui(); ui.LoadDocument(map);
                if (osu) { ui.View.ToggleOsuCanvas(); ui.Paint(); }
                ui.View.ShowAimod(); ui.Paint();
                var issue = ui.View.AimodIssues.First(i => i.Code == "overlap");
                int rowIndex = ui.View.AimodIssues.ToList().IndexOf(issue);
                Navigate(false, [target, remaining.Id]);
                typeof(EditorView).GetMethod("SelectObjects", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui.View, [new[] { target }, target]);
                ui.Key(46);
                Navigate(true, [remaining.Id]);
                ui.Key('Z', ctrl: true); Navigate(true, [target, remaining.Id]);
                ui.Key(46);
                Navigate(false, []); Navigate(true, []);
                ui.Key('Z', ctrl: true);
                Check(map.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Retained report navigation preserves deletion undo.");
                foreach (var finding in ui.View.AimodIssues.Where(i => i.ObjectIds is not { Length: > 0 }).ToArray())
                {
                    double time = ui.View.PlayheadMs;
                    typeof(EditorView).GetMethod("LocateAimodIssue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui.View, [finding]);
                    ui.Paint();
                    Check(ui.View.PlayheadMs == (finding.TimeMs ?? time) && map.ContentEquals(ui.View.Document),
                        "Timing, metadata and resource findings navigate without changing content.");
                }
                if (kind == 0 && !osu)
                {
                    ui.Key('B'); ui.ClickMap(3000, 200);
                    Navigate(false, [target, remaining.Id]);
                    ui.View.CancelInteraction(); ui.Paint();
                    Check(map.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Navigation during a slider draft preserves cancellation.");
                }
                ui.View.NewProject();

                void Navigate(bool keyboard, Guid[] selected)
                {
                    var before = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
                    var row = ui.View.AimodRowBounds(rowIndex); var header = ui.View.AimodHeaderBounds(0);
                    float x = keyboard ? row.Right - 10 : header.X + 12, y = row.Y + 10;
                    ui.View.PointerDown(x, y, 0, false, false); ui.View.PointerUp(x, y, 0);
                    if (keyboard) ui.Key(13); else ui.Paint();
                    Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals(selected) && ui.View.PlayheadMs == issue.TimeMs,
                        $"AiMod navigation selected missing targets: kind={kind}, osu={osu}, keyboard={keyboard}.");
                    Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty && !ui.View.ErrorVisible,
                        "AiMod navigation changes only selection and viewport.");
                }
            }
        }
        finally { L.SetLanguage(previous); }
        Console.WriteLine("PASS AiMod retained reports across object types, canvases, languages, mouse, Enter and undo");
    }
    public static void Run()
    {
        StaleNavigation();
        string previous = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(language);
                AudioDuration();
                DisabledMediaDiagnostics();
                var map = new MapDocument { DurationMs = 10000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1005, X = 400 }]);
                var ui = new Ui(); ui.LoadDocument(map);
                bool dirty = ui.View.IsDirty;
                ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("aimod.title"));
                Check(ui.View.AimodVisible && ui.View.AimodErrors.Count == 1, "Menu must open an initial check.");
                ui.Key(46); ui.Key('Z', ctrl: true); ui.Key('2'); ui.Key(116);
                Check(map.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty && !ui.View.IsTestplaying,
                    "Focused report keys preserve content and dirty state.");
                ui.Resize(800, 600);
                ui.ClickText(L.Get("aimod.refresh"));
                Check(ui.View.AimodIssues.Any(i => i.Message == L.Get("aimod.overlap")), "Localized error remains available in a narrow window.");
                var overlapRow = ui.View.AimodRowBounds(0);
                ui.View.PointerDown(overlapRow.Right - 10, overlapRow.Y + 10, 0, false, false);
                ui.View.PointerUp(overlapRow.Right - 10, overlapRow.Y + 10, 0); ui.Key(13);
                Check(ui.View.AimodVisible && ui.View.PlayheadMs == 1003
                    && ui.View.SelectedObjectIds.ToHashSet().SetEquals(map.Fruits.Select(f => f.Id)), "Error must locate and select both objects.");
                Check(map.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Navigation must not edit content.");
                ui.Key(46);
                ui.Key('A', ctrl: true, shift: true); ui.Paint();
                Check(ui.View.AimodVisible, "Ctrl+Shift+A opens AiMod");
                ui.ClickText(L.Get("aimod.tabCompose", 0));
                Check(ui.View.AimodErrors.Count == 0 && ui.Canvas.Texts.Any(t => t.Value == L.Get("aimod.empty")), "Reopening must inspect current content.");
                ui.Key(27); ui.Key('Z', ctrl: true);
                Check(map.ContentEquals(ui.View.Document), "AiMod must leave deletion undo intact.");
                ui.View.ShowAimod(); ui.Paint();
                ui.View.LoadDocument(new MapDocument());
                Check(ui.View.AimodErrors.Count == 0 && ui.View.AimodIssues.Count > 0, "Switching documents automatically checks the new map and discards old findings.");
                ui.View.NewProject();
            }
        }
        finally { L.SetLanguage(previous); }
    }

    private static void DisabledMediaDiagnostics()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "aimod-disabled-media", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var ui = new Ui(false);
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "video-with-audio.flv"), Path.Combine(root, "clip.flv"));
            File.WriteAllText(Path.Combine(root, "shared.osb"), "[Events]\nVideo,0,clip.flv\nVideo,0,missing.wmv\n");
            var map = new MapDocument { SourcePath = Path.Combine(root, "fixture.osu"), IsDemo = false };
            ui.View.LibrarySettings.EnableVideoStoryboard = false;
            ui.LoadDocument(map); ui.View.ShowAimod();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!ui.View.AimodIssues.Any(i => i.Code == "missingFile") && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(1); }
            Check(ui.View.AimodIssues.Any(i => i.Code == "missingFile" && i.Arguments.Contains("missing.wmv"))
                && !ui.View.AimodIssues.Any(i => i.Code is "unreadableMedia" or "checkFailed"),
                "Disabled playback still probes supported video and reports missing storyboard resources");
            Check(!ui.View.AimodIssues.Any(i => i.Code is "largeVideoWide" or "largeVideoStandard"),
                "AiMod preserves stable's both-axis resolution threshold for a wide short video");
            Check(map.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Disabled media checking preserves beatmap content");
            for (int i = 0; i < 10; i++) ui.View.ShowAimod();
            ui.LoadDocument(new MapDocument());
            deadline = DateTime.UtcNow.AddMilliseconds(100);
            do { ui.Paint(); Thread.Sleep(1); } while (DateTime.UtcNow < deadline);
            Check(!ui.View.AimodIssues.Any(i => i.Code is "missingFile" or "unreadableMedia" or "checkFailed"),
                "Repeated refresh and document replacement retire prior media findings");
        }
        finally { ui.View.NewProject(); Directory.Delete(root, true); }
    }

    private static void AudioDuration()
    {
        var map = new MapDocument { AudioPath = "song.mp3", DurationMs = 59000 };
        map.Fruits.AddRange([new() { TimeMs = 1000 }, new() { TimeMs = 59000 }]);
        var ui = new Ui(); ui.LoadDocument(map); ui.View.ShowAimod();
        Check(!ui.View.AimodIssues.Any(i => i.Code == "longAudio"), "Loading audio does not produce an estimated length warning");
        ui.View.UpdateTransport(0, 60000, true, false, false, null, map.AudioPath);
        Check(!ui.View.AimodIssues.Any(i => i.Code == "longAudio"), "AiMod uses the same loaded duration as the timeline");
        var issues = ui.View.AimodIssues;
        ui.View.UpdateTransport(1000, 60000, true, true, false, null, map.AudioPath);
        Check(ReferenceEquals(issues, ui.View.AimodIssues), "Playback position changes do not rerun AiMod");
        ui.View.UpdateTransport(1000, 120000, true, false, false, null, map.AudioPath);
        Check(ui.View.AimodIssues.Count(i => i.Code == "longAudio") == 1, "A changed playback duration updates the audio-length finding");
        ui.View.UpdateTransport(0, 0, false, false, true, null, map.AudioPath);
        Check(!ui.View.AimodIssues.Any(i => i.Code == "longAudio"), "Reloading audio clears the previous duration finding");
        ui.View.UpdateTransport(0, 60000, true, false, false, null, map.AudioPath);
        Check(!ui.View.AimodIssues.Any(i => i.Code == "longAudio") && map.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
            "Completing audio reload refreshes the report without content edits");
        ui.View.NewProject();
    }

    public static void TableAndDock()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(); ui.Resize(1440, 900);
                var map = new MapDocument { DurationMs = 40000 };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1005, X = 400 }, new() { TimeMs = 40000, X = 200 }]);
                ui.LoadDocument(map); ui.Key('A', ctrl: true, shift: true);
                string copied = ""; ui.View.RequestCopyText = text => copied = text;
                var firstRow = ui.View.AimodRowBounds(0);
                ui.View.PointerDown(firstRow.Right - 10, firstRow.Y + 10, 0, false, false);
                ui.View.PointerUp(firstRow.Right - 10, firstRow.Y + 10, 0); ui.Key('C', ctrl: true);
                Check(copied.StartsWith(L.Get("aimod.columnTime") + "\t") && copied.Contains(L.Get("aimod.overlap")) && copied.Contains("00:01:003") && ui.View.AimodVisible, "Time is first and selected rows copy complete values without navigating");
                var secondRow = ui.View.AimodRowBounds(1);
                ui.View.PointerDown(secondRow.X + 15, secondRow.Y + 10, 0, true, false);
                ui.View.PointerUp(secondRow.X + 15, secondRow.Y + 10, 0); ui.Key('C', ctrl: true);
                Check(copied.Split(Environment.NewLine).Length == 3, "Shift-click copies exactly the selected row range");
                var header = ui.View.AimodHeaderBounds(1);
                Drag(header.Right, header.Y + 12, header.Right + 60, header.Y + 12);
                Check(Math.Abs(ui.View.AimodHeaderBounds(1).Width - header.Width - 60) < 1, "Column boundary resizes independently");
                var resized = ui.View.AimodHeaderBounds(1);
                ui.View.PointerDown(resized.Right, resized.Y + 10, 0, false, false);
                ui.View.PointerMove(resized.Right + 30, resized.Y + 10, false, false); ui.Key(27);
                Check(!ui.View.WantsCapture && ui.View.AimodHeaderBounds(1).Width == resized.Width, "Escape cancels a column resize without closing the report");
                header = ui.View.AimodHeaderBounds(1);
                Drag(header.X + 30, header.Y + 12, ui.View.AimodHeaderBounds(0).X + 5, header.Y + 12);
                ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
                Check(copied.StartsWith(L.Get("aimod.columnSeverity") + "\t") && copied.Contains(L.Get("aimod.noBackground")), "Column reorder is reflected in copied selected rows");
                for (int column = 0; column < 3; column++)
                {
                    var scroll = ui.View.AimodHorizontalTrack;
                    var target = ui.View.AimodHeaderBounds(column);
                    if (target.Right > ui.View.AimodPanelBounds.Right - 13)
                        Drag(scroll.Right - 1, scroll.Y + 5, scroll.Right - 1, scroll.Y + 5);
                    else if (target.Right < ui.View.AimodPanelBounds.X)
                        Drag(scroll.X + 1, scroll.Y + 5, scroll.X + 1, scroll.Y + 5);
                    target = ui.View.AimodHeaderBounds(column);
                    Drag(target.Right, target.Y + 10, target.Right + 25, target.Y + 10);
                    Check(Math.Abs(ui.View.AimodHeaderBounds(column).Width - target.Width - 25) < 1,
                        "Every column has an independently draggable right boundary");
                }
                Check(ui.View.AimodDocked && ui.View.AimodVisible, "AiMod opens directly as an independent dock tab");
                var layout = ui.View.CaptureDockLayouts();
                Check(layout.Mapping!.Right!.Panels!.Contains("Aimod"), "Dock layout persists AiMod identity");
                ui.Key(27); ui.ClickMap(15000, 250); ui.Key('A', ctrl: true, shift: true);
                Check(ui.View.AimodDocked && ui.View.AimodVisible, "Shortcut focuses and refreshes an attached report");
                var tab = ui.View.DockTabBounds(EditorView.DockPanel.Aimod);
                Drag(tab.X + 15, tab.Y + 12, 8, 200);
                Check(ui.View.AimodDocked && ui.View.AimodPanelBounds.X < ui.Plot.X, "AiMod can attach on the left");
                Check(map.ContentEquals(ui.View.Document) && !ui.View.IsDirty && !ui.View.WantsCapture, "Report selection, copying and docking preserve beatmap and history");
                Check(ui.View.AddDifficulty(), "Add another difficulty while AiMod is docked"); ui.Paint();
                Check(ui.View.AimodErrors.Count == 0 && ui.View.AimodIssues.Count > 0 && ui.View.AimodDocked,
                    "Difficulty changes retain the panel and automatically check the new difficulty");
                var restored = new EditorView(false);
                restored.LibrarySettings.DockLayouts = layout;
                restored.RestoreDockLayouts();
                Check(restored.AimodIssues.Count == 0, "Restoring layout without a loaded map does not analyze a placeholder");
                restored.LoadDocument(map);
                Check(restored.AimodErrors.Count == 1 && restored.AimodVisible, "Restored AiMod checks immediately when the map finishes loading");
                var checkedErrors = restored.AimodErrors;
                restored.Render(new RecordingCanvas(), 1440, 900); restored.Render(new RecordingCanvas(), 1000, 700);
                Check(ReferenceEquals(checkedErrors, restored.AimodErrors), "Repaints and window resizing do not restart the initial analysis");
                Check(map.ContentEquals(restored.Document) && !restored.IsDirty, "Automatic initial checking does not edit the map");
                var loadedFirst = new EditorView(false); loadedFirst.LoadDocument(map);
                loadedFirst.LibrarySettings.DockLayouts = layout;
                loadedFirst.RestoreDockLayouts();
                Check(loadedFirst.AimodErrors.Count == 1, "Restoring layout after map loading also initializes the report");
                restored.NewProject(); loadedFirst.NewProject();
                ui.View.NewProject();

                void Drag(float x, float y, float endX, float endY)
                {
                    ui.View.PointerDown(x, y, 0, false, false);
                    ui.View.PointerMove(endX, endY, false, false); ui.Paint();
                    ui.View.PointerUp(endX, endY, 0); ui.Paint();
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
