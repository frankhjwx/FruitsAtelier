using System.Globalization;

namespace FruitsAtelier.Core;

public enum MapTemplateIcon { Neutral, Blue, Sky, Mint, Green, Yellow, Orange, Red, Pink, Violet, Indigo, Black }

public sealed record MapTemplate
{
    public string Name { get; set; } = "";
    private MapTemplateIcon icon;
    public MapTemplateIcon Icon { get => icon; set => icon = Enum.IsDefined(value) ? value : MapTemplateIcon.Neutral; }
    public double ApproachRate { get; set; } = 8;
    public double CircleSize { get; set; } = 5;
    public string HP { get; set; } = "5";
    public string OD { get; set; } = "5";
    public double SliderMultiplier { get; set; } = 1.92;
    public double SliderTickRate { get; set; } = 1;

    public static MapTemplate Capture(string name, MapDocument document) => new()
    {
        Name = name, ApproachRate = document.ApproachRate, CircleSize = document.CircleSize,
        HP = SongSetup.Get(document, "Difficulty", "HPDrainRate", "5"),
        OD = SongSetup.Get(document, "Difficulty", "OverallDifficulty", "5"),
        SliderMultiplier = document.EffectiveSliderMultiplier, SliderTickRate = document.SliderTickRate
    };

    public void Apply(MapDocument document)
    {
        document.ApproachRate = ApproachRate; document.CircleSize = CircleSize;
        document.SliderMultiplier = SliderMultiplier; document.SliderTickRate = SliderTickRate;
        document.OverrideSliderMultiplier = false; document.SliderMultiplierOverride = null;
        document.DistancePerBeatOverride = null;
        SongSetup.Set(document, "Difficulty", "HPDrainRate", HP);
        SongSetup.Set(document, "Difficulty", "OverallDifficulty", OD);
        foreach (var (key, value) in new[] { ("ApproachRate", ApproachRate), ("CircleSize", CircleSize),
            ("SliderMultiplier", SliderMultiplier), ("SliderTickRate", SliderTickRate) })
            SongSetup.Set(document, "Difficulty", key, value.ToString("R", CultureInfo.InvariantCulture));
    }
}
