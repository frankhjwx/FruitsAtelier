using FruitsAtelier.Core;

internal static class MultiProjectTests
{
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "project-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "song.catchproj");
            var a = new MapDocument { Name = "Song", IsDemo = false, AudioPath = Path.Combine(folder, "audio.ogg") };
            a.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
            var b = a.DeepClone(); b.Fruits[0].X = 400;
            var project = BeatmapProject.FromDocuments([a, b]);
            ProjectSerializer.WriteFile(project, path);
            var restored = ProjectSerializer.ReadProjectFile(path);
            Check(restored.Difficulties.Count == 2 && restored.Difficulties[0].Id == project.Difficulties[0].Id, "identity");
            Check(restored.Difficulties[0].Document.ContentEquals(a) && restored.Difficulties[1].Document.ContentEquals(b), "documents/resources");
            Check(!File.ReadAllText(path).Contains(folder), "relative paths");
            Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(a)).Difficulties.Count == 1, "schema 1");
            string before = File.ReadAllText(path);
            project.Difficulties[1].Id = project.Difficulties[0].Id;
            Reject(() => ProjectSerializer.WriteFile(project, path));
            Check(File.ReadAllText(path) == before, "atomic failure");
            Reject(() => ProjectSerializer.ReadProject("{\"SchemaVersion\":2,\"Project\":{\"Name\":\"x\",\"Difficulties\":[]}}"));
            Reject(() => ProjectSerializer.ReadProject(before.Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 3")));
            Reject(() => ProjectSerializer.ReadProject(before.Replace("audio.ogg", "//host/audio.ogg")));
        }
        finally { Directory.Delete(folder, true); }
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid project accepted");
    }
}
