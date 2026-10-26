using System.Text.Json;
using FruitsAtelier.Core;

internal static class StoryboardTests
{
    private static readonly string DirectoryPath = Path.GetFullPath("artifacts/tests/video-storyboard/fixtures");

    public static int Run()
    {
        int failed = 0;
        Test("storyboard macros preserve whitespace and ordered substitution", StoryboardMacroTests.Run);
        Test("storyboard command compilation preserves sampled playback", StoryboardCompileTests.Run);
        Test("interval queries preserve linear-scan states and preload order without allocations", StoryboardIntervalTests.Run);
        Test("numbered hitsound triggers preserve default zero and explicit sample indexes", () =>
        {
            for (int index = 0; index <= 2; index++)
            {
                var map = new MapDocument();
                map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 3, SampleIndex = 0 });
                map.Fruits.Add(new() { TimeMs = 100, X = 256, OriginalLine = $"256,192,100,1,8,3:3:{index}:100:" });
                var objects = CatchStreamConverter.Convert(map).Objects;
                var playback = new CatchStoryboardPlayback(new(objects, [], 5),
                    ["HitSoundDrumClap0", "HitSoundDrumClap1", "HitSoundDrumClap2"], objects,
                    new HitsoundResolver(map, objects, inspectResources: false), 0);
                playback.RecordSound(objects[0]);
                var triggers = new List<StoryboardTrigger>(); long version = -1;
                playback.CopyTriggers(triggers, ref version);
                Check(triggers.Count == 1 && triggers[0].Name == "HitSoundDrumClap" + index,
                    $"Sample index {index} matched the wrong numbered trigger.");
            }
        });
        Test("independent scalar and vector scale", () =>
        {
            var state = One(" S,0,0,1000,2,4\n V,0,0,1000,3,5,5,7", 500);
            Near(12, state.ScaleX); Near(18, state.ScaleY);
        });
        Test("Move and axis commands share position components", () =>
        {
            var state = One(" M,0,0,1000,0,10,100,110\n MX,0,500,1000,200,300\n MY,0,700,1000,400,500", 750);
            Near(250, state.X); Near(400 + 50d / 3, state.Y);
        });
        Test("same-time commands retain source order", () =>
        {
            Near(7, One(" S,0,0,1000,2,2\n S,0,0,1000,7,7", 500).ScaleX);
        });
        Test("loop duration excludes initial command delay", () =>
        {
            var scene = Scene(" L,1000,3\n  M,0,100,300,0,0,100,100");
            Near(25, At(scene, 1150).X); Near(25, At(scene, 1350).X); Near(25, At(scene, 1550).X);
            Near(100, At(scene, 1700).X); Empty(scene, 1701);
        });
        Test("instant parameters persist while sprite remains alive", () =>
        {
            var state = One(" F,0,0,1000,1,1\n P,0,100,100,A\n P,0,100,100,H\n P,0,100,100,V", 900);
            Check(state.Additive && state.FlipX && state.FlipY, "Instant parameters were cleared.");
        });
        Test("interval parameter switches at its end", () =>
        {
            string commands = " F,0,0,1000,1,1\n P,0,100,500,A";
            Check(One(commands, 499).Additive, "Parameter missing before its end.");
            Check(!One(commands, 500).Additive, "Parameter remained after its end.");
        });
        Test("compressed command values form consecutive intervals", () =>
        {
            var scene = Scene(" F,0,0,100,0,1,0");
            Near(.5, At(scene, 50).Opacity); Near(.5, At(scene, 150).Opacity); Empty(scene, 201);
        });
        Test("trigger-only sprites remain hidden until activated", () =>
        {
            var scene = Scene(" T,HitSound,0,1000\n  F,0,0,100,1,0");
            Empty(scene, 20);
            var triggers = new[] { new StoryboardTrigger("HitSound", 200) };
            Near(.5, At(scene, 250, triggers).Opacity); Empty(scene, 301, triggers);
        });
        Test("trigger without time bounds retains relative timing", () =>
        {
            var scene = Scene(" T,HitSound\n  F,0,100,300,0,1");
            var triggers = new[] { new StoryboardTrigger("HitSound", 1000) };
            Near(.5, At(scene, 1200, triggers).Opacity);
        });
        Test("trigger-only animation starts at its trigger", () =>
        {
            var scene = StoryboardReader.Read("Animation,Foreground,Centre,\"a.png\",320,240,4,100,LoopForever\n T,HitSound,0,2000\n  F,0,0,1000,1,1", null, DirectoryPath);
            var triggers = new[] { new StoryboardTrigger("HitSound", 500) };
            Check(Path.GetFileName(At(scene, 650, triggers).Path) == "a1.png", "Animation did not use trigger-relative elapsed time.");
        });
        Test("variables and loop-once animation", () =>
        {
            var scene = StoryboardReader.Read("[Variables]\n$image=a.png\n[Events]\nAnimation,Foreground,Centre,\"$image\",320,240,3,100,LoopOnce\n F,0,100,1000,1,1", null, DirectoryPath);
            Check(Path.GetFileName(At(scene, 150).Path) == "a0.png", "First animation frame mismatch.");
            Check(Path.GetFileName(At(scene, 900).Path) == "a2.png", "LoopOnce did not hold the final frame.");
        });
        Test("every easing has exact initial and terminal values", () =>
        {
            foreach (var kind in Enum.GetValues<StoryboardEasingKind>())
            { var easing = new StoryboardEasing(kind); Near(0, easing.ApplyEasing(0)); Near(1, easing.ApplyEasing(1)); }
        });
        Test("bad resource does not discard later valid elements", () =>
        {
            var scene = StoryboardReader.Read("Sprite,Foreground,Centre,\"../outside.png\",320,240\n F,0,0,1000,1,1\nSprite,Foreground,Centre,\"good.png\",320,240\n F,0,0,1000,1,1", null, DirectoryPath);
            Check(scene.Diagnostics.Count > 0 && Path.GetFileName(At(scene, 500).Path) == "good.png", "Resource error was not isolated.");
        });
        Test("nonmonotonic queries reproduce independently sampled states", () =>
        {
            var scene = Scene(" F,0,0,2000,1,1\n M,0,0,2000,0,0,200,100\n L,0,4\n  R,0,0,500,0,6.283185307179586");
            var expected = Enumerable.Range(0, 201).ToDictionary(i => i * 10d, t => At(scene, t * 10d));
            var random = new Random(729);
            for (int i = 0; i < 2000; i++) { double time = random.Next(201) * 10d; Check(expected[time] == At(scene, time), "Reverse/random seek retained another timestamp's state."); }
        });
        Test("trigger windows and later activations select relative state", () =>
        {
            var scene = Scene(" T,HitSound,100,500,3\n  M,0,0,100,0,0,100,0");
            Empty(scene, 50, [new("HitSound", 50)]);
            Near(50, At(scene, 250, [new("HitSound", 200)]).X);
            Near(10, At(scene, 260, [new("HitSound", 200), new("HitSound", 250)]).X);
            Empty(scene, 650, [new("HitSound", 600)]);
        });
        Test("health crosses Pass/Fail at half health", () =>
        {
            var map = new MapDocument();
            for (int i = 0; i < 12; i++) map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
            var objects = CatchStreamConverter.Convert(map).Objects;
            var configuration = new CatchStoryboardHealth(objects, [], 5);
            var playback = new CatchStoryboardPlayback(configuration, ["Passing", "Failing", "HitObjectHit", "HitSound"], objects, new HitsoundResolver(map, objects), 0);
            var triggers = new List<StoryboardTrigger>(); long version = -1;
            for (int i = 0; i < 4; i++) playback.Judge(objects[i], false);
            Check(playback.CopyAt(1000, triggers, ref version), "Exactly half health should still pass.");
            playback.Judge(objects[4], false);
            Check(!playback.CopyAt(1000, triggers, ref version), "Fifth fruit miss did not enter Fail.");
            for (int i = 5; i < 10; i++) playback.Judge(objects[i], true);
            Check(playback.CopyAt(1000, triggers, ref version), "Successful catches did not restore Pass.");
            Check(triggers.Count(t => t.Name == "Failing") == 1 && triggers.Count(t => t.Name == "Passing") == 1, "Health transitions were duplicated or omitted.");
            Check(triggers.Count(t => t.Name == "HitSound") == 5, "Misses emitted hitsound triggers or successful catches were lost.");
        });
        Test("sample-only health keeps judgment history without sprite sound triggers", () =>
        {
            var map = new MapDocument();
            for (int i = 0; i < 12; i++) map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
            var objects = CatchStreamConverter.Convert(map).Objects;
            var original = new CatchStoryboardPlayback(new(objects, [], 5), ["HitObjectHit", "HitSound"], objects, new HitsoundResolver(map, objects), 0);
            for (int i = 0; i < 5; i++) original.Judge(objects[i], false);
            var clone = original.ForSamples();
            for (int i = 5; i < 10; i++) { original.Judge(objects[i], true); clone.Judge(objects[i], true); }
            original.Advance(2000); clone.Advance(2000);
            foreach (double time in new[] { 0d, 999, 1000, 2000 })
            {
                var a = new List<StoryboardTrigger>(); var b = new List<StoryboardTrigger>(); long av = -1, bv = -1;
                Check(original.CopyAt(time, a, ref av) == clone.CopyAt(time, b, ref bv), "Sample-only clone changed historical Pass/Fail state.");
                Check(b.All(t => !t.Name.StartsWith("HitSound", StringComparison.Ordinal) && t.Name != "HitObjectHit"), "Sample-only clone retained visual sound triggers.");
            }
        });
        Test("native simulation owning callback covers all catch object kinds", () =>
        {
            foreach (bool hit in new[] { true, false })
            {
                var objects = Enum.GetValues<CatchObjectKind>().Select((kind, i) => new ConvertedCatchObject(Guid.NewGuid(), i, kind, 100 + 100 * i, hit ? 256 : 0, 0, 0, i)).ToArray();
                var clock = new Clock(); var judgements = new List<(CatchObjectKind Kind, bool Hit)>(); int advanced = 0;
                var session = new CatchTestplaySession(new(objects, 5, 0), new(0, 1, 0, false), 0, false, false, 37, 39, 16,
                    clock, 5, [], judged: (item, caught) => judgements.Add((item.Kind, caught)), advanced: _ => advanced++);
                clock.Advance(1000); session.Tick(); session.Tick();
                Check(judgements.Count == objects.Length && judgements.All(j => j.Hit == hit), "Owning judgement callback omitted, duplicated or changed an object result.");
                Check(judgements.Select(j => j.Kind).Distinct().Count() == objects.Length && advanced > 0, "A catch kind or owning time update was omitted.");
            }
        });
        Test("flips and negative vector scale adjust the declared origin", () =>
        {
            StoryboardSpriteState State(string command, double time = 100)
            {
                var scene = StoryboardReader.Read("Sprite,Foreground,TopLeft,\"a.png\",10,20\n F,0,0,1000,1,1\n" + command, null, DirectoryPath);
                return At(scene, time);
            }
            Near(1, State(" P,0,500,500,H").OriginX);
            Near(0, State(" P,0,500,800,H").OriginX);
            Near(1, State(" P,0,500,800,H", 600).OriginX);
            Near(1, State(" V,0,0,1000,-2,3,-2,3").OriginX);
            Near(0, State(" V,0,0,1000,-2,3,-2,3\n P,0,0,0,H").OriginX);
            Near(0, State(" S,0,0,1000,-2,-2").OriginX);
        });
        Test("primary variables remain available to shared storyboard text", () =>
        {
            var scene = StoryboardReader.Read("[Variables]\n$image=a.png\n[Events]\n", "[Events]\nSprite,Foreground,Centre,\"$image\",320,240\n F,0,0,1000,1,1", DirectoryPath);
            Check(Path.GetFileName(At(scene, 100).Path) == "a.png", "Shared source lost a primary variable.");
        });
        Test("retrigger cancels delayed future commands while retaining current state", () =>
        {
            var scene = Scene(" F,0,0,2000,1,1\n T,HitSound,0,1000\n  MX,0,0,100,0,100\n  C,0,300,400,255,255,255,255,0,0");
            StoryboardTrigger[] triggers = [new("HitSound", 100), new("HitSound", 250)];
            Check(At(scene, 425, triggers).Colour == 0xFFFFFF, "A cancelled instance's future colour command executed.");
            Check(At(scene, 600, triggers).Colour == 0xFF8080, "The latest trigger's delayed colour command did not execute.");
            Near(75, At(scene, 175, triggers).X);
            Check(At(scene, 425, triggers).Colour == 0xFFFFFF, "Reverse seek retained a later trigger state.");
        });
        Test("explicit trigger group cancels another trigger name's future transform", () =>
        {
            var scene = Scene(" F,0,0,2000,1,1\n T,HitSound,0,1000,5\n  MX,0,300,400,0,100\n T,HitObjectHit,0,1000,5\n  MY,0,0,100,0,100");
            var state = At(scene, 450, [new("HitSound", 100), new("HitObjectHit", 250)]);
            Near(320, state.X); Near(100, state.Y);
        });
        return failed == 0 ? 0 : 1;

        void Test(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
    }

    public static int Corpus(string path, string reportPath)
    {
        string? shared = System.IO.Directory.GetFiles(Path.GetDirectoryName(path)!, "*.osb").FirstOrDefault();
        var scene = StoryboardReader.Read(File.ReadAllText(path), shared is null ? null : File.ReadAllText(shared), Path.GetDirectoryName(path)!);
        var states = new List<StoryboardSpriteState>();
        var expected = new Dictionary<double, StoryboardSpriteState[]>();
        for (int time = 0; time <= 360000; time += 137) { scene.Evaluate(time, states); expected.Add(time, states.ToArray()); }
        var random = new Random(729); var times = expected.Keys.ToArray();
        for (int i = 0; i < 10000; i++)
        {
            double time = times[random.Next(times.Length)]; scene.Evaluate(time, states);
            Check(states.SequenceEqual(expected[time]), "Real-map random access differs from forward evaluation at " + time);
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { path, shared, scene.SpriteCount, images = scene.ImagePaths.Count,
            scene.Diagnostics, sampledTimestamps = expected.Count, randomQueries = 10000,
            maximumVisible = expected.Values.Max(s => s.Length), note = "Evaluation state equality across unrelated timestamps; excludes rendering and video decoding." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {Path.GetFileName(path)}: {scene.SpriteCount} sprites, {scene.Diagnostics.Count} diagnostics, 10000 nonmonotonic state comparisons");
        return scene.Diagnostics.Count == 0 ? 0 : 1;
    }

    private static StoryboardScene Scene(string commands) => StoryboardReader.Read("Sprite,Foreground,Centre,\"a.png\",320,240\n" + commands, null, DirectoryPath);
    private static StoryboardSpriteState One(string commands, double time) => At(Scene(commands), time);
    private static StoryboardSpriteState At(StoryboardScene scene, double time, IReadOnlyList<StoryboardTrigger>? triggers = null)
    {
        var states = new List<StoryboardSpriteState>(); scene.Evaluate(time, states, triggers: triggers);
        Check(states.Count == 1, $"Expected one visible sprite at {time}, got {states.Count}."); return states[0];
    }
    private static void Empty(StoryboardScene scene, double time, IReadOnlyList<StoryboardTrigger>? triggers = null)
    { var states = new List<StoryboardSpriteState>(); scene.Evaluate(time, states, triggers: triggers); Check(states.Count == 0, "Sprite visible at " + time); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < 1e-8, $"Expected {expected}, got {actual}.");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Clock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance(long amount) => milliseconds += amount;
    }
}
