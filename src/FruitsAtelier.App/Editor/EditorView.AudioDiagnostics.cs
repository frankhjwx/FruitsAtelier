using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal bool AudioDiagnosticSettingsVisible => librarySettingsOpen && settingsCategory == SettingsCategory.Audio;
    internal float AudioDiagnosticSettingsScroll => workspaceScroll;
    public Action<float>? RequestAudioDiagnosticRestart { get; set; }
    internal void OpenAudioDiagnosticSettings(float scroll)
    {
        OpenSettings();
        settingsCategory = SettingsCategory.Audio;
        workspaceScroll = Math.Max(0, scroll);
        audioSettingsContentHeight = Math.Max(audioSettingsContentHeight, workspaceScroll + SettingsBounds.Height);
    }
    internal void RestoreAudioDiagnosticDifficulty(int index) => SwitchDifficultyCore(index, reloadAudio: false);
    internal void RestoreAudioDiagnosticPosition(double positionMs) => SeekTo(positionMs);
    private bool AudioDiagnosticSettingsChanged => draftAudioDiagnostics != LibrarySettings.AudioDiagnostics
        || draftAudioDiagnosticFrames != LibrarySettings.AudioDiagnosticFrames
        || draftAudioDiagnosticProfile != LibrarySettings.AudioDiagnosticProfile;
    public bool SupportsAudioDiagnostics { get; set; }
    public Func<string>? AudioDiagnosticStatus { get; set; }
    public Func<string>? CurrentAudioDiagnosticProfile { get; set; }
    public Action? RequestAudioDiagnosticMarker { get; set; }
    public Action? RequestAudioDiagnosticFolder { get; set; }
    public Func<Task<string>>? RequestAudioDiagnosticExport { get; set; }
    private bool audioDiagnosticMarkerHeld;
    private bool AudioDiagnosticMarkerKeyDown(int key, bool ctrl, bool shift)
    {
        if (key != 119 || !ctrl || !shift || altHeld || !SupportsAudioDiagnostics
            || AudioDiagnosticStatus?.Invoke() != "audioDiagnostic.active") return false;
        if (!audioDiagnosticMarkerHeld)
        {
            audioDiagnosticMarkerHeld = true;
            RequestAudioDiagnosticMarker?.Invoke();
            audioReportStatus = L.Get("audioDiagnostic.marked");
        }
        return true;
    }
    private Task<string>? audioReportTask;
    private string audioReportStatus = "";
    private float audioSettingsContentHeight = 760;

    private void DrawAudioSettings(ICanvas c)
    {
        workspaceScrollBounds = new(SettingsContentX, SettingsTop + 116, SettingsContentWidth + 12, SettingsBounds.Height - 208);
        workspaceScroll = Math.Clamp(workspaceScroll, 0, Math.Max(0, audioSettingsContentHeight - workspaceScrollBounds.Height));
        int firstHit = hits.Count;
        c.Clip(workspaceScrollBounds);
        DrawVolumeControls(c);
        float y = SettingsTop - workspaceScroll + 346;
        SettingsButton(c, new(SettingsContentX, y, SettingsContentWidth, SettingsControlHeight),
            L.Get(LibrarySettings.UseSkinSounds ? "settings.skinSoundsOn" : "settings.skinSoundsOff"),
            ToggleSkinSounds, LibrarySettings.UseSkinSounds);
        c.Line(SettingsContentX, y + 44, SettingsContentX + SettingsContentWidth, y + 44, Grid);
        y += 64;
        c.Text(L.Get("audioDiagnostic.title"), SettingsContentX, y, SettingsSectionSize, Foreground, SettingsContentWidth, true);
        y += 32;
        if (SupportsAudioDiagnostics)
        {
            SettingsButton(c, new(SettingsContentX, y, SettingsContentWidth, SettingsControlHeight),
                L.Get(draftAudioDiagnostics ? "audioDiagnostic.on" : "audioDiagnostic.off"),
                () => draftAudioDiagnostics = !draftAudioDiagnostics, draftAudioDiagnostics);
            y += 44;
            var profileBounds = new Rect(SettingsContentX, y, SettingsContentWidth, SettingsControlHeight);
            SettingsButton(c, profileBounds, L.Get("audioDiagnostic.profile", draftAudioDiagnosticProfile) + " ▾",
                () => OpenAudioDiagnosticProfileMenu(profileBounds), enabled: draftAudioDiagnostics);
            y += 44;
            SettingsButton(c, new(SettingsContentX, y, SettingsContentWidth, SettingsControlHeight),
                L.Get(draftAudioDiagnosticFrames ? "audioDiagnostic.framesOn" : "audioDiagnostic.framesOff"),
                () => draftAudioDiagnosticFrames = !draftAudioDiagnosticFrames, draftAudioDiagnosticFrames, draftAudioDiagnostics);
            y += 44;
            y = SettingsParagraph(c, L.Get("audioDiagnostic.restart"), y);
            y = SettingsParagraph(c, L.Get("audioDiagnostic.explanation"), y);
            y = SettingsParagraph(c, L.Get(AudioDiagnosticStatus?.Invoke() ?? "audioDiagnostic.inactive", CurrentAudioDiagnosticProfile?.Invoke() ?? "event-10"), y + 8);
            SettingsButton(c, new(SettingsContentX, y + 8, SettingsContentWidth, SettingsControlHeight),
                L.Get("audioDiagnostic.marker"), () => { RequestAudioDiagnosticMarker?.Invoke(); audioReportStatus = L.Get("audioDiagnostic.marked"); },
                enabled: RequestAudioDiagnosticMarker is not null && AudioDiagnosticStatus?.Invoke() == "audioDiagnostic.active");
            y += 52;
            SettingsButton(c, new(SettingsContentX, y, SettingsContentWidth, SettingsControlHeight),
                L.Get("audioDiagnostic.folder"), () => RequestAudioDiagnosticFolder?.Invoke(), enabled: RequestAudioDiagnosticFolder is not null);
            y += 44;
            if (audioReportTask?.IsCompleted == true)
            {
                audioReportStatus = audioReportTask.IsCompletedSuccessfully ? L.Get("audioDiagnostic.exported", audioReportTask.Result)
                    : L.Get("audioDiagnostic.exportFailed", audioReportTask.Exception?.GetBaseException().Message ?? "");
                audioReportTask = null;
            }
            SettingsButton(c, new(SettingsContentX, y, SettingsContentWidth, SettingsControlHeight),
                L.Get(audioReportTask is null ? "audioDiagnostic.export" : "audioDiagnostic.exporting"),
                () => { audioReportStatus = ""; audioReportTask = RequestAudioDiagnosticExport?.Invoke(); },
                enabled: audioReportTask is null && RequestAudioDiagnosticExport is not null);
            y = SettingsParagraph(c, audioReportStatus, y + 44);
        }
        else y = SettingsParagraph(c, L.Get("audioDiagnostic.windowsOnly"), y);
        audioSettingsContentHeight = y - (SettingsTop - workspaceScroll + 116) + 24;
        c.Unclip();
        ClipSettingsHits(firstHit);
        DrawSettingsScrollbar(c, audioSettingsContentHeight);
    }

    private void OpenAudioDiagnosticProfileMenu(Rect bounds)
    {
        languageMenuOpen = false; menu = -1; contextItems.Clear(); libraryField = bindingCapture = -1;
        foreach (string profile in new[] { "event-10", "event-50", "poll-10", "poll-50" })
            contextItems.Add(new((profile == draftAudioDiagnosticProfile ? "✓ " : "") + L.Get("audioDiagnostic." + profile),
                () => draftAudioDiagnosticProfile = profile));
        const float menuHeight = 12 + 4 * 32;
        contextBounds = new(bounds.X, Math.Clamp(bounds.Bottom + 4, 0, Math.Max(0, height - menuHeight)), bounds.Width, menuHeight);
    }
}
