using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class DifficultyImportTests
{
    public static void Run()
    {
        string root = OperatingSystem.IsMacOS()
            ? Path.Combine("/private/tmp", "atelier-difficulty-import-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(Path.Combine("artifacts/tests/difficulty-import", Guid.NewGuid().ToString("N")));
        string resources = Path.Combine(root, "Resources");
        string downloads = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(resources); Directory.CreateDirectory(downloads);
        try
        {
            string Map(string artist = "Artist", string title = "Song", string version = "Imported") =>
                $"osu file format v14\n[General]\nAudioFilename:audio.mp3\nMode:2\n[Metadata]\nArtist:{artist}\nTitle:{title}\nArtistUnicode:同一艺术家\nTitleUnicode:同一标题\nVersion:{version}\n[Events]\n0,0,\"bg.jpg\",0,0\nSample,1000,0,\"sample.wav\",100\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n";
            string original = Path.Combine(resources, "original.osu");
            string incoming = Path.Combine(downloads, "incoming.osu");
            File.WriteAllText(original, Map(version: "Original"));
            foreach (string name in new[] { "audio.mp3", "bg.jpg", "sample.wav" })
            {
                File.WriteAllText(Path.Combine(resources, name), "project resource");
                File.WriteAllText(Path.Combine(downloads, name), "unrelated download");
            }
            var view = new EditorView();
            view.LibrarySettings.Workspace = Path.Combine(root, "Workspace");
            view.LoadDocument(OsuBeatmapReader.ReadFile(original));
            foreach (var mismatch in new[] { ("Other", "Song"), ("Artist", "Other"), ("artist", "Song"),
                ("Artist", "song"), ("", "Song"), ("Artist", "") })
            {
                File.WriteAllText(incoming, Map(mismatch.Item1, mismatch.Item2));
                Reject(() => view.ImportDifficultyFile(incoming));
                Check(view.DifficultyCount == 1 && !view.IsDirty && Directory.GetFiles(resources, "*.osu").Length == 1,
                    "Metadata rejection must precede file copies and project changes, even when Unicode fields match.");
            }
            File.WriteAllText(incoming, Map());
            File.WriteAllText(Path.Combine(resources, "incoming.osu"), "existing file");
            Check(view.ImportDifficultyFile(incoming), "Import matching difficulty.");
            Check(File.ReadAllText(Path.Combine(resources, "incoming.osu")) == "existing file", "Never overwrite an existing file.");
            Check(File.ReadAllText(incoming) == Map(), "Keep the downloaded source unchanged.");
            Check(view.DifficultyCount == 2 && view.Document.Fruits.Single().X == 100, "Import the difficulty content.");
            CheckResources(view.Document);
            string retainedSource = view.Document.SourcePath!;
            Check(view.ImportDifficultyFile(incoming) && view.DifficultyCount == 2 && view.Document.SourcePath == retainedSource,
                "Reimporting the downloaded difficulty selects its retained copy without another file or tab.");
            var session = WorkspaceProject.Create(view.LibrarySettings.Workspace, view.CaptureProject(), "");
            var reopened = WorkspaceProject.Open(session.Directory).Project.Difficulties[1].Document;
            CheckResources(reopened);
            Check(WorkspaceProject.MissingResources(WorkspaceProject.Open(session.Directory).Project).Count == 0,
                "The imported source remains available after saving and reopening.");

            File.Delete(Path.Combine(resources, "audio.mp3"));
            File.Delete(Path.Combine(resources, "bg.jpg"));
            CheckResources(reopened);
            Check(!File.Exists(reopened.AudioPath), "Missing project audio must not fall back to Downloads.");

            string local = Path.Combine(resources, "local.osu");
            File.WriteAllText(local, Map(version: "Local"));
            Check(view.ImportDifficultyFile(local) && view.Document.SourcePath == local, "A same-directory import keeps its source.");
            Check(view.ImportDifficultyFile(local) && view.DifficultyCount == 3, "Reimporting a local difficulty selects the existing tab.");

            view.LoadDocument(OsuBeatmapReader.Read(Map("", "Song"), original));
            Reject(() => view.ImportDifficultyFile(incoming));
            view.LoadDocument(OsuBeatmapReader.Read(Map("Artist", ""), original));
            Reject(() => view.ImportDifficultyFile(incoming));
            var withoutResources = OsuBeatmapReader.Read(Map());
            withoutResources.AudioPath = null;
            view.LoadDocument(withoutResources);
            Reject(() => view.ImportDifficultyFile(incoming));

            void CheckResources(MapDocument document)
            {
                Check(Path.GetDirectoryName(document.SourcePath) == resources && File.Exists(document.SourcePath), "Retain a source in the project's resource directory.");
                Check(document.AudioPath == Path.Combine(resources, "audio.mp3"), "Resolve audio against project resources.");
                var scene = StoryboardReader.Read(File.ReadAllText(document.SourcePath!), null, Path.GetDirectoryName(document.SourcePath)!);
                Check(scene.BackgroundPath == Path.Combine(resources, "bg.jpg"), "Resolve background against project resources.");
                Check(scene.Samples.Single().Path == Path.Combine(resources, "sample.wav"), "Resolve storyboard samples against project resources.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("An incompatible difficulty import was accepted.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
