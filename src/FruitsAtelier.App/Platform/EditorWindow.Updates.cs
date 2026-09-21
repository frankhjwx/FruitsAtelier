using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Updates;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private UpdateService? updates;
    private UpdateStatus? lastUpdateStatus;
    private const uint updateStatusChangedMessage = 0x8002;

    private void NotifyUpdateStatusChanged()
    {
        // Worker callbacks only wake the owner; view state belongs to the UI thread.
        Native.PostMessage(hwnd, updateStatusChangedMessage, 0, 0);
    }

    private void ConfigureUpdates()
    {
        try
        {
            updates = new(new VelopackBackend(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FruitsAtelier", "updates.json"), AppLog.Write, NotifyUpdateStatusChanged);
            view.AutomaticUpdateChecks = updates.Preferences.AutomaticChecks;
            view.RequestUpdateCheck = () => _ = Task.Run(() => updates.Check(DateTimeOffset.UtcNow));
            view.RequestUpdateDownload = () => _ = Task.Run(() => updates.Download());
            view.RequestUpdatePreference = () => FileOperation(() => { updates.Preferences.AutomaticChecks = view.AutomaticUpdateChecks; updates.SavePreferences(); });
            view.RequestUpdateNotes = () => FileOperation(() => Process.Start(new ProcessStartInfo(VelopackBackend.Repository + "/releases") { UseShellExecute = true }));
            view.RequestUpdateRestart = () => FileOperation(() => updates.Apply(() =>
            {
                if (view.IsTestplaying || !view.PrepareFileOperation()) return false;
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath)))
                {
                    using (process)
                        if (process.Id != Environment.ProcessId && string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                        { view.ShowError(L.Get("update.otherInstance")); return false; }
                }
                string application = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string? path in new[] { view.LibrarySettings.Workspace, view.LibrarySettings.OsuRoot, view.LibrarySettings.SelectedSkin,
                    view.LibrarySettings.DefaultSkin, view.WorkspaceSession?.Directory })
                    if (!string.IsNullOrWhiteSpace(path) && (Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(application, StringComparison.OrdinalIgnoreCase))
                    { view.ShowError(L.Get("update.dataInsideApp")); return false; }
                if (view.HasEditorProject && view.IsDirty && !SaveProject()) return false;
                view.SaveLibraryMemory();
                audio.Pause();
                return true;
            }));
            PollUpdates();
            if (updates.ShouldCheckOnStartup) view.RequestUpdateCheck();
        }
        catch (Exception e) { AppLog.Write(e.ToString()); }
    }

    private void PollUpdates()
    {
        var status = updates?.Status;
        if (status is null || status == lastUpdateStatus) return;
        lastUpdateStatus = status;
        view.UpdateStatus = lastUpdateStatus;
        if (lastUpdateStatus.Phase is UpdatePhase.Available or UpdatePhase.Ready)
            view.SetNotice(L.Get("update.notice", lastUpdateStatus.Version));
        Invalidate();
    }
}
