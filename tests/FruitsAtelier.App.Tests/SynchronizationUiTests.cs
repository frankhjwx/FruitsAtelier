using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using L = FruitsAtelier.Localization.Strings;

internal static class SynchronizationUiTests
{
    public static void ArScaleAndDecisions()
    {
        foreach (int ar in new[] { 1, 5, 9 })
        foreach (int width in new[] { 980, 1440 })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-ar", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "map.osu");
            string text = Fixture.Replace("ApproachRate:5", "ApproachRate:" + ar) + "200,192,1200,1,0,0:0:0:0:\n";
            Directory.CreateDirectory(set); File.WriteAllText(source, text);
            var ui = new Ui(false); ui.Resize(width, 900);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
            var session = LibraryOperations.ImportPath(source, ui.View.LibrarySettings); ui.View.LoadWorkspace(session);
            File.WriteAllText(source, text.Replace("123,192", "321,192").Replace("200,192", "400,192"));
            ui.View.RefreshSynchronization(); Wait(ui);
            var field = ui.Canvas.Clips[1];
            double Spacing() { var dots = ui.Canvas.Circles.Where(c => c.Filled && c.X < width / 2).OrderBy(c => c.Y).ToArray(); Check(dots.Length == 2, "two visible FA objects"); return dots[1].Y - dots[0].Y; }
            double expected = 200 * CatchScrollTiming.PixelsPerMs(ar, field.Width);
            Check(Math.Abs(Spacing() - expected) < .02, "comparison uses editor AR scale at each width");
            ui.View.Wheel(field.X + 10, field.Y + 10, 120, true); ui.Paint();
            Check(Math.Abs(Spacing() - expected * 1.2) < .02, "zoom is relative to AR scale");
            ui.ClickText(L.Get("sync.chooseLocal"));
            Check(Math.Abs(Spacing() - expected) < .02, "next conflict restores current AR scale");
            Check(ui.Canvas.Outlines.Any(c => c.Color == 0x70D69B && c.Bounds.X < width / 2)
                && ui.Canvas.Circles.Any(c => c.Filled && c.Opacity == .2f && c.X > width / 2), "previous decision distinguishes retained and rejected context");
            var green = ui.Canvas.Outlines.First(c => c.Color == 0x70D69B && c.Bounds.X < width / 2).Bounds;
            ui.View.PointerDown(green.X + 5, green.Y + green.Height / 2, 0, false, false); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.resolvedThisRound")))
                && ui.Canvas.Outlines.Any(o => o.Color == 0xED737B), "green interval remains clickable alongside unresolved red intervals");
            ui.ClickText(L.Get("sync.chooseExternal"));
            Check(ui.View.Document.Fruits[0].X == 123, "changing a green decision does not apply it early");
            ui.ClickText("‹");
            ui.ClickText(L.Get("sync.chooseLocal"));
            ui.ClickText(L.Get("sync.chooseLocal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
            Check(!ui.View.SynchronizationVisible, "retained differences do not repeat");
            File.WriteAllText(source, text.Replace("123,192", "333,192").Replace("200,192", "400,192"));
            ui.View.RefreshSynchronization(); Wait(ui);
            Check(ui.View.SynchronizationVisible && ui.Canvas.Texts.Any(t => t.Y == 86 && t.Value.Contains(L.Get("sync.unresolvedRange"))), "later external edit is a normal unresolved conflict");
            Check(ui.Canvas.Circles.Count(c => !c.Filled && c.Color == 0xED737B) == 2, "both corresponding objects remain highlighted");
            Check(ui.Canvas.Outlines.Any(o => o.Color == 0xD5A34D), "unchanged prior resolution has an amber interval");
            ui.ClickText(L.Get("sync.chooseLocal")); ui.ClickText("›");
            Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.alreadyResolved"))), "unchanged prior resolution is explicitly identified");
            ui.ClickText(L.Get("sync.chooseExternal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
            Check(ui.View.Document.Fruits.Any(f => f.TimeMs == 1200 && f.X == 400), "previously resolved group can be resolved to the other side");
            ui.View.RefreshSynchronization(reviewResolved: true); Wait(ui);
            Check(ui.View.SynchronizationVisible && ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.alreadyResolved"))), "explicit check can review retained differences without new edits");
        }
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-ui", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "map.osu");
                Directory.CreateDirectory(set); File.WriteAllText(source, Fixture);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
                var session = LibraryOperations.ImportPath(source, ui.View.LibrarySettings);
                ui.View.LoadWorkspace(session, checkAdditionalDifficulties: true); Wait(ui);
                Check(!ui.View.SynchronizationVisible, "new import remains editable");
                CheckBusyInput(ui);
                ui.View.ChangeAudioPath(Path.Combine(set, "pending.ogg"));
                Guid original = ui.View.Document.Fruits[0].Id;
                File.WriteAllText(source, Fixture.Replace("Title:Original", "Title:Updated"));
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(!ui.View.SynchronizationVisible && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Updated", "metadata auto-sync");
                Check(ui.View.Document.Fruits[0].Id == original && ui.View.Document.AudioPath!.EndsWith("pending.ogg"), "authoring preserved");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.AudioPath is null && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Updated", "undo retains synchronized metadata");
                File.Delete(source);
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.View.SynchronizationVisible && ui.View.DifficultySyncState(0) == WorkspaceSyncState.Missing, "missing blocks active editing");
                var before = ui.View.Document.DeepClone(); ui.Key(46); ui.Key('Z', ctrl: true); ui.Type("x");
                Check(ui.View.Document.ContentEquals(before), "modal isolates object input");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.missingBadge")), "missing badge drawn");
                ui.ClickText(L.Get("sync.restore")); Wait(ui);
                Check(File.Exists(source) && !ui.View.SynchronizationVisible, "explicit restore returns to editing");
                File.WriteAllText(source, File.ReadAllText(source).Replace("123,192", "200,192"));
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.View.SynchronizationVisible, "external object edit requires choice");
                ui.ClickText(L.Get("sync.chooseLocal"));
                ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(ui.View.Document.Fruits[0].Id == original && ui.View.Document.Fruits[0].X == 123, "per-object local choice retains editable identity");
                Check(OsuBeatmapReader.ReadFile(source).Fruits[0].X == 200, "resolution does not silently export");
                var legacy = WorkspaceProject.Open(session.Directory);
                legacy.Manifest.Difficulties[0].Sync = null;
                WorkspaceProject.Save(legacy, legacy.Project);
                File.WriteAllText(source, OsuBeatmapWriter.Serialize(ui.View.Document).Text.Replace("Title:Updated", "Title:Reviewed").Replace("123,192", "321,192"));
                File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
                ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory), checkAdditionalDifficulties: true); Wait(ui);
                Check(ui.View.DifficultySyncState(0) == WorkspaceSyncState.NeedsBaseline, "legacy comparison stays explicit");
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith("Metadata/Title")) && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.page", 1, 2)), "legacy first difference and count shown");
                Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.newerSaved")) && t.X > size.Item1 / 2), "external timestamp marks newer saved version");
                string authoringFile = Path.Combine(legacy.Directory, legacy.Manifest.Difficulties[0].File);
                DateTime savedTime = File.GetLastWriteTimeUtc(authoringFile);
                File.SetLastWriteTimeUtc(source, savedTime.AddMinutes(-1)); ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.newerSaved")) && t.X < size.Item1 / 2), "FA can be the newer saved version");
                File.SetLastWriteTimeUtc(source, savedTime); ui.View.RefreshSynchronization(); Wait(ui);
                Check(!ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.newerSaved"))), "equal timestamps do not invent a newer side");
                ui.ClickText(L.Get("sync.chooseExternal")); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.page", 2, 2)), "choosing advances to next unresolved item");
                var rings = ui.Canvas.Circles.Where(c => !c.Filled && c.Color == 0xED737B).ToArray();
                Check(rings.Length == 2 && Math.Abs(rings[0].Y - rings[1].Y) < .01 && rings[0].X < size.Item1 / 2 && rings[1].X > size.Item1 / 2, "both canvas versions highlight the aligned conflict");
                var unchanged = ui.View.Document.DeepClone();
                ui.View.Wheel(200, 260, 120, false); ui.Paint();
                var moved = ui.Canvas.Circles.Where(c => !c.Filled && c.Color == 0xED737B).ToArray();
                Check(moved.Length == 2 && Math.Abs(moved[0].Y - moved[1].Y) < .01 && moved[0].Y != rings[0].Y, "wheel scrolls both canvases together");
                ui.View.Wheel(200, 260, 120, true); ui.Paint();
                Check(ui.View.Document.ContentEquals(unchanged), "comparison scrolling and zoom never edit content");
                ui.ClickText(L.Get("sync.resultPreview"));
                var previewDeadline = DateTime.UtcNow.AddSeconds(10);
                while (!ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.previewPending")) && DateTime.UtcNow < previewDeadline) { Thread.Sleep(10); ui.Paint(); }
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.previewPending")), "merged result preview prepared asynchronously");
                ui.ClickText("‹"); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith("Metadata/Title")), "previous choice can be reviewed");
                ui.ClickText("›"); ui.ClickText(L.Get("sync.chooseLocal"));
                ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(!ui.View.SynchronizationVisible && ui.View.Document.Fruits[0].Id == original
                    && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Reviewed", "legacy mixed resolution applied");
                Check(ui.View.WorkspaceSession!.Manifest.Difficulties[0].Sync is not null, "resolved legacy comparison establishes baseline");
                legacy = WorkspaceProject.Open(session.Directory); legacy.Manifest.Difficulties[0].Sync = null;
                WorkspaceProject.Save(legacy, legacy.Project);
                ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory), checkAdditionalDifficulties: true); Wait(ui);
                Check(!ui.View.SynchronizationVisible && ui.View.Document.Fruits[0].Id == original && ui.View.Document.Fruits[0].X == 123,
                    "unchanged legacy fingerprint establishes baseline while retaining local differences");
                ui.View.ShowDeleteDifficulty(0); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == source), "deletion shows external target");
                ui.ClickText(L.Get("sync.delete")); Wait(ui);
                Check(!File.Exists(source) && ui.View.LibraryVisible, "delete last difficulty removes both and returns to library");
                ui.View.NewProject(); ui.View.SaveWorkspace(); ui.View.RefreshSynchronization(); Wait(ui);
                Check(!ui.View.SynchronizationVisible && ui.View.DifficultySyncState(0) == WorkspaceSyncState.Local, "unlinked project is exempt");
            }
        }
        finally { L.SetLanguage(language); }
    }

    internal static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do { ui.Paint(); if (!ui.View.SynchronizationBusy) break; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
        Check(!ui.View.SynchronizationBusy, "sync timeout"); ui.Paint();
        if (ui.View.ErrorVisible) throw new Exception("Unexpected error in synchronization UI");
    }

    private static void CheckBusyInput(Ui ui)
    {
        var field = typeof(FruitsAtelier.App.Editor.EditorView).GetField("syncCommitTask",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var pending = new TaskCompletionSource<WorkspaceSession>();
        var before = ui.View.Document.DeepClone();
        field.SetValue(ui.View, pending.Task);
        try
        {
            ui.Paint();
            Check(!ui.View.SynchronizationVisible && ui.View.SynchronizationBusy, "busy sync has no modal");
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.applying") && t.Y > 500), "busy progress appears in bottom status");
            Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.title")), "busy sync title is absent");
            ui.Key(27); ui.Key(13); ui.Key(46); ui.Key('Z', ctrl: true); ui.Type("x");
            ui.View.PointerDown(450, 300, 0, false, false);
            ui.View.PointerMove(550, 350, false, false);
            ui.View.PointerUp(550, 350, 0);
            ui.View.Wheel(450, 300, 120, false);
            Check(ui.View.SynchronizationBusy && ui.View.Document.ContentEquals(before), "busy sync consumes input without cancellation or edits");
        }
        finally { field.SetValue(ui.View, null); }
        ui.View.RefreshSynchronization();
        Check(ui.View.SynchronizationBusy && !ui.View.SynchronizationVisible, "scan has no modal");
        Wait(ui);
        Check(ui.View.StatusMessage == L.Get("sync.complete"), "completed scan clears busy feedback");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private const string Fixture = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Original\nArtist:Artist\nCreator:Mapper\nVersion:Catch\n[Difficulty]\nCircleSize:5\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n123,192,1000,1,0,0:0:0:0:\n";
}
