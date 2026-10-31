using FruitsAtelier.Core;

internal static class StoryboardMacroTests
{
    internal static void Run()
    {
        RawWhitespaceCommands();
        OrderedSubstitution();
        RandomizedSubstitution();
        LineBoundaries();
        SceneExpansionLimit();
        ExpansionLimit();
        SharedFileAdmission();
        ResourceAdmissionRoutes();
        Cancellation();
    }

    private static void RawWhitespaceCommands()
    {
        string source = "[Variables]\n$a=Sprite,Foreground,Centre,a.png,320,240\n"
            + "$\t= F,0,0,1000,1,1\n$r= R,0,0,1000,0,2\n$ = M,0,0,1000,0,0,100,200\n"
            + "[Events]\n$a\n$\t\n$r\n$ \n";
        var scene = StoryboardReader.Read(source, null, Path.GetFullPath("artifacts/tests/storyboard-macros"));
        var states = new List<StoryboardSpriteState>();
        scene.Evaluate(500, states);
        Check(scene.SpriteCount == 1 && scene.Diagnostics.Count == 0 && states.Count == 1,
            "Whitespace-named macros or indented command values were discarded.");
        var state = states[0];
        Check(state.X == 50 && state.Y == 100 && state.Rotation == 1 && state.Opacity == 1,
            "Macro expansion changed command position, opacity or rotation.");

        foreach (char suffix in new[] { '\t', '\v', '\f', ' ', '\u0085', '\u00a0', '\0' })
            Compare([("$" + suffix, " leading and trailing ")], "$" + suffix);
    }

    private static void OrderedSubstitution()
    {
        Compare([("$a", "A"), ("$b", "B")], " $a/$b/$a $missing $");
        Compare([("$a", "b"), ("$b", "x")], "$$a");
        Compare([("$a", ""), ("$b", "x")], "$$ab");
        Compare([("$a", "x$a")], "$a$a");
        Compare([("$a", "$b"), ("$b", "x")], "$a");
        Compare([("$b", "x"), ("$a", "$b")], "$a");
        Compare([("$a", "A"), ("$ab", "B")], "$ab");
        Compare([("$ab", "B"), ("$a", "A")], "$ab");
        Compare([("literal", "$a"), ("$a", "done")], "literal");
        Compare([("$a", "$b"), ("$b", "1"), ("$a", "2$b")], "$a");
        Compare([("$long", "ok"), ("x", "y")], new string('x', 70000) + "$long");

        var variables = new StoryboardVariables();
        variables.Set("$a", "old");
        Check(variables.Expand("$a") == "old", "Initial macro was not expanded.");
        variables.Set("$a", "new");
        Check(variables.Expand("$a") == "new", "Overwriting a prepared macro retained its previous value.");
        variables.Set("$b", "$a");
        Check(variables.Expand("$b") == "$a", "Introducing a recursive value did not restore ordered substitution.");
        variables.Set("$b", "final");
        Check(variables.Expand("$b") == "final", "Removing a recursive value retained stale macro data.");

        var shared = StoryboardReader.Read("[Variables]\n$a=old.png\n",
            "[Variables]\n$a=new.png\n[Events]\nSprite,Foreground,Centre,$a,320,240\n F,0,0,1000,1,1",
            Path.GetFullPath("artifacts/tests/storyboard-macros"));
        Check(shared.ImagePaths.Count == 1 && Path.GetFileName(shared.ImagePaths[0]) == "new.png",
            "Shared storyboard definitions did not overwrite primary variables.");
    }

    private static void RandomizedSubstitution()
    {
        var random = new Random(47071);
        string[] keys = ["$a", "$b", "$c", "$\t", "$\0", "$long", "plain"];
        string[] values = ["", "a", "b", " ", "_", "12", "$a", "x$b"];
        const string alphabet = "$abc _12";
        for (int test = 0; test < 400; test++)
        {
            var definitions = new List<(string, string)>();
            bool simple = test % 2 == 0;
            for (int i = 0; i < 12; i++)
                definitions.Add((keys[random.Next(simple ? 5 : keys.Length)], values[random.Next(simple ? 6 : values.Length)]));
            string line = new(Enumerable.Range(0, 150).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            Compare(definitions, line);
        }
    }

    private static void LineBoundaries()
    {
        string source = "[General]\nIgnored\r[Events]\nSprite,Foreground,Centre,ignored.png,0,0\n F,0,0,1000,1,1\n"
            + "[Events]\nSprite,Foreground,Centre,kept.png,0,0\n F,0,0,1000,1,1\n Q,0,0,1000,1,1\n";
        var scene = StoryboardReader.Read(source, null, Path.GetFullPath("artifacts/tests/storyboard-macros"));
        Check(scene.SpriteCount == 1 && scene.ImagePaths.Count == 1 && Path.GetFileName(scene.ImagePaths[0]) == "kept.png"
            && scene.Diagnostics.SequenceEqual(["8: Q"]), "LF-only sections or diagnostic line numbers changed.");
        var crlf = StoryboardReader.Read(source.Replace("\n", "\r\n"), null,
            Path.GetFullPath("artifacts/tests/storyboard-macros"));
        Check(crlf.Diagnostics.SequenceEqual(scene.Diagnostics) && crlf.SpriteCount == scene.SpriteCount,
            "CRLF changed storyboard line numbers or sections.");
    }

    private static void Cancellation()
    {
        var variables = new StoryboardVariables();
        variables.Set("$a", "replacement");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { variables.Expand("$a", cancelled.Token); throw new Exception("Cancelled expansion completed."); }
        catch (OperationCanceledException) { }
        string line = string.Concat(Enumerable.Repeat(" $a", 2000000));
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var work = Task.Run(() => { started.Set(); return variables.Expand(line, cancellation.Token); });
        Check(started.Wait(TimeSpan.FromSeconds(5)), "Large-line expansion did not start.");
        cancellation.Cancel();
        try { work.GetAwaiter().GetResult(); throw new Exception("Large-line expansion ignored cancellation."); }
        catch (OperationCanceledException) { }
    }

    private static void ExpansionLimit()
    {
        var chained = new StoryboardVariables();
        int definitionCharacters = 0;
        for (int i = 0; i < 20; i++)
        {
            string key = "$v" + i.ToString("D2"), next = "$v" + (i + 1).ToString("D2");
            chained.Set(key, next + next);
            definitionCharacters += key.Length + next.Length * 2 + 2;
        }
        chained.Set("$v20", "x");
        Check(chained.Expand("$v00").Length == 1 << 20, "Bounded chained macro substitution changed semantics");
        Console.WriteLine($"Macro amplification: {definitionCharacters + 7} definition characters -> {1 << 20} expanded characters.");
        string limitMessage = FruitsAtelier.Localization.Strings.Get("core.storyboard.expansionLimit");

        foreach (string key in new[] { "$a", "$long" })
        {
            var variables = new StoryboardVariables();
            variables.Set(key, new string('x', 128 * 1024));
            string line = string.Concat(Enumerable.Repeat(key, 257));
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            try { variables.Expand(line); throw new Exception("Oversized macro expansion was accepted"); }
            catch (InvalidDataException error)
            {
                Check(error.Message == limitMessage,
                    "Macro expansion limit did not report the localized loading failure");
            }
            Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1_000_000,
                "Oversized macro expansion allocated its expanded output before rejecting it");
        }
        var shrinking = new StoryboardVariables();
        shrinking.Set("$b", "");
        shrinking.Set("$a", new string('x', OsuBeatmapReader.MaximumFileBytes));
        string atLimit = shrinking.Expand("$a$b");
        Check(atLimit.Length == OsuBeatmapReader.MaximumFileBytes && atLimit[0] == 'x' && atLimit[^1] == 'x',
            "A shrinking macro caused valid expansion at the supported limit to be rejected");
    }

    private static void Compare(IEnumerable<(string Key, string Value)> definitions, string line)
    {
        var variables = new StoryboardVariables();
        var reference = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in definitions) { variables.Set(key, value); reference[key] = value; }
        string expected = line;
        foreach (var pair in reference) expected = expected.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        Check(variables.Expand(line) == expected, "Macro expansion differs from insertion-ordered ordinal Replace.");
    }

    private static void SharedFileAdmission()
    {
        string directory = Path.GetFullPath("artifacts/tests/storyboard-macros");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "size.osb");
        using (var output = File.Create(path)) output.SetLength(OsuBeatmapReader.MaximumFileBytes + 1L);
        string expected = FruitsAtelier.Localization.Strings.Get("core.storyboard.fileLimit");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        try { StoryboardReader.ReadSharedFile(path); throw new Exception("Oversized shared storyboard was admitted"); }
        catch (InvalidDataException error) { Check(error.Message == expected, "Shared storyboard admission reported an unexpected diagnostic"); }
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1_000_000, "Oversized shared storyboard allocated its source text before rejection");

        const string text = "[General]\nWidescreenStoryboard:1\n// Unicode: 雪\n";
        File.WriteAllText(path, text, System.Text.Encoding.Unicode);
        Check(StoryboardReader.ReadSharedFile(path) == text, "Shared storyboard admission changed BOM detection or Unicode text");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { StoryboardReader.ReadSharedFile(path, cancelled.Token); throw new Exception("Shared storyboard admission ignored cancellation"); }
        catch (OperationCanceledException) { }
        using var growing = new GrowingStoryboardStream();
        try { StoryboardReader.ReadSharedText(growing); throw new Exception("Growing shared storyboard bypassed its byte budget"); }
        catch (InvalidDataException error) { Check(error.Message == expected, "Growing storyboard reported an unexpected diagnostic"); }
        Check(growing.Position <= OsuBeatmapReader.MaximumFileBytes + 81920L, "Growing storyboard was read without a bounded byte count");
    }

    private sealed class GrowingStoryboardStream : Stream
    {
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, OsuBeatmapReader.MaximumFileBytes + 1L - position);
            buffer[..count].Fill((byte)' '); position += count;
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void ResourceAdmissionRoutes()
    {
        string directory = Path.GetFullPath("artifacts/tests/storyboard-macros/resource-admission");
        Directory.CreateDirectory(directory);
        string shared = Path.Combine(directory, "shared.osb");
        using (var output = File.Create(shared)) output.SetLength(OsuBeatmapReader.MaximumFileBytes + 1L);
        var map = new MapDocument { SourcePath = Path.Combine(directory, "map.osu") };
        var failures = new List<string>();
        ExpectLimit(() => Aimod.AnalyzeResources(map), "core.storyboard.fileLimit", "AiMod shared file admission");
        ExpectLimit(() => HitsoundResourcePlan.Delete(map, [map]), "core.storyboard.fileLimit", "Hitsound cleanup shared file admission");
        File.WriteAllText(shared, "[Events]\nSample,0,Foreground,protected.wav,100\n");
        File.WriteAllBytes(Path.Combine(directory, "protected.wav"), [0, 1]);
        _ = Aimod.AnalyzeResources(map);
        HitsoundResourcePlan.Delete(map, [map]).Apply();
        Check(File.Exists(Path.Combine(directory, "protected.wav")), "Supported shared sample was lost during hitsound cleanup");

        map.OriginalSections.Add(new() { Name = "Variables", Lines = { "$a=" + new string('x', 128 * 1024) } });
        map.OriginalSections.Add(new() { Name = "Events", Lines = { string.Concat(Enumerable.Repeat("$a", 257)) } });
        ExpectLimit(() => Aimod.AnalyzeResources(map), "core.storyboard.expansionLimit", "AiMod macro expansion");
        if (failures.Count > 0) throw new Exception(string.Join("; ", failures));

        void ExpectLimit(Action action, string key, string label)
        {
            try { action(); failures.Add(label + " bypassed its size budget"); }
            catch (InvalidDataException error) { Check(error.Message == FruitsAtelier.Localization.Strings.Get(key), label + " reported an unexpected diagnostic"); }
        }
    }

    private static void SceneExpansionLimit()
    {
        var variables = new StoryboardVariables();
        string line = new('x', OsuBeatmapReader.MaximumFileBytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < ProjectSerializer.MaximumFileBytes / line.Length; i++)
            Check(ReferenceEquals(variables.Expand(line), line), "Unchanged storyboard lines were copied while consuming the scene budget");
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1_000_000, "Unchanged scene budget checks allocated expanded text");
        string expected = FruitsAtelier.Localization.Strings.Get("core.storyboard.sceneExpansionLimit");
        try { variables.Expand("x"); throw new Exception("Repeated individually supported lines bypassed the scene expansion budget"); }
        catch (InvalidDataException error) { Check(error.Message == expected, "Scene expansion budget reported an unexpected diagnostic"); }
        Check(new StoryboardVariables().Expand("x") == "x", "Scene expansion budget leaked into another reader scope");

        var sharedBudget = new StoryboardExpansionBudget(10);
        var firstScope = new StoryboardVariables(sharedBudget);
        firstScope.Set("$a", "123456");
        Check(firstScope.Expand("$a") == "123456", "Shared analysis budget changed the first source's substitutions");
        var secondScope = new StoryboardVariables(sharedBudget);
        Check(secondScope.Expand("$a") == "$a", "Shared analysis budget leaked variables between source files");
        secondScope.Set("$b", "123");
        try { secondScope.Expand("$b"); throw new Exception("A second file reset the shared analysis expansion budget"); }
        catch (InvalidDataException error) { Check(error.Message == expected, "Shared analysis budget reported an unexpected diagnostic"); }
        Check(firstScope.Expand("xy") == "xy", "Rejected shared output consumed the remaining analysis budget");

        var ordered = LeaveCharacters(10);
        ordered.Set("$a", string.Concat(Enumerable.Repeat("$b", 10)));
        ordered.Set("$b", "x");
        Check(ordered.Expand("$a") == new string('x', 10), "Ordered intermediate text incorrectly consumed the final-output scene budget");

        var oversized = LeaveCharacters(10);
        oversized.Set("$a", new string('x', 128 * 1024));
        allocated = GC.GetAllocatedBytesForCurrentThread();
        try { oversized.Expand("$a"); throw new Exception("Expanded output exceeded the remaining scene budget"); }
        catch (InvalidDataException error) { Check(error.Message == expected, "Remaining scene budget reported an unexpected diagnostic"); }
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 1_000_000, "Scene overflow allocated its expanded result before rejection");

        static StoryboardVariables LeaveCharacters(int remaining)
        {
            var scope = new StoryboardVariables();
            string block = new('x', 1024 * 1024);
            for (int i = 0; i < ProjectSerializer.MaximumFileBytes / block.Length - 1; i++) scope.Expand(block);
            scope.Expand(block[..^remaining]);
            return scope;
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
