using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public enum SetupAudioCommand { Play, Pause, Stop }

public sealed partial class EditorView
{
    private int firstRunStep = -1;
    private string? firstRunSettingsPath;
    private LibrarySettings? firstRunOriginalSettings;
    private CatchSkin? firstRunOriginalSkin, firstRunOriginalDefaultSkin;
    private string firstRunOriginalLanguage = "en";
    private bool setupAudioPlaying, setupAudioLoading;
    private double setupAudioPosition, setupAudioDuration;
    private string setupAudioError = "";
    private float firstRunContentHeight = 360;
    public bool FirstRunSetupVisible => firstRunStep >= 0;
    internal bool FirstRunHeaderDraggable => FirstRunSetupVisible && !languageMenuOpen && pendingLanguage is null && contextItems.Count == 0;
    internal int FirstRunStep => firstRunStep;
    public Action<SetupAudioCommand>? RequestSetupAudio { get; set; }
    public Action<string>? RequestSetupLink { get; set; }
    public Action? RequestSetupFinished { get; set; }
    public Rect SetupAudioButtonBounds(int index) => new(SettingsContentX + index * 52, SettingsTop + 364, 42, 32);
    public static (double Width, double Height) FirstRunWindowSize(double workWidth, double workHeight, double scale)
        => (Math.Min(880, workWidth * .85 / scale), Math.Min(620, workHeight * .85 / scale));
    public static string SetupAudioPath => Path.Combine(AppContext.BaseDirectory, "assets", "setup", "campus-after-class.wav");
    private static readonly string[] FirstRunTitles = ["setup.paths", "setup.skin", "setup.audio", "setup.droplets", "setup.metadata", "settings.testplay"];

    internal void BeginFirstRunSetup(string? settingsPath = null)
    {
        firstRunSettingsPath = settingsPath;
        firstRunOriginalSettings = LibrarySettings;
        firstRunOriginalLanguage = L.Language;
        firstRunOriginalSkin = skin; firstRunOriginalDefaultSkin = defaultSkin;
        LibrarySettings = System.Text.Json.JsonSerializer.Deserialize<LibrarySettings>(System.Text.Json.JsonSerializer.Serialize(LibrarySettings))!;
        if (!LibrarySettings.FirstRunSetupCompleted)
            LibrarySettings.MasterVolume = LibrarySettings.SongVolume = LibrarySettings.HitsoundVolume = 50;
        ApplyAudioVolume();
        ResetSettingsDrafts();
        firstRunStep = 0;
        librarySettingsOpen = true;
        settingsCategory = SettingsCategory.General;
        libraryField = bindingCapture = -1;
        libraryError = "";
        workspaceScroll = 0;
        hits.Clear(); fields.Clear();
    }

    public void UpdateSetupAudio(double position, double duration, bool playing, bool loading, string? error)
    {
        setupAudioPosition = position; setupAudioDuration = duration;
        setupAudioPlaying = playing; setupAudioLoading = loading;
        setupAudioError = error ?? "";
    }

    private void StopSetupAudio()
    {
        RequestSetupAudio?.Invoke(SetupAudioCommand.Stop);
        UpdateSetupAudio(0, 0, false, false, null);
    }

    private void MoveFirstRun(int step)
    {
        FinishVolumeDrag(); FinishBackgroundDimDrag();
        libraryError = "";
        int previousVersion = LibrarySettings.FirstRunSetupVersion;
        if (step == 6) LibrarySettings.FirstRunSetupCompleted = true;
        ApplySettings(firstRunSettingsPath, force: true, persist: step == 6);
        if (libraryError.Length > 0) { LibrarySettings.FirstRunSetupVersion = previousVersion; return; }
        if (step == 6)
        {
            firstRunOriginalSettings = null;
            if (L.Language != firstRunOriginalLanguage) RequestLanguagePreference?.Invoke(L.Language);
        }
        StopSetupAudio();
        firstRunStep = step;
        workspaceScroll = 0;
        settingsCategory = step switch { 1 => SettingsCategory.Appearance, 2 => SettingsCategory.Audio, 5 => SettingsCategory.Testplay, _ => SettingsCategory.General };
        libraryField = bindingCapture = -1;
        languageMenuOpen = false; contextItems.Clear(); hits.Clear(); fields.Clear();
    }

    public void CancelFirstRunSetup()
    {
        if (!FirstRunSetupVisible) return;
        StopSetupAudio(); FinishBackgroundDimDrag(); FinishVolumeDrag();
        if (firstRunOriginalSettings is not null)
        {
            LibrarySettings = firstRunOriginalSettings;
            skin = firstRunOriginalSkin; defaultSkin = firstRunOriginalDefaultSkin;
            L.SetLanguage(firstRunOriginalLanguage); RefreshLanguage();
            ApplyAudioVolume(); RefreshSkinHitsounds();
            firstRunOriginalSettings = null;
        }
        firstRunStep = -1;
        pendingLanguage = null;
        ResetSettingsDrafts(); CloseSettings();
    }

    private void ExitFirstRun()
    {
        CancelFirstRunSetup(); RequestClose?.Invoke();
    }

    private void FinishFirstRun()
    {
        StopSetupAudio();
        firstRunStep = -1;
        CloseSettings();
        RequestSetupFinished?.Invoke();
        LoadLibraryMemory(); StartLibraryScan(); EnableFileMonitoring();
        historyCompressionEnabled = true;
        StartHistoryCompression(Path.GetFullPath(LibrarySettings.Workspace));
    }

    private void DrawFirstRunSetup(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        c.Fill(new(0, 0, width, height), Background);
        if (firstRunStep == 6)
        {
            var complete = SettingsBounds;
            c.Fill(complete, Panel, 8);
            float x = complete.X + 32, y = complete.Y + Math.Max(24, (complete.Height - 360) / 2);
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"), new(x, y, 96, 72));
            c.Text(L.Get("setup.complete"), x, y + 104, 28, Foreground, complete.Width - 64, true);
            float hintEnd = SettingsParagraph(c, L.Get("setup.completeHint"), y + 158, 14);
            float discordY = Math.Max(y + 218, hintEnd + 20);
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "setup", "discord.png"), new(x, discordY, 160, 160 * 32f / 219));
            c.Text(L.Get("setup.discordHint"), x, discordY + 54, 14, Foreground, complete.Width - 64);
            float buttonWidth = (complete.Width - 88) / 3;
            SettingsButton(c, new(x, discordY + 92, buttonWidth, 36), L.Get("setup.discord"), () => RequestSetupLink?.Invoke("https://discord.gg/ur9QKs4EG2"));
            SettingsButton(c, new(x + buttonWidth + 12, discordY + 92, buttonWidth, 36), L.Get("setup.start"), FinishFirstRun, active: true);
            SettingsButton(c, new(x + (buttonWidth + 12) * 2, discordY + 92, buttonWidth, 36), L.Get("setup.exit"), ExitFirstRun);
            return;
        }
        var r = SettingsBounds;
        float tabWidth = r.Width / 6;
        for (int i = 0; i < 6; i++)
        {
            float x = r.X + i * tabWidth;
            uint colour = i == firstRunStep ? Accent : i < firstRunStep ? 0x366C64u : Surface;
            if (!c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "setup", "progress-tab.png"), new(x, r.Y, tabWidth, 46), colour))
            {
                c.Fill(new(x, r.Y, tabWidth - 12, 46), colour);
                for (int row = 0; row < 46; row++)
                {
                    float tip = 12 * (1 - Math.Abs(row - 22.5f) / 23);
                    c.Fill(new(x + tabWidth - 12, r.Y + row, tip, 1.1f), colour);
                }
            }
            c.Text(L.Get("setup.step", i + 1, L.Get(FirstRunTitles[i])), x + 10, r.Y + 15, 12,
                Foreground, tabWidth - 26, true);
        }
        c.Fill(new(r.X, r.Y + 60, r.Width, r.Height - 60), Panel, 8);
        c.Text(L.Get(FirstRunTitles[firstRunStep]), SettingsContentX, r.Y + 82, 24, Foreground, SettingsContentWidth, true);
        workspaceScrollBounds = new(r.X + 20, r.Y + 116, r.Width - 40, Math.Max(1, r.Height - 234));
        workspaceScroll = Math.Clamp(workspaceScroll, 0, Math.Max(0, firstRunContentHeight - workspaceScrollBounds.Height));
        float contentBottom = SettingsTop + 466;
        int firstHit = hits.Count;
        c.Clip(workspaceScrollBounds);
        switch (firstRunStep)
        {
            case 0:
                float pathsEnd = SettingsParagraph(c, L.Get("setup.pathsHint"), SettingsTop + 126);
                float pathsY = Math.Max(SettingsTop + 188, pathsEnd + 20);
                LibraryTextField(c, 1, L.Get("library.songs"), draftOsuRoot, pathsY);
                LibraryTextField(c, 0, L.Get("library.workspace"), draftWorkspace, pathsY + 90);
                c.Text(L.Get("ui.language"), SettingsContentX, pathsY + 198, 13, Foreground, 200, true);
                DrawLanguageButton(c, new(SettingsContentX + 220, pathsY + 188, SettingsContentWidth - 220, 32));
                contentBottom = pathsY + 230;
                break;
            case 1:
                DrawSkinSelector(c, SettingsSkinSelectorBounds);
                LibraryTextField(c, 4, L.Get("skin.defaultArchive"), draftDefaultSkin, SettingsTop + 184);
                SettingsButton(c, new(SettingsContentX, SettingsTop + 268, SettingsContentWidth, 32),
                    L.Get("setup.downloadSkin"), () => RequestSetupLink?.Invoke("https://osu.ppy.sh/community/forums/topics/1411279?n=1"));
                SettingsButton(c, new(SettingsContentX, SettingsTop + 320, SettingsContentWidth, 32),
                    L.Get(LibrarySettings.UseSkinSounds ? "settings.skinSoundsOn" : "settings.skinSoundsOff"), ToggleSkinSounds, LibrarySettings.UseSkinSounds);
                contentBottom = SettingsParagraph(c, L.Get("settings.skinSoundsHint"), SettingsTop + 368) + 12;
                DrawSetupSkinPreview(c, new(r.Right - 292, SettingsTop + 124, 260, 320));
                contentBottom = Math.Max(contentBottom, SettingsTop + 456);
                break;
            case 2:
                DrawVolumeControls(c);
                c.Text(L.Get("setup.music"), SettingsContentX, SettingsTop + 336, 13, Foreground, SettingsContentWidth, true);
                DrawSetupAudioButton(c, 0,
                    () => RequestSetupAudio?.Invoke(SetupAudioCommand.Play), enabled: !setupAudioPlaying && !setupAudioLoading);
                DrawSetupAudioButton(c, 1,
                    () => RequestSetupAudio?.Invoke(SetupAudioCommand.Pause), enabled: setupAudioPlaying || setupAudioLoading);
                DrawSetupAudioButton(c, 2, StopSetupAudio);
                c.Text(setupAudioError.Length > 0 ? setupAudioError : L.Get("setup.audioTime", setupAudioPosition / 1000, setupAudioDuration / 1000),
                    SettingsContentX, SettingsTop + 405, 12, setupAudioError.Length > 0 ? Error : Muted, SettingsContentWidth);
                string[] sounds = ["hitnormal", "hitwhistle", "hitfinish", "hitclap"];
                float soundWidth = (SettingsContentWidth - 36) / 4;
                for (int i = 0; i < sounds.Length; i++)
                {
                    string sound = sounds[i];
                    SettingsButton(c, new(SettingsContentX + i * (soundWidth + 12), SettingsTop + 434, soundWidth, 32),
                        L.Get("timing." + sound), () => PreviewSetupSample(sound));
                }
                break;
            case 3:
                float dropletsY = Math.Max(SettingsTop + 216, SettingsParagraph(c, L.Get("setup.dropletsHint"), SettingsTop + 126) + 24);
                SettingsButton(c, new(SettingsContentX, dropletsY, SettingsContentWidth, 32),
                    L.Get(draftDerandomizeDroplets ? "settings.derandomizeOn" : "settings.derandomizeOff"),
                    () => draftDerandomizeDroplets = !draftDerandomizeDroplets, draftDerandomizeDroplets);
                SettingsButton(c, new(SettingsContentX, dropletsY + 56, SettingsContentWidth, 32),
                    L.Get(draftDerandomizeNewProjects ? "settings.newProjectDerandomizeOn" : "settings.newProjectDerandomizeOff"),
                    () => draftDerandomizeNewProjects = !draftDerandomizeNewProjects, draftDerandomizeNewProjects);
                contentBottom = dropletsY + 100;
                break;
            case 4:
                float metadataY = Math.Max(SettingsTop + 218, SettingsParagraph(c, L.Get("setup.metadataHint"), SettingsTop + 126) + 24);
                c.Text(L.Get("settings.romanisedLabel"), SettingsContentX, metadataY, 13, Foreground, SettingsContentWidth, true);
                SettingsButton(c, new(SettingsContentX, metadataY + 34, 240, 32),
                    L.Get(draftRomanisedMetadata ? "settings.romanisedOn" : "settings.romanisedOff"),
                    () => draftRomanisedMetadata = !draftRomanisedMetadata, draftRomanisedMetadata);
                contentBottom = metadataY + 80;
                break;
            case 5:
                DrawTestplayBindings(c);
                break;
        }
        c.Unclip();
        ClipSettingsHits(firstHit);
        firstRunContentHeight = contentBottom - SettingsTop - 116;
        DrawSettingsScrollbar(c, firstRunContentHeight);
        c.Text(libraryError, SettingsContentX, r.Bottom - 110, 12, Error, r.Width - 64);
        c.Line(SettingsContentX, r.Bottom - 86, r.Right - 32, r.Bottom - 86, Grid);
        SettingsButton(c, new(SettingsContentX, r.Bottom - 64, 160, 32), L.Get("setup.back"),
            () => MoveFirstRun(firstRunStep - 1), enabled: firstRunStep > 0);
        SettingsButton(c, new(r.Right - 360, r.Bottom - 64, 150, 32), L.Get("setup.exit"), ExitFirstRun);
        SettingsButton(c, new(r.Right - 192, r.Bottom - 64, 160, 32), L.Get(firstRunStep == 5 ? "setup.finish" : "setup.next"),
            () => MoveFirstRun(firstRunStep + 1), active: true);
    }

    private void DrawSetupAudioButton(ICanvas c, int index, Action action, bool enabled = true)
    {
        var r = SetupAudioButtonBounds(index);
        SettingsButton(c, r, "", action, enabled: enabled);
        uint ink = enabled ? Foreground : Muted;
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        if (index == 0)
        {
            for (int row = -8; row <= 8; row++)
                c.Line(cx - 6, cy + row, cx + 8 - Math.Abs(row) * 14f / 8, cy + row, ink, 1.2f);
        }
        else if (index == 1)
        {
            c.Fill(new(cx - 7, cy - 8, 5, 16), ink);
            c.Fill(new(cx + 2, cy - 8, 5, 16), ink);
        }
        else c.Fill(new(cx - 7, cy - 7, 14, 14), ink);
        if (r.Contains(mouseX, mouseY))
            c.Text(L.Get(index == 0 ? "setup.play" : index == 1 ? "setup.pause" : "setup.stop"), r.X, r.Bottom + 4, 11, Muted, 100);
    }

    private void PreviewSetupSample(string sound)
    {
        var map = new MapDocument { IsDemo = false };
        map.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500, Uninherited = true, SampleSet = 1, Volume = 100 });
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1," +
            (sound switch { "hitwhistle" => 2, "hitfinish" => 4, "hitclap" => 8, _ => 0 }) + ",0:0:0:0:" });
        var objects = CatchStreamConverter.Convert(map).Objects;
        var resolver = new HitsoundResolver(map, objects, HitsoundSkinFolders);
        foreach (var sample in resolver.Resolve(objects[0]).Where(s => s.Name == sound)) RequestAuditionHitsound?.Invoke(sample);
    }

    private void DrawSetupSkinPreview(ICanvas c, Rect r)
    {
        c.Fill(r, Background, 6); c.Stroke(r, Grid, radius: 6);
        c.Clip(r);
        float diameter = 30;
        void Object(CatchSkinObject kind, int index, float x, float y, uint colour)
        {
            if (skin?.Draw(c, kind, index, x, y, diameter, colour) != true)
                c.Circle(x, y, kind == CatchSkinObject.Droplet ? 8 : diameter / 2, colour);
        }
        Object(CatchSkinObject.Fruit, 0, r.X + r.Width * .3f, r.Y + 54, 0xF098A0);
        for (int i = 0; i < 5; i++)
        {
            float x = r.X + r.Width * (.38f + i * .08f), y = r.Y + 114 + i * 22;
            if (i < 4) c.Line(x, y, x + r.Width * .08f, y + 22, Accent, 2, .5f);
            Object(i is 0 or 4 ? CatchSkinObject.Fruit : CatchSkinObject.Droplet, 1, x, y, Accent);
        }
        DrawCatcherBody(c, r.X + r.Width / 2, r.Bottom - 64, r.Width * 1.5f, 0xFFFFFF, 1, false, false);
        c.Unclip();
    }
}
