using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Updates;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private UpdateService? updates;
    private UpdateStatus? lastUpdateStatus;

    private void ConfigureUpdates()
    {
        try
        {
            updates = new(new VelopackBackend(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FruitsAtelier", "updates.json"), AppLog.Write);
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
        if (updates is null || updates.Status == lastUpdateStatus) return;
        lastUpdateStatus = updates.Status;
        view.UpdateStatus = lastUpdateStatus;
        if (lastUpdateStatus.Phase is UpdatePhase.Available or UpdatePhase.Ready)
            view.SetNotice(L.Get("update.notice", lastUpdateStatus.Version));
        Invalidate();
    }
}
