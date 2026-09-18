using FruitsAtelier.Core;

internal static class DenseImportedSliderTests
{
    public static void ReadAndConvert()
    {
        string Map(int points) => "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,B|"
            + string.Join('|', Enumerable.Range(1, points - 1).Select(i => $"{100 + (i / 2) % 2}:192")) + ",1,1000";
        foreach (int count in new[] { 19564, ImportedSlider.MaximumControlPoints })
        {
            var document = OsuBeatmapReader.Read(Map(count));
            if (document.ImportedSliders.Single().ControlPoints.Count != count || CurveMath.Validate(document).Count != 0)
                throw new Exception("Dense imported slider was rejected or lost control points.");
            var converted = CatchStreamConverter.Convert(document);
            if (!converted.Success || converted.Sliders.Count != 1 || converted.Sliders[0].DurationMs != 5000)
                throw new Exception("Dense imported slider failed conversion or changed duration.");
        }
        try { OsuBeatmapReader.Read(Map(ImportedSlider.MaximumControlPoints + 1)); }
        catch (InvalidDataException) { return; }
        throw new Exception("Control-point limit was not enforced.");
    }
}
