using System.Diagnostics;
using System.Reflection;
using System.Text;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class StoryboardSourceCacheTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run()
    {
        string root = OperatingSystem.IsMacOS()
            ? Path.Combine("/private/tmp", "atelier-storyboard-source-" + Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(Path.Combine("artifacts/tests/storyboard-source-cache", Guid.NewGuid().ToString("N")));
        using (var fixture = new Fixture(Path.Combine(root, "ordinary")))
        {
            foreach ((int index, string line) in new[] { (1, "AudioLeadIn:250"), (2, "AudioFilename:replacement.ogg"), (3, "SampleSet:Soft") })
                fixture.Change(() => fixture.General.Lines[index] = line, rebuild: false);
            fixture.Change(() => fixture.General.Lines[0] = "WidescreenStoryboard:1", rebuild: true);
            if (!fixture.Scene.Widescreen) throw new Exception("Widescreen edit did not reach the storyboard scene.");
            fixture.Change(() => fixture.Events.Lines[2] = " M,0,0,10000,100,240,540,240", rebuild: true);
            fixture.Change(() => fixture.Variables.Lines[0] = "$image=b.png", rebuild: true);
            fixture.Change(() => fixture.Events.Lines.Add(" UnknownCommand,0,0,10000,1"), rebuild: true);
            string originalDiagnostic = fixture.Scene.Diagnostics.Single();
            int originalDiagnosticLine = int.Parse(originalDiagnostic.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
            fixture.Change(() => fixture.General.Lines.Add("AudioLeadIn:500"), rebuild: true);
            int shiftedDiagnosticLine = int.Parse(fixture.Scene.Diagnostics.Single().Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
            if (shiftedDiagnosticLine != originalDiagnosticLine + 1)
                throw new Exception("Adding an ordinary General line did not advance the storyboard diagnostic line number.");
            fixture.Change(() => fixture.General.Lines.RemoveAt(fixture.General.Lines.Count - 1), rebuild: true);
            if (fixture.Scene.Diagnostics.Single() != originalDiagnostic)
                throw new Exception("Removing an ordinary General line did not restore the storyboard diagnostic line number.");

            var pending = new TaskCompletionSource<StoryboardScene>(TaskCreationOptions.RunContinuationsAsynchronously);
            ((CancellationTokenSource?)Get(fixture.View, "storyboardCancellation"))?.Dispose();
            var cancellation = new CancellationTokenSource();
            Set(fixture.View, "storyboardLoad", pending.Task);
            Set(fixture.View, "storyboardCancellation", cancellation);
            fixture.General.Lines[1] = "AudioLeadIn:750";
            Call(fixture.View, "RefreshStoryboard");
            if (!ReferenceEquals(Get(fixture.View, "storyboardLoad"), pending.Task) || cancellation.IsCancellationRequested)
                throw new Exception("An unrelated General edit replaced or cancelled pending storyboard preparation.");
            pending.SetResult(fixture.Scene); fixture.Settle(); fixture.CheckOutput();

            string second = Path.Combine(root, "relocated");
            Fixture.WriteImages(second);
            fixture.Change(() => fixture.View.Document.SourcePath = Path.Combine(second, "map.osu"), rebuild: true);
            var before = fixture.Scene;
            Call(fixture.View, "RetireStoryboard", false, false);
            Call(fixture.View, "RefreshStoryboard"); fixture.Settle();
            if (ReferenceEquals(before, fixture.Scene)) throw new Exception("Retired storyboard source was reused.");
            fixture.CheckOutput();
        }

        using (var fixture = new Fixture(Path.Combine(root, "duplicates")))
        {
            fixture.Change(() => fixture.General.Lines.Add("WidescreenStoryboard:1"), rebuild: true);
            if (!fixture.Scene.Widescreen) throw new Exception("The last duplicate Widescreen field did not win.");
            fixture.Change(() => fixture.General.Lines.Add("WidescreenStoryboard:0"), rebuild: true);
            if (fixture.Scene.Widescreen) throw new Exception("A later Widescreen field did not override an earlier value.");
            var laterGeneral = new OsuSection { Name = "General", Lines = { "WidescreenStoryboard:1" } };
            fixture.Change(() => fixture.View.Document.OriginalSections.Add(laterGeneral), rebuild: true);
            if (!fixture.Scene.Widescreen) throw new Exception("A second General section did not override the first section's Widescreen field.");
            fixture.Change(() => laterGeneral.Lines[0] = "WidescreenStoryboard:0", rebuild: true);
            fixture.Change(() => fixture.General.Lines[0] = "WidescreenStoryboard:1", rebuild: true);
            if (fixture.Scene.Widescreen) throw new Exception("An earlier General section overrode a later section's Widescreen field.");
        }

        string sharedDirectory = Path.Combine(root, "shared"); Directory.CreateDirectory(sharedDirectory);
        File.WriteAllText(Path.Combine(sharedDirectory, "shared.osb"), "[General]\nWidescreenStoryboard:1\nWidescreenStoryboard:0\n");
        using (var fixture = new Fixture(sharedDirectory))
        {
            fixture.Change(() => fixture.General.Lines[0] = "WidescreenStoryboard:1", rebuild: true);
            if (fixture.Scene.Widescreen) throw new Exception("Shared storyboard General did not override the beatmap's Widescreen field.");
        }

        foreach (bool embeddedNewline in new[] { false, true })
        using (var fixture = new Fixture(Path.Combine(root, embeddedNewline ? "embedded-newline" : "embedded-section")))
        {
            fixture.Change(() =>
            {
                fixture.General.Lines[1] = embeddedNewline ? "AudioLeadIn:0\n[Events]" : " [Events] ";
                fixture.General.Lines[2] = "Sprite,Foreground,Centre,\"a.png\",123,240";
                fixture.General.Lines[3] = " F,0,0,10000,1,1";
            }, rebuild: true);
            fixture.Change(() => fixture.General.Lines[3] = " F,0,0,10000,0.5,0.5", rebuild: true);
            if (!fixture.States(500).Any(s => s.X == 123 && s.Opacity == .5))
                throw new Exception("General's embedded Events section lost the Reader's sprite semantics.");
        }
        Console.WriteLine("PASS storyboard General dependencies, pending preparation, source boundaries and Reader-equivalent output");
    }

    private static object? Get(EditorView view, string name) => typeof(EditorView).GetField(name, Private)!.GetValue(view);
    private static void Set(EditorView view, string name, object value) => typeof(EditorView).GetField(name, Private)!.SetValue(view, value);
    private static void Call(EditorView view, string name, params object[] arguments) => typeof(EditorView).GetMethod(name, Private)!.Invoke(view, arguments);

    private sealed class Fixture : IDisposable
    {
        internal EditorView View { get; } = new(false);
        internal OsuSection General => View.Document.OriginalSections.First(s => s.Name == "General");
        internal OsuSection Variables => View.Document.OriginalSections.Single(s => s.Name == "Variables");
        internal OsuSection Events => View.Document.OriginalSections.Single(s => s.Name == "Events");
        internal StoryboardScene Scene => (StoryboardScene)Get(View, "storyboardScene")!;

        internal Fixture(string directory)
        {
            WriteImages(directory);
            View.InitializeLibrary(false, new LibrarySettings { FirstRunSetupCompleted = true, EnableVideoStoryboard = true, Workspace = Path.Combine(directory, "workspace") });
            View.RequestViewPreference = null;
            var map = new MapDocument { SourcePath = Path.Combine(directory, "map.osu"), DurationMs = 10000 };
            map.OriginalSections.Add(new() { Name = "General", Lines = { "WidescreenStoryboard:0", "AudioLeadIn:0", "AudioFilename:original.ogg", "SampleSet:Normal" } });
            map.OriginalSections.Add(new() { Name = "Variables", Lines = { "$image=a.png" } });
            map.OriginalSections.Add(new() { Name = "Events", Lines = { "Sprite,Foreground,Centre,\"$image\",320,240", " F,0,0,10000,1,1", " M,0,0,10000,0,240,640,240" } });
            View.LoadDocument(map); Call(View, "RefreshStoryboard"); Settle(); CheckOutput();
        }

        internal static void WriteImages(string directory)
        {
            Directory.CreateDirectory(directory);
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII=");
            foreach (string name in new[] { "a.png", "b.png" }) File.WriteAllBytes(Path.Combine(directory, name), png);
        }

        internal void Change(Action change, bool rebuild)
        {
            var before = Scene;
            change(); Call(View, "RefreshStoryboard");
            if (!rebuild && Get(View, "storyboardLoad") is not null)
                throw new Exception("An unrelated General value queued storyboard preparation.");
            Settle();
            if (ReferenceEquals(before, Scene) == rebuild)
                throw new Exception(rebuild ? "A storyboard source dependency reused its old scene." : "An unrelated General value rebuilt its scene.");
            CheckOutput();
        }

        internal void Settle()
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Call(View, "PumpStoryboard");
                if (Get(View, "storyboardLoad") is null)
                {
                    if (Get(View, "storyboardScene") is null) throw new Exception("Storyboard preparation did not publish a scene.");
                    return;
                }
                Thread.Sleep(1);
            } while (timer.Elapsed.TotalSeconds < 5);
            throw new TimeoutException("Storyboard source preparation did not settle.");
        }

        internal List<StoryboardSpriteState> States(double time)
        {
            var states = new List<StoryboardSpriteState>(); Scene.Evaluate(time, states); return states;
        }

        internal void CheckOutput()
        {
            var text = new StringBuilder();
            foreach (var section in View.Document.OriginalSections.Where(s => s.Name is "General" or "Variables" or "Events"))
            {
                text.Append('[').Append(section.Name).AppendLine("]");
                foreach (string line in section.Lines) text.AppendLine(line);
            }
            string directory = Path.GetDirectoryName(View.Document.SourcePath)!;
            string? shared = Directory.EnumerateFiles(directory, "*.osb").Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            var reference = StoryboardReader.Read(text.ToString(), shared is null ? null : File.ReadAllText(shared), directory);
            if (Scene.Widescreen != reference.Widescreen || Scene.SpriteCount != reference.SpriteCount
                || Scene.BackgroundPath != reference.BackgroundPath || !Scene.Diagnostics.SequenceEqual(reference.Diagnostics)
                || !Scene.Videos.SequenceEqual(reference.Videos) || !Scene.Samples.SequenceEqual(reference.Samples))
                throw new Exception("Cached storyboard metadata differs from reading the full original sections.");
            var expected = new List<StoryboardSpriteState>();
            foreach (double time in new[] { 0d, 500d, 10000d, 2500d, 1000d })
            {
                reference.Evaluate(time, expected);
                if (!States(time).SequenceEqual(expected)) throw new Exception("Cached storyboard states differ from the full original source.");
            }
        }

        public void Dispose()
        {
            Call(View, "RetireStoryboard", false, false);
            View.StopFileMonitoring(); View.CompleteLibraryCacheWrites();
        }
    }
}
