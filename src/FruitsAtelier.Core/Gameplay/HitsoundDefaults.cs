namespace FruitsAtelier.Core;

/// <summary>Packaged osu! legacy samples, independent of optional user skin images.</summary>
public static class HitsoundDefaults
{
    public static string? Find(int sampleSet, string name)
    {
        string bank = sampleSet switch { 2 => "soft", 3 => "drum", _ => "normal" };
        string file = name == "catch-banana" ? "catch-banana.wav" : bank + "-" + name + ".wav";
        if (name is not ("hitnormal" or "hitwhistle" or "hitfinish" or "hitclap" or "slidertick" or "catch-banana")) return null;
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "audio", "osu", file);
        return File.Exists(path) ? path : null;
    }
}
