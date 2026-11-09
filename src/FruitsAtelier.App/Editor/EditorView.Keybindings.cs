using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Dictionary<string, ShortcutBinding[]> draftShortcuts = [];
    private ShortcutScope shortcutScope = ShortcutScope.Compose;
    private bool shortcutLazer;
    private bool ShortcutSearchAll => !string.IsNullOrWhiteSpace(shortcutQuery);
    private string shortcutQuery = "";
    private ShortcutCommand? shortcutCapture;
    private int shortcutCaptureIndex;
    private ShortcutBinding? shortcutCandidate;
    private readonly HashSet<int> shortcutPhysicalKeys = [];
    private readonly HashSet<int> shortcutCaptureKeys = [];
    private ShortcutCommand[] shortcutConflicts = [];
    private bool shortcutConfirming;
    private float shortcutConflictScroll;
    private float shortcutContentHeight;
    private readonly Dictionary<int, (int Canonical, ShortcutBinding Binding)> shortcutReleases = [];
    public bool CapturingShortcut => librarySettingsOpen && shortcutCapture is not null;
    private bool ShortcutSearchFocused => librarySettingsOpen && libraryField == 8;
    private bool ShortcutsChanged => !BindingsEqual(draftShortcuts, LibrarySettings.ShortcutBindings ?? []);

    private static Dictionary<string, ShortcutBinding[]> CopyBindings(Dictionary<string, ShortcutBinding[]>? source) =>
        (source ?? []).ToDictionary(p => p.Key, p => (p.Value ?? []).Where(b => b?.Keys is not null)
            .Select(b => ShortcutBinding.From(b.Keys)).Where(b => b.Keys.Length > 0).ToArray());
    private static bool BindingsEqual(Dictionary<string, ShortcutBinding[]> a, Dictionary<string, ShortcutBinding[]> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var values) && values is not null
            && p.Value.Length == values.Length && p.Value.Zip(values).All(pair => pair.First.Same(pair.Second)));
    internal ShortcutBinding[] CommandBindings(ShortcutCommand command, bool draft = false)
    {
        var overrides = draft ? draftShortcuts : LibrarySettings.ShortcutBindings;
        if (overrides is not null && overrides.TryGetValue(command.StorageKey, out var value)) return value ?? [];
        if (command.Scope == ShortcutScope.Testplay && command.Id is "moveLeft" or "moveRight" or "dash")
        {
            int i = command.Id == "moveLeft" ? 0 : command.Id == "moveRight" ? 1 : 2;
            return [ShortcutBinding.From(draft ? draftTestplayKeys[i] : i == 0 ? LibrarySettings.TestplayLeftKey
                : i == 1 ? LibrarySettings.TestplayRightKey : LibrarySettings.TestplayDashKey)];
        }
        if (command.Scope == ShortcutScope.Testplay && command.Id is "volumeUp" or "volumeDown")
        {
            int key = command.Id == "volumeUp" ? 38 : 40;
            bool used = ShortcutCommands.Where(c => c.Scope == ShortcutScope.Testplay && c.Lazer == command.Lazer && c.Group == "movement")
                .Any(c => CommandBindings(c, draft).Any(binding => binding.Keys.Contains(key)));
            if (used) return command.Defaults.Where(binding => binding.Keys.Length > 1).ToArray();
        }
        return command.Defaults;
    }
    private static string BindingName(ShortcutBinding binding) => string.Join(" + ", binding.Keys
        .OrderBy(k => k == 17 ? -4 : k == 18 ? -3 : k == 16 ? -2 : ShortcutBinding.Modifier(k) ? -1 : k)
        .Select(k => k switch { 9 => "Tab", 27 => "Esc", 91 or 92 => OperatingSystem.IsMacOS() ? "Command" : "Win",
            17 when OperatingSystem.IsMacOS() => "Cmd", 18 when OperatingSystem.IsMacOS() => "Option", _ => KeyName(k) }));
    private string ShortcutContext(ShortcutCommand command) => L.Get("shortcuts.context",
        L.Get("shortcuts.scope." + command.Scope), L.Get(command.Lazer ? "shortcuts.lazer" : "shortcuts.default"));
    private string ShortcutMenuLabel(string label, string id)
    {
        var command = ShortcutCommands.FirstOrDefault(c => c.Scope == ActiveShortcutScope && c.Id == id && c.Lazer == LibrarySettings.LazerEditing);
        if (command is null || !LibrarySettings.ShortcutBindings.ContainsKey(command.StorageKey)) return label;
        string title = label.Split("  ", 2)[0].TrimEnd();
        string keys = string.Join(" / ", CommandBindings(command).Select(BindingName));
        return keys.Length == 0 ? title : title + "  " + keys;
    }
    private void ResetShortcutDraft()
    {
        draftShortcuts = CopyBindings(LibrarySettings.ShortcutBindings);
        CancelShortcutCapture();
    }
    private void CancelShortcutCapture()
    {
        shortcutReset = null; shortcutCapture = null; shortcutCandidate = null; shortcutConflicts = [];
        shortcutCaptureKeys.Clear(); shortcutConfirming = false; shortcutConflictScroll = 0;
    }
    private void BeginShortcutCapture(ShortcutCommand command, int index)
    {
        CancelShortcutCapture(); shortcutCapture = command; shortcutCaptureIndex = index;
        shortcutPhysicalKeys.Clear(); libraryField = -1; contextItems.Clear();
    }
    private void CaptureShortcut(int key, bool ctrl, bool shift)
    {
        if (shortcutConfirming || key is <= 0 or >= 255) return;
        key = ShortcutBinding.Normalize(key);
        shortcutPhysicalKeys.Add(key); shortcutCaptureKeys.Clear(); shortcutCaptureKeys.UnionWith(shortcutPhysicalKeys);
        if (ctrl) shortcutCaptureKeys.Add(17);
        if (shift) shortcutCaptureKeys.Add(16);
        if (altHeld) shortcutCaptureKeys.Add(18);
        shortcutCandidate = ShortcutBinding.From(shortcutCaptureKeys.ToArray());
    }
    internal ShortcutCommand[] FindShortcutConflicts(ShortcutCommand command, ShortcutBinding binding) => ShortcutCommands
        .Where(other => other.StorageKey != command.StorageKey && other.Lazer == command.Lazer
            && (other.Scope == command.Scope || other.Scope == ShortcutScope.Global || command.Scope == ShortcutScope.Global)
            && CommandBindings(other, true).Any(binding.Overlaps)).ToArray();
    private void AcceptShortcutCandidate()
    {
        if (shortcutCapture is null || shortcutCandidate is null) return;
        shortcutConflicts = FindShortcutConflicts(shortcutCapture, shortcutCandidate);
        if (shortcutConflicts.Length > 0) { shortcutConfirming = true; return; }
        CommitShortcutCandidate();
    }
    private void CommitShortcutCandidate()
    {
        if (shortcutCapture is null || shortcutCandidate is null) return;
        foreach (var other in shortcutConflicts)
            draftShortcuts[other.StorageKey] = CommandBindings(other, true).Where(b => !b.Overlaps(shortcutCandidate)).ToArray();
        var values = CommandBindings(shortcutCapture, true).ToList();
        if (shortcutCaptureIndex >= 0 && shortcutCaptureIndex < values.Count) values.RemoveAt(shortcutCaptureIndex);
        if (!values.Any(shortcutCandidate.Same)) values.Add(shortcutCandidate);
        draftShortcuts[shortcutCapture.StorageKey] = values.ToArray();
        CancelShortcutCapture();
    }
    private void ResetShortcut(ShortcutCommand command)
    {
        // A reset can reintroduce a collision, so use the same explicit replacement confirmation.
        var collisions = command.Defaults.SelectMany(b => FindShortcutConflicts(command, b)).Distinct().ToArray();
        if (collisions.Length > 0)
        {
            BeginShortcutCapture(command, -1); shortcutReset = command.Defaults;
            shortcutCandidate = command.Defaults[0]; shortcutConflicts = collisions; shortcutConfirming = true;
        }
        else draftShortcuts[command.StorageKey] = command.Defaults;
    }
    private ShortcutBinding[]? shortcutReset;
    private void ConfirmShortcutReplacement()
    {
        if (shortcutReset is { } defaults && shortcutCapture is { } command)
        {
            foreach (var other in shortcutConflicts)
                draftShortcuts[other.StorageKey] = CommandBindings(other, true).Where(b => !defaults.Any(b.Overlaps)).ToArray();
            draftShortcuts[command.StorageKey] = defaults; shortcutReset = null; CancelShortcutCapture();
        }
        else CommitShortcutCandidate();
    }
    private ShortcutCommand[] FilterShortcutCommands() => ShortcutCommands.Where(c => c.Lazer == shortcutLazer
        && (ShortcutSearchAll || c.Scope == shortcutScope)
        && (string.IsNullOrWhiteSpace(shortcutQuery) || (L.Get(c.NameKey) + " " + L.Get(c.DescriptionKey) + " "
            + L.Get("shortcuts.scope." + c.Scope) + " " + string.Join(" ", CommandBindings(c, true).Select(BindingName)))
            .Contains(shortcutQuery.Trim(), StringComparison.CurrentCultureIgnoreCase))).ToArray();

    private void DrawShortcutSettings(ICanvas c)
    {
        float x = SettingsContentX, w = SettingsContentWidth, top = SettingsTop;
        var search = new Rect(x, top + 122, w, 32);
        c.Fill(search, Surface, 4); c.Stroke(search, ShortcutSearchFocused ? Accent : Grid, radius: 4);
        if (shortcutQuery.Length == 0 && !ShortcutSearchFocused)
            c.Text(L.Get("shortcuts.search"), x + 12, search.Y + 7, 13, Muted, search.Width - 28);
        else DrawInputText(c, new(x + 12, search.Y + 6, search.Width - 28, 20), shortcutQuery, 13, ShortcutSearchFocused, "library:8");
        hits.Add(new(search, () => { libraryField = 8; SelectInput("library:8", shortcutQuery); }, true));
        var scopes = Enum.GetValues<ShortcutScope>();
        int columns = Math.Min(scopes.Length, Math.Max(1, (int)(w / 100)));
        float tabWidth = w / columns;
        float extra = ((scopes.Length - 1) / columns) * 32;
        for (int i = 0; i < scopes.Length; i++)
        {
            var scope = scopes[i]; var tab = new Rect(x + i % columns * tabWidth, top + 178 + i / columns * 32, tabWidth - 4, 28);
            c.Fill(tab, Surface, 4); c.Stroke(tab, Grid, radius: 4);
            Button(c, tab, L.Get("shortcuts.scope." + scope), () =>
            { shortcutScope = scope; shortcutQuery = ""; workspaceScroll = 0; libraryField = -1; }, scope == shortcutScope && !ShortcutSearchAll,
                fontSize: 12, bold: false, textPadding: 8, textRightPadding: 8);
        }
        top += extra;
        c.Text(L.Get("shortcuts.editingMode"), x, top + 224, 13, Foreground, 112, true);
        SettingsButton(c, new(x + 120, top + 216, 82, 30), L.Get("shortcuts.default"), () => { shortcutLazer = false; workspaceScroll = 0; }, !shortcutLazer);
        SettingsButton(c, new(x + 206, top + 216, 82, 30), L.Get("shortcuts.lazer"), () => { shortcutLazer = true; workspaceScroll = 0; }, shortcutLazer);
        if (w > 550) c.Text(L.Get("shortcuts.separateModes"), x + 306, top + 224, 12, Muted, w - 306);
        float keyX = x + w * .59f;
        c.Fill(new(x, top + 258, w, 28), Surface, 3);
        c.Text(L.Get("shortcuts.commandHeader"), x + 8, top + 263, 13, Foreground, w * .57f, true);
        c.Text(L.Get("shortcuts.bindingsHeader"), keyX, top + 263, 13, Foreground, w * .4f, true);
        workspaceScrollBounds = new(x, top + 290, w, Math.Max(28, SettingsBounds.Height - 362 - extra));
        var commands = FilterShortcutCommands();
        float rowHeight = 28, groupHeight = 24;
        shortcutContentHeight = 0; string previous = "";
        foreach (var command in commands)
        {
            string group = command.Scope + "/" + command.Group;
            if (group != previous) { shortcutContentHeight += groupHeight; previous = group; }
            shortcutContentHeight += rowHeight;
        }
        workspaceScroll = Math.Clamp(workspaceScroll, 0, Math.Max(0, shortcutContentHeight - workspaceScrollBounds.Height));
        int firstHit = hits.Count; c.Clip(workspaceScrollBounds);
        float y = workspaceScrollBounds.Y - workspaceScroll; previous = "";
        foreach (var command in commands)
        {
            string group = command.Scope + "/" + command.Group;
            if (group != previous)
            {
                c.Text(ShortcutSearchAll ? L.Get("shortcuts.groupContext", L.Get("shortcuts.scope." + command.Scope), L.Get("shortcuts.group." + command.Group))
                    : L.Get("shortcuts.group." + command.Group), x + 8, y + 4, 12, Accent, w - 16, true);
                y += groupHeight; previous = group;
            }
            if (y + rowHeight >= workspaceScrollBounds.Y && y < workspaceScrollBounds.Bottom)
            {
                c.Line(x, y + rowHeight, x + w, y + rowHeight, Grid);
                float nameWidth = Math.Min(170, w * .26f);
                c.Text(L.Get(command.NameKey), x + 8, y + 7, 12, Foreground, nameWidth - 12, true);
                c.Text(L.Get(command.DescriptionKey), x + nameWidth, y + 7, 11, Muted, keyX - x - nameWidth - 12);
                float bx = keyX; var bindings = CommandBindings(command, true);
                for (int j = 0; j < bindings.Length; j++)
                {
                    int index = j; string text = BindingName(bindings[j]);
                    float available = x + w - 64 - bx;
                    float desired = Math.Max(28, c.MeasureText(text, 11) + 16);
                    if (j > 0 && desired > available)
                    {
                        ShortcutKeyButton(c, new(Math.Min(bx, x + w - 88), y + 3, 24, 23), "…", () => BeginShortcutCapture(command, index));
                        bx = x + w - 58; break;
                    }
                    float bw = Math.Min(desired, available - (j < bindings.Length - 1 ? 29 : 0));
                    if (bw < 24) break;
                    ShortcutKeyButton(c, new(bx, y + 3, bw, 23), text, () => BeginShortcutCapture(command, index)); bx += bw + 5;
                }
                if (bindings.Length == 0)
                {
                    ShortcutKeyButton(c, new(bx, y + 3, Math.Min(100, w * .24f), 23), L.Get("shortcuts.unassigned"), () => BeginShortcutCapture(command, -1));
                    bx += Math.Min(100, w * .24f) + 5;
                }
                ShortcutKeyButton(c, new(Math.Min(bx, x + w - 58), y + 3, 24, 23), "+", () => BeginShortcutCapture(command, -1));
                ShortcutKeyButton(c, new(x + w - 28, y + 3, 24, 23), "↶", () => ResetShortcut(command));
            }
            y += rowHeight;
        }
        if (commands.Length == 0) c.Text(L.Get("shortcuts.noResults"), x + 8, y + 12, 13, Muted, w - 16);
        c.Unclip(); ClipSettingsHits(firstHit); DrawSettingsScrollbar(c, shortcutContentHeight);
        float footer = SettingsBounds.Bottom - 52;
        c.Line(x, footer - 16, x + w, footer - 16, Grid);
        c.Text(L.Get("shortcuts.count", commands.Length), x, footer + 8, 12, Muted, 140);
        SettingsButton(c, new(x + 145, footer, 160, 32), L.Get("shortcuts.resetMode"), () =>
        {
            shortcutResetMode = true; shortcutConfirming = true;
        });
        bool canApply = SettingsChanged && (!SettingsRootsChanged || scanTask is null && searchTask is null);
        SettingsButton(c, new(x + w - 110, footer, 110, 32), L.Get("library.apply"), () => ApplySettings(), canApply, canApply);
        if (libraryError.Length > 0) c.Text(libraryError, x, footer - 34, 12, Error, w);
        if (shortcutCapture is not null || shortcutResetMode) DrawShortcutCapture(c);
    }
    private bool shortcutResetMode;
    private void ShortcutKeyButton(ICanvas c, Rect bounds, string label, Action action)
    {
        c.Fill(bounds, Surface, 3); c.Stroke(bounds, Grid, radius: 3);
        Button(c, bounds, label, action, fontSize: 11, bold: false, textPadding: 5, textRightPadding: 5);
    }
    private void DrawShortcutCapture(ICanvas c)
    {
        hits.Clear();
        c.Fill(new(0, 0, width, height), Background, opacity: .75f);
        float w = Math.Min(550, width - 32), h = Math.Min(shortcutConfirming ? Math.Max(360, 288 + shortcutConflicts.Length * 22) : 310, height - 32);
        var box = new Rect((width - w) / 2, (height - h) / 2, w, h);
        c.Fill(box, Panel, 8); c.Stroke(box, Grid, radius: 8);
        c.Text(L.Get(shortcutResetMode ? "shortcuts.resetMode" : shortcutConfirming ? "shortcuts.conflictTitle" : "shortcuts.assignTitle"), box.X + 24, box.Y + 20, 19, Foreground, w - 76, true);
        void Cancel() { CancelShortcutCapture(); shortcutResetMode = false; shortcutReset = null; }
        SettingsButton(c, new(box.Right - 42, box.Y + 12, 28, 28), "×", Cancel);
        if (shortcutResetMode)
        {
            c.Text(L.Get("shortcuts.resetModePrompt", L.Get(shortcutLazer ? "shortcuts.lazer" : "shortcuts.default")), box.X + 24, box.Y + 80, 14, Foreground, w - 48);
            SettingsButton(c, new(box.Right - 180, box.Bottom - 56, 156, 32), L.Get("shortcuts.reset"), () =>
            {
                foreach (var cmd in ShortcutCommands.Where(cmd => cmd.Lazer == shortcutLazer)) draftShortcuts[cmd.StorageKey] = cmd.Defaults;
                Cancel();
            }, true);
        }
        else if (shortcutCapture is { } command)
        {
            c.Text(ShortcutContext(command), box.X + 24, box.Y + 58, 12, Muted, w - 48);
            var alternatives = CommandBindings(command, true);
            c.Text(L.Get(command.NameKey), box.X + 24, box.Y + 82, 16, Foreground, w - (alternatives.Length > 1 && !shortcutConfirming ? 190 : 48), true);
            if (alternatives.Length > 1 && !shortcutConfirming)
            {
                int index = Math.Clamp(shortcutCaptureIndex, 0, alternatives.Length - 1);
                ShortcutKeyButton(c, new(box.Right - 148, box.Y + 78, 24, 26), "‹",
                    () => BeginShortcutCapture(command, (index + alternatives.Length - 1) % alternatives.Length));
                c.Text($"{index + 1} / {alternatives.Length}", box.Right - 116, box.Y + 84, 12, Muted, 58);
                ShortcutKeyButton(c, new(box.Right - 48, box.Y + 78, 24, 26), "›",
                    () => BeginShortcutCapture(command, (index + 1) % alternatives.Length));
            }
            var keyBox = new Rect(box.X + 24, box.Y + 116, w - 48, 52);
            c.Fill(keyBox, Surface, 4); c.Stroke(keyBox, Accent, 1.5f, 4);
            c.Text(shortcutCandidate is null ? L.Get("shortcuts.pressKeys") : BindingName(shortcutCandidate), keyBox.X + 16, keyBox.Y + 14, 18, Foreground, keyBox.Width - 32);
            if (shortcutConfirming)
            {
                var list = new Rect(box.X + 24, box.Y + 184, w - 48, Math.Max(22, h - 288));
                shortcutConflictScroll = Math.Clamp(shortcutConflictScroll, 0, Math.Max(0, shortcutConflicts.Length * 22 - list.Height));
                c.Clip(list);
                for (int i = 0; i < shortcutConflicts.Length; i++)
                {
                    var other = shortcutConflicts[i];
                    c.Text(L.Get("shortcuts.conflictingCommand", L.Get(other.NameKey), L.Get("shortcuts.scope." + other.Scope)),
                        list.X, list.Y + i * 22 - shortcutConflictScroll, 13, Foreground, list.Width);
                }
                c.Unclip();
                c.Text(L.Get("shortcuts.replaceHint"), box.X + 24, box.Bottom - 90, 12, Muted, w - 48);
                SettingsButton(c, new(box.Right - 190, box.Bottom - 56, 166, 32), L.Get("shortcuts.replace"), ConfirmShortcutReplacement, true);
            }
            else
            {
                int line = 0;
                foreach (string text in L.Get("shortcuts.captureHint").Split('\n'))
                    c.Text(text, box.X + 24, box.Y + 184 + line++ * 18, 12, Muted, w - 48);
                if (shortcutCaptureIndex >= 0)
                    SettingsButton(c, new(box.X + 24, box.Bottom - 56, 100, 32), L.Get("shortcuts.clear"), () =>
                    {
                        draftShortcuts[command.StorageKey] = CommandBindings(command, true).Where((_, i) => i != shortcutCaptureIndex).ToArray(); Cancel();
                    });
                SettingsButton(c, new(box.Right - 170, box.Bottom - 56, 146, 32), L.Get("shortcuts.useBinding"), AcceptShortcutCandidate, true, shortcutCandidate is not null);
            }
        }
        SettingsButton(c, new(box.Right - (shortcutConfirming ? 312 : 292), box.Bottom - 56, 110, 32), L.Get("shortcuts.cancel"), Cancel);
    }
}
