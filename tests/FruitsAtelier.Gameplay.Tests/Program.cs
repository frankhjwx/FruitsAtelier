using FruitsAtelier.Core;

var tests = new (string Name, Action Run)[]
{
    ("Catch stars match 21 official algorithm fixtures", CatchDifficultyTests.OfficialValues),
    ("Catch star calculation respects object participation and input boundaries", CatchDifficultyTests.Participation),
    ("CS scales nominal fruit, default droplets and catcher independently", Sizes),
    ("Static bananas use the arrival scale across the CS range", BananaSizes),
    ("Hyperdash uses full catcher width and marks the departure object", Departure),
    ("Droplet participates while tiny droplet does not interrupt hyperdash context", DropletParticipation),
    ("Same-direction excess from a prefix can turn the next jump red", PrefixExcess),
    ("Direction reversal restores the full half-catcher allowance", DirectionReversal),
    ("A hyperdash resets excess for the next jump", HyperdashReset),
    ("Each fractional timestamp is truncated separately", TimestampTruncation),
    ("Simultaneous objects retain stable source ordering", StableOrdering),
    ("CS changes reachability without changing object coordinates", CircleSizeThreshold),
    ("Recomputation returns fresh flags and leaves source objects unchanged", PureRecompute),
    ("Red-start lookup preserves the source and nested event identity", RedStartIdentity),
    ("Invalid CS and non-finite input are rejected", Validation)
};

int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} gameplay tests passed.");
return failures == 0 ? 0 : 1;

static void Sizes()
{
    Near(0.85, CatchSize.Scale(0));
    Near(0.5, CatchSize.Scale(5));
    Near(0.15, CatchSize.Scale(10));
    Near(64, CatchSize.FruitDiameter(5));
    Near(32, CatchSize.FruitRadius(5));
    Near(8, CatchSize.DefaultDropletRadius(5));
    Near(4, CatchSize.DefaultTinyDropletRadius(5));
    Near(106.75, CatchSize.CatcherWidth(5));
    Near(85.4, CatchSize.CatchWidth(5));
    True(CatchSize.CatcherWidth(0) > CatchSize.CatcherWidth(5)
        && CatchSize.CatcherWidth(5) > CatchSize.CatcherWidth(10), "Catcher size is not decreasing with CS.");
}

static void BananaSizes()
{
    Near(0.6, CatchSize.BananaScaleFactor);
    Near(32.64, CatchSize.BananaRadius(0));
    Near(19.2, CatchSize.BananaRadius(5));
    Near(5.76, CatchSize.BananaRadius(10));
    foreach (double cs in new[] { 0, 3.7, 5, 10 })
    {
        Near(0.6, CatchSize.BananaRadius(cs) / CatchSize.FruitRadius(cs));
        True(CatchSize.BananaRadius(cs) < CatchSize.FruitRadius(cs), "Banana is not smaller than the corresponding fruit.");
    }
    Throws(() => CatchSize.BananaRadius(double.NaN));
}

static void Departure()
{
    var white = HyperDashCalculator.Calculate([Obj(0, 0), Obj(100, 140)], 5);
    True(!white[0].IsHyperDash && white[0].DistanceToHyperDash > 9,
        "Full-width reachable jump was marked hyperdash, as it would be with the narrower catching margin.");
    var red = HyperDashCalculator.Calculate([Obj(0, 0), Obj(100, 150)], 5);
    True(red[0].IsHyperDash && red[0].TargetIndex == 1, "Departure fruit does not identify the hyperdash target.");
    True(!red[1].IsHyperDash && red[1].TargetIndex is null, "Landing/last fruit was incorrectly marked red.");
}

static void DropletParticipation()
{
    var objects = new[]
    {
        Obj(0, 0), Obj(50, 500, CatchObjectKind.TinyDroplet),
        Obj(100, 140, CatchObjectKind.Droplet), Obj(200, 246)
    };
    var result = HyperDashCalculator.Calculate(objects, 5);
    True(!result[0].IsHyperDash, "Tiny droplet became a hyperdash target.");
    True(!result[1].IsHyperDash && result[1].DistanceToHyperDash == 0, "Tiny droplet received a hyperdash state.");
    True(result[2].IsHyperDash && result[2].TargetIndex == 3, "Tick droplet cannot initiate hyperdash.");
}

static void PrefixExcess()
{
    var complete = HyperDashCalculator.Calculate([Obj(0, 0), Obj(100, 140), Obj(200, 246)], 5);
    var clipped = HyperDashCalculator.Calculate([Obj(100, 140), Obj(200, 246)], 5);
    True(!complete[0].IsHyperDash && complete[1].IsHyperDash, "Prefix movement allowance was not propagated.");
    True(!clipped[0].IsHyperDash, "Prefix fixture does not distinguish whole-map context from clipped input.");
}

static void DirectionReversal()
{
    var result = HyperDashCalculator.Calculate([Obj(0, 0), Obj(100, 140), Obj(200, 0)], 5);
    True(!result[0].IsHyperDash && !result[1].IsHyperDash, "Direction reversal retained the previous direction's excess.");
}

static void HyperdashReset()
{
    var result = HyperDashCalculator.Calculate([Obj(0, 0), Obj(100, 150), Obj(200, 290)], 5);
    True(result[0].IsHyperDash && !result[1].IsHyperDash, "Hyperdash did not reset the allowance for the next jump.");
}

static void TimestampTruncation()
{
    var first = HyperDashCalculator.Calculate([Obj(0.9, 0), Obj(100.1, 148.8)], 5);
    True(!first[0].IsHyperDash, "Timestamp difference was truncated instead of each timestamp.");
    var second = HyperDashCalculator.Calculate([Obj(0.1, 0), Obj(99.9, 148.5)], 5);
    True(second[0].IsHyperDash, "Fractional timestamps skipped the stable integer-time boundary.");
}

static void StableOrdering()
{
    var result = HyperDashCalculator.Calculate([Obj(1000, 256), Obj(0, 0), Obj(0, 512)], 5);
    True(result.Length == 3 && result[1].TargetIndex == 2, "Equal-time source order or original result indices changed.");
    True(!result[0].IsHyperDash && !result[2].IsHyperDash, "Last chronological object or long return jump became red.");
}

static void CircleSizeThreshold()
{
    ConvertedCatchObject[] objects = [Obj(0, 0), Obj(100, 140)];
    True(!HyperDashCalculator.Calculate(objects, 0)[0].IsHyperDash, "CS0 catcher's reachable jump was marked red.");
    True(HyperDashCalculator.Calculate(objects, 10)[0].IsHyperDash, "CS10 did not shrink the hyperdash allowance.");
    Near(0, objects[0].X);
    Near(140, objects[1].X);
}

static void PureRecompute()
{
    ConvertedCatchObject[] objects = [Obj(0, 0), Obj(100, 512), Obj(1000, 256)];
    var saved = objects.ToArray();
    var first = HyperDashCalculator.Calculate(objects, 5);
    True(first[0].IsHyperDash, "Fixture does not contain a hyperdash.");
    var shorter = HyperDashCalculator.Calculate([objects[0]], 5);
    True(!shorter[0].IsHyperDash && shorter[0].DistanceToHyperDash == 0, "A recomputed final fruit kept stale hyperdash state.");
    True(objects.SequenceEqual(saved), "Hyperdash modified its source objects.");
    True(HyperDashCalculator.Calculate([], 5).Length == 0, "Empty map did not return an empty result.");
}

static void Validation()
{
    Throws(() => CatchSize.Scale(double.NaN));
    Throws(() => CatchSize.Scale(-1));
    Throws(() => CatchSize.Scale(11));
    Throws(() => HyperDashCalculator.Calculate([Obj(double.PositiveInfinity, 0)], 5));
    Throws(() => HyperDashCalculator.Calculate([Obj(100, double.NaN)], 5));
}

static void RedStartIdentity()
{
    Guid source = Guid.NewGuid();
    ConvertedCatchObject[] objects =
    [
        Obj(0, 0) with { SourceId = source, EventIndex = 0 },
        Obj(100, 140, CatchObjectKind.Droplet) with { SourceId = source, EventIndex = 1 },
        Obj(200, 246) with { SourceId = source, EventIndex = 2 }
    ];
    var starts = HyperDashCalculator.GetHyperDashStarts(objects, 5);
    True(starts.Count == 1 && starts.Contains((source, 1)), "Red-start lookup lost the nested droplet identity.");
}

static ConvertedCatchObject Obj(double time, double x, CatchObjectKind kind = CatchObjectKind.Fruit)
    => new(Guid.NewGuid(), 0, kind, time, x, x, x, 0);

static void Near(double expected, double actual)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.0001)
        throw new Exception($"Expected {expected:R}, got {actual:R}.");
}

static void True(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Throws(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new Exception("Expected an invalid-input exception.");
}
