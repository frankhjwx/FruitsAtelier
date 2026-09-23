using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private enum SettingsCategory { Workspace, Appearance, Testplay, Updates }
    private SettingsCategory settingsCategory;
    private bool settingsFromLibrary;
    private bool draftRomanisedMetadata;
    private readonly uint[] draftIndicatorColours = new uint[4];
    private int settingsColourIndex = -1;
    private uint settingsColourOriginal;
    private string settingsColourHex = "", settingsColourError = "";
    private double settingsHue, settingsSaturation, settingsValue;
    private Rect settingsPalette, settingsHueTrack;
    private int settingsColourDrag;
    private const float SettingsContentX = 246;

    public void OpenSettings()
    {
        if (librarySettingsOpen || IsTestplaying || !PrepareFileOperation()) return;
        settingsFromLibrary = LibraryVisible;
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        ResetSettingsDrafts();
        settingsCategory = SettingsCategory.Workspace;
        LibraryVisible = librarySettingsOpen = true;
        updatesPage = exportPage = resourcePage = false;
        libraryField = bindingCapture = menu = -1;
        libraryError = "";
        languageMenuOpen = false;
        contextItems.Clear(); hits.Clear(); fields.Clear();
    }

    private void ResetSettingsDrafts()
    {
        draftWorkspace = LibrarySettings.Workspace;
        draftOsuRoot = LibrarySettings.OsuRoot;
        draftDefaultSkin = LibrarySettings.DefaultSkin ?? "";
        draftRomanisedMetadata = LibrarySettings.RomanisedMetadata;
        draftIndicatorColours[0] = LibrarySettings.StandIndicatorColour;
        draftIndicatorColours[1] = LibrarySettings.WalkIndicatorColour;
        draftIndicatorColours[2] = LibrarySettings.DashIndicatorColour;
        draftIndicatorColours[3] = LibrarySettings.HyperDashIndicatorColour;
        settingsColourIndex = -1; settingsColourDrag = 0; settingsColourHex = settingsColourError = "";
        draftTestplayKeys = [LibrarySettings.TestplayLeftKey, LibrarySettings.TestplayRightKey, LibrarySettings.TestplayDashKey];
    }

    private bool SettingsChanged => draftWorkspace != LibrarySettings.Workspace ||
        draftOsuRoot != LibrarySettings.OsuRoot ||
        draftDefaultSkin != (LibrarySettings.DefaultSkin ?? "") ||
        draftRomanisedMetadata != LibrarySettings.RomanisedMetadata ||
        draftIndicatorColours[0] != LibrarySettings.StandIndicatorColour ||
        draftIndicatorColours[1] != LibrarySettings.WalkIndicatorColour ||
        draftIndicatorColours[2] != LibrarySettings.DashIndicatorColour ||
        draftIndicatorColours[3] != LibrarySettings.HyperDashIndicatorColour ||
        draftTestplayKeys[0] != LibrarySettings.TestplayLeftKey ||
        draftTestplayKeys[1] != LibrarySettings.TestplayRightKey ||
        draftTestplayKeys[2] != LibrarySettings.TestplayDashKey;

    private void CloseSettings()
    {
        FinishVolumeDrag();
        LibraryVisible = settingsFromLibrary;
        librarySettingsOpen = false;
        settingsColourIndex = -1;
        settingsColourDrag = 0;
        libraryField = bindingCapture = -1;
        languageMenuOpen = false;
        contextItems.Clear(); hits.Clear();
    }

    private void DrawSettings(ICanvas c)
    {
        DrawHeader(c);
        c.Text(L.Get("library.settings"), 109, 11, 13, Foreground, 200, true);
        Button(c, HeaderNavigationBounds, L.Get(settingsFromLibrary ? "library.back" : "library.editor"), CloseSettings);
        c.Fill(new(0, HeaderHeight, 214, height - HeaderHeight), Panel);
        string[] categories = ["settings.workspace", "settings.appearance", "settings.testplay", "update.title"];
        for (int i = 0; i < categories.Length; i++)
        {
            var category = (SettingsCategory)i;
            if (category == SettingsCategory.Updates && RequestUpdateCheck is null) continue;
            Button(c, new(16, 78 + i * 48, 182, 38), L.Get(categories[i]), () =>
            {
                FinishVolumeDrag(); libraryField = bindingCapture = -1;
                settingsCategory = category;
                if (category == SettingsCategory.Updates && UpdateStatus.Phase is UpdatePhase.Idle or UpdatePhase.Current or UpdatePhase.Failed)
                    RequestUpdateCheck?.Invoke();
            }, settingsCategory == category);
        }
        if (settingsCategory != SettingsCategory.Updates)
            c.Text(L.Get(categories[(int)settingsCategory]), SettingsContentX, 82, 24, Foreground, width - SettingsContentX - 32, true);
        switch (settingsCategory)
        {
            case SettingsCategory.Workspace:
                c.Text(L.Get("library.settingsDescription"), SettingsContentX, 128, 14, Muted, width - SettingsContentX - 32);
                LibraryTextField(c, 0, L.Get("library.workspace"), draftWorkspace, 180);
                LibraryTextField(c, 1, L.Get("library.songs"), draftOsuRoot, 284);
                break;
            case SettingsCategory.Appearance:
                LibraryTextField(c, 4, L.Get("skin.defaultArchive"), draftDefaultSkin, 160);
                Button(c, new(SettingsContentX, 270, Math.Min(520, width - SettingsContentX - 32), 38),
                    L.Get(draftRomanisedMetadata ? "settings.romanisedOn" : "settings.romanisedOff"),
                    () => draftRomanisedMetadata = !draftRomanisedMetadata, draftRomanisedMetadata);
                DrawLanguageButton(c, new(SettingsContentX, 330, 280, 38));
                DrawIndicatorColours(c);
                break;
            case SettingsCategory.Testplay:
                DrawTestplayBindings(c);
                break;
            case SettingsCategory.Updates:
                DrawUpdates(c, SettingsContentX, true);
                break;
        }
        c.Line(230, height - 86, width - 24, height - 86, Grid);
        c.Text(libraryError, SettingsContentX, height - 116, 13, Error, width - SettingsContentX - 32);
        bool canApply = SettingsChanged && scanTask is null && searchTask is null;
        Button(c, new(SettingsContentX, height - 64, 200, 38), L.Get("library.apply"), () => ApplySettings(),
            active: canApply, enabled: canApply);
        if (settingsColourIndex >= 0) DrawIndicatorColourPicker(c);
    }

    internal void ApplySettings(string? settingsPath = null)
    {
        if (!SettingsChanged) return;
        try
        {
            var settings = new LibrarySettings { Workspace = draftWorkspace, OsuRoot = draftOsuRoot, SelectedSkin = LibrarySettings.SelectedSkin, DefaultSkin = string.IsNullOrWhiteSpace(draftDefaultSkin) ? null : Path.GetFullPath(draftDefaultSkin) };
            settings.TestplayLeftKey = draftTestplayKeys[0]; settings.TestplayRightKey = draftTestplayKeys[1]; settings.TestplayDashKey = draftTestplayKeys[2];
            settings.RomanisedMetadata = draftRomanisedMetadata;
            settings.StandIndicatorColour = draftIndicatorColours[0]; settings.WalkIndicatorColour = draftIndicatorColours[1];
            settings.DashIndicatorColour = draftIndicatorColours[2]; settings.HyperDashIndicatorColour = draftIndicatorColours[3];
            settings.MasterVolume = LibrarySettings.MasterVolume; settings.SongVolume = LibrarySettings.SongVolume; settings.HitsoundVolume = LibrarySettings.HitsoundVolume;
            settings.PlaybackLineFromBottom = LibrarySettings.PlaybackLineFromBottom;
            if (settings.DefaultSkin is { } archive) settings.DefaultSkin = StoreSkinArchive(settings.Workspace, archive).Archive;
            bool rootsChanged = settings.Workspace != LibrarySettings.Workspace || settings.OsuRoot != LibrarySettings.OsuRoot;
            settings.Save(settingsPath);
            SaveLibraryMemory(); LibrarySettings = settings;
            ResetSettingsDrafts();
            libraryField = bindingCapture = -1;
            libraryError = "";
            InitializeSkin();
            if (!rootsChanged) return;
            libraryRatings.Clear(); libraryBrowser?.Retire(); libraryBrowser = null; libraryDatabase = null; libraryResultsReady = false;
            LoadLibraryMemory(); StartLibraryScan();
        }
        catch (Exception e) { libraryError = e.Message; }
    }

    private void DrawIndicatorColours(ICanvas c)
    {
        float available = width - SettingsContentX - 32;
        float cellWidth = (available - 12) / 2;
        c.Text(L.Get("settings.indicatorColours"), SettingsContentX, 380, 13, Foreground, available - 190, true);
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var bounds = new Rect(SettingsContentX + i % 2 * (cellWidth + 12), 410 + i / 2 * 42, cellWidth, 34);
            c.Fill(bounds, Surface, 4);
            c.Stroke(bounds, Grid, radius: 4);
            var swatch = new Rect(bounds.X + 7, bounds.Y + 7, 20, 20);
            c.Fill(swatch, draftIndicatorColours[i], 3); c.Stroke(swatch, Grid, radius: 3);
            c.Text(L.Get(names[i]), bounds.X + 36, bounds.Y + 9, 12, Foreground, cellWidth - 116, true);
            c.Text($"#{draftIndicatorColours[i]:X6}", bounds.Right - 82, bounds.Y + 9, 12, Muted, 76);
            hits.Add(new(bounds, () => OpenIndicatorColourPicker(index), true));
        }
        Button(c, new(SettingsContentX + available - 174, 374, 174, 30), L.Get("settings.indicatorReset"), () =>
        {
            draftIndicatorColours[0] = FruitsAtelier.Core.LibrarySettings.DefaultStandIndicatorColour;
            draftIndicatorColours[1] = FruitsAtelier.Core.LibrarySettings.DefaultWalkIndicatorColour;
            draftIndicatorColours[2] = FruitsAtelier.Core.LibrarySettings.DefaultDashIndicatorColour;
            draftIndicatorColours[3] = FruitsAtelier.Core.LibrarySettings.DefaultHyperDashIndicatorColour;
        });
    }

    private void OpenIndicatorColourPicker(int index)
    {
        settingsColourIndex = index;
        settingsColourOriginal = draftIndicatorColours[index];
        settingsColourHex = $"#{draftIndicatorColours[index]:X6}";
        settingsColourError = "";
        (settingsHue, settingsSaturation, settingsValue) = ColourToHsv(draftIndicatorColours[index], settingsHue);
        libraryField = -1;
    }

    private void CancelIndicatorColourPicker()
    {
        draftIndicatorColours[settingsColourIndex] = settingsColourOriginal;
        settingsColourIndex = -1;
        settingsColourDrag = 0;
        libraryField = -1;
    }

    private void SetIndicatorColour(uint colour)
    {
        draftIndicatorColours[settingsColourIndex] = colour;
        settingsColourHex = $"#{colour:X6}";
        settingsColourError = "";
        (settingsHue, settingsSaturation, settingsValue) = ColourToHsv(colour, settingsHue);
        libraryField = -1;
    }

    private bool BeginIndicatorColourDrag(float x, float y, int button)
    {
        if (settingsColourIndex < 0 || button != 0) return false;
        settingsColourDrag = settingsPalette.Contains(x, y) ? 1 : settingsHueTrack.Contains(x, y) ? 2 : 0;
        if (settingsColourDrag == 0) return false;
        libraryField = -1;
        UpdateIndicatorColourDrag(x, y);
        return true;
    }

    private void UpdateIndicatorColourDrag(float x, float y)
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
        draftIndicatorColours[settingsColourIndex] = colour;
        settingsColourHex = $"#{colour:X6}";
        settingsColourError = "";
    }

    private bool CommitIndicatorColourHex()
    {
        string value = settingsColourHex.Trim().TrimStart('#');
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        { settingsColourError = L.Get("song.invalidHex"); return false; }
        SetIndicatorColour(colour);
        return true;
    }

    private void DrawIndicatorColourPicker(ICanvas c)
    {
        var screen = new Rect(0, 0, width, height);
        c.Fill(screen, 0x000000, opacity: .72f);
        hits.Add(new(screen, () => { }, true));
        var dialog = new Rect((width - 560) / 2, (height - 430) / 2, 560, 430);
        c.Fill(dialog, Panel, 8); c.Stroke(dialog, Grid, radius: 8);
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
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
        c.Fill(preview, draftIndicatorColours[settingsColourIndex], 4); c.Stroke(preview, Grid, radius: 4);
        var hexField = new Rect(dialog.X + 68, dialog.Y + 350, 170, 38);
        c.Fill(hexField, Surface, 4); c.Stroke(hexField, libraryField == 5 ? Accent : Grid, radius: 4);
        DrawInputText(c, new(hexField.X + 10, hexField.Y + 10, hexField.Width - 20, 18),
            settingsColourHex, 14, libraryField == 5, "library:5");
        hits.Add(new(hexField, () => { libraryField = 5; FocusInput("library:5", settingsColourHex, mouseX); }, true));
        c.Text(settingsColourError, dialog.X + 20, dialog.Y + 397, 12, Error, 230);
        Button(c, new(dialog.Right - 276, dialog.Bottom - 55, 120, 34), L.Get("settings.indicatorCancel"), CancelIndicatorColourPicker);
        Button(c, new(dialog.Right - 144, dialog.Bottom - 55, 120, 34), L.Get("settings.indicatorDone"), () =>
        {
            if (!CommitIndicatorColourHex()) return;
            settingsColourIndex = -1; libraryField = -1;
        });
    }
}
