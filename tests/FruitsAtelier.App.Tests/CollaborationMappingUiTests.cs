using System.Diagnostics;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class CollaborationMappingUiTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var a = Difficulty("A", 250); var b = Difficulty("B", 1250);
                var c = Difficulty("C", 2200); var d = Difficulty("D", 2400);
                foreach (var difficulty in new[] { a, b, c, d }) difficulty.Document.TimingPoints[0].TimeMs = .75;
                c.Document.CollaborationReferences = [new(Guid.NewGuid(), a.Id, 0, 1000), new(Guid.NewGuid(), b.Id, 1000, 2000)];
                d.Document.CollaborationReferences = [new(Guid.NewGuid(), a.Id, 0, 1000)];
                var project = new BeatmapProject { Name = "Collaboration", Difficulties = [a, b, c, d] };
                var ui = new Ui(); ui.View.LoadProject(project); ui.Paint();
                ui.ClickText(L.Get("dock.layout"));
                ui.ClickText(L.Get("collab.title"));
                Check(ui.View.CollaborationPanelVisible, "Layout menu opens the Collab Mapping panel tab");
                foreach (var size in new[] { (980f, 620f), (1440f, 900f) })
                {
                    ui.Resize(size.Item1, size.Item2);
                    Check(ui.Canvas.Texts.Any(t => t.Value == "C") && ui.Canvas.Texts.Any(t => t.Value == "D"), "Every reverse target is visible");
                    var before = ui.View.Document.DeepClone();
                    ui.Key('F'); ui.Key(46); ui.View.Wheel(400, 300, 120, false); ui.Paint();
                    Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Viewing references and ordinary canvas keys are content neutral");
                }
                Check(ui.View.SwitchDifficulty(2), "Switch to merge target"); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == "A") && ui.Canvas.Texts.Any(t => t.Value == "B"), "Merge target lists its sources");
                var input = ui.View.CollaborationStartBounds; float x = input.X + 10;
                string copied = "", clipboard = "00:00:050 (1,2) - ";
                ui.View.RequestCopyText = text => copied = text;
                ui.View.RequestPasteField = () => ui.View.PasteFieldText(clipboard, ui.View.ActiveTextInputField);
                ui.Click(x, input.Y + 10); ui.Key('C', ctrl: true);
                Check(copied == "00:00:000", "Timestamp copy uses osu format");
                ui.Key('V', ctrl: true); ui.Key(13);
                Check(ui.View.Document.CollaborationReferences![0].StartMs == 50 && ui.View.IsDirty, "Range input commits and becomes dirty");
                ui.Click(x, input.Y + 10); ui.Key('A', ctrl: true); ui.Type("50.5"); ui.Key(13);
                Check(ui.View.Document.CollaborationReferences![0].StartMs == 50 && ui.View.IsEditingText, "Fractional milliseconds are rejected");
                ui.Key(27);
                ui.Key('Z', ctrl: true); Check(ui.View.Document.CollaborationReferences![0].StartMs == 0, "Reference edit undo");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.CollaborationReferences![0].StartMs == 50, "Reference edit redo");
                ui.View.UpdateTransport(1234, 5000, true, false, false, null, null);
                double playhead = ui.View.PlayheadMs, viewStart = ui.View.ViewStartMs;
                ui.View.SynchronizeCollaboration();
                Check(ui.View.CollaborationBusy, "Synchronization is submitted to a worker");
                var watch = Stopwatch.StartNew();
                while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
                Check(!ui.View.CollaborationBusy && ui.View.Document.Fruits.Count == 3, "Sources merged and outside target object retained");
                Check(!ui.View.CollaborationReportVisible, "No exclusions leave the report closed");
                Check(ui.View.Document.TimingPoints.Count == 1 && ui.View.Document.TimingPoints[0].TimeMs == .75,
                    "Synchronization retains the original fractional red point without section boundary lines");
                Check(Math.Abs(TimingMap.Snap(ui.View.Document, 1250, 4) - 1250.75) < 1e-7, "Placement stays on the target beat grid");
                Check(ui.View.PlayheadMs == playhead && ui.View.ViewStartMs == viewStart && ui.View.AudioReady, "Sync preserves transport and viewport");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits.Count == 1, "Synchronization undo");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.Fruits.Count == 3, "Synchronization redo");
                var loaded = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(ui.View.CaptureProject()));
                ui.View.LoadProject(loaded); ui.View.SwitchDifficulty(2); ui.View.ShowCollaborationPanel(); ui.Paint();
                Check(ui.View.Document.CollaborationReferences!.Count == 2, "Restart retains references");
                Check(ui.View.Document.TimingPoints.Count == 1 && ui.View.Document.TimingPoints[0].TimeMs == .75,
                    "Restart retains fractional timing without extra red or green points");
                ui.ClickText(L.Get("collab.add")); Check(ui.View.Document.CollaborationReferences!.Count == 3, "Multiple references can be added");
                Check(ui.View.Document.CollaborationReferences![2].DifficultyId == Guid.Empty, "New references start unassigned");
                Check(ui.View.SwitchDifficulty(0), "Source navigation"); ui.Paint();
                Check(ui.Canvas.Texts.Count(t => t.Value == "C") >= 1 && ui.Canvas.Texts.Count(t => t.Value == "D") >= 1, "Reverse graph updates after restart");
                Console.WriteLine("PASS Collab Mapping UI " + locale);
                BookmarkSections();
                ExclusionReport();
                ObjectSoundsOption();
                AimodAfterSync();
                ReverseActions();
            }
        }
        finally { L.SetLanguage(language); }
        foreach (var (text, expected) in new[] { ("84771", 84771d), ("01:24:771", 84771d), ("00:00:000", 0d), ("35791:23:647", 2147483647d) })
            Check(FruitsAtelier.App.Editor.EditorView.TryParseCollaborationTime(text, out double result) && result == expected, "Timestamp parser " + text);
        foreach (string text in new[] { "84771.521739", "1e3", "-1", "00:60:000", "00:00:1000", "2147483648" })
            Check(!FruitsAtelier.App.Editor.EditorView.TryParseCollaborationTime(text, out _), "Invalid timestamp " + text);
        ResourceUndo();
    }

    private static void ReverseActions()
    {
        var part = Difficulty("Part", 1200); var merged = Difficulty("Merged", 500);
        var lockedTrack = new CurveTrack { Kind = CurveKind.Linear };
        lockedTrack.Nodes.AddRange([new() { TimeMs = 1100, X = 300 }, new() { TimeMs = 1400, X = 350 }]);
        part.Document.Tracks.Add(lockedTrack);
        part.Document.BananaShowers.Add(new() { TimeMs = 2200, EndTimeMs = 2400 });
        merged.Document.Fruits.Add(new() { TimeMs = 1800, X = 240 });
        merged.Document.CollaborationReferences = [new(Guid.NewGuid(), part.Id, 0, 1000), new(Guid.NewGuid(), part.Id, 1500, 2000)];
        var ui = new Ui(); ui.View.LoadProject(new BeatmapProject { Difficulties = [part, merged] });
        ui.View.ShowCollaborationPanel(); ui.Paint();
        Check(ui.Canvas.Texts.Count(t => t.Value == "Merged" && t.Y > ui.View.CollaborationPanelViewport.Y) == 1, "Same incoming difficulty is grouped");
        ui.View.SynchronizeCollaborationFrom(merged.Id);
        var watch = Stopwatch.StartNew();
        while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
        Check(ui.View.Document.Fruits.Count == 3 && ui.View.Document.CollaborationReferences is null, "Reverse worker syncs both ranges");
        Check(ui.View.Document.CollaborationEditableRanges?.Count == 2, "Reverse sync automatically locks outside both guest intervals");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 1 && ui.View.Document.CollaborationEditableRanges is null, "Reverse sync and automatic lock undo together");
        ui.Key('Y', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 3 && ui.View.Document.CollaborationEditableRanges?.Count == 2, "Reverse sync and automatic lock redo together");
        var overlays = ui.Canvas.PaintCalls.Where(p => p.Color == 0xFF9BAA && p.FillBounds is not null).ToArray();
        Check(overlays.Length >= 3 && overlays.All(p => p.Opacity == .30f), "Canvas and both timelines draw 30 percent pink overlays");
        Check(overlays.Where(p => p.FillBounds!.Value.Y >= ui.Plot.Y && p.FillBounds.Value.Y < ui.Plot.Bottom)
            .All(p => p.FillBounds!.Value.X == ui.View.PlayfieldBounds.X && p.FillBounds.Value.Width == ui.View.PlayfieldBounds.Width),
            "Canvas lock overlay stays inside the playfield");
        Guid lockedId = ui.View.Document.Fruits.Single(f => f.TimeMs == 1200).Id;
        ui.Key('1'); ui.ClickMap(1200, 180);
        Check(!ui.View.SelectedObjectIds.Contains(lockedId), "Locked canvas object cannot be clicked");
        ui.ClickMap(1100, 300);
        Check(ui.View.SelectedObjectIds.Count == 0 && ui.View.SelectedAnchorIds.Count == 0, "Locked FSlider anchors cannot be selected");
        ui.ClickMap(1250, 325);
        Check(ui.View.SelectedObjectIds.Count == 0, "Locked FSlider path cannot be selected");
        ui.ClickMap(2300, 256);
        Check(ui.View.SelectedObjectIds.Count == 0, "Locked spinner cannot be selected");
        ui.Key('A', ctrl: true);
        Check(ui.View.SelectedObjectIds.Count == 2 && !ui.View.SelectedObjectIds.Contains(lockedId), "Select all excludes locked objects");
        var field = ui.View.PlayfieldBounds; var plot = ui.Plot;
        ui.View.PointerDown(field.X + 3, plot.Bottom - 3, 0, false, false);
        ui.View.PointerMove(field.Right - 3, plot.Y + 3, false, false);
        ui.View.PointerUp(field.Right - 3, plot.Y + 3, 0); ui.Paint();
        Check(!ui.View.SelectedObjectIds.Contains(lockedId), "Canvas marquee excludes locked objects");
        ui.View.UpdateTransport(1200, 5000, true, false, false, null, null); ui.Paint();
        var timeline = ui.View.ObjectTimelineBounds;
        float lockedX = timeline.X + (float)((1200 - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        ui.Click(lockedX, timeline.Y + timeline.Height / 2);
        Check(!ui.View.SelectedObjectIds.Contains(lockedId), "Locked timeline object cannot be clicked");
        ui.View.ToggleOsuCanvas(); ui.Paint();
        var osuPoint = ui.View.OsuCanvasScreen(180, 192); ui.Click(osuPoint.X, osuPoint.Y);
        Check(!ui.View.SelectedObjectIds.Contains(lockedId), "Locked geometric canvas object cannot be clicked");
        ui.View.ToggleOsuCanvas(); ui.Paint();
        ui.View.RemoveNotesOutsideCollaborationSections(merged.Id); ui.Paint();
        Check(ui.View.Document.Fruits.Select(f => f.TimeMs).Order().SequenceEqual(new double[] { 500, 1800 }), "Cleanup removes gap even when locked");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits.Count == 3, "Cleanup undo");
        ui.View.ToggleCollaborationSectionLock(merged.Id);
        Check(ui.View.Document.CollaborationEditableRanges is null, "Explicit unlock");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.CollaborationEditableRanges?.Count == 2, "Unlock undo");
        var saved = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(ui.View.CaptureProject()));
        ui.View.LoadProject(saved); Check(ui.View.Document.CollaborationEditableRanges?.Count == 2, "UI restart retains section locks");
    }

    private static void AimodAfterSync()
    {
        var source = Difficulty("Source", 500); var target = Difficulty("Target", 2200);
        target.Document.TimingPoints.Add(new() { TimeMs = 2000, BeatLengthMs = 500, SourceOrder = int.MinValue });
        target.Document.CollaborationReferences = [new(Guid.NewGuid(), source.Id, 0, 2000)];
        var ui = new Ui(); ui.View.LoadProject(new() { Name = "refresh report", Difficulties = [source, target] });
        ui.View.SwitchDifficulty(1); ui.View.ShowAimod(); ui.Paint();
        Check(ui.View.AimodIssues.Any(i => i.Code == "conflictTiming"), "AiMod initially reports the target endpoint timing conflict");
        ui.View.ShowCollaborationPanel(); ui.Paint();
        ui.View.SynchronizeCollaboration();
        var watch = Stopwatch.StartNew();
        while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
        Check(!ui.View.CollaborationBusy && !ui.View.AimodIssues.Any(i => i.Code == "conflictTiming"),
            "Successful synchronization refreshes the existing AiMod report automatically");
        Check(ui.View.CollaborationPanelVisible && !ui.View.AimodVisible, "Automatic AiMod refresh preserves the active dock tab");
    }

    private static void BookmarkSections()
    {
        var source = Difficulty("Source", 250); var target = Difficulty("Target", 1250);
        SongSetup.Set(target.Document, "Editor", "Bookmarks", "500,1500");
        target.Document.CollaborationReferences = [new(Guid.NewGuid(), source.Id, 0, 1000)];
        var ui = new Ui(); ui.View.LoadProject(new() { Name = "sections", Difficulties = [source, target] });
        ui.View.SwitchDifficulty(1); ui.View.ShowCollaborationPanel(); ui.Resize(1440, 900);
        ui.View.UpdateTransport(0, 6000, true, false, false, null, null); ui.Paint();
        var before = ui.View.Document.DeepClone();
        ui.ClickText(L.Get("collab.fromBookmarks"));
        var sections = ui.View.Document.CollaborationReferences!;
        Check(sections.Count == 3 && sections.All(r => r.DifficultyId == Guid.Empty), "Bookmark generation replaces references with empty sources");
        Check(sections[0].StartMs == 0 && sections[0].EndMs == 500 && sections[1].EndMs == 1500 && sections[2].EndMs == 6000,
            "Generated sections cover the whole audio timeline");
        var generated = ui.View.Document.DeepClone();
        ui.View.SynchronizeCollaboration();
        Check(!ui.View.CollaborationBusy && ui.View.Document.ContentEquals(generated), "Empty sources skip synchronization");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "Generation undo restores assigned references");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(generated), "Generation redo restores empty sections");
        ui.Paint();
        void OpenSource()
        {
            var input = ui.View.CollaborationStartBounds;
            ui.Click(ui.View.CollaborationPanelViewport.X + 40, input.Y + 10);
        }
        OpenSource(); ui.ClickObjectAction("Source");
        Check(ui.View.Document.CollaborationReferences![0].DifficultyId == source.Id, "User assigns a generated section source");
        OpenSource(); ui.ClickObjectAction(L.Get("collab.unassigned"));
        Check(ui.View.Document.CollaborationReferences![0].DifficultyId == Guid.Empty, "User clears the source without deleting the section");
        var loaded = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(ui.View.CaptureProject()));
        Check(loaded.Difficulties[1].Document.CollaborationReferences!.All(r => r.DifficultyId == Guid.Empty), "Empty UI sources survive restart");
        var first = ui.View.CollaborationStartBounds;
        ui.Click(first.X + 10, first.Y + 10); ui.Key('A', ctrl: true); ui.Type("501"); ui.Key(13);
        Check(ui.View.Document.CollaborationReferences![0].StartMs == 0, "Empty section rejects an invalid range"); ui.Key(27);
        var second = ui.View.CollaborationStartBounds;
        ui.Click(second.X + 10, second.Y + 50); ui.Key('A', ctrl: true); ui.Type("499"); ui.Key(13);
        Check(ui.View.Document.CollaborationReferences![1].StartMs == 500, "Empty sections reject overlap in the UI"); ui.Key(27);
    }
    private static void ResourceUndo()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/collaboration", "ui-resources-" + Guid.NewGuid().ToString("N")));
        string sourceRoot = Path.Combine(root, "source"), targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot); Directory.CreateDirectory(targetRoot);
        File.WriteAllBytes(Path.Combine(sourceRoot, "tap.wav"), [1, 2, 3]);
        var source = new ProjectDifficulty { Name = "source", Document = OsuBeatmapReader.Read(
            "osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n180,192,500,1,0,1:1:0:65:tap.wav\n", Path.Combine(sourceRoot, "source.osu")) };
        var target = Difficulty("target", 2000); target.Document.SourcePath = Path.Combine(targetRoot, "target.osu");
        target.Document.CollaborationReferences = [new(Guid.NewGuid(), source.Id, 0, 1000)];
        var ui = new Ui(); ui.View.LoadProject(new() { Name = "resources", Difficulties = [source, target] });
        ui.View.SwitchDifficulty(1); ui.View.ShowCollaborationPanel(); ui.Paint(); ui.View.SynchronizeCollaboration();
        var watch = Stopwatch.StartNew();
        while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
        Check(ui.View.Document.Fruits.Count == 2, "Cross-folder sync commits content");
        string copied = Directory.EnumerateFiles(targetRoot, "tap.wav", SearchOption.AllDirectories).Single();
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 1 && !File.Exists(copied), "Resource copies follow synchronization undo");
        ui.Key('Y', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 2 && File.Exists(copied), "Resource copies follow synchronization redo");
    }

    private static void ObjectSoundsOption()
    {
        var source = Difficulty("Source", 250); var target = Difficulty("Target", 250);
        source.Document.Fruits[0].OriginalLine = "180,192,250,1,8,2:2:0:70:";
        source.Document.Fruits.Add(new() { TimeMs = 500, X = 180, OriginalLine = "180,192,500,1,8,2:2:0:70:" });
        target.Document.Fruits[0].OriginalLine = "180,192,250,1,2,1:1:0:35:";
        target.Document.CollaborationReferences = [new(Guid.NewGuid(), source.Id, 0, 1000)];
        var ui = new Ui(); ui.View.LoadProject(new() { Name = "sound option", Difficulties = [source,target] });
        ui.View.SwitchDifficulty(1); ui.View.ShowCollaborationPanel(); ui.Resize(980,620);
        var before = ui.View.Document.DeepClone();
        ui.ClickText(L.Get("collab.syncObjectSounds"));
        Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Changing the sync sound option is content neutral");
        Sync();
        var output = OsuBeatmapWriter.Serialize(ui.View.Document); var resolver = new HitsoundResolver(ui.View.Document, output.PlayableObjects);
        Check(resolver.Describe(output.PlayableObjects.Single(o => o.TimeMs == 250)) is { Additions:2, Volume:35 }, "Object-only UI sync retains target sound at the same time");
        Check(resolver.Describe(output.PlayableObjects.Single(o => o.TimeMs == 500)) is { Additions:0, NormalSet:1 }, "Object-only UI sync defaults new objects");
        ui.Key('Z', ctrl:true); Check(ui.View.Document.ContentEquals(before), "Object-only sync undo restores target audio and objects");
        ui.Key('Y', ctrl:true); ui.ClickText(L.Get("collab.syncObjectSounds")); Sync();
        output = OsuBeatmapWriter.Serialize(ui.View.Document); resolver = new HitsoundResolver(ui.View.Document, output.PlayableObjects);
        Check(resolver.Describe(output.PlayableObjects.Single(o => o.TimeMs == 250)) is { Additions:8, Volume:70, NormalSet:2 }, "Re-enabling object sounds copies the source sound");
        void Sync()
        {
            ui.View.SynchronizeCollaboration(); var watch = Stopwatch.StartNew();
            while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
            Check(!ui.View.CollaborationBusy && ui.View.Document.Fruits.Count == 2, "Sound option sync completes");
        }
    }

    private static void ExclusionReport()
    {
        var source = Difficulty("Source", 250); var target = Difficulty("Target", 2200);
        for (int i = 0; i < 12; i++) source.Document.BananaShowers.Add(new() { TimeMs = 800 + i, EndTimeMs = 1500 + i });
        target.Document.CollaborationReferences = [new(Guid.NewGuid(), source.Id, 0, 1000)];
        var ui = new Ui(); ui.View.LoadProject(new() { Name = "exclusions", Difficulties = [source, target] });
        ui.View.SwitchDifficulty(1); ui.View.ShowCollaborationPanel(); ui.Resize(980, 620);
        ui.View.SynchronizeCollaboration();
        var watch = Stopwatch.StartNew();
        while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
        Check(ui.View.Document.Fruits.Count == 2 && ui.View.Document.BananaShowers.Count == 0 && ui.View.CollaborationReportVisible,
            "Successful sync presents partial-object exclusions");
        var before = ui.View.Document.DeepClone(); var r = ui.View.CollaborationReportBounds;
        ui.View.PointerDown(r.X + 50, r.Y + 16, 0, false, false);
        Check(ui.View.WantsCapture, "Report header captures dragging");
        ui.View.PointerMove(r.X + 90, r.Y + 46, false, false); ui.View.PointerUp(r.X + 90, r.Y + 46, 0); ui.Paint();
        var moved = ui.View.CollaborationReportBounds;
        Check(moved.X == r.X + 40 && moved.Y == r.Y + 30 && ui.View.Document.ContentEquals(before), "Report dragging is content neutral");
        ui.View.PointerDoubleClick(400, 300, false, false);
        Check(ui.View.Document.ContentEquals(before), "Report blocks double-click edits behind it");
        ui.View.Wheel(moved.X + 25, moved.Y + 110, -120, false); ui.Paint();
        ui.Click(moved.X + 25, moved.Y + 110);
        Check(ui.View.CurrentDifficultyName == "Source" && ui.View.PlayheadMs == 801
            && ui.View.SelectedObjectIds.Contains(source.Document.BananaShowers[1].Id), "Scrolling and clicking an exclusion locates the source object");
        ui.Key(27); Check(!ui.View.CollaborationReportVisible, "Escape closes the report");
        ui.View.SwitchDifficulty(1); ui.Paint(); ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 1, "Navigation and popup do not add undo history");
        ui.Key('Y', ctrl: true); ui.View.SynchronizeCollaboration();
        watch.Restart();
        while (ui.View.CollaborationBusy && watch.Elapsed.TotalSeconds < 15) { ui.Paint(); Thread.Sleep(1); }
        r = ui.View.CollaborationReportBounds; ui.Click(r.Right - 20, r.Y + 16);
        Check(!ui.View.CollaborationReportVisible, "Report close button");
    }
    private static ProjectDifficulty Difficulty(string name, double time)
    {
        var document = new MapDocument { Name = name, DurationMs = 5000, IsDemo = false };
        document.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        document.Fruits.Add(new() { TimeMs = time, X = 180 });
        SongSetup.Set(document, "Metadata", "Version", name);
        return new() { Name = name, Document = document };
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
