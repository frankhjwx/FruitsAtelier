using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public enum UpdatePhase { Idle, Unsupported, Checking, Current, Available, Downloading, Ready, Failed }
public sealed record UpdateStatus(UpdatePhase Phase, string Version = "", int Progress = 0);

public sealed partial class EditorView
{
    public Action? RequestUpdateCheck { get; set; }
    public Action? RequestUpdateDownload { get; set; }
    public Action? RequestUpdateRestart { get; set; }
    public Action? RequestUpdateNotes { get; set; }
    public Action? RequestUpdatePreference { get; set; }
    public bool AutomaticUpdateChecks { get; set; } = true;
    public UpdateStatus UpdateStatus { get; set; } = new(UpdatePhase.Idle);
    private bool updatesPage;

    private void OpenUpdates()
    {
        if (!PrepareFileOperation()) return;
        updatesPage = true; libraryField = bindingCapture = -1; FinishVolumeDrag();
        if (UpdateStatus.Phase is UpdatePhase.Idle or UpdatePhase.Current or UpdatePhase.Failed)
            RequestUpdateCheck?.Invoke();
    }

    private void DrawUpdateNotice(ICanvas c)
    {
        if (RequestUpdateCheck is null || UpdateStatus.Phase is not (UpdatePhase.Available or UpdatePhase.Ready)) return;
        Button(c, new(Math.Max(16, width - 430), height - 34, Math.Min(414, width - 32), 28),
            L.Get("update.noticeButton", UpdateStatus.Version), OpenUpdates, active: true);
    }

    private void DrawUpdates(ICanvas c, float x = 32, bool embedded = false)
    {
        float textSize = embedded ? SettingsTextSize : 15;
        if (!embedded) c.Text(L.Get("update.title"), x, 96, 22, Foreground, width - x - 32, true);
        c.Text(L.Get("update.currentVersion", DisplayVersion), x, 144, textSize, Muted, width - x - 32);
        Button(c, new(x, 186, 340, 36), L.Get(AutomaticUpdateChecks ? "update.automaticOn" : "update.automaticOff"), () =>
        { AutomaticUpdateChecks = !AutomaticUpdateChecks; RequestUpdatePreference?.Invoke(); }, AutomaticUpdateChecks, fontSize: embedded ? SettingsTextSize : 12, bold: embedded ? false : null);
        string key = UpdateStatus.Phase switch
        {
            UpdatePhase.Unsupported => "update.unsupported", UpdatePhase.Checking => "update.checking",
            UpdatePhase.Current => "update.current", UpdatePhase.Available => "update.available",
            UpdatePhase.Downloading => "update.downloading", UpdatePhase.Ready => "update.ready",
            UpdatePhase.Failed => "update.failed", _ => "update.help"
        };
        c.Text(L.Get(key, UpdateStatus.Version, UpdateStatus.Progress), x, 246, textSize, Foreground, width - x - 32);
        var phase = UpdateStatus.Phase;
        Button(c, new(x, 296, 210, 38), L.Get("update.check"), () => RequestUpdateCheck?.Invoke(), active: true,
            enabled: phase is not (UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Unsupported), fontSize: embedded ? SettingsTextSize : 12, bold: embedded ? false : null);
        if (phase is UpdatePhase.Available or UpdatePhase.Ready)
            Button(c, new(x + 222, 296, 320, 38), L.Get(phase == UpdatePhase.Ready ? "update.restart" : "update.download"), () =>
        {
            if (phase == UpdatePhase.Ready) RequestUpdateRestart?.Invoke();
            else RequestUpdateDownload?.Invoke();
        }, active: true, fontSize: embedded ? SettingsTextSize : 12, bold: embedded ? false : null);
        Button(c, new(x, 350, 220, 38), L.Get("update.notes"), () => RequestUpdateNotes?.Invoke(), fontSize: embedded ? SettingsTextSize : 12);
        c.Text(L.Get("update.saveHelp"), x, 410, embedded ? SettingsTextSize : 14, Muted, width - x - 32);
        if (!embedded) Button(c, new(x, 466, 200, 36), L.Get(LibraryVisible ? "update.back" : "library.editor"), () => updatesPage = false);
    }
}
