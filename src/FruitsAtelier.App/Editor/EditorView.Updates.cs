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

    private void DrawUpdateSettingsButton(ICanvas c)
    {
        if (RequestUpdateCheck is null) return;
        Button(c, new(250, 574, 270, 32), L.Get(UpdateStatus.Phase is UpdatePhase.Available or UpdatePhase.Ready ? "update.availableButton" : "update.title"), () =>
        { updatesPage = true; libraryField = bindingCapture = -1; FinishVolumeDrag(); });
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
        Button(c, new(32, 296, 260, 38), L.Get(phase == UpdatePhase.Ready ? "update.restart" : phase == UpdatePhase.Available ? "update.download" : "update.check"), () =>
        {
            if (phase == UpdatePhase.Ready) RequestUpdateRestart?.Invoke();
            else if (phase == UpdatePhase.Available) RequestUpdateDownload?.Invoke();
            else RequestUpdateCheck?.Invoke();
        }, enabled: phase is not (UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Unsupported));
        Button(c, new(310, 296, 220, 38), L.Get("update.notes"), () => RequestUpdateNotes?.Invoke());
        c.Text(L.Get("update.saveHelp"), 32, 364, 14, Muted, width - 64);
        Button(c, new(32, 420, 200, 36), L.Get("update.back"), () => updatesPage = false);
    }
}
