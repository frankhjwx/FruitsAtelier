using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using L = FruitsAtelier.Localization.Strings;

internal static class SynchronizationUiTests
{
    public static void PlaybackSave()
    {
        var ui = new Ui(false);
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/playback-save", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!); File.WriteAllText(source, Fixture);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.LoadWorkspace(LibraryOperations.ImportPath(source, ui.View.LibrarySettings)); Wait(ui);
        ui.View.WorkspaceSession!.Manifest.Difficulties[0].ExportConfirmed = true;
        ui.View.UpdateTransport(1000, 20000, true, true, false, null, ui.View.Document.AudioPath);
        int pauses = 0, foregroundExports = 0;
        ui.View.RequestPausePlayback = () => pauses++;
        ui.View.RequestWorkspaceExport = (_, _) => foregroundExports++;
        ui.View.RequestSave = ui.View.SaveCurrentDifficulty;
        var history = (EditorHistory)ui.View.GetType().GetProperty("history", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
        var commitField = ui.View.GetType().GetField("syncCommitTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            history.Begin("note"); history.Document.Fruits.Add(new Fruit { TimeMs = 3000, X = 200 }); history.Commit();
            ui.Key('S', ctrl: true);
            WorkspaceFileMonitorTests.Await(() => { ui.Paint(); return commitField.GetValue(ui.View) is not null; }, "Playback save starts publication");
            var committing = (Task<WorkspaceSession>)commitField.GetValue(ui.View)!;
            var gate = new TaskCompletionSource<WorkspaceSession>();
            commitField.SetValue(ui.View, gate.Task);
            Check(committing.Wait(TimeSpan.FromSeconds(15)), "Background publication completes");
            Check(!ui.View.DiscardConfirmationVisible && pauses == 0 && ui.View.AudioPlaying, "Local publication keeps playback and input active");
            ui.Key('Z', ctrl: true);
            Check(!ui.View.Document.Fruits.Any(f => f.TimeMs == 3000), "Undo works while publication is pending");
            ui.Key('Y', ctrl: true);
            history.Begin("newer note"); history.Document.Fruits.Add(new Fruit { TimeMs = 4000, X = 300 });
            gate.SetResult(committing.Result); ui.Paint();
            Check(history.HasActiveTransaction && ui.View.IsDirty && ui.View.Document.Fruits.Any(f => f.TimeMs == 4000),
                "Save completion retains an active newer edit and acknowledges only the published snapshot");
            Check(!OsuBeatmapReader.ReadFile(source).Fruits.Any(f => f.TimeMs == 4000), "An unfinished edit is not part of the earlier publication");
            history.Commit(); Wait(ui);
            Check(OsuBeatmapReader.ReadFile(source).Fruits.Any(f => f.TimeMs == 4000) && !ui.View.IsDirty,
                "The subsequent synchronization publishes the newer committed edit");
            Check(pauses == 0 && foregroundExports == 0 && ui.View.AudioPlaying, "Playback saves avoid pausing or repeating export on the foreground thread");
            ui.Key('Z', ctrl: true); Wait(ui);
            Check(!ui.View.Document.Fruits.Any(f => f.TimeMs == 4000), "The newer edit remains undoable after saving");
        }
        finally { ui.View.StopFileMonitoring(); }
    }

    public static void LocalBreaks()
    {
        var ui = new Ui(false);
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-local-breaks", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, Fixture.Replace("Mode:2", "Mode:2\nAudioFilename:music.wav"));
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(source)!, "music.wav"), [1, 2, 3, 4]);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.LoadWorkspace(LibraryOperations.ImportPath(source, ui.View.LibrarySettings)); Wait(ui);
        ui.View.WorkspaceSession!.Manifest.Difficulties[0].ExportConfirmed = true;
        int audioReloads = 0;
        double requestedSeek = -1;
        ui.View.RequestSeek = time => requestedSeek = time;
        ui.View.RequestDifficultyChanged = () =>
        {
            audioReloads++;
            ui.View.UpdateTransport(0, 20000, true, false, false, null, ui.View.Document.AudioPath);
        };
        ui.View.UpdateTransport(0, 20000, true, false, false, null, ui.View.Document.AudioPath);
        ui.View.UpdateTransport(9000, 20000, true, false, false, null, ui.View.Document.AudioPath); ui.Paint();
        try
        {
            var history = (EditorHistory)ui.View.GetType().GetProperty("history", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ui.View)!;
            history.Begin("content");
            OsuTimeline.AddBreak(ui.View.Document, 4000, 10000);
            SongSetup.Set(ui.View.Document, "Metadata", "Tags", "FA tags");
            history.Commit();
            void Synchronize()
            {
                double position = ui.View.PlayheadMs, start = ui.View.ViewStartMs;
                int reloads = audioReloads;
                ui.View.SaveCurrentDifficulty(); Wait(ui);
                source = ui.View.WorkspaceSession!.Manifest.Difficulties.Single().Source!;
                Check(ui.View.PlayheadMs == position && ui.View.ViewStartMs == start,
                    "saving and automatic export retain nonzero transport and viewport positions");
                Check(audioReloads == reloads, "content-only save does not reload unchanged audio");
                Check(!ui.View.SynchronizationVisible, "local break sync keeps the editor open");
                Check(OsuTimeline.Breaks(OsuBeatmapReader.ReadFile(source)).SequenceEqual(OsuTimeline.Breaks(ui.View.Document)), "external breaks match FA");
                Check(ReferenceEquals(history.Document, ui.View.Document), "sync retains the history owner");
                Check(WorkspaceSynchronization.ObjectLines(File.ReadAllText(source)).SequenceEqual(
                    WorkspaceSynchronization.ObjectLines(OsuBeatmapWriter.Serialize(ui.View.Document).Text)), "automatic export includes FA notes");
                Check(OsuBeatmapReader.Setting(OsuBeatmapReader.ReadFile(source), "Metadata", "Tags") == "FA tags", "FA Tags sync without review");
            }
            Synchronize();
            history.Begin("note in break"); ui.View.Document.Fruits.Add(new Fruit { TimeMs = 7000, X = 200 }); history.Commit();
            Synchronize();
            Check(OsuTimeline.Breaks(ui.View.Document).Count == 2, "note splits the break");
            ui.Key('Z', ctrl: true); Synchronize();
            Check(OsuTimeline.Breaks(ui.View.Document).Single() == new BreakPeriod(4000, 10000), "undo retains the original break");
            ui.Key('Y', ctrl: true); Synchronize();
            Check(OsuTimeline.Breaks(ui.View.Document).Count == 2, "redo retains the split break");
            history.Begin("standalone note"); ui.View.Document.Fruits.Add(new Fruit { TimeMs = 12000, X = 300 }); history.Commit();
            Synchronize();
            Check(OsuBeatmapReader.ReadFile(source).Fruits.Any(f => f.TimeMs == 12000), "object-only changes trigger automatic export");
            foreach (string key in new[] { "Artist", "Title", "Creator", "Version" })
            {
                string previous = source;
                history.Begin("metadata"); SongSetup.Set(ui.View.Document, "Metadata", key, "Changed " + key); history.Commit();
                Synchronize();
                Check(source != previous && !File.Exists(previous) && File.Exists(source), "save renames the linked " + key + " file");
                Check(ui.View.Document.SourcePath == source && ui.View.CaptureProject().Difficulties.Count == 1, "editor keeps one difficulty with its current path");
                ui.Key('Z', ctrl: true); Synchronize();
                Check(ui.View.Document.SourcePath == source && !ui.View.SynchronizationVisible, "metadata undo saves through the rebased source path");
            }
            string beforeOverwrite = source;
            history.Begin("metadata"); SongSetup.Set(ui.View.Document, "Metadata", "Artist", "Explicit artist"); history.Commit();
            var project = ui.View.CaptureProject();
            var plan = WorkspaceExport.Plan(ui.View.WorkspaceSession!, project.Difficulties[0], songs, true, "", true);
            LibraryOperations.Export(ui.View.WorkspaceSession!, project, plan); ui.View.LibraryExportFinished(plan);
            source = plan.Target;
            Check(!File.Exists(beforeOverwrite) && ui.View.Document.SourcePath == source && !ui.View.IsDirty, "explicit overwrite renames and acknowledges the saved editor path");
            ui.Key('Z', ctrl: true); Synchronize();
            double audioPosition = ui.View.PlayheadMs;
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(source)!, "music.wav"), [5, 6, 7, 8]);
            ui.View.RefreshSynchronization(); Wait(ui);
            Check(audioReloads == 1 && ui.View.PlayheadMs == audioPosition && requestedSeek == audioPosition,
                "same-path audio replacement reloads while retaining the current transport position");
        }
        finally { ui.View.StopFileMonitoring(); }
    }

    public static void TimingRows()
    {
        foreach (string language in L.AvailableLanguages)
        foreach (int width in new[] { 980, 1440 })
        {
            L.SetLanguage(language);
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-timing", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!); File.WriteAllText(source, Fixture);
            var ui = new Ui(false); ui.Resize(width, 800);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
            var session = LibraryOperations.ImportPath(source, ui.View.LibrarySettings);
            var document = session.Project.Difficulties[0].Document;
            document.DurationMs = 10000;
            var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 3000, X = 100 }, new() { TimeMs = 3500, X = 400 }]);
            document.Tracks.Add(track);
            var output = OsuBeatmapWriter.Serialize(document);
            File.WriteAllText(source, output.Text);
            var entry = session.Manifest.Difficulties[0]; entry.SourceHash = WorkspaceSynchronization.Digest(output.Text);
            entry.Sync = WorkspaceSynchronization.Capture(source, document, session.Directory, output.Text, output.ObjectSources);
            WorkspaceProject.Save(session, session.Project);
            ui.View.LoadWorkspace(session); Wait(ui);
            try
            {
                const string green = "500,-100,4,1,0,100,0,0";
                var before = ui.View.Document.DeepClone();
                File.WriteAllText(source, output.Text.Replace("0,500,4,1,0,100,1,0", "0,500,4,1,0,100,1,0\r\n" + green));
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.View.SynchronizationVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.timingHelp")), "timing review explains the changed rows");
                var highlighted = ui.Canvas.Texts.Where(t => t.Color == 0xED737B && t.Value.Contains(',')).ToArray();
                Check(highlighted.Length == 1 && highlighted[0].Value == green, "only the single changed green is highlighted");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.emptyValue")) && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.textPage", 1, 1)),
                    "addition has an empty local side and needs one text page");
                Check(ui.View.Document.ContentEquals(before), "reading timing differences preserves authoring");
                ui.ClickText(L.Get("sync.chooseExternal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(!ui.View.SynchronizationVisible && ui.View.Document.TimingPoints.Count == before.TimingPoints.Count + 1
                    && ui.View.Document.Tracks.Single().Id == track.Id, "apply transfers one timing addition and keeps editable curves");
                var shifted = OsuBeatmapReader.ReadFile(source);
                foreach (var point in shifted.TimingPoints) point.TimeMs += 12;
                File.WriteAllText(source, OsuBeatmapWriter.Serialize(shifted).Text);
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.timingOffsetSummary", shifted.TimingPoints.Count, 12))
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.timingOffsetSummary", shifted.TimingPoints.Count, 0)),
                    "uniform offset shows one localized summary per side");
                Check(!ui.Canvas.Texts.Any(t => System.Text.RegularExpressions.Regex.IsMatch(t.Value, @"^-?\d+(?:\.\d+)?,")), "pure offset does not fill the panes with green lines");
                ui.ClickText(L.Get("sync.chooseLocal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(ui.View.Document.TimingPoints.Count == before.TimingPoints.Count + 1, "offset summary retains the FA choice");
            }
            finally { ui.View.StopFileMonitoring(); }
        }
        L.SetLanguage("zh-CN");
    }

    public static void SectionText()
    {
        string unicode = new string('a', 4095) + "🍎" + new string('b', 4094) + "🍊";
        Check(string.Concat(Enumerable.Range(0, 3).Select(i => FruitsAtelier.App.Editor.EditorView.SyncSectionSlice(unicode, i))) == unicode,
            "text pages preserve surrogate pairs at boundaries");
        foreach (string language in L.AvailableLanguages)
        {
            L.SetLanguage(language);
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/section-text", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, Fixture);
            var ui = new Ui(false); ui.Resize(980, 700);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
            var session = LibraryOperations.ImportPath(source, ui.View.LibrarySettings); ui.View.LoadWorkspace(session); Wait(ui);
            try
            {
                File.WriteAllText(source, Fixture.Replace("[HitObjects]", "[Events]\n//Break Periods\n//Storyboard\n\n[HitObjects]"));
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(!ui.View.SynchronizationVisible, "comment-only Events save needs no review");
                string events = "Sprite,Foreground,Centre,\"sprite.png\",320,240\n"
                    + string.Concat(Enumerable.Repeat(" F,0,0,500,0,1\n// storyboard command 012345678901234567890123456789\n", 70000)) + "// END_STORYBOARD";
                File.WriteAllText(source, Fixture.Replace("[HitObjects]", "[Events]\n" + events + "\n[HitObjects]"));
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.View.SynchronizationVisible && ui.Canvas.Texts.Any(t => t.Value == "Events/"), "external Events change opens text review");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.resultPreview")), "sections use text instead of object preview");
                var before = ui.View.Document.DeepClone();
                long allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 30; i++) ui.Paint();
                Console.WriteLine($"Section text warm render: {watch.Elapsed.TotalMilliseconds / 30:F2} ms, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 30} bytes/frame; {events.Length} characters");
                Check(ui.Canvas.Texts.Count < 150, "only visible text is submitted for multi-megabyte Events");
                ui.ClickText("»");
                ui.View.Wheel(200, 220, -120000, false); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value.Contains("END_STORYBOARD")), "last page exposes final storyboard text");
                ui.ClickText("«");
                Check(ui.View.Document.ContentEquals(before), "paging and scrolling do not edit content");
                ui.ClickText(L.Get("sync.chooseExternal"));
                ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(string.Join('\n', ui.View.Document.OriginalSections.Single(s => s.Name == "Events").Lines).Contains(events), "applying a text page retains the complete section");
                Check(ui.View.Document.Fruits[0].Id == before.Fruits[0].Id, "text merge retains object identity");
            }
            finally { ui.View.StopFileMonitoring(); }
        }
        L.SetLanguage("zh-CN");
    }

    public static void FailureText()
    {
        foreach (string language in L.AvailableLanguages)
        foreach (int width in new[] { 980, 1440 })
        {
            L.SetLanguage(language);
            var ui = new Ui(false); ui.Resize(width, 700);
            var type = ui.View.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            string message = string.Join("\n", Enumerable.Repeat(L.Get("core.writer.incompletePrefix") + L.Get("core.sliderGeometry.horizontalSpeed"), 30)) + "\nEND";
            type.GetField("syncFailure", flags)!.SetValue(ui.View, message);
            type.GetField("syncPage", flags)!.SetValue(ui.View, "failed");
            var before = ui.View.Document.DeepClone();
            ui.Paint();
            var body = (FruitsAtelier.App.Rendering.Rect)type.GetField("syncCanvasBounds", flags)!.GetValue(ui.View)!;
            var rows = ui.Canvas.Texts.Where(t => t.Color == 0xFF7F8D).ToArray();
            Check(rows.Length > 1 && rows.All(t => ((FruitsAtelier.App.Rendering.ICanvas)ui.Canvas).MeasureText(t.Value, 15) <= body.Width), "failure text wraps inside the dialog");
            ui.View.Wheel(body.X + 10, body.Y + 10, -12000, false); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == "END" && t.Y < body.Bottom), "failure text scrolls to the final diagnostic");
            Check(ui.View.Document.ContentEquals(before), "reading diagnostics preserves the document");
            ui.ClickText(L.Get("mac.cancel"));
            Check(!ui.View.SynchronizationVisible, "wrapped diagnostics retain the Cancel action");
        }
        L.SetLanguage("zh-CN");
    }

    public static void DeleteLocalVersion()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/delete-local", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!); File.WriteAllText(source, Fixture);
        var ui = new Ui(false); ui.Resize(1440, 900);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        var session = LibraryOperations.ImportPath(source, ui.View.LibrarySettings);
        ui.View.LoadWorkspace(session); Wait(ui);
        ui.View.ChangeAudioPath("local-edit.ogg");
        var edited = ui.View.Document.DeepClone();
        ui.View.ShowDeleteDifficulty(0, localOnly: true); ui.Paint();
        ui.ClickText(L.Get("mac.cancel")); Wait(ui);
        Check(ui.View.Document.ContentEquals(edited), "cancelling local deletion preserves edits");
        ui.View.ShowDeleteDifficulty(0, localOnly: true); ui.Paint();
        ui.ClickText(L.Get("sync.deleteLocal")); Wait(ui);
        Check(!ui.View.SynchronizationVisible && ui.View.Document.AudioPath is null && File.ReadAllText(source) == Fixture,
            "deleting the local version reimports without modifying osu");
        Check(ui.View.DifficultyCount == 1 && WorkspaceProject.Open(session.Directory).Project.Difficulties[0].Document.AudioPath is null,
            "reimport regenerates the persisted difficulty even for a single-difficulty project");
        ui.View.ChangeAudioPath("keep-on-failure.ogg"); edited = ui.View.Document.DeepClone();
        File.Delete(source);
        ui.View.ShowDeleteDifficulty(0, localOnly: true); ui.Paint();
        ui.ClickText(L.Get("sync.deleteLocal")); Wait(ui);
        Check(ui.View.Document.ContentEquals(edited) && File.Exists(Path.Combine(session.Directory, session.Manifest.Difficulties[0].File)),
            "missing source cannot discard local authoring");
        ui.View.StopFileMonitoring();
    }

    public static void MetadataRows()
    {
        var suffix = FruitsAtelier.App.Editor.MetadataTextDiff.Compare("same tags", "same tags aaa");
        Check(suffix.Local.Single().Length == 0 && suffix.External.Single() == new FruitsAtelier.App.Editor.MetadataTextSpan(9, 4), "suffix highlights only added text and the opposite insertion marker");
        Check(FruitsAtelier.App.Editor.MetadataTextDiff.Compare("same", "same").Local.Count == 0, "equal text has no highlight");
        var separated = FruitsAtelier.App.Editor.MetadataTextDiff.Compare("A same B", "X same Y");
        Check(separated.Local.Count == 2 && separated.External.Count == 2, "separate edits preserve unchanged middle text");
        foreach (int width in new[] { 980, 1440 })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/metadata-rows", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "map.osu");
            Directory.CreateDirectory(set); File.WriteAllText(source, Fixture);
            var ui = new Ui(false); ui.Resize(width, 900);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
            var session = LibraryOperations.ImportPath(source, ui.View.LibrarySettings); ui.View.LoadWorkspace(session);
            try
            {
                File.WriteAllText(source, Fixture.Replace("Title:Original", "Title:Original added").Replace("Artist:Artist", "Artist:Artist changed"));
                ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith("Title · ")) && ui.Canvas.Texts.Any(t => t.Value.StartsWith("Artist · ")), "metadata differences share one page");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.page", 1, 1)), "metadata uses a single navigation page");
                Check(ui.Canvas.Texts.Any(t => t.Value == " added" && t.Color == 0xED737B), "only changed suffix is red");
                var before = ui.View.Document.DeepClone();
                SelectSyncField(ui, "Title", true);
                Check(ui.Canvas.Texts.Any(t => t.Value == " added" && t.Color == 0x70D69B), "selected row turns green without disappearing");
                SelectSyncField(ui, "Title", false); SelectSyncField(ui, "Artist", true);
                Check(ui.View.Document.ContentEquals(before), "row choices do not edit before Apply");
                ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Original" && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Artist") == "Artist changed", "each row retains its selected side");
                ui.View.RefreshSynchronization(reviewResolved: true); Wait(ui);
                Check(ui.Canvas.Texts.Any(t => t.Value == " added" && t.Color == 0xD5A34D), "previously resolved mismatch is amber");
                SelectSyncField(ui, "Title", true); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Original added", "accepted metadata can be resolved again");
                File.WriteAllText(source, File.ReadAllText(source).Replace("Version:Catch", "Version:Catch\nTags:" + string.Join(" ", Enumerable.Repeat("long wrapped metadata", 100))));
                ui.View.RefreshSynchronization(); Wait(ui);
                float tagY = ui.Canvas.Texts.Single(t => t.Value.StartsWith("Tags · ")).Y;
                ui.View.Wheel(200, 260, -120, false); ui.Paint();
                Check(ui.Canvas.Texts.Single(t => t.Value.StartsWith("Tags · ")).Y < tagY, "wheel scrolls the metadata page");
                ui.View.Wheel(200, 260, 120, false); ui.Paint();
                Check(ui.Canvas.Texts.Single(t => t.Value.StartsWith("Tags · ")).Y == tagY, "metadata scroll returns to the original row position");
            }
            finally { ui.View.StopFileMonitoring(); }
        }
    }

    public static void ArScaleAndDecisions()
    {
        foreach (int ar in new[] { 1, 5, 9 })
        foreach (int width in new[] { 980, 1440 })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-ar", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "map.osu");
            string text = Fixture.Replace("ApproachRate:5", "ApproachRate:" + ar) + "200,192,1200,1,0,0:0:0:0:\n";
            Directory.CreateDirectory(set); File.WriteAllText(source, text);
            var ui = new Ui(false); ui.Resize(width, 1100);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
            var session = LibraryOperations.ImportPath(source, ui.View.LibrarySettings); ui.View.LoadWorkspace(session);
            File.WriteAllText(source, text.Replace("123,192", "321,192").Replace("200,192", "400,192"));
            ui.View.RefreshSynchronization(); Wait(ui);
            var field = ui.Canvas.Clips[2];
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
            Page(ui, false);
            ui.ClickText(L.Get("sync.chooseExternal"));
            Check(ui.View.Document.Fruits[0].X == 123, "changing a green decision does not apply it early");
            Page(ui, false);
            ui.ClickText(L.Get("sync.chooseLocal"));
            ui.ClickText(L.Get("sync.chooseLocal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
            Check(!ui.View.SynchronizationVisible, "retained differences do not repeat");
            File.WriteAllText(source, text.Replace("123,192", "333,192").Replace("200,192", "400,192"));
            ui.View.RefreshSynchronization(); Wait(ui);
            Check(ui.View.SynchronizationVisible && ui.Canvas.Texts.Any(t => t.Y == 122 && t.Value.Contains(L.Get("sync.unresolvedRange"))), "later external edit is a normal unresolved conflict");
            Check(ui.Canvas.Circles.Count(c => !c.Filled && c.Color == 0xED737B) == 2, "both corresponding objects remain highlighted");
            Check(ui.Canvas.Outlines.Any(o => o.Color == 0xD5A34D), "unchanged prior resolution has an amber interval");
            ui.ClickText(L.Get("sync.chooseLocal")); Page(ui, true);
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
                Check(ui.View.SynchronizationVisible && ui.Canvas.Circles.Count == 0, "one-sided metadata change opens text comparison");
                SelectSyncField(ui, "Title", true); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(!ui.View.SynchronizationVisible && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Updated", "metadata choice applies");
                Check(ui.View.Document.Fruits[0].Id == original && ui.View.Document.AudioPath!.EndsWith("pending.ogg"), "authoring preserved");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.AudioPath is null && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Updated", "undo retains chosen metadata");
                File.Delete(source);
                CheckSearchingInput(ui);
                Check(!ui.View.SynchronizationVisible && ui.View.DifficultySyncState(0) == WorkspaceSyncState.Missing, "missing reference leaves editing available");
                ui.View.RefreshSynchronization(reviewResolved: true); Wait(ui);
                Check(ui.View.SynchronizationVisible, "explicit review offers missing reference repair");
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
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith("Title")) && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.page", 1, 1)), "legacy first category and group count shown");
                Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.newerSaved")) && t.X > size.Item1 / 2), "external timestamp marks newer saved version");
                string authoringFile = Path.Combine(legacy.Directory, legacy.Manifest.Difficulties[0].File);
                DateTime savedTime = File.GetLastWriteTimeUtc(authoringFile);
                File.SetLastWriteTimeUtc(source, savedTime.AddMinutes(-1)); ui.View.RefreshSynchronization(); Wait(ui);
                Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.newerSaved")) && t.X < size.Item1 / 2), "FA can be the newer saved version");
                File.SetLastWriteTimeUtc(source, savedTime); ui.View.RefreshSynchronization(); Wait(ui);
                Check(!ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.newerSaved"))), "equal timestamps do not invent a newer side");
                SelectSyncField(ui, "Title", true); ui.ClickText("Objects"); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.page", 1, 1)), "Objects tab opens object conflicts");
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
                ui.ClickText("Metadata"); ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith("Title")), "previous choice can be reviewed");
                ui.ClickText("Objects"); ui.ClickText(L.Get("sync.chooseLocal"));
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

    public static void FileNotifications()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-watch", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "map.osu");
        Directory.CreateDirectory(set); File.WriteAllText(source, Fixture);
        var ui = new Ui(false); ui.Resize(1440, 900);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.LoadWorkspace(LibraryOperations.ImportPath(source, ui.View.LibrarySettings));
        ui.View.EnableFileMonitoring();
        void Until(Func<bool> condition, string message) => WorkspaceFileMonitorTests.Await(() => { ui.Paint(); return condition(); }, message);
        try
        {
            Guid identity = ui.View.Document.Fruits[0].Id;
            File.WriteAllText(source, Fixture.Replace("Mode:2", "Mode:2\nPreviewTime:1000"));
            Until(() => ui.View.SynchronizationVisible && !ui.View.SynchronizationBusy, "file notification opens field text review");
            Check(OsuBeatmapReader.Setting(ui.View.Document, "General", "PreviewTime") is null, "field review waits for a choice");
            SelectSyncField(ui, "PreviewTime", true); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
            Check(ui.View.Document.Fruits[0].Id == identity && OsuBeatmapReader.Setting(ui.View.Document, "General", "PreviewTime") == "1000", "field synchronization preserves authoring");
            string renamed = Path.Combine(songs, "renamed set"); Directory.Move(set, renamed); source = Path.Combine(renamed, "map.osu");
            Until(() => ui.View.WorkspaceSession!.Manifest.Difficulties[0].Source == source && !ui.View.SynchronizationBusy, "directory rename recovered automatically");
            string second = Path.Combine(renamed, "second.osu");
            File.WriteAllText(second, Fixture.Replace("Version:Catch", "Version:Second").Replace("123,192", "222,192"));
            Until(() => ui.View.DifficultyCount == 2 && !ui.View.SynchronizationBusy, "new difficulty imported automatically");
            File.WriteAllText(second, File.ReadAllText(second).Replace("222,192", "333,192"));
            Until(() => ui.View.DifficultySyncState(1) == WorkspaceSyncState.Changed && !ui.View.SynchronizationBusy, "inactive conflict detected");
            Check(!ui.View.SynchronizationVisible, "inactive conflict does not interrupt active map");
            Check(!ui.View.SwitchDifficulty(1) && ui.View.SynchronizationVisible, "entering conflicted difficulty opens merge");
            ui.Paint();
            ui.ClickText(L.Get("sync.allLocal")); Wait(ui);
            ui.View.SwitchDifficulty(0); ui.Paint();
            var capture = typeof(FruitsAtelier.App.Editor.EditorView).GetField("tabPointer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            capture.SetValue(ui.View, true);
            File.WriteAllText(source, File.ReadAllText(source).Replace("123,192", "321,192"));
            var pauseUntil = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < pauseUntil) { ui.Paint(); Thread.Sleep(20); }
            Check(!ui.View.SynchronizationVisible, "notification waits for pointer release");
            capture.SetValue(ui.View, false);
            Until(() => ui.View.SynchronizationVisible, "active conflict opens after interaction");
            ui.ClickText(L.Get("sync.chooseLocal"));
            File.WriteAllText(source, File.ReadAllText(source).Replace("321,192", "400,192"));
            pauseUntil = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < pauseUntil) { ui.Paint(); Thread.Sleep(20); }
            Check(ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("sync.resolvedThisRound"))), "notifications preserve ongoing review choices");
            ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
            Check(ui.View.SynchronizationVisible && ui.View.Document.Fruits[0].X == 123, "apply rechecks changed external version without applying stale choice");
            ui.ClickText(L.Get("sync.chooseExternal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
            Check(ui.View.Document.Fruits[0].X == 400, "refreshed review accepts latest version");
            byte[] updating = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(source).Replace("PreviewTime:1000", "PreviewTime:2000"));
            using (var writing = new FileStream(source, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writing.Write(updating); writing.Flush();
                pauseUntil = DateTime.UtcNow.AddMilliseconds(1400);
                while (DateTime.UtcNow < pauseUntil) { ui.Paint(); Thread.Sleep(20); }
                Check(!ui.View.SynchronizationVisible, "temporarily locked source retries without opening merge");
            }
            Until(() => ui.View.SynchronizationVisible && !ui.View.SynchronizationBusy,
                "locked source synchronizes after writer closes without another write notification");
            SelectSyncField(ui, "PreviewTime", true); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
            Check(OsuBeatmapReader.Setting(ui.View.Document, "General", "PreviewTime") == "2000", "retried field applies after review");
            ui.View.ShowLibrary(); ui.Paint();
            string newSet = Path.Combine(songs, "new set"); Directory.CreateDirectory(newSet);
            File.WriteAllText(Path.Combine(newSet, "new.osu"), Fixture.Replace("Title:Original", "Title:New set"));
            Until(() => ui.View.LibrarySetTotal >= 2 && !ui.View.LibraryLoading, "new set updates library through notification");
            Check(ui.View.LibraryVisible && !ui.View.SynchronizationVisible, "library discovery does not open merge");
        }
        finally { ui.View.StopFileMonitoring(); }
    }

    public static void RefreshedReviewChoices()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/sync-review-refresh", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "map.osu");
        string text = Fixture + "200,192,1200,1,0,0:0:0:0:\n";
        Directory.CreateDirectory(set); File.WriteAllText(source, text);
        var ui = new Ui(false); ui.Resize(1440, 900);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.LoadWorkspace(LibraryOperations.ImportPath(source, ui.View.LibrarySettings));
        File.WriteAllText(source, text.Replace("123,192", "321,192").Replace("200,192", "400,192"));
        ui.View.RefreshSynchronization(); Wait(ui);
        ui.ClickText(L.Get("sync.chooseLocal")); ui.ClickText(L.Get("sync.chooseLocal"));
        File.WriteAllText(source, text.Replace("123,192", "321,192").Replace("200,192", "450,192"));
        ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
        Check(ui.View.Document.Fruits[0].X == 123 && ui.View.Document.Fruits[1].X == 200, "stale review does not publish any choices");
        Check(ui.Canvas.Outlines.Any(o => o.Color == 0x70D69B) && ui.Canvas.Outlines.Any(o => o.Color == 0xED737B),
            "unchanged choice survives refresh while changed group becomes unresolved");
        Page(ui, true); ui.ClickText(L.Get("sync.chooseExternal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
        Check(ui.View.Document.Fruits[0].X == 123 && ui.View.Document.Fruits[1].X == 450, "refreshed choices merge correctly");
    }

    public static void ProjectLibraryChanges()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/project-library-watch", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set");
        Directory.CreateDirectory(set);
        string Text(int n) => Fixture.Replace("Version:Catch", "Version:Diff " + n).Replace("123,192", (100 + n * 20) + ",192");
        string FilePath(int n) => Path.Combine(set, n + ".osu");
        for (int n = 1; n <= 3; n++) File.WriteAllText(FilePath(n), Text(n));
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), Songs = songs };
        var session = LibraryOperations.ImportPath(FilePath(1), settings);
        var authoring = Directory.GetFiles(session.Directory).ToDictionary(p => p, WorkspaceProject.Hash);
        var ui = new Ui(false); ui.Resize(1440, 900); ui.View.InitializeLibrary(true, settings);
        void Until(Func<bool> predicate, string message) => WorkspaceFileMonitorTests.Await(() =>
        { ui.Paint(); return predicate() && !ui.View.LibraryLoading; }, message);
        bool Has(string value) => ui.Canvas.Texts.Any(t => t.Value == value);
        try
        {
            Until(() => ui.View.LibrarySetTotal == 1, "initial library ready");
            ui.ClickText(L.Get("library.projects"));
            Until(() => Has(L.Get("library.projectCount", 3)) && Has("Diff 3"), "project shows saved difficulties");
            File.WriteAllText(FilePath(4), Text(4)); File.WriteAllText(FilePath(5), Text(5));
            Until(() => Has(L.Get("library.projectCount", 5)) && Has("Diff 4") && Has("Diff 5"), "project count and details refresh from three to five");
            File.Delete(FilePath(5));
            Until(() => Has(L.Get("library.projectCount", 4)) && !Has("Diff 5"), "unimported deletion updates count and details");
            File.Delete(FilePath(1));
            Until(() => Has(L.Get("library.projectMissingCount", 3, 1)) && Has(L.Get("sync.missingBadge")), "linked deletion remains a missing authoring row");
            Check(ui.Canvas.Texts.Any(t => t.Value == "Diff 1" && t.Color == 0x718092u), "missing difficulty is muted");
            File.WriteAllText(Path.Combine(set, "renamed.osu"), Text(1));
            Until(() => Has(L.Get("library.projectCount", 4)) && !Has(L.Get("sync.missingBadge")), "background discovery finds renamed reference without opening project");
            string moved = Path.Combine(songs, "moved set"); Directory.Move(set, moved); set = moved;
            Until(() => Has(L.Get("library.projectCount", 4)) && Has("Diff 4") && !Has(L.Get("sync.missingBadge")), "moved project still includes newly discovered difficulties");
            Check(ui.View.LibraryVisible && ui.View.WorkspaceSession is null && !ui.View.SynchronizationVisible, "library refresh never opens project or merge");
            Check(authoring.All(p => WorkspaceProject.Hash(p.Key) == p.Value), "library discovery does not rewrite authoring or baselines");
            var db = new LibraryDatabase(settings.Workspace, songs); db.Scan();
            using var snapshot = db.SearchSnapshot("", projectsOnly: true);
            var queue = (SemaphoreSlim)typeof(LibrarySearchSnapshot).GetField("discoveryQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(snapshot)!;
            queue.Wait();
            try
            {
                var firstPage = Task.Run(() => snapshot.Page(0));
                Check(firstPage.Wait(3000) && firstPage.Result.Count == 1, "indexed project is visible while reference discovery is blocked");
                Check(snapshot.Difficulties(firstPage.Result.Single(), 0).Any(d => d.ReferenceSearching), "pending reference search has a visible state");
            }
            finally { queue.Release(); }
            Check(SpinWait.SpinUntil(() => snapshot.DiscoveryRevision > 0, 10000), "reference lookup completes");
            var row = snapshot.Page(0).Single(); var details = snapshot.Difficulties(row, 0);
            Check(row.Count == 4 && row.InSongs == true && details.Count == 4, "fresh snapshot recovers entire renamed directory without duplicates");
            File.WriteAllText(Path.Combine(set, "ambiguous-copy.osu"), Text(1)); db.Scan();
            using var ambiguous = db.SearchSnapshot("", projectsOnly: true);
            ambiguous.Page(0);
            Check(SpinWait.SpinUntil(() => ambiguous.DiscoveryRevision > 0, 10000), "ambiguous lookup completes");
            var uncertain = ambiguous.Page(0).Single();
            Check(uncertain.MissingCount == 1 && ambiguous.Difficulties(uncertain, 0).Single(d => d.Difficulty == "Diff 1" && d.Path.EndsWith(".catchdiff")).ExternalMissing,
                "ambiguous copies do not silently relink the retained FA difficulty");
        }
        finally { ui.View.StopFileMonitoring(); }
    }

    public static void ProjectLibraryExportSources()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/project-library-export", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "original.osu");
        Directory.CreateDirectory(set); File.WriteAllText(source, Fixture);
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), Songs = songs };
        var session = LibraryOperations.ImportPath(source, settings);
        string exported = Path.Combine(set, "exported.osu"); File.WriteAllText(exported, Fixture);
        session.Manifest.Difficulties[0].ExportTarget = exported;
        WorkspaceProject.Save(session, session.Project);
        var db = new LibraryDatabase(settings.Workspace, songs); db.Scan();
        using var snapshot = db.SearchSnapshot("", projectsOnly: true);
        var row = snapshot.Page(0).Single(); var details = snapshot.Difficulties(row, 0);
        Check(row.Count == 2 && details.Count(d => d.Path.EndsWith(".catchdiff")) == 1 && details.Any(d => d.Path == source),
            "separate live import source remains visible beside its FA exported target");
    }

    public static void DeletedCopiesRemainMissing()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/deleted-copies-ui", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "set"), source = Path.Combine(set, "Platter.osu");
        Directory.CreateDirectory(set); File.WriteAllText(source, Fixture.Replace("Version:Catch", "Version:Platter"));
        foreach (string name in new[] { "Test", "Test2" })
            File.WriteAllText(Path.Combine(set, name + ".osu"), Fixture.Replace("Version:Catch", "Version:" + name));
        var ui = new Ui(false); ui.Resize(1440, 900);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
        var session = LibraryOperations.Open(LibraryDatabase.ReadMetadata(source)!, ui.View.LibrarySettings);
        File.Delete(Path.Combine(set, "Test.osu")); File.Delete(Path.Combine(set, "Test2.osu"));
        ui.View.LoadWorkspace(WorkspaceProject.Open(session.Directory), checkAdditionalDifficulties: true); Wait(ui);
        Check(!ui.View.SynchronizationVisible && ui.View.DifficultyCount == 3, "opening after deleting copies does not prompt duplicate cleanup or discard authoring");
        var project = ui.View.CaptureProject();
        foreach (string name in new[] { "Test", "Test2" })
            Check(ui.View.DifficultySyncState(project.Difficulties.FindIndex(d => d.Name == name)) == WorkspaceSyncState.Missing, "deleted copies are missing");
        int deleted = project.Difficulties.FindIndex(d => d.Name == "Test");
        ui.View.ShowDeleteDifficulty(deleted); ui.Paint();
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.state.Duplicate")), "deleted-copy cleanup does not list unrelated surviving difficulties");
        ui.ClickText(L.Get("sync.delete")); Wait(ui);
        Check(File.Exists(source) && ui.View.DifficultyCount == 2 && ui.View.CaptureProject().Difficulties.Any(d => d.Name == "Platter"),
            "removing the orphan FA copy preserves the surviving osu file and authoring");
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

    private static void CheckSearchingInput(Ui ui)
    {
        ui.View.RefreshSynchronization();
        var field = typeof(FruitsAtelier.App.Editor.EditorView).GetField("syncTask",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var scan = field.GetValue(ui.View)!;
        var gateType = typeof(TaskCompletionSource<>).MakeGenericType(field.FieldType.GenericTypeArguments[0]);
        var gate = Activator.CreateInstance(gateType)!;
        field.SetValue(ui.View, gateType.GetProperty("Task")!.GetValue(gate));
        var original = ui.View.Document.DeepClone();
        try
        {
            ui.Paint();
            Check(!ui.View.DiscardConfirmationVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.searchingReference")), "reference search is indicated on its tab without input lock");
            Check(!ui.Canvas.Texts.Any(t => t.Value.StartsWith(L.Get("library.missingResources", ""))), "no persistent resource error banner");
            ui.Key('A', ctrl: true); ui.Key(46);
            Check(ui.View.Document.Fruits.Count == 0, "keyboard edits work during search");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(original), "undo works during search");
            ui.View.ChangeAudioPath(Path.GetFullPath("artifacts/search-edit.ogg"));
        }
        finally { field.SetValue(ui.View, scan); }
        Wait(ui);
        Check(ui.View.Document.AudioPath!.EndsWith("search-edit.ogg"), "completed search rebases against edits instead of replacing them");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(original), "search retains edit history");
    }
    private static void Page(Ui ui, bool next)
    {
        var label = ui.Canvas.Texts.Single(t => t.X == 116 && System.Text.RegularExpressions.Regex.IsMatch(t.Value, @"^\d+ / \d+$"));
        ui.Click(next ? 254 : 54, label.Y + 10);
    }

    private static void SelectSyncField(Ui ui, string key, bool external)
    {
        ui.Paint();
        var row = ui.Canvas.Texts.Single(t => t.Value.StartsWith(key + " · "));
        var heading = ui.Canvas.Texts.Single(t => t.Value == (external ? "osu!" : "FA"));
        ui.Click(heading.X + 20, row.Y + 40);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private const string Fixture = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Original\nArtist:Artist\nCreator:Mapper\nVersion:Catch\n[Difficulty]\nCircleSize:5\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n123,192,1000,1,0,0:0:0:0:\n";
}
