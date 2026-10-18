using FruitsAtelier.Core;

internal static class LargeImportedPathTests
{
    public static void Run()
    {
        var map = new MapDocument();
        var slider = new ImportedSlider { X = 0, Y = 192, TimeMs = 1000, PixelLength = 100 };
        slider.ControlPoints.Add(new(0, 192));
        for (int i = 0; i < 40000; i++)
        {
            double x = (i % 2) * 2;
            slider.ControlPoints.Add(new(1, 196));
            slider.ControlPoints.Add(new(2 - x, 192));
            slider.ControlPoints.Add(new(2 - x, 192));
        }
        map.ImportedSliders.Add(slider);
        var path = ImportedSliderConverter.Path(slider);
        Check(path.Count > 1 && path.All(p => double.IsFinite(p.X) && double.IsFinite(p.GeometryY)), "Large segmented path did not produce finite geometry.");
        string text = ProjectSerializer.Serialize(map);
        Check(!text.Contains('\n'), "Large imported projects must use compact JSON.");
        Check(map.ContentEquals(ProjectSerializer.Read(text)), "Compact project lost imported controls.");
        var project = BeatmapProject.FromDocuments([map]);
        string multi = ProjectSerializer.Serialize(project);
        Check(!multi.Contains('\n') && ProjectSerializer.ReadProject(multi).Difficulties.Single().Document.ContentEquals(map),
            "Compact multi-difficulty project lost imported controls.");
        var overLimit = new ImportedSlider();
        overLimit.ControlPoints.AddRange(Enumerable.Repeat(new SliderPathPoint(0, 192), ImportedSlider.MaximumControlPoints + 1));
        var invalid = new MapDocument(); invalid.ImportedSliders.Add(overLimit);
        try { OsuBeatmapReader.Validate(invalid); throw new Exception("Control point limit was not enforced."); }
        catch (InvalidDataException) { }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
