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

    private void DrawUpdateSettingsButton(ICanvas c)
    {
        if (RequestUpdateCheck is null) return;
        Button(c, new(250, 574, 270, 32), L.Get(UpdateStatus.Phase is UpdatePhase.Available or UpdatePhase.Ready ? "update.availableButton" : "update.title"), () =>
        { OpenUpdates(); });
    }

    private void DrawUpdates(ICanvas c)
    {
        c.Text(L.Get("update.title"), 32, 96, 22, Foreground, width - 64, true);
        c.Text(L.Get("update.currentVersion", DisplayVersion), 32, 144, 15, Muted, width - 64);
        Button(c, new(32, 186, 340, 36), L.Get(AutomaticUpdateChecks ? "update.automaticOn" : "update.automaticOff"), () =>
        { AutomaticUpdateChecks = !AutomaticUpdateChecks; RequestUpdatePreference?.Invoke(); }, AutomaticUpdateChecks);
        string key = UpdateStatus.Phase switch
        {
            UpdatePhase.Unsupported => "update.unsupported", UpdatePhase.Checking => "update.checking",
            UpdatePhase.Current => "update.current", UpdatePhase.Available => "update.available",
            UpdatePhase.Downloading => "update.downloading", UpdatePhase.Ready => "update.ready",
            UpdatePhase.Failed => "update.failed", _ => "update.help"
        };
        c.Text(L.Get(key, UpdateStatus.Version, UpdateStatus.Progress), 32, 246, 15, Foreground, width - 64);
        var phase = UpdateStatus.Phase;
        Button(c, new(32, 296, 210, 38), L.Get("update.check"), () => RequestUpdateCheck?.Invoke(), active: true,
            enabled: phase is not (UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Unsupported));
        if (phase is UpdatePhase.Available or UpdatePhase.Ready)
            Button(c, new(254, 296, 320, 38), L.Get(phase == UpdatePhase.Ready ? "update.restart" : "update.download"), () =>
        {
            if (phase == UpdatePhase.Ready) RequestUpdateRestart?.Invoke();
            else RequestUpdateDownload?.Invoke();
        }, active: true);
        Button(c, new(32, 350, 220, 38), L.Get("update.notes"), () => RequestUpdateNotes?.Invoke());
        c.Text(L.Get("update.saveHelp"), 32, 410, 14, Muted, width - 64);
        Button(c, new(32, 466, 200, 36), L.Get(LibraryVisible ? "update.back" : "library.editor"), () => updatesPage = false);
    }
}
