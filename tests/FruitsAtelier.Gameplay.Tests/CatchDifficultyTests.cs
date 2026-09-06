using FruitsAtelier.Core;

internal static class CatchDifficultyTests
{
    // Golden values evaluated by the unmodified upstream Movement / preprocessing / strain classes
    // at ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f. No-mod, CS=3/5/8.
    public static void OfficialValues()
    {
        CheckPattern(120, i => i % 2 == 0 ? 60 : 450, i => 1000 + i * 125, [6.933532855219926, 7.711620144689329, 11.120968031532314]);
        CheckPattern(50, i => 50 + i * 8, i => 1000 + i * 100, [0.22565405121848128, 0.2675666918098227, 0.3920523829823548]);
        CheckPattern(80, i => i % 2 == 0 ? 220 : 270, i => i * 50, [0.24353333378909106, 0.3433420425815413, 1.6921301640679927]);
        CheckPattern(40, i => i * 179 % 512, i => i * 250 + (i > 20 ? 10000 : 0), [2.405625585101705, 2.697394023438512, 3.968731511772716]);
        CheckPattern(100, i => i * 123.456 % 512, i => i * 83.33333, [5.891783709930346, 7.132411201702147, 8.633074076019987]);
        CheckPattern(60, i => i * 200 % 512, i => i / 3 * 200, [7.394058210856286, 8.00754292218996, 10.851859770533698]);
        CheckPattern(100, i => 256, i => i * 100, [0, 0, 0]);
    }

    public static void Participation()
    {
        var objects = Pattern(50, i => i % 2 == 0 ? 50 : 450, i => i * 100);
        double expected = CatchDifficultyCalculator.Calculate(objects, 5).StarRating;
        var mixed = objects.Concat(objects.Select(o => o with { Kind = CatchObjectKind.TinyDroplet }))
            .Concat(objects.Select(o => o with { Kind = CatchObjectKind.Banana })).Reverse().ToArray();
        var result = CatchDifficultyCalculator.Calculate(mixed, 5);
        Near(result.StarRating, expected);
        if (result.MaxCombo != 50) throw new Exception("Only fruit and droplets contribute combo");
        Near(CatchDifficultyCalculator.Calculate(objects.Select(o => o with { Kind = CatchObjectKind.Droplet }).ToArray(), 5).StarRating, expected);
        Near(CatchDifficultyCalculator.Calculate([], 5).StarRating, 0);
        Near(CatchDifficultyCalculator.Calculate([objects[0]], 5).StarRating, 0);
        try { CatchDifficultyCalculator.Calculate(objects, double.NaN); throw new Exception("NaN CS accepted"); }
        catch (ArgumentOutOfRangeException) { }
    }

    private static void CheckPattern(int count, Func<int, double> x, Func<int, double> time, double[] expected)
    {
        var objects = Pattern(count, x, time);
        for (int i = 0; i < 3; i++) Near(CatchDifficultyCalculator.Calculate(objects, new double[] { 3, 5, 8 }[i]).StarRating, expected[i]);
    }
    private static ConvertedCatchObject[] Pattern(int count, Func<int, double> x, Func<int, double> time)
        => Enumerable.Range(0, count).Select(i => new ConvertedCatchObject(Guid.Empty, i, CatchObjectKind.Fruit, time(i), x(i), 0, 0, 0)).ToArray();
    private static void Near(double actual, double expected)
    {
        if (Math.Abs(actual - expected) > 1e-12) throw new Exception($"Expected {expected:R}, got {actual:R}");
    }
}
