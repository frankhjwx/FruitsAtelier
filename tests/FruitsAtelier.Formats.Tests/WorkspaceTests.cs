using FruitsAtelier.Core;

internal static class WorkspaceTests
{
    public static void Run()
    {
        string root = Path.Combine((OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath()), "atelier-workspace-" + Guid.NewGuid());
        string workspace = Path.Combine(root, "Workspace"), songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "123 artist song");
        Directory.CreateDirectory(set);
        try
        {
            string source = Path.Combine(set, "original.osu");
            File.WriteAllText(Path.Combine(set, "audio.mp3"), "fixture-audio");
            File.WriteAllText(Path.Combine(set, "bg.jpg"), "fixture-image");
            File.WriteAllText(source, Fixture());
            string original = File.ReadAllText(source);
            var document = OsuBeatmapReader.ReadFile(source);
            var project = BeatmapProject.FromDocuments([document]);
            var session = WorkspaceProject.Create(workspace, project, songs);
            Check(File.Exists(Path.Combine(session.Directory, "project.catchdiff")), "manifest");
            string file = session.Manifest.Difficulties[0].File;
            Check(file == "Artist - Romanised Title (Mapper) [Rain].catchdiff", "osu naming");
            Check(Directory.GetFiles(session.Directory).All(p => p.EndsWith(".catchdiff")), "no resources copied");
            Check(File.ReadAllText(source) == original, "create leaves Songs untouched");
            project.Difficulties[0].Document.Fruits[0].X = 400;
            WorkspaceProject.Save(session, project);
            var loaded = WorkspaceProject.Open(session.Directory);
            Check(loaded.Project.Difficulties[0].Document.Fruits[0].X == 400, "diff persisted");
            Check(loaded.Project.Difficulties[0].Document.AudioPath == document.AudioPath, "reference resolved");
            Check(File.ReadAllText(source) == original, "save leaves Songs untouched");
            project.Difficulties[0].Name = "Platter";
            WorkspaceProject.Save(session, project);
            Check(!File.Exists(Path.Combine(session.Directory, file)), "old filename removed");
            Check(session.Manifest.Difficulties[0].File.EndsWith("[Platter].catchdiff"), "rename");
            Directory.Move(session.Directory, session.Directory + ".previous");
            loaded = WorkspaceProject.Open(session.Directory);
            Check(loaded.Project.Difficulties[0].Name == "Platter", "interrupted directory publication recovered");
            var db = new LibraryDatabase(workspace, songs);
            Check(db.Scan().Count == 1, "scan");
            Check(db.Search("romanised artist").Count == 1 && db.Search("原始 歌手").Count == 1, "both metadata forms");
            Check(db.Search("drum bass").Count == 1 && db.Search("missing").Count == 0, "tag search");
            Check(db.Search("%_'").Count == 0, "literal SQL search");
            Check(db.Search("", true).Single().ProjectPath == session.Directory, "source association");
            var plan = WorkspaceExport.Plan(session, project.Difficulties[0], songs, true, "", true);
            WorkspaceExport.Commit(session, plan); WorkspaceProject.Save(session, project);
            Check(OsuBeatmapReader.ReadFile(source).Fruits[0].X == 400, "explicit overwrite");
            File.AppendAllText(source, "\n// external update\n");
            Reject(() => WorkspaceExport.Plan(session, project.Difficulties[0], songs, true, "", true));
            var fresh = WorkspaceExport.Plan(session, project.Difficulties[0], songs, false, "New Rain", true);
            Check(!File.Exists(fresh.Target), "plan is read only");
            WorkspaceExport.Commit(session, fresh);
            Check(OsuBeatmapReader.Setting(OsuBeatmapReader.ReadFile(fresh.Target), "Metadata", "BeatmapID") == "0", "new diff ID");
            Reject(() => WorkspaceExport.Plan(session, project.Difficulties[0], songs, false, "New Rain", true));
            var events = project.Difficulties[0].Document.OriginalSections.First(s => s.Name == "Events");
            events.Lines.Add("Sprite,Foreground,Centre,\"image,comma.png\",320,240");
            events.Lines.Add("Animation,Foreground,Centre,\"frame.png\",320,240,2,100,LoopForever");
            File.WriteAllText(Path.Combine(set, "image,comma.png"), "image");
            File.WriteAllText(Path.Combine(set, "frame0.png"), "frame");
            var referenceErrors = WorkspaceProject.MissingResources(project);
            Check(referenceErrors.Count == 1 && referenceErrors[0].EndsWith("frame1.png"), "quoted resources and actual animation frames");
            events.Lines.RemoveRange(events.Lines.Count - 2, 2);
            File.Delete(document.AudioPath!);
            Check(WorkspaceProject.MissingResources(project).Contains(document.AudioPath!), "missing reference reported");
            WorkspaceProject.Save(session, project);
            Reject(() => WorkspaceExport.Plan(session, project.Difficulties[0], songs, false, "Missing audio", true));
            Reject(() => WorkspaceProject.Create(Path.Combine(songs, "bad"), project, songs));
            File.WriteAllText(Path.Combine(set, "standard.osu"), Fixture().Replace("Mode:2", "Mode:0"));
            Check(db.Scan().Count == 2, "ignore standard and discover new diff");
            File.Delete(source); db.Scan(); Check(db.Search("").Count == 1, "incremental removal");
            File.Delete(Path.Combine(workspace, "library.db"));
            db = new(workspace, songs); db.Scan(); Check(db.Search("", true).Any(), "database rebuild retains projects");
            string moved = songs + "-offline"; Directory.Move(songs, moved);
            WorkspaceProject.Save(session, project);
            Check(WorkspaceProject.MissingResources(WorkspaceProject.Open(session.Directory).Project).Count >= 2, "offline reference error and saving");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    private static string Fixture() => """
osu file format v14
[General]
AudioFilename:audio.mp3
Mode:2
[Metadata]
Title:Romanised Title
TitleUnicode:原始歌名
Artist:Artist
ArtistUnicode:歌手
Creator:Mapper
Version:Rain
Tags:drum bass
BeatmapID:123
BeatmapSetID:456
[Difficulty]
HPDrainRate:5
CircleSize:5
OverallDifficulty:5
ApproachRate:5
SliderMultiplier:1.4
SliderTickRate:1
[Events]
0,0,"bg.jpg",0,0
[TimingPoints]
0,500,4,1,0,100,1,0
[HitObjects]
100,192,1000,1,0,0:0:0:0:
""";
}
