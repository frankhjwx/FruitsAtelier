using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static readonly (CatchMods Mod, string Texture, string Acronym)[] TestplayModChoices =
    [
        (CatchMods.Easy, "easy", "EZ"), (CatchMods.HalfTime, "halftime", "HT"),
        (CatchMods.HardRock, "hardrock", "HR"), (CatchMods.DoubleTime, "doubletime", "DT"),
        (CatchMods.Hidden, "hidden", "HD"), (CatchMods.Flashlight, "flashlight", "FL")
    ];
    private string SelectedModNames
    {
        get
        {
            var names = TestplayModChoices.Where(m => LibrarySettings.TestplayMods.HasFlag(m.Mod)).Select(m => m.Acronym);
            return LibrarySettings.TestplayMods == CatchMods.None ? "NM" : string.Join(" ", names);
        }
    }
    private float testplayTabScroll, testplayTabMaximum;
    private Rect testplayTabBounds;
    private double ConfiguredTestplaySpeed => (LibrarySettings.TestplayMods & (CatchMods.DoubleTime | CatchMods.HalfTime)) != 0
        ? CatchModRules.ClockRate(LibrarySettings.TestplayMods) : LibrarySettings.TestplaySpeed;
    private bool HiddenMod => LibrarySettings.TestplayMods.HasFlag(CatchMods.Hidden);
    private bool FlashlightMod => LibrarySettings.TestplayMods.HasFlag(CatchMods.Flashlight);

    internal void ToggleTestplayMod(CatchMods mod)
    {
        if (IsTestplaying) return;
        LibrarySettings.TestplayMods = CatchModRules.Toggle(LibrarySettings.TestplayMods, mod);
        if (mod is CatchMods.DoubleTime or CatchMods.HalfTime)
            SetPlaybackSpeed(CatchModRules.ClockRate(LibrarySettings.TestplayMods));
        testplayApproachRates.Clear();
        RequestViewPreference?.Invoke();
    }

    internal void OpenTestplayTab()
    {
        if (!DockPanelExists(DockPanel.Testplay)) AddDockPanel(DockPanel.Testplay, false);
        var group = PanelGroup(DockPanel.Testplay);
        group.Active = DockPanel.Testplay; group.Collapsed = false;
    }

    private void DrawModChoice(ICanvas c, Rect bounds, int index)
    {
        var choice = TestplayModChoices[index];
        bool selected = LibrarySettings.TestplayMods.HasFlag(choice.Mod);
        string label = bounds.Width < 70 ? choice.Acronym : L.Get("testplay.mod." + choice.Texture);
        Button(c, bounds, label, () => ToggleTestplayMod(choice.Mod), active: selected, fontSize: 11, textPadding: 3, textRightPadding: 3);
    }

    private bool WheelTestplayTab(float x, float y, float delta)
    {
        if (!DockContentInputAvailable || !PanelVisible(DockPanel.Testplay) || !testplayTabBounds.Contains(x, y)) return false;
        testplayTabScroll = Math.Clamp(testplayTabScroll - delta / 120 * 40, 0, testplayTabMaximum);
        return true;
    }

    private void DrawTestplayTab(ICanvas c, Rect bounds)
    {
        testplayTabBounds = bounds;
        testplayTabScroll = Math.Clamp(testplayTabScroll, 0, testplayTabMaximum);
        float y = bounds.Y - testplayTabScroll, x = bounds.X, w = bounds.Width;
        int firstHit = hits.Count;
        c.Clip(bounds);
        Button(c, new(x, y, w, 30), L.Get("testplay.start"), StartTestplay);
        y += 40;
        c.Text(L.Get("testplay.modsReduction"), x, y, 14, 0x66CC66, w); y += 25;
        for (int i = 0; i < 2; i++) DrawModChoice(c, new(x + i * (w / 4), y, w / 4 - 4, 32), i);
        y += 44;
        c.Text(L.Get("testplay.modsIncrease"), x, y, 14, 0xFF8855, w); y += 25;
        for (int i = 0; i < 4; i++)
            DrawModChoice(c, new(x + i * (w / 4), y, w / 4 - 4, 32), i + 2);
        y += 42;
        Button(c, new(x, y, w, 28), L.Get("testplay.modsReset"), () =>
        {
            LibrarySettings.TestplayMods = CatchMods.None; testplayApproachRates.Clear();
            SetPlaybackSpeed(1); RequestViewPreference?.Invoke();
        });
        y += 40;
        string[] keyLabels = ["testplay.left", "testplay.right", "testplay.dash"];
        string[] commands = ["moveLeft", "moveRight", "dash"];
        for (int i = 0; i < 3; i++)
        {
            string id = commands[i];
            var command = ShortcutCommands.Single(cmd => cmd.Scope == ShortcutScope.Testplay && cmd.Id == id && cmd.Lazer == LibrarySettings.LazerEditing);
            c.Text(L.Get(keyLabels[i]), x, y + 7, 12, Foreground, w * .35f);
            Button(c, new(x + w * .35f, y, w * .65f, 28), string.Join(" / ", CommandBindings(command, false).Select(BindingName)), () =>
            {
                OpenSettings(); settingsCategory = SettingsCategory.Shortcuts;
                shortcutScope = ShortcutScope.Testplay; shortcutLazer = draftLazerEditing; shortcutQuery = ""; workspaceScroll = 0;
                BeginShortcutCapture(command, CommandBindings(command, true).Length > 0 ? 0 : -1);
            });
            y += 36;
        }
        Step("testplay.startupDelay", L.Get("testplay.startupDelayValue", LibrarySettings.TestplayStartupDelaySeconds),
            d => LibrarySettings.TestplayStartupDelaySeconds += d * .5);
        bool rateMod = (LibrarySettings.TestplayMods & (CatchMods.DoubleTime | CatchMods.HalfTime)) != 0;
        Step("testplay.speed", L.Get("ui.zoomPercent", ConfiguredTestplaySpeed * 100),
            d => LibrarySettings.TestplaySpeed = Math.Round((LibrarySettings.TestplaySpeed + d * .05) * 100) / 100, !rateMod);
        Toggle("testplay.showCombo", LibrarySettings.ShowTestplayCombo, () => LibrarySettings.ShowTestplayCombo = !LibrarySettings.ShowTestplayCombo);
        Toggle("testplay.showFruitStack", LibrarySettings.ShowTestplayFruitStack, () => LibrarySettings.ShowTestplayFruitStack = !LibrarySettings.ShowTestplayFruitStack);
        Step("testplay.dimLabel", L.Get("ui.zoomPercent", LibrarySettings.BackgroundDim), d => LibrarySettings.BackgroundDim = Math.Clamp(LibrarySettings.BackgroundDim + d * 5, 0, 100));
        Toggle("settings.enableVideoStoryboard", LibrarySettings.EnableVideoStoryboard, () => { LibrarySettings.EnableVideoStoryboard = !LibrarySettings.EnableVideoStoryboard; ApplyMediaPreference(); });
        Toggle("settings.forceBackgroundDim", LibrarySettings.ForceBackgroundDim, () => LibrarySettings.ForceBackgroundDim = !LibrarySettings.ForceBackgroundDim);
        testplayTabMaximum = Math.Max(0, y + testplayTabScroll - bounds.Bottom);
        c.Unclip();
        if (testplayTabMaximum > 0)
        {
            float thumb = Math.Max(24, bounds.Height * bounds.Height / (bounds.Height + testplayTabMaximum));
            c.Fill(new(bounds.Right - 3, bounds.Y + (bounds.Height - thumb) * testplayTabScroll / testplayTabMaximum, 3, thumb), Muted, 1);
        }
        for (int i = firstHit; i < hits.Count; i++)
        {
            var hit = hits[i]; var r = hit.Bounds;
            float top = Math.Max(r.Y, bounds.Y), bottom = Math.Min(r.Bottom, bounds.Bottom);
            hits[i] = hit with { Bounds = new(r.X, top, r.Width, Math.Max(0, bottom - top)) };
        }

        void Step(string key, string value, Action<int> change, bool enabled = true)
        {
            c.Text(L.Get(key), x, y, 12, Foreground, w); y += 20;
            c.Text(value, x + 34, y + 6, 12, Foreground, w - 68);
            Button(c, new(x, y, 26, 28), "‹", () => { change(-1); RequestViewPreference?.Invoke(); }, enabled: enabled);
            Button(c, new(x + w - 26, y, 26, 28), "›", () => { change(1); RequestViewPreference?.Invoke(); }, enabled: enabled);
            y += 36;
        }
        void Toggle(string key, bool value, Action change)
        {
            DrawTestplayCheckbox(c, new(x, y, w, 34), L.Get(key), value, () => { change(); RequestViewPreference?.Invoke(); });
            y += 38;
        }
    }
}
