using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class KeybindingTests
{
    public static void Run()
    {
        string language = L.Language;
        try { L.SetLanguage("en"); Catalog(); Routing(); Settings(); GameplayChords(); Hints(); DropletSelectionLockTests.Tooltips(); }
        finally { L.SetLanguage(language); }
        Console.WriteLine("PASS shortcut catalog, routing, contexts, profiles, capture, conflicts, persistence and layout");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Hints()
    {
        var ui = new Ui(false); var view = ui.View;
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (bool lazer in new[] { false, true })
        {
            L.SetLanguage(locale); view.LibrarySettings.LazerEditing = lazer;
            var command = Command("hitWhistle", EditorView.ShortcutScope.Hitsounding, lazer);
            string body = L.Get("hitsounding.hint.brush", L.Get("timing.soft"));
            view.LibrarySettings.ShortcutBindings[command.StorageKey] = [ShortcutBinding.From(17, 'U'), ShortcutBinding.From('J')];
            string hint = view.ShortcutHint(body, command.Id, command.Scope);
            Check(hint.Contains("Ctrl + U / J") && hint.EndsWith(body[body.IndexOf('\n')..]), "hint shows all bindings and preserves mouse instructions");
            Check(view.ShortcutKeys(command.Id, command.Scope, first: true) == "Ctrl + U", "button keycap shows first configured chord");
            view.LibrarySettings.ShortcutBindings[command.StorageKey] = [];
            Check(view.ShortcutHint(body, command.Id, command.Scope) == body, "unbound hint retains only its description, without empty parentheses");
            Check(view.ShortcutKeys(command.Id, command.Scope) == "", "unbound button and menu have no shortcut text");
            view.LibrarySettings.ShortcutBindings.Remove(command.StorageKey);
            Check(view.ShortcutKeys(command.Id, command.Scope) == "A", "restoring defaults restores the hint");
        }
    }
    private static EditorView.ShortcutCommand Command(string id, EditorView.ShortcutScope scope = EditorView.ShortcutScope.Compose, bool lazer = false)
        => EditorView.ShortcutCommands.Single(c => c.Id == id && c.Scope == scope && c.Lazer == lazer);
    private static void Bind(EditorView view, string id, params int[] keys) => view.LibrarySettings.ShortcutBindings[Command(id).StorageKey] = [ShortcutBinding.From(keys)];
    private static void Press(EditorView view, int key, bool ctrl = false, bool shift = false, bool alt = false)
    { view.SetModifiers(alt, shift); view.KeyDown(key, ctrl, shift); view.KeyUp(key); view.SetModifiers(false, false); }
    private static void Catalog()
    {
        Check(EditorView.ShortcutCommands.Select(c => c.StorageKey).Distinct().Count() == EditorView.ShortcutCommands.Length, "stable command IDs are unique");
        foreach (var command in EditorView.ShortcutCommands)
        {
            Check(!L.Get(command.NameKey).StartsWith('['), "command name is localized");
            Check(L.Get(command.DescriptionKey).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 20, "descriptions use at most twenty words");
            Check(command.Defaults.All(b => b.Keys.Length > 0), "commands have executable defaults");
        }
        Check(ShortcutBinding.From(17, 65).Same(ShortcutBinding.From(65, 162)), "left/right modifiers normalize");
        Check(ShortcutBinding.From(65).Overlaps(ShortcutBinding.From(65, 66)), "simultaneous chord prefixes conflict");
        Check(!ShortcutBinding.From(65).Overlaps(ShortcutBinding.From(17, 65)), "distinct modifier sets coexist");
    }
    private static void Routing()
    {
        var ui = new Ui(false); var view = ui.View;
        Bind(view, "select", 'U');
        Press(view, '2'); Check(view.ActiveTool == "Fruit", "default tool survives overrides");
        Press(view, '1'); Check(view.ActiveTool == "Fruit", "reassigned default no longer executes");
        Press(view, 'U'); Check(view.ActiveTool == "Select", "new binding executes");
        view.LibrarySettings.LazerEditing = true;
        Press(view, '2'); Press(view, 'U'); Check(view.ActiveTool == "Fruit", "Default override does not leak into lazer");
        Press(view, '1'); Check(view.ActiveTool == "Select", "lazer keeps its own default");
        view.LibrarySettings.LazerEditing = false;
        Bind(view, "fruit", 17, 18, 16, 'P');
        Press(view, 'P', true, true, true); Check(view.ActiveTool == "Fruit", "arbitrary modifier combination executes through existing routing");
        Press(view, 'U'); Bind(view, "fruit", 'K', 'J');
        view.KeyDown('K', false, false); Check(view.ActiveTool == "Select", "multi-key prefix waits");
        view.KeyDown('J', false, false); Check(view.ActiveTool == "Fruit", "simultaneous nonmodifier chord executes");
        view.KeyUp('K'); view.KeyUp('J');
        view.LibrarySettings.ShortcutBindings[Command("compose", EditorView.ShortcutScope.Timing).StorageKey] = [ShortcutBinding.From('U')];
        Press(view, 114); Check(view.TimingPageVisible, "Timing opens with default page key");
        Press(view, 112); Check(view.TimingPageVisible, "Timing's replaced default stays disabled");
        Press(view, 'U'); Check(!view.TimingPageVisible, "same physical key runs the active interface command");
        int shots = 0; view.RequestScreenshot = () => shots++;
        view.LibrarySettings.ShortcutBindings[Command("screenshot", EditorView.ShortcutScope.Global).StorageKey] = [ShortcutBinding.From('O')];
        Press(view, 'O'); Press(view, 'O'); Press(view, 123);
        Check(shots == 2, "mapped global key releases its latch and old F12 stays disabled");
        view.OpenSettings(); ui.Paint();
        Check(view.FindShortcutConflicts(Command("screenshot", EditorView.ShortcutScope.Global), ShortcutBinding.From(17, 'D'))
            .Any(c => c.Scope == EditorView.ShortcutScope.Compose && c.Id == "duplicate"), "global changes detect local conflicts");
        var r = view.SettingsBounds; ui.Click(r.X + 35, r.Y + 78 + 6 * 48 + 10);
        ui.Paint(); r = view.SettingsBounds;
        ui.Click(r.X + 250, r.Y + 150); ui.Type("fruit"); ui.Paint();
        Check(view.IsEditingText && ui.Canvas.Texts.Any(t => t.Value == "fruit"), "search accepts text without firing a custom command");
        view.KeyDown(27, false, false); view.KeyUp(27); view.KeyDown(27, false, false); view.KeyUp(27);
        Check(!view.IsDirty, "settings and bindings do not edit content");
    }
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
    private static void GameplayChords()
    {
        var clock = new Clock();
        var session = new CatchTestplaySession(new CatchTestplay(
            [new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 10000, 256, 256, 256, 0)], 5, 0),
            new(0, 1, 0, false), 0, false, false, 37, 39, 16, clock, 5, []);
        session.ConfigureBindings([ShortcutBinding.From(17, 'A')], [ShortcutBinding.From('D'), ShortcutBinding.From('J', 'K')],
            [ShortcutBinding.From(16)], [ShortcutBinding.From(17, 'D')]);
        session.SetKey('A', true); clock.Milliseconds += 100; session.Tick();
        Check(session.X == 256, "movement chord waits for modifier");
        session.SetKey(17, true); clock.Milliseconds += 100; session.Tick();
        Check(Math.Abs(session.X - 206) < .001, "modifier chord moves catcher");
        session.SetKey(17, false); clock.Milliseconds += 100; session.Tick();
        Check(Math.Abs(session.X - 206) < .001, "releasing modifier immediately stops movement");
        session.ReleaseKeys(); session.SetKey('D', true); session.SetKey(16, true);
        clock.Milliseconds += 100; session.Tick();
        Check(Math.Abs(session.X - 306) < .001, "dash combines with unmodified movement");
        session.ReleaseKeys(); session.SetKey(17, true); session.SetKey('D', true);
        clock.Milliseconds += 100; session.Tick();
        Check(Math.Abs(session.X - 306) < .001, "command chord suppresses overlapping movement");
        session.ReleaseKeys(); session.SetKey('J', true); session.SetKey('K', true);
        clock.Milliseconds += 100; session.Tick();
        Check(Math.Abs(session.X - 356) < .001, "alternate simultaneous chord moves catcher");
        session.SetKey('J', false); clock.Milliseconds += 100; session.Tick();
        Check(Math.Abs(session.X - 356) < .001, "either key release stops a chord");
        var baseline = new CatchTestplaySession(new CatchTestplay(
            [new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 10000, 256, 256, 256, 0)], 5, 0),
            new(0, 1, 0, false), 0, false, false, 37, 39, 16, clock, 5, []);
        long Measure(CatchTestplaySession target)
        {
            for (int i = 0; i < 100; i++) target.Tick();
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) target.Tick();
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
        long baselineBytes = Measure(baseline), chordBytes = Measure(session);
        Check(chordBytes <= baselineBytes + 2048, $"chord polling adds no per-tick allocations: baseline={baselineBytes}, chords={chordBytes}");
        var ui = new Ui(false, clock);
        var view = ui.View;
        var move = Command("moveRight", EditorView.ShortcutScope.Testplay);
        view.LibrarySettings.ShortcutBindings[move.StorageKey] = [ShortcutBinding.From(17, 'D')];
        view.StartTestplay(); view.KeyDown(17, true, false); view.KeyDown('D', true, false);
        double before = view.TestplayCatcherX; clock.Milliseconds += 100; ui.Paint();
        Check(view.TestplayCatcherX > before, "editor passes physical chord state into testplay");
        view.KeyUp(17); view.KeyUp('D'); view.StopTestplay();
        var old = System.Text.Json.JsonSerializer.Deserialize<LibrarySettings>("{\"ShortcutBindings\":null}")!;
        Check(old.ShortcutBindings.Count == 0, "null profile table falls back to defaults");
    }
    private static void Settings()
    {
        var ui = new Ui(false); var view = ui.View; ui.Resize(1440, 900);
        view.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.shortcuts"));
        var r = view.SettingsBounds;
        string[] firstTwelve = ["select", "fruit", "slider", "banana", "undo", "redo", "copy", "duplicate", "play", "previousBeat", "nextBeat", "bookmark"];
        foreach (string id in firstTwelve)
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get(Command(id).NameKey) && t.Y >= r.Y + 290 && t.Y + 16 <= r.Bottom - 72), "twelve complete command rows fit the initial viewport: " + id);
        float keyX = r.X + 230 + (r.Width - 262) * .59f;
        var row = ui.Canvas.Texts.First(t => t.Value == L.Get(Command("select").NameKey));
        ui.Click(keyX + 12, row.Y); Check(view.CapturingShortcut, "keycap starts capture");
        Press(view, 'D', true); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "Ctrl + D"), "capture displays combination");
        ui.ClickText(L.Get("shortcuts.useBinding"));
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("shortcuts.conflictTitle")), "collision asks before overwrite");
        ui.ClickText(L.Get("shortcuts.cancel"));
        Check(view.CommandBindings(Command("duplicate"), true).Any(b => b.Same(ShortcutBinding.From(17, 'D'))), "cancel preserves existing binding");
        row = ui.Canvas.Texts.First(t => t.Value == L.Get(Command("select").NameKey)); ui.Click(keyX + 12, row.Y);
        Press(view, 'D', true); ui.Paint(); ui.ClickText(L.Get("shortcuts.useBinding")); ui.ClickText(L.Get("shortcuts.replace"));
        Check(view.CommandBindings(Command("duplicate"), true).Length == 0, "replacement clears the old command");
        Check(view.CommandBindings(Command("duplicate", lazer: true), true).Length > 0, "replacement preserves other mode");
        Check(view.CommandBindings(Command("select")).Any(b => b.Same(ShortcutBinding.From('1'))), "draft does not activate before Apply");
        string folder = Path.GetFullPath("artifacts/tests/keybindings"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.json");
        view.ApplySettings(path); ui.Paint();
        var loaded = LibrarySettings.Load(path);
        Check(loaded.ShortcutBindings[Command("duplicate").StorageKey].Length == 0, "explicit unassigned state survives persistence");
        Check(loaded.ShortcutBindings[Command("select").StorageKey][0].Same(ShortcutBinding.From(17, 'D')), "custom chord survives persistence");
        Check(!view.IsDirty, "binding changes do not enter content history");
        ui.ClickText(L.Get("shortcuts.resetMode")); ui.ClickText(L.Get("shortcuts.cancel"));
        Check(view.CommandBindings(Command("duplicate"), true).Length == 0, "canceling a mode reset preserves unassigned commands");
        ui.ClickText(L.Get("shortcuts.lazer")); ui.ClickText(L.Get("shortcuts.default"));
        row = ui.Canvas.Texts.First(t => t.Value == L.Get(Command("select").NameKey)); ui.Click(keyX + 12, row.Y);
        Press(view, 27); ui.Paint(); Check(view.CapturingShortcut && ui.Canvas.Texts.Any(t => t.Value == "Esc"), "Escape is recordable, not a hidden cancel action");
        ui.ClickText(L.Get("shortcuts.cancel"));
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(locale); ui.Paint();
            Check(!ui.Canvas.Texts.Any(t => t.Value.StartsWith("[shortcuts.")), "localized settings contain no missing keys");
            ui.Resize(980, 620); var small = view.SettingsBounds;
            Check(ui.Canvas.Texts.Where(t => t.Value == L.Get("library.apply")).All(t => t.Y < small.Bottom), "Apply stays reachable in short windows");
            ui.Resize(1440, 900);
        }
    }
}
