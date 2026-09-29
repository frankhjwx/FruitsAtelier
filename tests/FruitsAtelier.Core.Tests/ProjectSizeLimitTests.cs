using System.Text;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ProjectSizeLimitTests
{
    public static void Run()
    {
        string directory = Path.Combine("artifacts", "tests", "project-size-limit");
        Directory.CreateDirectory(directory);
        var document = new MapDocument();
        var section = new OsuSection { Name = "Events" };
        string line = "//" + new string('x', 1022);
        for (int i = 0; i < 33 * 1024; i++) section.Lines.Add(line);
        document.OriginalSections.Add(section);
        string single = Path.Combine(directory, "large.catchdiff");
        ProjectSerializer.WriteFile(document, single);
        Check(new FileInfo(single).Length > 32 * 1024 * 1024, "Fixture must exceed the old limit.");
        Check(ProjectSerializer.ReadFile(single).ContentEquals(document), "Large difficulty did not round trip.");
        var project = BeatmapProject.FromDocuments([document]);
        string multi = Path.Combine(directory, "large.catchproj");
        ProjectSerializer.WriteFile(project, multi);
        Check(ProjectSerializer.ReadProjectFile(multi).Difficulties[0].Document.ContentEquals(document), "Large project did not round trip.");

        string small = ProjectSerializer.Serialize(new MapDocument());
        string boundary = small.PadRight(ProjectSerializer.MaximumFileBytes);
        ProjectSerializer.Read(boundary);
        ProjectSerializer.ReadProject(boundary);
        boundary = null!;

        // Multi-byte input must be rejected by byte count before JSON parsing.
        string oversized = new string('\u4e2d', ProjectSerializer.MaximumFileBytes / 3 + 1);
        Reject(() => ProjectSerializer.Read(oversized), "core.project.readLimit");
        Reject(() => ProjectSerializer.ReadProject(oversized), "core.project.readLimit");
        oversized = null!;
        string tooLarge = Path.Combine(directory, "oversized.catchdiff");
        using (var file = File.Create(tooLarge)) file.SetLength(ProjectSerializer.MaximumFileBytes + 1L);
        Reject(() => ProjectSerializer.ReadFile(tooLarge), "core.project.readLimit");
        Reject(() => ProjectSerializer.ReadProjectFile(tooLarge), "core.project.readLimit");

        // JSON escapes each character to six ASCII bytes.
        var huge = new MapDocument { Name = new string('\u4e2d', ProjectSerializer.MaximumFileBytes / 6 + 1) };
        string protectedFile = Path.Combine(directory, "protected.catchdiff");
        File.WriteAllText(protectedFile, "existing content", Encoding.UTF8);
        Reject(() => ProjectSerializer.WriteFile(huge, protectedFile), "core.project.writeLimit");
        Check(File.ReadAllText(protectedFile) == "existing content", "Rejected save changed the existing file.");
    }

    private static void Reject(Action action, string key)
    {
        try { action(); }
        catch (InvalidDataException error) when (error.Message == L.Get(key)) { return; }
        throw new Exception("Expected size limit rejection: " + key);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
