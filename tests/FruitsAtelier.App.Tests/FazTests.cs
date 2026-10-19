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
        var db = new LibraryDatabase(linked.Workspace, linked.Songs);
        var document = project.Difficulties[0].Document.DeepClone();
        SongSetup.Set(document, "Metadata", "BeatmapID", "0"); SongSetup.Set(document, "Metadata", "BeatmapSetID", "-1");
        Check(db.AssociationCandidates(document).Single().Path == target, "exact artist and title fallback");
        SongSetup.Set(document, "Metadata", "Title", "Son");
        Check(db.AssociationCandidates(document).Count == 0, "substring is not a match");
        var duplicate = db.AssociationCandidates(project.Difficulties[0].Document).Single();
        Check(FazArchive.Recommend(project.Difficulties[0], [duplicate, duplicate with { Path = target + "2" }]) is null, "ambiguous identity needs choice");

        var partial = Settings("partial"); Directory.CreateDirectory(partial.Songs);
        string partialMap = Path.Combine(partial.Songs, "local.osu"); File.Copy(map, partialMap);
        File.Copy(Path.Combine(folder, "audio.wav"), Path.Combine(partial.Songs, "audio.wav"));
        var pair = ProjectSerializer.ReadProject(before);
        pair.Difficulties.Add(new ProjectDifficulty { Name = "Second", Document = pair.Difficulties[0].Document.DeepClone() });
        string mixed = Path.Combine(root, "mixed.faz"); FazArchive.Export(pair, mixed, false, false, source.Workspace);
        using (var package = FazArchive.Prepare(mixed, partial))
        {
            var session = FazArchive.Import(package, partial, new string?[] { partialMap, null }, false, false);
            Check(session.Manifest.Difficulties.Count(d => d.Source is not null) == 1, "partial association has one owner");
            Check(Directory.GetFiles(Path.Combine(partial.Workspace, "Resources"), "audio.wav", SearchOption.AllDirectories).Length == 1, "partial association installs only independent resources");
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

        var view = new EditorView(false); view.InitializeLibrary(true, independent);
        int opened = 0; view.RequestFazOpen = _ => opened++;
        Check(view.CanDropFile(native), "library accepts FAZ"); view.DropLibraryFiles([native]); Check(opened == 1, "FAZ drop dispatched");
        view.CloseLibrary(); Check(view.CanDropFile(native), "editor accepts FAZ");
        view.BeginFazOpen(native, () => { });
        var canvas = new RecordingCanvas();
        var taskField = typeof(EditorView).GetField("fazTask", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (taskField.GetValue(view) is not null && timer.Elapsed < TimeSpan.FromSeconds(30)) { view.Render(canvas, 980, 620); Thread.Sleep(10); }
        view.Render(canvas, 980, 620);
        Check(!view.ErrorVisible && view.DiscardConfirmationVisible, "prepared package shows modal");
        Check(!view.PrepareFileOperation(), "import modal prevents competing file operations");
        view.KeyDown(27, false, false);
        Check(!view.DiscardConfirmationVisible, "escape cancels import");
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
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
