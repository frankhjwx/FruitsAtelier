using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private enum SettingsCategory { General, Workspace, Appearance, Audio, Testplay, MappingCanvas, Experimental, OsuLinks, Updates }
    private SettingsCategory settingsCategory;
    private bool draftAudioDiagnostics, draftAudioDiagnosticFrames;
    private string draftAudioDiagnosticProfile = "event-10";
    private bool draftRomanisedMetadata;
    private string draftMapperUsernames = "";
    private bool draftScanStandardMaps;
    private ImportSliderConversionMode draftImportSliderConversion;
    private bool draftConfirmCompensatedImports;
    private bool draftDerandomizeDroplets;
    private bool draftDerandomizeNewProjects;
    public bool SupportsDisplayMode { get; set; }
    private bool draftLowLatencyDisplay;
    private int draftUiScalePercent;
    public float UiScaleFactor => StartupDialogVisible ? 1 : LibrarySettings.UiScalePercent / (float)FruitsAtelier.Core.LibrarySettings.DefaultUiScalePercent;
    public bool SupportsFullscreen { get; set; }
    public Action<bool>? RequestFullscreen { get; set; }
    private bool draftFullscreen, fullscreenShortcutHeld;
    private bool draftExperimentalSnapDivisions;
    private bool draftPreserveSameTimeObjects;
    public Rect PreserveSameTimeObjectsToggleBounds { get; private set; }
    private bool draftLazerEditing;
    private CatchHitLightingMode draftCatchHitLighting;
    internal Rect HitLightingSettingsBounds => new(SettingsContentX, SettingsTop + 464, SettingsContentWidth, SettingsControlHeight);
    internal Rect LazerEditingToggleBounds { get; private set; }
    private bool draftReverseCanvasScroll;
    private double draftTestplayStartupDelaySeconds;
    private double draftTestplaySpeed;
    private bool draftShowTestplayCombo;
    private int draftBackgroundDim, draftMappingCanvasBackgroundDim, draftCanvasGridOpacity;
    private bool draftForceBackgroundDim;
    private readonly uint[] draftSettingsColours = new uint[7];
    private uint SelectionHighlightColour => LibrarySettings.SelectionHighlightColour;
    private int settingsColourIndex = -1;
    private uint settingsColourOriginal;
    private string settingsColourHex = "", settingsColourError = "";
    private double settingsHue, settingsSaturation, settingsValue;
    private Rect settingsPalette, settingsHueTrack;
    private int settingsColourDrag;
    internal Rect SettingsBounds => FirstRunSetupVisible ? new(0, 0, width, height) : new((width - Math.Min(1040, width - 24)) / 2,
        (height - Math.Min(680, height - 24)) / 2, Math.Min(1040, width - 24), Math.Min(680, height - 24));
    private float SettingsContentX => SettingsBounds.X + (FirstRunSetupVisible ? 32 : 230);
    private float SettingsTop => SettingsBounds.Y - (FirstRunSetupVisible ? workspaceScroll : 0);
    private float SettingsRight => SettingsBounds.Right;
    private const float SettingsTextSize = 13;
    private const float SettingsSectionSize = 16;
    private const float SettingsHintSize = 12;
    private const float SettingsControlHeight = 32;
    private const float SettingsControlPadding = 12;
    private float SettingsContentWidth => SettingsRight - SettingsContentX - 32 - (FirstRunSetupVisible && firstRunStep == 1 ? 300 : 0);
    private bool SettingsAudioVisible => librarySettingsOpen && (FirstRunSetupVisible ? firstRunStep == 2 : settingsCategory == SettingsCategory.Audio);
    internal Rect SettingsSkinSelectorBounds => new(SettingsContentX, SettingsTop + 128,
        SettingsContentWidth, SettingsControlHeight);

    private void SettingsButton(ICanvas c, Rect bounds, string label, Action action, bool active = false, bool enabled = true)
    {
        c.Fill(bounds, Surface, 4);
        c.Stroke(bounds, Grid, radius: 4);
        Button(c, bounds, label, action, active, enabled, SettingsTextSize, bold: false,
            textPadding: SettingsControlPadding, textRightPadding: SettingsControlPadding);
    }

    private float SettingsParagraph(ICanvas c, string text, float y, float size = SettingsHintSize)
    {
        while (text.Length > 0)
        {
            int low = 1, high = text.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (c.MeasureText(text[..middle], size) <= SettingsContentWidth) low = middle;
                else high = middle - 1;
            }
            int count = low;
            if (count < text.Length)
            {
                int space = text.LastIndexOf(' ', count - 1, count);
                if (space > 0) count = space;
                if (count > 1 && char.IsHighSurrogate(text[count - 1])) count--;
            }
            c.Text(text[..count], SettingsContentX, y, size, Muted, SettingsContentWidth);
            text = text[count..].TrimStart();
            y += size + 6;
        }
        return y;
    }

    public void OpenSettings()
    {
        if (librarySettingsOpen || IsTestplaying || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        ResetSettingsDrafts();
        RefreshOsuLinkSettings();
        settingsCategory = SettingsCategory.General;
        librarySettingsOpen = true;
        updatesPage = exportPage = resourcePage = false;
        libraryField = bindingCapture = menu = -1;
        libraryError = "";
        languageMenuOpen = false;
        contextItems.Clear(); hits.Clear(); fields.Clear();
    }

    private void ResetSettingsDrafts()
    {
        draftAudioDiagnostics = LibrarySettings.AudioDiagnostics;
        draftAudioDiagnosticFrames = LibrarySettings.AudioDiagnosticFrames;
        draftAudioDiagnosticProfile = LibrarySettings.AudioDiagnosticProfile;
        draftWorkspace = LibrarySettings.Workspace;
        draftOsuRoot = LibrarySettings.OsuRoot;
        draftDefaultSkin = LibrarySettings.DefaultSkin ?? "";
        draftRomanisedMetadata = LibrarySettings.RomanisedMetadata;
        draftMapperUsernames = string.Join("; ", LibrarySettings.MapperUsernames ?? []);
        draftScanStandardMaps = LibrarySettings.ScanStandardMaps;
        draftImportSliderConversion = LibrarySettings.ImportSliderConversion;
        draftConfirmCompensatedImports = LibrarySettings.ConfirmCompensatedImports;
        draftDerandomizeDroplets = LibrarySettings.DerandomizeDroplets;
        draftDerandomizeNewProjects = LibrarySettings.DerandomizeNewProjects;
        draftExperimentalSnapDivisions = LibrarySettings.ExperimentalSnapDivisions;
        draftPreserveSameTimeObjects = LibrarySettings.PreserveSameTimeObjects;
        draftLazerEditing = LibrarySettings.LazerEditing;
        draftCatchHitLighting = LibrarySettings.CatchHitLighting;
        draftReverseCanvasScroll = LibrarySettings.ReverseCanvasScroll;
        draftLowLatencyDisplay = LibrarySettings.LowLatencyDisplay;
        draftUiScalePercent = LibrarySettings.UiScalePercent;
        draftFullscreen = LibrarySettings.Fullscreen;
        draftSettingsColours[0] = LibrarySettings.StandIndicatorColour;
        draftSettingsColours[1] = LibrarySettings.WalkIndicatorColour;
        draftSettingsColours[2] = LibrarySettings.DashIndicatorColour;
        draftSettingsColours[3] = LibrarySettings.HyperDashIndicatorColour;
        draftSettingsColours[4] = LibrarySettings.CanvasGridColour;
        draftSettingsColours[5] = LibrarySettings.CanvasCentreLineColour;
        draftSettingsColours[6] = LibrarySettings.SelectionHighlightColour;
        draftCanvasGridOpacity = LibrarySettings.CanvasGridOpacity;
        settingsColourIndex = -1; settingsColourDrag = 0; settingsColourHex = settingsColourError = "";
        draftTestplayKeys = [LibrarySettings.TestplayLeftKey, LibrarySettings.TestplayRightKey, LibrarySettings.TestplayDashKey];
        draftTestplayStartupDelaySeconds = LibrarySettings.TestplayStartupDelaySeconds;
        draftTestplaySpeed = LibrarySettings.TestplaySpeed;
        draftShowTestplayCombo = LibrarySettings.ShowTestplayCombo;
        draftBackgroundDim = LibrarySettings.BackgroundDim;
        draftMappingCanvasBackgroundDim = LibrarySettings.MappingCanvasBackgroundDim;
        draftForceBackgroundDim = LibrarySettings.ForceBackgroundDim;
    }

    private bool SettingsRootsChanged => draftWorkspace != LibrarySettings.Workspace || draftOsuRoot != LibrarySettings.OsuRoot;

    private bool SettingsChanged => draftMapperUsernames != string.Join("; ", LibrarySettings.MapperUsernames ?? []) ||
        draftScanStandardMaps != LibrarySettings.ScanStandardMaps || draftMappingCanvasBackgroundDim != LibrarySettings.MappingCanvasBackgroundDim ||
        draftSettingsColours[4] != LibrarySettings.CanvasGridColour ||
        draftSettingsColours[5] != LibrarySettings.CanvasCentreLineColour ||
        draftCanvasGridOpacity != LibrarySettings.CanvasGridOpacity ||
        draftAudioDiagnostics != LibrarySettings.AudioDiagnostics ||
        draftAudioDiagnosticFrames != LibrarySettings.AudioDiagnosticFrames ||
        draftAudioDiagnosticProfile != LibrarySettings.AudioDiagnosticProfile ||
        draftForceBackgroundDim != LibrarySettings.ForceBackgroundDim || draftBackgroundDim != LibrarySettings.BackgroundDim || draftShowTestplayCombo != LibrarySettings.ShowTestplayCombo || draftWorkspace != LibrarySettings.Workspace ||
        draftOsuRoot != LibrarySettings.OsuRoot ||
        draftDefaultSkin != (LibrarySettings.DefaultSkin ?? "") ||
        draftRomanisedMetadata != LibrarySettings.RomanisedMetadata ||
        draftImportSliderConversion != LibrarySettings.ImportSliderConversion ||
        draftConfirmCompensatedImports != LibrarySettings.ConfirmCompensatedImports ||
        FirstRunSetupVisible && (draftDerandomizeDroplets != LibrarySettings.DerandomizeDroplets ||
            draftDerandomizeNewProjects != LibrarySettings.DerandomizeNewProjects) ||
        draftLowLatencyDisplay != LibrarySettings.LowLatencyDisplay ||
        draftUiScalePercent != LibrarySettings.UiScalePercent ||
        draftSettingsColours[6] != LibrarySettings.SelectionHighlightColour ||
        draftFullscreen != LibrarySettings.Fullscreen ||
        draftExperimentalSnapDivisions != LibrarySettings.ExperimentalSnapDivisions ||
        draftPreserveSameTimeObjects != LibrarySettings.PreserveSameTimeObjects ||
        draftLazerEditing != LibrarySettings.LazerEditing ||
        draftCatchHitLighting != LibrarySettings.CatchHitLighting ||
        draftReverseCanvasScroll != LibrarySettings.ReverseCanvasScroll ||
        draftSettingsColours[0] != LibrarySettings.StandIndicatorColour ||
        draftSettingsColours[1] != LibrarySettings.WalkIndicatorColour ||
        draftSettingsColours[2] != LibrarySettings.DashIndicatorColour ||
        draftSettingsColours[3] != LibrarySettings.HyperDashIndicatorColour ||
        draftTestplayKeys[0] != LibrarySettings.TestplayLeftKey ||
        draftTestplayKeys[1] != LibrarySettings.TestplayRightKey ||
        draftTestplayKeys[2] != LibrarySettings.TestplayDashKey ||
        draftTestplayStartupDelaySeconds != LibrarySettings.TestplayStartupDelaySeconds ||
        draftTestplaySpeed != LibrarySettings.TestplaySpeed;

    private void CloseSettings()
    {
        if (FirstRunSetupVisible) { ExitFirstRun(); return; }
        FinishBackgroundDimDrag();
        FinishVolumeDrag();
        workspaceScrollDragging = false;
        librarySettingsOpen = false;
        settingsColourIndex = -1;
        settingsColourDrag = 0;
        libraryField = bindingCapture = -1;
        languageMenuOpen = false;
        contextItems.Clear(); hits.Clear();
    }

    private void DrawSettings(ICanvas c)
    {
        if (!librarySettingsOpen) return;
        if (FirstRunSetupVisible) { DrawFirstRunSetup(c); return; }
        hits.Clear(); fields.Clear();
        var r = SettingsBounds;
        c.Fill(new(0, 0, width, height), Background, opacity: .8f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("library.settings"), r.X + 20, r.Y + 16, 19, Foreground, r.Width - 80, true);
        SettingsButton(c, new(r.Right - 48, r.Y + 10, 32, 28), "×", CloseSettings);
        c.Line(r.X + 214, r.Y + 56, r.X + 214, r.Bottom - 20, Grid);
        string[] categories = ["settings.general", "settings.workspace", "settings.appearance", "settings.audio", "settings.testplay", "settings.mappingCanvas", "settings.experimental", "osuLinks.title", "update.title"];
        for (int i = 0; i < categories.Length; i++)
        {
            var category = (SettingsCategory)i;
            if (category == SettingsCategory.Updates && RequestUpdateCheck is null) continue;
            Button(c, new(r.X + 16, r.Y + 78 + i * 48, 182, 38), L.Get(categories[i]), () =>
            {
                FinishVolumeDrag(); FinishBackgroundDimDrag(); libraryField = bindingCapture = -1; contextItems.Clear();
                settingsCategory = category; workspaceScroll = 0;
                if (category == SettingsCategory.OsuLinks) { RefreshOsuLinkSettings(); LoadOsuLinkApplications(); }
                if (category == SettingsCategory.Workspace) { workspaceScroll = 0; StartStorage(); }
                if (category == SettingsCategory.Updates && UpdateStatus.Phase is UpdatePhase.Idle or UpdatePhase.Current or UpdatePhase.Failed)
                    RequestUpdateCheck?.Invoke();
            }, settingsCategory == category, fontSize: SettingsTextSize);
        }
        c.Text(L.Get(categories[(int)settingsCategory]), SettingsContentX, SettingsTop + 82, 24, Foreground, SettingsContentWidth, true);
        switch (settingsCategory)
        {
            case SettingsCategory.OsuLinks:
                DrawOsuLinkSettings(c);
                break;
            case SettingsCategory.General:
                workspaceScrollBounds = new(SettingsContentX, SettingsTop + 116, SettingsContentWidth + 12, SettingsBounds.Height - 208);
                workspaceScroll = Math.Clamp(workspaceScroll, 0, Math.Max(0, GeneralContentHeight - workspaceScrollBounds.Height));
                float generalTop = SettingsTop - workspaceScroll;
                int generalFirstHit = hits.Count;
                c.Clip(workspaceScrollBounds);
                float rowWidth = SettingsContentWidth;
                float labelWidth = Math.Min(240, rowWidth / 2);
                float controlX = SettingsContentX + labelWidth + 16;
                float controlWidth = rowWidth - labelWidth - 16;
                c.Text(L.Get("settings.romanisedLabel"), SettingsContentX, generalTop + 138.5f, SettingsTextSize, Foreground, labelWidth, true);
                var romanisedBounds = new Rect(controlX, generalTop + 128, controlWidth, 32);
                c.Fill(romanisedBounds, Surface, 4); c.Stroke(romanisedBounds, Grid, radius: 4);
                SettingsButton(c, romanisedBounds,
                    L.Get(draftRomanisedMetadata ? "settings.romanisedOn" : "settings.romanisedOff"),
                    () => draftRomanisedMetadata = !draftRomanisedMetadata, draftRomanisedMetadata);
                c.Text(L.Get("ui.language"), SettingsContentX, generalTop + 186.5f, SettingsTextSize, Foreground, labelWidth, true);
                DrawLanguageButton(c, new(controlX, generalTop + 176, controlWidth, 32));
                if (SupportsDisplayMode)
                {
                    float displayWidth = SettingsContentWidth;
                    c.Text(L.Get("settings.displayMode"), SettingsContentX, generalTop + 232, SettingsTextSize, Foreground, displayWidth, true);
                    c.Fill(new(SettingsContentX, generalTop + 260, displayWidth, 44), Gold, 4, .12f);
                    c.Text(L.Get("settings.displayDelayHint"), SettingsContentX + 12, generalTop + 269, 12, Gold, displayWidth - 24);
                    c.Text(L.Get("settings.displayChangeHint"), SettingsContentX + 12, generalTop + 287, 12, Gold, displayWidth - 24);
                    var displayBounds = new Rect(SettingsContentX, generalTop + 320, displayWidth, 32);
                    SettingsButton(c, displayBounds,
                        L.Get(draftLowLatencyDisplay ? "settings.displayImmediate" : "settings.displayVsync") + " ▾",
                        () => OpenDisplayModeMenu(displayBounds));
                }
                float fullscreenTop = generalTop + (SupportsDisplayMode ? 376 : 232);
                if (SupportsFullscreen)
                {
                    c.Text(L.Get("settings.fullscreen"), SettingsContentX, fullscreenTop, SettingsTextSize, Foreground, SettingsContentWidth, true);
                    SettingsButton(c, new(SettingsContentX, fullscreenTop + 28, SettingsContentWidth, SettingsControlHeight),
                        L.Get(draftFullscreen ? "settings.fullscreenOn" : "settings.fullscreenOff"),
                        () => draftFullscreen = !draftFullscreen, draftFullscreen);
                    c.Text(L.Get("settings.fullscreenShortcut"), SettingsContentX, fullscreenTop + 72, SettingsHintSize, Muted, SettingsContentWidth);
                }
                float dropletsTop = fullscreenTop + (SupportsFullscreen ? 108 : 0);
                c.Line(SettingsContentX, dropletsTop, SettingsContentX + SettingsContentWidth, dropletsTop, Grid);
                c.Text(L.Get("randomize.title"), SettingsContentX, dropletsTop + 12, SettingsSectionSize, Foreground, SettingsContentWidth, true);
                var dropletHint = WrapSyncText(c, L.Get("settings.dropletManagementHint"), SettingsContentWidth);
                for (int i = 0; i < dropletHint.Length; i++)
                    c.Text(dropletHint[i].Text, SettingsContentX, dropletsTop + 40 + i * 18, SettingsHintSize, Muted, SettingsContentWidth);
                SettingsButton(c, new(SettingsContentX, dropletsTop + 46 + dropletHint.Length * 18, SettingsContentWidth, SettingsControlHeight),
                    L.Get("settings.openDropletSettings"), OpenDropletSettingsFromGeneral);
                float importBottom = DrawMapImportSettings(c, dropletsTop + 100 + dropletHint.Length * 18);
                LibraryTextField(c, 7, L.Get("settings.mapperUsernames"), draftMapperUsernames, importBottom + 16);
                float libraryBottom = SettingsParagraph(c, L.Get("settings.mapperUsernamesHint"), importBottom + 84);
                float scanTop = libraryBottom + 16;
                c.Line(SettingsContentX, scanTop, SettingsContentX + SettingsContentWidth, scanTop, Grid);
                c.Text(L.Get("settings.importSettings"), SettingsContentX, scanTop + 12, SettingsSectionSize, Foreground, SettingsContentWidth, true);
                TimingCheck(c, new(SettingsContentX, scanTop + 44, SettingsContentWidth, SettingsControlHeight),
                    "settings.scanStandardMaps", draftScanStandardMaps, () => draftScanStandardMaps = !draftScanStandardMaps);
                generalContentHeight = scanTop + 76 - generalTop - 116 + 12;
                c.Unclip();
                ClipSettingsHits(generalFirstHit);
                DrawSettingsScrollbar(c, GeneralContentHeight);
                break;
            case SettingsCategory.Workspace:
                DrawWorkspaceSettings(c);
                break;
            case SettingsCategory.Appearance:
                DrawSkinSelector(c, SettingsSkinSelectorBounds);
                LibraryTextField(c, 4, L.Get("skin.defaultArchive"), draftDefaultSkin, SettingsTop + 184);
                c.Text(L.Get("settings.uiScale"), SettingsContentX, SettingsTop + 278, SettingsTextSize, Foreground, SettingsContentWidth, true);
                var scaleBounds = new Rect(SettingsContentX, SettingsTop + 306, SettingsContentWidth, SettingsControlHeight);
                SettingsButton(c, scaleBounds, $"{draftUiScalePercent}% ▾", () => OpenUiScaleMenu(scaleBounds));
                SettingsParagraph(c, L.Get("settings.uiScaleHint"), SettingsTop + 350);
                var highlightBounds = new Rect(SettingsContentX, SettingsTop + 408, SettingsContentWidth - 130, SettingsControlHeight);
                c.Fill(highlightBounds, Surface, 4); c.Stroke(highlightBounds, Grid, radius: 4);
                c.Fill(new(highlightBounds.X + 7, highlightBounds.Y + 6, 20, 20), draftSettingsColours[6], 3);
                c.Text(L.Get("settings.selectionHighlight"), highlightBounds.X + 36, highlightBounds.Y + 7.5f,
                    SettingsTextSize, Foreground, highlightBounds.Width - 124);
                c.Text($"#{draftSettingsColours[6]:X6}", highlightBounds.Right - 82, highlightBounds.Y + 7.5f, SettingsTextSize, Muted, 76);
                hits.Add(new(highlightBounds, () => OpenSettingsColourPicker(6), true));
                SettingsButton(c, new(highlightBounds.Right + 10, highlightBounds.Y, 120, SettingsControlHeight),
                    L.Get("settings.indicatorReset"), () => draftSettingsColours[6] = FruitsAtelier.Core.LibrarySettings.DefaultSelectionHighlightColour);
                SettingsButton(c, HitLightingSettingsBounds, L.Get(HitLightingKey(draftCatchHitLighting)) + " ▾",
                    () => OpenHitLightingMenu(HitLightingSettingsBounds));
                break;
            case SettingsCategory.Experimental:
                float snapToggleTop = SettingsParagraph(c, L.Get("settings.experimentalSnapDivisions"), SettingsTop + 128, SettingsTextSize);
                SettingsButton(c, new(SettingsContentX, snapToggleTop + 12, SettingsContentWidth, SettingsControlHeight),
                    L.Get(draftExperimentalSnapDivisions ? "settings.romanisedOn" : "settings.romanisedOff"),
                    () => draftExperimentalSnapDivisions = !draftExperimentalSnapDivisions, draftExperimentalSnapDivisions);
                LazerEditingToggleBounds = new(SettingsContentX, snapToggleTop + 76, SettingsContentWidth, SettingsControlHeight);
                SettingsButton(c, LazerEditingToggleBounds,
                    L.Get("settings.lazerEditing", L.Get(draftLazerEditing ? "settings.romanisedOn" : "settings.romanisedOff")),
                    () => draftLazerEditing = !draftLazerEditing, draftLazerEditing);
                float preserveToggleTop = SettingsParagraph(c, L.Get("settings.preserveSameTimeObjects"), snapToggleTop + 128, SettingsTextSize);
                PreserveSameTimeObjectsToggleBounds = new(SettingsContentX, preserveToggleTop + 12, SettingsContentWidth, SettingsControlHeight);
                SettingsButton(c, PreserveSameTimeObjectsToggleBounds,
                    L.Get(draftPreserveSameTimeObjects ? "settings.romanisedOn" : "settings.romanisedOff"),
                    () => draftPreserveSameTimeObjects = !draftPreserveSameTimeObjects, draftPreserveSameTimeObjects);
                break;
            case SettingsCategory.MappingCanvas:
                DrawMappingCanvasSettings(c);
                break;
            case SettingsCategory.Testplay:
                DrawTestplayBindings(c);
                break;
            case SettingsCategory.Updates:
                DrawUpdates(c, SettingsContentX, true);
                break;
            case SettingsCategory.Audio:
                DrawAudioSettings(c);
                break;
        }
        if (settingsCategory == SettingsCategory.OsuLinks) return;
        c.Line(SettingsContentX, r.Bottom - 86, r.Right - 24, r.Bottom - 86, Grid);
        c.Text(libraryError, SettingsContentX, r.Bottom - 116, 13, Error, SettingsContentWidth);
        bool canApply = SettingsChanged && (!SettingsRootsChanged || scanTask is null && searchTask is null);
        SettingsButton(c, new(SettingsContentX, r.Bottom - 64, 200, SettingsControlHeight), L.Get("library.apply"), () => ApplySettings(),
            active: canApply, enabled: canApply);
        if (settingsColourIndex >= 0) DrawSettingsColourPicker(c);
        if (settingsCategory == SettingsCategory.Workspace) DrawStorageTooltip(c);
    }

    private static string HitLightingKey(CatchHitLightingMode mode) => mode switch
    {
        CatchHitLightingMode.On => "skin.hitLightingOn",
        CatchHitLightingMode.Off => "skin.hitLightingOff",
        _ => "skin.hitLightingFollowMode"
    };

    private void OpenHitLightingMenu(Rect bounds)
    {
        contextItems.Clear();
        foreach (var mode in Enum.GetValues<CatchHitLightingMode>())
            contextItems.Add(new((draftCatchHitLighting == mode ? "✓ " : "") + L.Get(HitLightingKey(mode)),
                () => draftCatchHitLighting = mode));
        float menuHeight = 12 + contextItems.Count * 32;
        contextBounds = new(bounds.X, Math.Clamp(bounds.Bottom + 4, 0, Math.Max(0, height - menuHeight)), bounds.Width, menuHeight);
    }

    private void OpenUiScaleMenu(Rect bounds)
    {
        languageMenuOpen = false; menu = -1; contextItems.Clear();
        libraryField = bindingCapture = -1;
        for (int percent = FruitsAtelier.Core.LibrarySettings.MinimumUiScalePercent;
            percent <= FruitsAtelier.Core.LibrarySettings.MaximumUiScalePercent; percent += FruitsAtelier.Core.LibrarySettings.UiScaleStep)
        {
            int value = percent;
            contextItems.Add(new((draftUiScalePercent == value ? "✓ " : "") + $"{value}%", () => draftUiScalePercent = value));
        }
        float menuHeight = 12 + contextItems.Count * 32;
        contextBounds = new(bounds.X, Math.Clamp(bounds.Bottom + 4, 0, Math.Max(0, height - menuHeight)), bounds.Width, menuHeight);
    }

    private void OpenDisplayModeMenu(Rect bounds)
    {
        languageMenuOpen = false; menu = -1; contextItems.Clear();
        libraryField = bindingCapture = -1;
        foreach (bool lowLatency in new[] { false, true })
            contextItems.Add(new((draftLowLatencyDisplay == lowLatency ? "✓ " : "") +
                L.Get(lowLatency ? "settings.displayImmediate" : "settings.displayVsync"),
                () => draftLowLatencyDisplay = lowLatency));
        const float menuHeight = 12 + 2 * 32;
        contextBounds = new(bounds.X, Math.Clamp(bounds.Bottom + 4, 0, Math.Max(0, height - menuHeight)), bounds.Width, menuHeight);
    }

    internal void ApplySettings(string? settingsPath = null, bool force = false, bool persist = true)
    {
        FinishBackgroundDimDrag();
        if (!SettingsChanged && !force) return;
        try
        {
            bool restartAudio = persist && AudioDiagnosticSettingsChanged && RequestAudioDiagnosticRestart is not null;
            float audioScroll = workspaceScroll;
            var settings = new LibrarySettings { Workspace = draftWorkspace, OsuRoot = draftOsuRoot, SelectedSkin = LibrarySettings.SelectedSkin, DefaultSkin = string.IsNullOrWhiteSpace(draftDefaultSkin) ? null : Path.GetFullPath(draftDefaultSkin) };
            settings.FirstRunSetupVersion = LibrarySettings.FirstRunSetupVersion;
            settings.TestplayLeftKey = draftTestplayKeys[0]; settings.TestplayRightKey = draftTestplayKeys[1]; settings.TestplayDashKey = draftTestplayKeys[2];
            settings.TestplayStartupDelaySeconds = draftTestplayStartupDelaySeconds;
            settings.TestplaySpeed = draftTestplaySpeed;
            settings.ShowTestplayCombo = draftShowTestplayCombo;
            settings.BackgroundDim = draftBackgroundDim;
            settings.MappingCanvasBackgroundDim = draftMappingCanvasBackgroundDim;
            settings.CanvasGridColour = draftSettingsColours[4];
            settings.CanvasCentreLineColour = draftSettingsColours[5];
            settings.CanvasGridOpacity = draftCanvasGridOpacity;
            settings.ForceBackgroundDim = draftForceBackgroundDim;
            settings.RomanisedMetadata = draftRomanisedMetadata;
            settings.MapperUsernames = draftMapperUsernames.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
            settings.ScanStandardMaps = draftScanStandardMaps;
            settings.ImportSliderConversion = draftImportSliderConversion;
            settings.ConfirmCompensatedImports = draftConfirmCompensatedImports;
            settings.DerandomizeDroplets = FirstRunSetupVisible ? draftDerandomizeDroplets : LibrarySettings.DerandomizeDroplets;
            settings.DerandomizeNewProjects = FirstRunSetupVisible ? draftDerandomizeNewProjects : LibrarySettings.DerandomizeNewProjects;
            settings.DefaultDropletMode = LibrarySettings.DefaultDropletMode;
            settings.DefaultDropletHardRock = LibrarySettings.DefaultDropletHardRock;
            settings.DefaultDropletStrength = LibrarySettings.DefaultDropletStrength;
            settings.DefaultDropletSeed = LibrarySettings.DefaultDropletSeed;
            settings.LowLatencyDisplay = draftLowLatencyDisplay;
            settings.UiScalePercent = draftUiScalePercent;
            settings.SelectionHighlightColour = draftSettingsColours[6];
            settings.Fullscreen = draftFullscreen;
            settings.ExperimentalSnapDivisions = draftExperimentalSnapDivisions;
            settings.PreserveSameTimeObjects = draftPreserveSameTimeObjects;
            settings.LazerEditing = draftLazerEditing;
            settings.MappingSpeedDistanceSnap = LibrarySettings.MappingSpeedDistanceSnap;
            settings.LazerSpeedDistanceSnap = LibrarySettings.LazerSpeedDistanceSnap;
            settings.LazerDistanceCustomSpeeds = LibrarySettings.LazerDistanceCustomSpeeds;
            settings.LazerDistanceGuidesVisible = LibrarySettings.LazerDistanceGuidesVisible;
            settings.RotationSelectionCentre = LibrarySettings.RotationSelectionCentre;
            settings.RotationClockwise = LibrarySettings.RotationClockwise;
            settings.CatchHitLighting = draftCatchHitLighting;
            settings.ReverseCanvasScroll = draftReverseCanvasScroll;
            settings.StandIndicatorColour = draftSettingsColours[0]; settings.WalkIndicatorColour = draftSettingsColours[1];
            settings.DashIndicatorColour = draftSettingsColours[2]; settings.HyperDashIndicatorColour = draftSettingsColours[3];
            settings.MasterVolume = LibrarySettings.MasterVolume; settings.SongVolume = LibrarySettings.SongVolume; settings.HitsoundVolume = LibrarySettings.HitsoundVolume;
            settings.UseSkinSounds = LibrarySettings.UseSkinSounds;
            settings.AudioDiagnostics = draftAudioDiagnostics;
            settings.AudioDiagnosticFrames = draftAudioDiagnosticFrames;
            settings.AudioDiagnosticProfile = draftAudioDiagnosticProfile;
            settings.PlaybackLineFromBottom = LibrarySettings.PlaybackLineFromBottom;
            settings.CanvasZoom = LibrarySettings.CanvasZoom;
            settings.CanvasTimeZoom = LibrarySettings.CanvasTimeZoom;
            settings.UniformCanvasScale = LibrarySettings.UniformCanvasScale;
            settings.MapEditingPreferences = LibrarySettings.MapEditingPreferences;
            settings.MapTemplates = LibrarySettings.MapTemplates;
            settings.NewDifficultyUseSource = LibrarySettings.NewDifficultyUseSource;
            settings.ObjectTimelineScale = LibrarySettings.ObjectTimelineScale;
            settings.ObjectTimelineHeight = LibrarySettings.ObjectTimelineHeight;
            settings.OverviewTimelineHeight = LibrarySettings.OverviewTimelineHeight;
            settings.WaveformSpanMs = LibrarySettings.WaveformSpanMs;
            if (settings.DefaultSkin is { } archive) settings.DefaultSkin = StoreSkinArchive(settings.Workspace, archive).Archive;
            bool rootsChanged = settings.Workspace != LibrarySettings.Workspace || settings.OsuRoot != LibrarySettings.OsuRoot;
            bool scanningChanged = settings.ScanStandardMaps != LibrarySettings.ScanStandardMaps;
            bool mappersChanged = !(LibrarySettings.MapperUsernames ?? []).SequenceEqual(settings.MapperUsernames);
            if (persist) settings.Save(settingsPath);
            else
            {
                settings.Workspace = Path.GetFullPath(settings.Workspace);
                if (!string.IsNullOrWhiteSpace(settings.OsuRoot)) settings.OsuRoot = Path.GetFullPath(settings.OsuRoot);
                WorkspaceProject.ValidateRoots(settings.Workspace, settings.Songs);
            }
            if (FirstRunSetupVisible)
            {
                LibrarySettings = settings; ResetSettingsDrafts();
                libraryField = bindingCapture = -1; libraryError = "";
                InitializeSkin(); return;
            }
            bool fullscreenChanged = settings.Fullscreen != LibrarySettings.Fullscreen;
            bool lazerEditingChanged = settings.LazerEditing != LibrarySettings.LazerEditing;
            if (lazerEditingChanged) CancelInteraction();
            SaveLibraryMemory(); LibrarySettings = settings;
            if (lazerEditingChanged) ResetLazerPresentation();
            if (!settings.ExperimentalSnapDivisions)
            {
                RestoreTemporarySnap();
                divisor = Math.Min(divisor, 16);
                dsSnap = Math.Min(dsSnap, 16);
                timingSnap = Math.Min(timingSnap, 16);
            }
            ResetSettingsDrafts();
            if (fullscreenChanged) RequestFullscreen?.Invoke(settings.Fullscreen);
            libraryField = bindingCapture = -1;
            libraryError = "";
            InitializeSkin();
            if (restartAudio)
            {
                CloseSettings();
                RequestAudioDiagnosticRestart?.Invoke(audioScroll);
                return;
            }
            if (!rootsChanged)
            {
                if (mappersChanged) InvalidateLibraryOrganization();
                if (scanningChanged) { InvalidateLibraryOrganization(); StartLibraryScan(); }
                return;
            }
            EnableFileMonitoring();
            InvalidateLibraryRatings(); libraryBrowser?.Retire(); libraryBrowser = null; libraryDatabase = null; libraryResultsReady = false;
            foreach (var cached in libraryCategoryBrowsers.Values) cached.Browser?.Retire();
            libraryCategoryBrowsers.Clear();
            LoadLibraryMemory(); StartLibraryScan();
        }
        catch (Exception e) { libraryError = e.Message; }
    }

    private bool FullscreenKeyDown(int virtualKey, bool ctrl, bool shift)
    {
        if (virtualKey != 13 || !altHeld || ctrl || shift || !SupportsFullscreen || CapturingTestplayKey || FirstRunSetupVisible) return false;
        if (!fullscreenShortcutHeld)
        {
            fullscreenShortcutHeld = true;
            LibrarySettings.Fullscreen = !LibrarySettings.Fullscreen;
            draftFullscreen = LibrarySettings.Fullscreen;
            RequestFullscreen?.Invoke(LibrarySettings.Fullscreen);
            RequestViewPreference?.Invoke();
        }
        return true;
    }

    public void UpdateFullscreenState(bool enabled)
    {
        if (LibrarySettings.Fullscreen == enabled) return;
        LibrarySettings.Fullscreen = draftFullscreen = enabled;
        RequestViewPreference?.Invoke();
    }

    private void DrawIndicatorColours(ICanvas c)
    {
        float available = SettingsContentWidth;
        float cellWidth = (available - 12) / 2;
        c.Text(L.Get("settings.indicatorColours"), SettingsContentX, MappingCanvasSettingsTop + 270, SettingsSectionSize, Foreground, available - 190, true);
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var bounds = new Rect(SettingsContentX + i % 2 * (cellWidth + 12), MappingCanvasSettingsTop + 308 + i / 2 * 44, cellWidth, SettingsControlHeight);
            c.Fill(bounds, Surface, 4);
            c.Stroke(bounds, Grid, radius: 4);
            var swatch = new Rect(bounds.X + 7, bounds.Y + 6, 20, 20);
            c.Fill(swatch, draftSettingsColours[i], 3); c.Stroke(swatch, Grid, radius: 3);
            c.Text(L.Get(names[i]), bounds.X + 36, bounds.Y + 7.5f, SettingsTextSize, Foreground, cellWidth - 116);
            c.Text($"#{draftSettingsColours[i]:X6}", bounds.Right - 82, bounds.Y + 7.5f, SettingsTextSize, Muted, 76);
            hits.Add(new(bounds, () => OpenSettingsColourPicker(index), true));
        }
        SettingsButton(c, new(SettingsContentX + available - 174, MappingCanvasSettingsTop + 264, 174, SettingsControlHeight), L.Get("settings.indicatorReset"), () =>
        {
            draftSettingsColours[0] = FruitsAtelier.Core.LibrarySettings.DefaultStandIndicatorColour;
            draftSettingsColours[1] = FruitsAtelier.Core.LibrarySettings.DefaultWalkIndicatorColour;
            draftSettingsColours[2] = FruitsAtelier.Core.LibrarySettings.DefaultDashIndicatorColour;
            draftSettingsColours[3] = FruitsAtelier.Core.LibrarySettings.DefaultHyperDashIndicatorColour;
        });
    }

    private void OpenSettingsColourPicker(int index)
    {
        settingsColourIndex = index;
        settingsColourOriginal = draftSettingsColours[index];
        settingsColourHex = $"#{draftSettingsColours[index]:X6}";
        settingsColourError = "";
        (settingsHue, settingsSaturation, settingsValue) = ColourToHsv(draftSettingsColours[index], settingsHue);
        libraryField = -1;
    }

    private void CancelSettingsColourPicker()
    {
        draftSettingsColours[settingsColourIndex] = settingsColourOriginal;
        settingsColourIndex = -1;
        settingsColourDrag = 0;
        libraryField = -1;
    }

    private void SetSettingsColour(uint colour)
    {
        draftSettingsColours[settingsColourIndex] = colour;
        settingsColourHex = $"#{colour:X6}";
        settingsColourError = "";
        (settingsHue, settingsSaturation, settingsValue) = ColourToHsv(colour, settingsHue);
        libraryField = -1;
    }

    private bool BeginSettingsColourDrag(float x, float y, int button)
    {
        if (settingsColourIndex < 0 || button != 0) return false;
        settingsColourDrag = settingsPalette.Contains(x, y) ? 1 : settingsHueTrack.Contains(x, y) ? 2 : 0;
        if (settingsColourDrag == 0) return false;
        libraryField = -1;
        UpdateSettingsColourDrag(x, y);
        return true;
    }

    private void UpdateSettingsColourDrag(float x, float y)
    {
        if (settingsColourDrag == 1)
        {
            settingsSaturation = Math.Clamp((x - settingsPalette.X) / settingsPalette.Width, 0, 1);
            settingsValue = 1 - Math.Clamp((y - settingsPalette.Y) / settingsPalette.Height, 0, 1);
        }
        else if (settingsColourDrag == 2)
            settingsHue = Math.Clamp((x - settingsHueTrack.X) / settingsHueTrack.Width, 0, .999999) * 360;
        else return;
        uint colour = SongHsv(settingsHue, settingsSaturation, settingsValue);
        draftSettingsColours[settingsColourIndex] = colour;
        settingsColourHex = $"#{colour:X6}";
        settingsColourError = "";
    }

    private bool CommitSettingsColourHex()
    {
        string value = settingsColourHex.Trim().TrimStart('#');
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        { settingsColourError = L.Get("song.invalidHex"); return false; }
        SetSettingsColour(colour);
        return true;
    }

    private void DrawSettingsColourPicker(ICanvas c)
    {
        var screen = new Rect(0, 0, width, height);
        c.Fill(screen, 0x000000, opacity: .72f);
        hits.Add(new(screen, () => { }, true));
        var dialog = new Rect((width - 560) / 2, (height - 430) / 2, 560, 430);
        c.Fill(dialog, Panel, 8); c.Stroke(dialog, Grid, radius: 8);
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash", "settings.canvasGridLine", "settings.canvasCentreLine", "settings.selectionHighlight"];
        c.Text(L.Get(names[settingsColourIndex]), dialog.X + 20, dialog.Y + 17, 18, Foreground, 300, true);
        settingsPalette = new(dialog.X + 20, dialog.Y + 58, dialog.Width - 40, 216);
        for (int row = 0; row < 24; row++)
        for (int col = 0; col < 40; col++)
            c.Fill(new(settingsPalette.X + col * settingsPalette.Width / 40, settingsPalette.Y + row * 9,
                settingsPalette.Width / 40 + .5f, 9.5f), SongHsv(settingsHue, col / 39d, 1 - row / 23d));
        c.Circle(settingsPalette.X + (float)settingsSaturation * settingsPalette.Width,
            settingsPalette.Y + (float)(1 - settingsValue) * settingsPalette.Height, 5, Foreground, false, 2);
        settingsHueTrack = new(settingsPalette.X, settingsPalette.Bottom + 14, settingsPalette.Width, 22);
        for (int i = 0; i < 60; i++)
            c.Fill(new(settingsHueTrack.X + i * settingsHueTrack.Width / 60, settingsHueTrack.Y,
                settingsHueTrack.Width / 60 + .5f, 22), SongHsv(i * 6, 1, 1));
        float hueX = settingsHueTrack.X + (float)(settingsHue / 360) * settingsHueTrack.Width;
        c.Stroke(new(hueX - 3, settingsHueTrack.Y - 2, 6, 26), Foreground, 2);
        c.Text(L.Get("settings.indicatorHex"), dialog.X + 68, dialog.Y + 330, 13, Foreground, 120);
        var preview = new Rect(dialog.X + 20, dialog.Y + 350, 38, 38);
        c.Fill(preview, draftSettingsColours[settingsColourIndex], 4); c.Stroke(preview, Grid, radius: 4);
        var hexField = new Rect(dialog.X + 68, dialog.Y + 350, 170, 38);
        c.Fill(hexField, Surface, 4); c.Stroke(hexField, libraryField == 5 ? Accent : Grid, radius: 4);
        DrawInputText(c, new(hexField.X + 10, hexField.Y + 10, hexField.Width - 20, 18),
            settingsColourHex, SettingsTextSize, libraryField == 5, "library:5");
        hits.Add(new(hexField, () => { libraryField = 5; FocusInput("library:5", settingsColourHex, mouseX); }, true));
        c.Text(settingsColourError, dialog.X + 20, dialog.Y + 397, 12, Error, 230);
        SettingsButton(c, new(dialog.Right - 276, dialog.Bottom - 55, 120, SettingsControlHeight), L.Get("settings.indicatorCancel"), CancelSettingsColourPicker);
        SettingsButton(c, new(dialog.Right - 144, dialog.Bottom - 55, 120, SettingsControlHeight), L.Get("settings.indicatorDone"), () =>
        {
            if (!CommitSettingsColourHex()) return;
            settingsColourIndex = -1; libraryField = -1;
        });
    }
}
