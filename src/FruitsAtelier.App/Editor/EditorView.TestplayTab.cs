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
        y += 44;
        c.Line(x, y, x + w, y, Grid);
        y += 20;
        c.Text(L.Get("testplay.keyBindingSettings"), x, y, 15, Foreground, w, true);
        y += 30;
        string[] keyLabels = ["testplay.left", "testplay.right", "testplay.dash"];
        string[] commands = ["moveLeft", "moveRight", "dash"];
        float keyLabelWidth = keyLabels.Max(key => c.MeasureText(L.Get(key), 13, true)) + 16;
        bool keyColumns = w >= Math.Max(480, keyLabelWidth * 3 + 24);
        float keyTop = y, keyCell = (w - 16) / 3;
        for (int i = 0; i < 3; i++)
        {
            string id = commands[i];
            var command = ShortcutCommands.Single(cmd => cmd.Scope == ShortcutScope.Testplay && cmd.Id == id && cmd.Lazer == LibrarySettings.LazerEditing);
            bool stacked = keyColumns || w < keyLabelWidth + 90;
            float keyX = keyColumns ? x + i * (keyCell + 8) : x;
            float rowWidth = keyColumns ? keyCell : w;
            c.Text(L.Get(keyLabels[i]), keyX, y + (stacked ? 0 : 8), 13, Foreground,
                stacked ? rowWidth : keyLabelWidth - 12, true);
            var keyBounds = stacked ? new Rect(keyX, y + 24, rowWidth, 32)
                : new Rect(keyX + keyLabelWidth, y, rowWidth - keyLabelWidth, 32);
            Button(c, keyBounds, string.Join(" / ", CommandBindings(command, false).Select(BindingName)), () =>
            {
                OpenSettings(); settingsCategory = SettingsCategory.Shortcuts;
                shortcutScope = ShortcutScope.Testplay; shortcutLazer = draftLazerEditing; shortcutQuery = ""; workspaceScroll = 0;
                BeginShortcutCapture(command, CommandBindings(command, true).Length > 0 ? 0 : -1);
            });
            if (!keyColumns) y += stacked ? 68 : 42;
        }
        if (keyColumns) y = keyTop + 68;
        y += 12;
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
            string label = L.Get(key);
            bool inline = w >= 440 && c.MeasureText(label, 13, true) <= w * .45f - 12;
            float controlX = inline ? x + w * .45f : x;
            float controlWidth = w - (controlX - x);
            c.Text(label, x, y + (inline ? 8 : 0), 13, Foreground, inline ? w * .45f - 12 : w, true);
            if (!inline) y += 24;
            var valueBounds = new Rect(controlX + 32, y, controlWidth - 64, 32);
            c.Fill(valueBounds, Surface, 4);
            c.Stroke(valueBounds, Grid, radius: 4);
            float valueWidth = c.MeasureText(value, 13);
            c.Text(value, valueBounds.X + (valueBounds.Width - valueWidth) / 2, valueBounds.Y + 8,
                13, enabled ? Foreground : Muted, valueBounds.Width);
            Button(c, new(controlX, y, 28, 32), "‹", () => { change(-1); RequestViewPreference?.Invoke(); }, enabled: enabled);
            Button(c, new(controlX + controlWidth - 28, y, 28, 32), "›", () => { change(1); RequestViewPreference?.Invoke(); }, enabled: enabled);
            y += 44;
        }
        void Toggle(string key, bool value, Action change)
        {
            DrawTestplayCheckbox(c, new(x, y, w, 34), L.Get(key), value, () => { change(); RequestViewPreference?.Invoke(); });
            y += 38;
        }
    }
}
