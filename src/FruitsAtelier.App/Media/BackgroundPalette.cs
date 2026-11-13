namespace FruitsAtelier.App.Media;

internal static class BackgroundPalette
{
    private readonly record struct Lab(double L, double A, double B)
    {
        public double Distance(Lab other) => Math.Pow(L - other.L, 2) + Math.Pow(A - other.A, 2) + Math.Pow(B - other.B, 2);
    }

    internal static double Luminosity(uint colour)
    {
        double r = colour >> 16 & 255, g = colour >> 8 & 255, b = colour & 255;
        return (Math.Max(r, Math.Max(g, b)) + Math.Min(r, Math.Min(g, b))) / 2;
    }
    internal static bool OutsideLuminosityGuideline(uint colour)
    {
        double luminosity = Luminosity(colour);
        return luminosity <= 50 || luminosity > 220;
    }

    internal static double PerceivedBrightness(uint colour)
    {
        double r = colour >> 16 & 255, g = colour >> 8 & 255, b = colour & 255;
        return Math.Sqrt(.299 * r * r + .587 * g * g + .114 * b * b);
    }

    internal static uint[] Generate(StoryboardImageCache.Pixels pixels, int count)
    {
        if (count is < 3 or > 8) throw new ArgumentOutOfRangeException(nameof(count));
        var samples = new List<Lab>();
        int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pixels.Width * (double)pixels.Height / 16384)));
        for (int y = 0; y < pixels.Height; y += step)
        for (int x = 0; x < pixels.Width; x += step)
        {
            int p = (y * pixels.Width + x) * 4;
            double alpha = pixels.Data[p + 3] / 255d;
            if (alpha < .5) continue;
            samples.Add(ToLab(pixels.Data[p + 2] / alpha, pixels.Data[p + 1] / alpha, pixels.Data[p] / alpha));
        }
        if (samples.Count == 0) return [];
        int clusters = Math.Min(24, samples.Count);
        var centres = new List<Lab> { samples[samples.Count / 2] };
        while (centres.Count < clusters)
        {
            var next = samples.MaxBy(s => centres.Min(c => c.Distance(s)));
            if (centres.Min(c => c.Distance(next)) < .00001) break;
            centres.Add(next);
        }
        int[] weights = new int[centres.Count];
        for (int pass = 0; pass < 12; pass++)
        {
            Array.Clear(weights);
            var sums = new (double L, double A, double B)[centres.Count];
            foreach (var sample in samples)
            {
                int nearest = 0; double distance = double.MaxValue;
                for (int i = 0; i < centres.Count; i++)
                    if (centres[i].Distance(sample) < distance) { nearest = i; distance = centres[i].Distance(sample); }
                weights[nearest]++;
                sums[nearest].L += sample.L; sums[nearest].A += sample.A; sums[nearest].B += sample.B;
            }
            for (int i = 0; i < centres.Count; i++)
                if (weights[i] > 0) centres[i] = new(sums[i].L / weights[i], sums[i].A / weights[i], sums[i].B / weights[i]);
        }
        // Separate lightness from the dominant background while retaining its hue family.
        double backgroundLightness = samples.Average(s => s.L);
        var candidates = centres.Select((c, i) => (Colour: Fit(c, backgroundLightness), Weight: weights[i] *
            (.25 + Math.Min(1, Math.Sqrt(c.A * c.A + c.B * c.B) * 8)) * (c.L is > .15 and < .9 ? 1 : .25))).ToList();
        var result = new List<uint>();
        while (result.Count < count && candidates.Count > 0)
        {
            var best = candidates.MaxBy(c => Math.Sqrt(c.Weight) * (result.Count == 0 ? 1 :
                result.Min(r => ToLab(r).Distance(ToLab(c.Colour)))));
            candidates.Remove(best);
            if (result.All(r => ToLab(r).Distance(ToLab(best.Colour)) >= .0049)) result.Add(best.Colour);
        }
        // Low-colour images need lightness variants; achromatic sources stay achromatic.
        var variants = centres.SelectMany(source => Enumerable.Range(0, 24)
            .Select(i => Fit(source with { L = .32 + i * .025 }, -1))).Distinct().Except(result).ToList();
        while (result.Count < count && variants.Count > 0)
        {
            uint colour = variants.MaxBy(c => result.Min(r => ToLab(r).Distance(ToLab(c))));
            result.Add(colour); variants.Remove(colour);
        }
        return result.ToArray();
    }

    private static uint Fit(Lab colour, double background)
    {
        if (background >= 0 && Math.Abs(colour.L - background) < .16)
            colour = colour with { L = background > .58 ? Math.Max(.38, background - .22) : Math.Min(.82, background + .22) };
        uint rgb = FromLab(colour);
        for (int i = 0; i < 100 && (Luminosity(rgb) < 60 || Luminosity(rgb) > 210 || PerceivedBrightness(rgb) < 60 || PerceivedBrightness(rgb) > 225); i++)
        {
            colour = colour with { L = colour.L + (Luminosity(rgb) < 60 || PerceivedBrightness(rgb) < 60 ? .005 : -.005) };
            rgb = FromLab(colour);
        }
        return rgb;
    }

    private static Lab ToLab(uint rgb) => ToLab(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255);
    private static Lab ToLab(double r, double g, double b)
    {
        r = Linear(r / 255); g = Linear(g / 255); b = Linear(b / 255);
        double l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
        double m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
        double s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
        return new(.2104542553 * l + .793617785 * m - .0040720468 * s,
            1.9779984951 * l - 2.428592205 * m + .4505937099 * s,
            .0259040371 * l + .7827717662 * m - .808675766 * s);
    }
    private static uint FromLab(Lab c)
    {
        double l = Math.Pow(c.L + .3963377774 * c.A + .2158037573 * c.B, 3);
        double m = Math.Pow(c.L - .1055613458 * c.A - .0638541728 * c.B, 3);
        double s = Math.Pow(c.L - .0894841775 * c.A - 1.291485548 * c.B, 3);
        return Channel(4.0767416621 * l - 3.3077115913 * m + .2309699292 * s) << 16
            | Channel(-1.2684380046 * l + 2.6097574011 * m - .3413193965 * s) << 8
            | Channel(-.0041960863 * l - .7034186147 * m + 1.707614701 * s);
    }
    private static double Linear(double x) => x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4);
    private static uint Channel(double x) => (uint)Math.Round(Math.Clamp(x <= .0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - .055, 0, 1) * 255);
}
