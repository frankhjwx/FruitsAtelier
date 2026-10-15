using FruitsAtelier.Core;

internal static class CatchLightingTests
{
    public static void Run()
    {
        var fruit = new ConvertedCatchObject(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 1000, 256, 256, 256, 0);
        var entry = new CatchLightingEvent(fruit, 0, 200);
        Check(CatchHitLighting.At(entry, 999) == default, "no flash before catch");
        var arrival = CatchHitLighting.At(entry, 1000);
        Check(arrival.TallOpacity == 1 && arrival.WideOpacity == 1, "flash starts on judgement timestamp");
        var grown = CatchHitLighting.At(entry, 1160);
        Near(16, grown.TallScaleX);
        Near(1.1, grown.TallScaleY);
        Near(1 - 160d / 300, grown.TallOpacity);
        Check(CatchHitLighting.At(entry, 1300).TallOpacity == 0
            && CatchHitLighting.At(entry, 1300).WideOpacity > 0, "separate 300 and 700 ms layers");
        Check(CatchHitLighting.At(entry, 1700) == default, "flash expires at 700 ms");
        Near(16 * .35, CatchHitLighting.At(entry with { Combo = 1 }, 1160).TallScaleX);
        Near(16 * 1.125, CatchHitLighting.At(entry with { Combo = 1000 }, 1160).TallScaleX);
        foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
        {
            var droplet = CatchHitLighting.At(entry with { Object = fruit with { Kind = kind } }, 1000);
            Check(droplet.TallOpacity == 0 && droplet.WideOpacity == 1, "droplets only show wide flash");
        }

        var missed = fruit with { EventIndex = 1, TimeMs = 1100, X = 0 };
        var game = new CatchTestplay([fruit, missed], 5, 0);
        var time = new LightingTime();
        var clock = new CatchTestplayClock(0, 1, 0, false);
        var session = new CatchTestplaySession(game, clock, 0, false, false,
            37, 39, 16, time, 5, []);
        time.Now = 1200; session.Tick();
        var snapshot = session.Capture();
        Check(snapshot.Lighting.Length == 1 && snapshot.Lighting[0].Object == fruit,
            "live lighting contains catches only");
        Check(snapshot.Lighting[0].Combo == 1, "lighting receives combo after successful judgement");
        Check(snapshot.Lighting[0].Offset == 0, "live lighting retains catch offset");
        time.Now = 1800; session.Tick();
        Check(session.Capture().Lighting.Length == 0, "live snapshots prune expired lighting");

        var jump = fruit with { EventIndex = 1, TimeMs = 1010, X = 512 };
        var original = new CatchAutoPreview([fruit, jump], 5);
        var lazer = new CatchAutoPreview([fruit, jump], 5, lazerEditor: true);
        Check(original.At(1005).Dashing && !lazer.At(1005).Dashing,
            "Lazer canvas uses upstream impossible-jump frames without changing preview defaults");
        var overlapping = fruit with { SourceId = Guid.NewGuid(), TimeMs = 1500, X = 0 };
        var tail = fruit with { EventIndex = 1, TimeMs = 2000, X = 512 };
        var chronological = new CatchAutoPreview([fruit, overlapping, tail], 5);
        var parentOrder = new CatchAutoPreview([fruit, overlapping, tail], 5, [fruit, tail, overlapping], lazerEditor: true);
        Check(chronological.At(1500).X < 100 && parentOrder.At(1500).X > 200,
            "Lazer canvas visits complete parents before following overlapping parents");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual)
    { if (Math.Abs(expected - actual) > .00001) throw new Exception($"Expected {expected}, got {actual}"); }
    private sealed class LightingTime : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
}
