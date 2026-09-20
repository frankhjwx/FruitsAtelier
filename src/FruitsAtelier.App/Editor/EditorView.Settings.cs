using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private enum SettingsCategory { Workspace, Appearance, Testplay, Updates }
    private SettingsCategory settingsCategory;
    private bool settingsFromLibrary;
    private bool draftRomanisedMetadata;
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
        draftTestplayKeys = [LibrarySettings.TestplayLeftKey, LibrarySettings.TestplayRightKey, LibrarySettings.TestplayDashKey];
    }

    private bool SettingsChanged => draftWorkspace != LibrarySettings.Workspace ||
        draftOsuRoot != LibrarySettings.OsuRoot ||
        draftDefaultSkin != (LibrarySettings.DefaultSkin ?? "") ||
        draftRomanisedMetadata != LibrarySettings.RomanisedMetadata ||
        draftTestplayKeys[0] != LibrarySettings.TestplayLeftKey ||
        draftTestplayKeys[1] != LibrarySettings.TestplayRightKey ||
        draftTestplayKeys[2] != LibrarySettings.TestplayDashKey;

    private void CloseSettings()
    {
        FinishVolumeDrag();
        LibraryVisible = settingsFromLibrary;
        librarySettingsOpen = false;
        libraryField = bindingCapture = -1;
        languageMenuOpen = false;
        contextItems.Clear(); hits.Clear();
    }

    private void DrawSettings(ICanvas c)
    {
        DrawHeader(c);
        c.Text(L.Get("library.settings"), 109, 11, 13, Foreground, 200, true);
        DrawLanguageButton(c, HeaderLanguageBounds);
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
    }

    internal void ApplySettings(string? settingsPath = null)
    {
        if (!SettingsChanged) return;
        try
        {
            var settings = new LibrarySettings { Workspace = draftWorkspace, OsuRoot = draftOsuRoot, SelectedSkin = LibrarySettings.SelectedSkin, DefaultSkin = string.IsNullOrWhiteSpace(draftDefaultSkin) ? null : Path.GetFullPath(draftDefaultSkin) };
            settings.TestplayLeftKey = draftTestplayKeys[0]; settings.TestplayRightKey = draftTestplayKeys[1]; settings.TestplayDashKey = draftTestplayKeys[2];
            settings.RomanisedMetadata = draftRomanisedMetadata;
            settings.MasterVolume = LibrarySettings.MasterVolume; settings.SongVolume = LibrarySettings.SongVolume; settings.HitsoundVolume = LibrarySettings.HitsoundVolume;
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
}
