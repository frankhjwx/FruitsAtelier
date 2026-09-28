using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal bool StorageBusy => storageTask is not null;
    private Task<WorkspaceStorageReport>? storageTask;
    private WorkspaceStorageReport? storageReport;
    private Rect storageFolderBounds;
    private int storageFolderScroll, storageFolderVisible;
    private string storageRoot = "", storageError = "";
    private bool storageReindex;
    private DateTime nextStorageMaintenance = DateTime.UtcNow.AddSeconds(30);

    private void StartStorage(bool clean = false, bool cache = false)
    {
        if (storageTask is not null) return;
        string root = Path.GetFullPath(LibrarySettings.Workspace), songs = LibrarySettings.Songs;
        if (storageRoot != root) { storageReport = null; storageFolderScroll = 0; }
        storageRoot = root; storageError = ""; storageReindex = cache;
        string[] livePaths = difficulties.SelectMany(d => new[] { d.History.Document.AudioPath, d.History.Document.SourcePath }).OfType<string>().ToArray();
        storageTask = Task.Run(() =>
        {
            var report = clean || cache ? WorkspaceStorage.Clean(root, cache, protectedPaths: livePaths) : WorkspaceStorage.Inspect(root);
            if (cache && !report.RecoveryPending)
            {
                new LibraryDatabase(root, songs).ClearDerivedCache();
                var after = WorkspaceStorage.Inspect(root);
                return after with { ReclaimedBytes = report.ReclaimedBytes + Math.Max(0, report.TotalBytes - after.TotalBytes) };
            }
            return report;
        });
    }

    private void PumpStorage()
    {
        if (storageTask is { IsCompleted: true } completed)
        {
            storageTask = null;
            try { storageReport = completed.GetAwaiter().GetResult(); }
            catch (Exception e) { storageError = e.Message; }
            if (storageReindex && storageRoot == Path.GetFullPath(LibrarySettings.Workspace))
            { libraryProjectsNeedReindex = true; StartLibraryScan(); }
            storageReindex = false;
        }
        if (fileMonitor is not null && LibraryVisible && WorkspaceSession is null && storageTask is null
            && scanTask is null && searchTask is null && DateTime.UtcNow >= nextStorageMaintenance)
        {
            nextStorageMaintenance = DateTime.UtcNow.AddDays(1);
            StartStorage(clean: true);
        }
    }

    private void DrawStorage(ICanvas c)
    {
        float x = SettingsContentX, y = SettingsTop + 124, w = SettingsRight - x - 32;
        c.Text(LibrarySettings.Workspace, x, y, 11, Muted, w);
        c.Text(L.Get("storage.policy"), x, y + 24, 11, Muted, w);
        if (storageReport is { } report && storageRoot == Path.GetFullPath(LibrarySettings.Workspace))
        {
            c.Text(L.Get("storage.total", Size(report.TotalBytes)), x, y + 50, 15, Foreground, w, true);
            uint[] colours = [0x4FCFC4, 0xE1B759, 0x847BEA, 0x6AB8E5, 0xD580AB, 0xA1ACB8];
            float barX = x;
            for (int i = 0; i < report.Categories.Count; i++)
            {
                var part = report.Categories[i]; float share = report.TotalBytes == 0 ? 0 : (float)part.Bytes / report.TotalBytes;
                c.Fill(new(barX, y + 78, w * share, 10), colours[i % colours.Length]); barX += w * share;
                string labelKey = "storage." + part.Name;
                c.Text(L.Get(labelKey) + "  " + Size(part.Bytes) + "  " + share.ToString("P1"), x, y + 98 + i * 20, 12, colours[i % colours.Length], w);
            }
            float folderY = y + 224;
            c.Text(L.Get("storage.folders"), x, folderY, 12, Foreground, w, true);
            int visible = Math.Clamp((int)((SettingsBounds.Bottom - 192 - folderY) / 20), 0, 4);
            storageFolderVisible = visible;
            storageFolderScroll = Math.Clamp(storageFolderScroll, 0, Math.Max(0, report.Folders.Count - visible));
            storageFolderBounds = new(x, folderY + 18, w, visible * 20);
            foreach (var folder in report.Folders.Skip(storageFolderScroll).Take(visible))
            {
                folderY += 20;
                c.Text(folder.Name, x, folderY, 11, Muted, w * .6f);
                c.Text(Size(folder.Bytes) + "  " + (report.TotalBytes == 0 ? 0 : (double)folder.Bytes / report.TotalBytes).ToString("P1"), x + w * .62f, folderY, 11, Foreground, w * .38f);
            }
        }
        float bottom = SettingsBounds.Bottom;
        string message = storageTask is not null ? L.Get("storage.working") : storageError.Length > 0 ? storageError
            : storageReport?.RecoveryPending == true ? L.Get("storage.pending") : L.Get("storage.reclaimed", Size(storageReport?.ReclaimedBytes ?? 0));
        c.Text(message, x, bottom - 172, 11, storageError.Length > 0 ? Error : Muted, w);
        bool available = storageTask is null && !SynchronizationBusy && !SynchronizationVisible && scanTask is null && searchTask is null;
        float buttonWidth = (w - 16) / 3;
        SettingsButton(c, new(x, bottom - 145, buttonWidth, 30), L.Get("storage.refresh"), () => StartStorage(), enabled: storageTask is null);
        SettingsButton(c, new(x + buttonWidth + 8, bottom - 145, buttonWidth, 30), L.Get("storage.cleanHistory"), () => StartStorage(clean: true), enabled: available);
        SettingsButton(c, new(x + 2 * (buttonWidth + 8), bottom - 145, buttonWidth, 30), L.Get("storage.clearCache"), () => StartStorage(cache: true), enabled: available);
    }

    private void ScrollStorage(float x, float y, float delta)
    {
        if (settingsCategory == SettingsCategory.Storage && storageReport is { } report && storageFolderBounds.Contains(x, y))
            storageFolderScroll = Math.Clamp(storageFolderScroll - Math.Sign(delta), 0, Math.Max(0, report.Folders.Count - storageFolderVisible));
    }

    private static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):F2} GiB" : $"{bytes / (1024d * 1024):F1} MiB";
}
