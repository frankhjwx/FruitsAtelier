using FruitsAtelier.Core;

internal static class StoryboardMacroTests
{
    internal static void Run()
    {
        RawWhitespaceCommands();
        OrderedSubstitution();
        RandomizedSubstitution();
        LineBoundaries();
        ExpansionLimit();
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

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
