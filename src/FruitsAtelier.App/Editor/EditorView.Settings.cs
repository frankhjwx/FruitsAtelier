using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private enum SettingsCategory { Workspace, Appearance, Audio, Testplay, Updates }
    private SettingsCategory settingsCategory;
    private bool settingsFromLibrary;
    private bool draftRomanisedMetadata;
    private const float SettingsContentX = 246;

    public void OpenSettings()
    {
        if (librarySettingsOpen || IsTestplaying || !PrepareFileOperation()) return;
        settingsFromLibrary = LibraryVisible;
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        draftWorkspace = LibrarySettings.Workspace;
        draftOsuRoot = LibrarySettings.OsuRoot;
        draftDefaultSkin = LibrarySettings.DefaultSkin ?? "";
        draftRomanisedMetadata = LibrarySettings.RomanisedMetadata;
        draftTestplayKeys = [LibrarySettings.TestplayLeftKey, LibrarySettings.TestplayRightKey, LibrarySettings.TestplayDashKey];
        settingsCategory = SettingsCategory.Workspace;
        LibraryVisible = librarySettingsOpen = true;
        updatesPage = exportPage = resourcePage = false;
        libraryField = bindingCapture = menu = -1;
        libraryError = "";
        languageMenuOpen = false;
        contextItems.Clear(); hits.Clear(); fields.Clear();
    }

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
        string[] categories = ["settings.workspace", "settings.appearance", "settings.audio", "settings.testplay", "update.title"];
        for (int i = 0; i < categories.Length; i++)
        {
            if (i == 4 && RequestUpdateCheck is null) continue;
            var category = (SettingsCategory)i;
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
            case SettingsCategory.Audio:
                DrawVolumeControls(c);
                c.Text(L.Get("settings.immediate"), SettingsContentX, 420, 14, Muted, width - SettingsContentX - 32);
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
        Button(c, new(SettingsContentX, height - 64, 200, 38), L.Get("library.apply"), ApplySettings,
            active: true, enabled: scanTask is null && searchTask is null);
    }

    private void ApplySettings()
    {
        try
        {
            var settings = new LibrarySettings { Workspace = draftWorkspace, OsuRoot = draftOsuRoot, SelectedSkin = LibrarySettings.SelectedSkin, DefaultSkin = string.IsNullOrWhiteSpace(draftDefaultSkin) ? null : Path.GetFullPath(draftDefaultSkin) };
            settings.TestplayLeftKey = draftTestplayKeys[0]; settings.TestplayRightKey = draftTestplayKeys[1]; settings.TestplayDashKey = draftTestplayKeys[2];
            settings.RomanisedMetadata = draftRomanisedMetadata;
            settings.MasterVolume = LibrarySettings.MasterVolume; settings.SongVolume = LibrarySettings.SongVolume; settings.HitsoundVolume = LibrarySettings.HitsoundVolume;
            if (settings.DefaultSkin is { } archive) settings.DefaultSkin = StoreSkinArchive(settings.Workspace, archive).Archive;
            bool rootsChanged = settings.Workspace != LibrarySettings.Workspace || settings.OsuRoot != LibrarySettings.OsuRoot;
            settings.Save();
            SaveLibraryMemory(); LibrarySettings = settings; InitializeSkin(); CloseSettings();
            if (!rootsChanged) return;
            libraryRatings.Clear(); libraryBrowser?.Retire(); libraryBrowser = null; libraryDatabase = null; libraryResultsReady = false;
            LoadLibraryMemory(); StartLibraryScan();
        }
        catch (Exception e) { libraryError = e.Message; }
    }
}
