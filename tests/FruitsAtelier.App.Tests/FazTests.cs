using System.IO.Compression;
using System.Reflection;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class FazTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/faz", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var source = Settings("sender"); Directory.CreateDirectory(source.Songs);
        string folder = Path.Combine(source.Songs, "Fixture"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "audio.wav"), new byte[44]);
        File.WriteAllText(Path.Combine(folder, "bg.jpg"), "background fixture");
        File.WriteAllText(Path.Combine(folder, "normal-hitnormal.wav"), "hitsound fixture");
        string map = Path.Combine(folder, "map.osu");
        File.WriteAllText(map, "osu file format v14\n[General]\nAudioFilename:audio.wav\nMode:2\n[Metadata]\nTitle:Song\nArtist:Artist\nCreator:Mapper\nVersion:Hard\nBeatmapID:123\nBeatmapSetID:456\n[Difficulty]\nCircleSize:4\nOverallDifficulty:5\nApproachRate:8\nHPDrainRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[Events]\n0,0,\"bg.jpg\",0,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
        var project = BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(map)]);
        var curve = new CurveTrack { CompensateTinyDroplets = false };
        curve.Nodes.AddRange([new() { TimeMs = 3000, X = 100, HandleOut = new(250, 15) }, new() { TimeMs = 4000, X = 240, HandleIn = new(-250, -15) }]);
        project.Difficulties[0].Document.Tracks.Add(curve);
        project.Difficulties[0].Document.DurationMs = 10000;
        project.Notes = "Native notes";
        string before = ProjectSerializer.Serialize(project);
        string native = Path.Combine(root, "native.faz"), full = Path.Combine(root, "complete.faz");
        FazArchive.Export(project, native, false, false, source.Workspace);
        FazArchive.Export(project, full, true, false, source.Workspace);
        Check(ProjectSerializer.Serialize(project) == before, "export preserves live source");
        using (var zip = ZipFile.OpenRead(native))
        {
            Check(zip.GetEntry("beatmap.osz") is null, "native package omits OSZ");
            using var reader = new StreamReader(zip.GetEntry("project.catchproj")!.Open());
            var portable = ProjectSerializer.ReadProject(reader.ReadToEnd());
            Check(portable.Difficulties.All(d => !d.Document.SourcePath!.Contains('\\') && !d.Document.AudioPath!.Contains('\\')), "package references are portable across Windows and macOS");
        }
        using (var zip = ZipFile.OpenRead(full)) Check(zip.GetEntry("beatmap.osz") is not null, "complete package contains OSZ");

        var independent = Settings("independent");
        using (var package = FazArchive.Prepare(native, independent))
        {
            var session = FazArchive.Import(package, independent, new string?[] { null }, false, false);
            Check(session.Manifest.Difficulties[0].Source is null, "independent has no synchronization target");
            Check(File.Exists(session.Project.Difficulties[0].Document.AudioPath), "independent audio exists");
            Check(WorkspaceProject.MissingResources(session.Project).Count == 0, "independent resource references resolve");
            Check(session.Project.Notes == project.Notes && session.Project.Difficulties[0].Document.Fruits[0].X == 100, "authoring roundtrip");
            Check(session.Project.Difficulties[0].Document.Tracks[0].Nodes[0].HandleOut == curve.Nodes[0].HandleOut, "editable curve handles survive import");
            WorkspaceProject.Save(session, session.Project);
            using var repeat = FazArchive.Prepare(native, independent);
            Check(repeat.ExistingProject == session.Directory, "repeat import remains detectable after save");
        }

        var complete = Settings("complete"); Directory.CreateDirectory(complete.Songs);
        using (var package = FazArchive.Prepare(full, complete))
        {
            var session = FazArchive.Import(package, complete, new string?[] { null }, true, false);
            Check(File.Exists(session.Manifest.Difficulties[0].Source), "complete installs Songs map");
            Check(!Directory.Exists(Path.Combine(complete.Workspace, "Resources")), "complete shares installed Songs resources");
            Check(OsuBeatmapReader.ReadFile(session.Manifest.Difficulties[0].Source!).Fruits.Count == 1, "Songs map opens");
            Check(WorkspaceProject.Open(session.Directory).Project.Difficulties.Count == 1, "FA project opens");
            Check(session.Manifest.Difficulties[0].Sync is not null, "newly installed map has matching authoring baseline");
        }

        var linked = Settings("linked"); Directory.CreateDirectory(linked.Songs);
        string targetFolder = Path.Combine(linked.Songs, "Existing"); Directory.CreateDirectory(targetFolder);
        string target = Path.Combine(targetFolder, "local.osu");
        File.Copy(map, target); File.Copy(Path.Combine(folder, "audio.wav"), Path.Combine(targetFolder, "audio.wav"));
        string hash = WorkspaceProject.Hash(target);
        using (var package = FazArchive.Prepare(native, linked))
        {
            Check(FazArchive.Recommend(package.Project.Difficulties[0], package.Candidates[0]) == target, "ID recommendation");
            var session = FazArchive.Import(package, linked, new[] { target }, false, false);
            Check(!Directory.Exists(Path.Combine(linked.Workspace, "Resources")), "linked imports no resources");
            Check(WorkspaceProject.Hash(target) == hash, "association does not write map");
            Check(session.Project.Difficulties[0].Document.AudioPath == Path.Combine(targetFolder, "audio.wav"), "uses target audio");
            Check(session.Manifest.Difficulties[0].Sync is null, "fresh association requires baseline review");
            var comparison = WorkspaceSynchronization.CompareWithoutBaseline(session.Project.Difficulties[0].Document, WorkspaceSynchronization.ReadStable(target), session.Directory, false);
            Check(comparison.RequiresResolution && !comparison.CanExportLocalChanges, "different authoring cannot automatically overwrite associated map");
            Throws(() => FazArchive.Import(package, linked, new[] { target }, false, false), "duplicate owner rejected");
        }
        foreach (bool equalFirst in new[] { false, true })
        {
            var reviewSettings = Settings("link-review-" + equalFirst);
            string reviewFolder = Path.Combine(reviewSettings.Songs, "Existing"); Directory.CreateDirectory(reviewFolder);
            string reviewMap = Path.Combine(reviewFolder, "local.osu");
            File.Copy(map, reviewMap); File.Copy(Path.Combine(folder, "audio.wav"), Path.Combine(reviewFolder, "audio.wav"));
            var reviewView = new EditorView(false);
            reviewView.LibrarySettings.Workspace = reviewSettings.Workspace; reviewView.LibrarySettings.Songs = reviewSettings.Songs;
            string reviewArchive = native;
            string[] reviewTargets = [reviewMap];
            if (equalFirst)
            {
                var two = ProjectSerializer.ReadProject(before);
                two.Difficulties[0].Name = "Second";
                SongSetup.Set(two.Difficulties[0].Document, "Metadata", "Version", "Second");
                SongSetup.Set(two.Difficulties[0].Document, "Metadata", "BeatmapID", "124");
                two.Difficulties.Insert(0, BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(map)]).Difficulties[0]);
                string second = Path.Combine(reviewFolder, "second.osu");
                File.WriteAllText(second, File.ReadAllText(map).Replace("BeatmapID:123", "BeatmapID:124").Replace("Version:Hard", "Version:Second"));
                reviewArchive = Path.Combine(root, "review-two.faz");
                FazArchive.Export(two, reviewArchive, false, false, source.Workspace);
                reviewTargets = [reviewMap, second];
            }
            var reviewPackage = FazArchive.Prepare(reviewArchive, reviewSettings);
            var reviewHashes = reviewTargets.ToDictionary(p => p, WorkspaceProject.Hash);
            typeof(EditorView).GetField("fazPackage", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reviewView, reviewPackage);
            typeof(EditorView).GetField("fazTargets", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reviewView, reviewTargets);
            typeof(EditorView).GetField("fazMode", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reviewView, 1);
            typeof(EditorView).GetMethod("ImportFaz", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(reviewView, new object?[] { null });
            var watch = System.Diagnostics.Stopwatch.StartNew(); var recording = new RecordingCanvas();
            do
            {
                reviewView.Render(recording, 980, 620); Thread.Sleep(10);
            } while (watch.Elapsed < TimeSpan.FromSeconds(30) && (reviewView.WorkspaceSession is null || reviewView.SynchronizationBusy));
            Check(typeof(EditorView).GetField("syncPage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reviewView) as string == "resolve", "link import immediately opens resolution for differing authoring");
            Check(reviewView.WorkspaceSession!.Manifest.Difficulties.Last().Sync is null, "conflicted first association does not silently establish a baseline");
            Check(reviewHashes.All(p => WorkspaceProject.Hash(p.Key) == p.Value), "review does not write any Songs difficulty before a user choice");
            Check(reviewView.CaptureProject().Difficulties.Last().Document.Tracks.Count == 1, "review retains imported editable curves before a choice");
            if (equalFirst)
            {
                var merges = (Dictionary<Guid, WorkspaceMerge>)typeof(EditorView).GetField("syncMerges", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reviewView)!;
                var choices = (Dictionary<string, bool>)typeof(EditorView).GetField("syncChoices", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reviewView)!;
                foreach (var conflict in merges[reviewView.WorkspaceSession!.Manifest.Difficulties.Last().Id].Conflicts) choices[conflict.Key] = false;
            }
            typeof(EditorView).GetMethod("ResolveSync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(reviewView, new object?[] { equalFirst ? null : (bool?)false });
            WaitForReview();
            Check(!reviewView.SynchronizationVisible, "linked resolution completes");
            var published = reviewView.WorkspaceSession!;
            var owner = published.Manifest.Difficulties.Last();
            Check(WorkspaceProject.Hash(owner.Source!) != reviewHashes[reviewTargets.Last()], "FA resolution writes the linked osu file");
            Check(OsuBeatmapReader.ReadFile(owner.Source!).ImportedSliders.Count > 0, "linked output contains the chosen FA curve");
            Check(owner.Sync!.LocalOverrides.Count == 0 && owner.Sync.RetainedObjects.Count == 0, "published baseline has no pending choices");
            if (equalFirst) Check(WorkspaceProject.Hash(reviewMap) == reviewHashes[reviewMap], "resolution leaves other difficulties unchanged");
            else
            {
                var diff = published.Project.Difficulties.Single();
                diff.Document.Fruits[0].X = 141;
                var candidate = WorkspaceSynchronization.ReadStable(owner.Source!);
                var oldReview = WorkspaceSynchronization.CompareWithoutBaseline(diff.Document, candidate, published.Directory, false);
                var oldChoices = oldReview.Conflicts.ToDictionary(c => c.Key, _ => false);
                WorkspaceSynchronization.Accept(published, owner, candidate, diff.Document, false, review: oldReview, choices: oldChoices);
                WorkspaceProject.Save(published, published.Project);
                reviewView.LoadWorkspace(WorkspaceProject.Open(published.Directory));
                reviewView.SaveCurrentDifficulty();
                WaitForReview();
                Check(OsuBeatmapReader.ReadFile(owner.Source!).Fruits[0].X == 141, "save publishes an earlier accepted choice without a new edit");
            }
            reviewView.StopFileMonitoring();
            void WaitForReview()
            {
                watch.Restart();
                do { reviewView.Render(recording, 980, 620); Thread.Sleep(10); }
                while (watch.Elapsed < TimeSpan.FromSeconds(30) && (reviewView.SynchronizationBusy
                    || typeof(EditorView).GetField("difficultySaveTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reviewView) is not null));
            }
        }

        var db = new LibraryDatabase(linked.Workspace, linked.Songs);
        var document = project.Difficulties[0].Document.DeepClone();
        SongSetup.Set(document, "Metadata", "BeatmapID", "0"); SongSetup.Set(document, "Metadata", "BeatmapSetID", "-1");
        Check(db.AssociationCandidates(document).Single().Path == target, "exact artist and title fallback");
        SongSetup.Set(document, "Metadata", "Title", "Son");
        Check(db.AssociationCandidates(document).Count == 0, "substring is not a match");
        var duplicate = db.AssociationCandidates(project.Difficulties[0].Document).Single();
        Check(FazArchive.Recommend(project.Difficulties[0], [duplicate, duplicate with { Path = target + "2" }]) is null, "ambiguous identity needs choice");

        var partial = Settings("partial"); Directory.CreateDirectory(partial.Songs);
        string partialFolder = Path.Combine(partial.Songs, "Local"); Directory.CreateDirectory(partialFolder);
        string partialMap = Path.Combine(partialFolder, "local.osu"); File.Copy(map, partialMap);
        File.Copy(Path.Combine(folder, "audio.wav"), Path.Combine(partialFolder, "audio.wav"));
        var pair = ProjectSerializer.ReadProject(before);
        pair.Difficulties.Add(new ProjectDifficulty { Name = "Second", Document = pair.Difficulties[0].Document.DeepClone() });
        SongSetup.Set(pair.Difficulties[1].Document, "Metadata", "BeatmapID", "124");
        SongSetup.Set(pair.Difficulties[1].Document, "Metadata", "Version", "Second");
        string mixed = Path.Combine(root, "mixed.faz"); FazArchive.Export(pair, mixed, false, false, source.Workspace);
        using (var package = FazArchive.Prepare(mixed, partial))
        {
            Check(package.Projects.Count == 0, "incomplete folder is not recommended");
            Throws(() => FazArchive.Import(package, partial, new string?[] { partialMap, null }, false, false), "partial association rejected");
            Throws(() => FazArchive.Import(package, partial, new[] { partialMap, partialMap }, false, false), "one map cannot satisfy two difficulties");
            string second = Path.Combine(partialFolder, "second.osu");
            File.WriteAllText(second, File.ReadAllText(map).Replace("BeatmapID:123", "BeatmapID:124").Replace("Version:Hard", "Version:Second"));
            string extra = Path.Combine(partialFolder, "extra.osu");
            File.WriteAllText(extra, File.ReadAllText(map).Replace("BeatmapID:123", "BeatmapID:125").Replace("Version:Hard", "Version:Extra"));
            var match = FazArchive.MatchProject(package, partial, partialFolder);
            Check(match is not null && match.DifficultyCount == 3 && match.Targets.SequenceEqual(new[] { partialMap, second }), "complete folder may contain extra difficulties");
            string other = Path.Combine(partial.Songs, "Other"); Directory.CreateDirectory(other);
            string split = Path.Combine(other, "second.osu"); File.Copy(second, split);
            Throws(() => FazArchive.Import(package, partial, new[] { partialMap, split }, false, false), "cross-folder association rejected");
            File.Delete(second);
            Throws(() => FazArchive.Import(package, partial, match!.Targets, false, false), "removed difficulty is revalidated before import");
            File.Copy(split, second);
            using var refreshed = FazArchive.Prepare(mixed, partial);
            Check(refreshed.Projects.Count == 1, "only complete project is recommended");
            var session = FazArchive.Import(refreshed, partial, refreshed.Projects[0].Targets, false, false);
            Check(session.Manifest.Difficulties.All(d => d.Source is not null), "whole project is associated");
            Check(!Directory.Exists(Path.Combine(partial.Workspace, "Resources")), "linked project installs no packaged resources");
        }

        string recoveryId = Guid.NewGuid().ToString("N");
        string abandoned = Path.Combine(partial.Workspace, "Abandoned [" + recoveryId[..8] + "]");
        string abandonedResources = Path.Combine(partial.Workspace, "Resources", "FAZ-" + recoveryId);
        Directory.CreateDirectory(abandonedResources); File.WriteAllText(Path.Combine(abandonedResources, "partial"), "partial");
        File.WriteAllText(Path.Combine(partial.Workspace, ".faz-import-" + recoveryId + ".json"), System.Text.Json.JsonSerializer.Serialize(new { Project = abandoned, Resources = abandonedResources, Song = (string?)null, Completed = false }));
        FazArchive.Recover(partial);
        Check(!Directory.Exists(abandonedResources), "interrupted unpublished import rolls back owned resources");

        string attack = Path.Combine(root, "traversal.faz");
        using (var zip = ZipFile.Open(attack, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("../escape").Open())) writer.Write("bad");
        Throws(() => { using var _ = FazArchive.Prepare(attack, independent); }, "traversal rejected");
        string corrupt = Path.Combine(root, "corrupt.faz"); File.Copy(native, corrupt);
        using (var zip = ZipFile.Open(corrupt, ZipArchiveMode.Update))
        { zip.GetEntry("project.catchproj")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("project.catchproj").Open()); writer.Write("{}"); }
        Throws(() => { using var _ = FazArchive.Prepare(corrupt, independent); }, "checksum failure rejected");

        var cleanupSettings = Settings("cleanup");
        using (var package = FazArchive.Prepare(native, cleanupSettings))
        {
            FazArchive.CleanupTemporaryFiles(cleanupSettings.Workspace);
            Check(Directory.Exists(package.Directory), "cleanup preserves active package staging");
            var session = FazArchive.Import(package, cleanupSettings, new string?[] { null }, false, false);
            string resources = Path.Combine(cleanupSettings.Workspace, "Resources", "FAZ-" + session.Manifest.Id.ToString("N"));
            WorkspaceSynchronization.Archive(session, "test");
            LibraryOperations.DeleteProject(session.Directory, cleanupSettings);
            Check(!Directory.Exists(resources), "default delete removes owned resources");
            Check(!Directory.Exists(Path.Combine(cleanupSettings.Workspace, ".sync-history", session.Manifest.Id.ToString("N"))), "default delete removes recovery history");
            session = FazArchive.Import(package, cleanupSettings, new string?[] { null }, false, false);
            resources = Path.Combine(cleanupSettings.Workspace, "Resources", "FAZ-" + session.Manifest.Id.ToString("N"));
            LibraryOperations.DeleteProject(session.Directory, cleanupSettings, keepBackup: true);
            Check(Directory.Exists(resources) && Directory.Exists(Path.Combine(cleanupSettings.Workspace, ".sync-history", session.Manifest.Id.ToString("N"))), "opt-in backup retains resources and history");
            session = FazArchive.Import(package, cleanupSettings, new string?[] { null }, false, false);
            resources = Path.Combine(cleanupSettings.Workspace, "Resources", "FAZ-" + session.Manifest.Id.ToString("N"));
            var shared = WorkspaceProject.Create(cleanupSettings.Workspace, session.Project, "");
            LibraryOperations.DeleteProject(session.Directory, cleanupSettings);
            Check(Directory.Exists(resources) && File.Exists(shared.Project.Difficulties[0].Document.AudioPath), "another project protects shared resources");
            LibraryOperations.DeleteProject(shared.Directory, cleanupSettings, keepBackup: true);
            var dependent = WorkspaceProject.Create(cleanupSettings.Workspace, shared.Project, "");
            LibraryOperations.DeleteProject(dependent.Directory, cleanupSettings);
            Check(Directory.Exists(resources), "another project's retained backup protects shared resources");
        }
        string stagingRoot = Path.Combine(cleanupSettings.Workspace, ".faz-staging");
        string abandonedStage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandonedStage); File.WriteAllText(Path.Combine(abandonedStage, "payload"), "temporary");
        string busyStage = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(busyStage);
        using (var lease = new FileStream(busyStage + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            FazArchive.CleanupTemporaryFiles(cleanupSettings.Workspace);
            Check(!Directory.Exists(abandonedStage) && Directory.Exists(busyStage), "cleanup reclaims abandoned staging and preserves externally leased staging");
        }
        FazArchive.CleanupTemporaryFiles(cleanupSettings.Workspace);
        Check(!Directory.Exists(stagingRoot), "cleanup removes released staging and empty cache root");

        var view = new EditorView(false); view.InitializeLibrary(true, independent);
        int opened = 0; view.RequestFazOpen = _ => opened++;
        Check(view.CanDropFile(native), "library accepts FAZ"); view.DropLibraryFiles([native]); Check(opened == 1, "FAZ drop dispatched");
        view.CloseLibrary(); Check(view.CanDropFile(native), "editor accepts FAZ");
        var pendingScan = new TaskCompletionSource<(LibraryDatabase Database, LibraryScan Scan)>(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(EditorView).GetField("scanTask", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, pendingScan.Task);
        Directory.CreateDirectory(independent.Songs);
        view.BeginFazOpen(native, () => { });
        var canvas = new RecordingCanvas();
        var taskField = typeof(EditorView).GetField("fazTask", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (taskField.GetValue(view) is not null && timer.Elapsed < TimeSpan.FromSeconds(30)) { view.Render(canvas, 980, 620); Thread.Sleep(10); }
        view.Render(canvas, 980, 620);
        Check(!view.ErrorVisible && view.DiscardConfirmationVisible, "prepared package shows modal");
        Check(taskField.GetValue(view) is null && !pendingScan.Task.IsCompleted, "archive opens without waiting for library scan");
        Check(typeof(EditorView).GetField("fazSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view) is Task { IsCompleted: false }, "association discovery waits for existing scan separately");
        Check(!view.PrepareFileOperation(), "import modal prevents competing file operations");
        view.KeyDown(27, false, false);
        Check(!view.DiscardConfirmationVisible, "escape cancels import while library scan remains pending");
        pendingScan.SetCanceled();
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            Check(MapFileDialog.FazFilter == "Fruits Atelier Project\0*.faz\0\0", "FAZ save filter names the document type");
            string registryPath = @"Software\FruitsAtelier.Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                using var classes = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath);
                Check(FazFileAssociation.EnsureRegistered(classes, @"C:\FA\FA.exe", @"C:\FA\faz.ico"), "missing association is registered");
                Check(!FazFileAssociation.EnsureRegistered(classes, @"C:\FA\FA.exe", @"C:\FA\faz.ico"), "valid association is unchanged");
                Check(FazFileAssociation.EnsureRegistered(classes, @"C:\Moved FA\FA.exe", @"C:\Moved FA\faz.ico"), "moved application registration is refreshed");
                using var command = classes.OpenSubKey(@"FruitsAtelier.ProjectArchive\shell\open\command");
                Check(command?.GetValue("") as string == "\"C:\\Moved FA\\FA.exe\" \"%1\"", "activation quotes executable and file paths");
                classes.DeleteSubKeyTree(".faz");
                Check(FazFileAssociation.EnsureRegistered(classes, @"C:\Moved FA\FA.exe", @"C:\Moved FA\faz.ico"), "missing extension is repaired");
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
            string channel = "FruitsAtelier-faz-test-" + Guid.NewGuid().ToString("N");
            using var cancellation = new CancellationTokenSource();
            var listener = FazFileAssociation.Listen(cancellation.Token, channel);
            try
            {
                Check(FazFileAssociation.Forward(native, channel), "running instance accepts file activation");
                Check(SpinWait.SpinUntil(() => !FazFileAssociation.Pending.IsEmpty, 3000), "activation reaches UI queue");
                Check(FazFileAssociation.Pending.TryDequeue(out string? delivered) && delivered == native, "activation retains exact file path");
            }
            finally { cancellation.Cancel(); Check(listener.Wait(3000), "activation listener shuts down"); }
        }
#endif
        Console.WriteLine("PASS FAZ roundtrips, association precedence, duplicate ownership, archive validation and UI entry points");

        LibrarySettings Settings(string name) => new() { Workspace = Path.Combine(root, name, "Workspace"), Songs = Path.Combine(root, name, "Songs") };
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws(Action action, string message)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException) { return; }
        throw new Exception(message);
    }
}
