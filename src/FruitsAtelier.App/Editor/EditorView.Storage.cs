using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal bool StorageBusy => storageTask is not null;
    private Task<WorkspaceStorageReport>? storageTask;
    private WorkspaceStorageReport? storageReport;
    private Rect workspaceScrollBounds, workspaceScrollTrack, workspaceScrollThumb;
    private float workspaceScroll, workspaceScrollGrab;
    private bool workspaceScrollDragging;
    private float WorkspaceContentHeight => 555 + StorageFolderCount * 20;
    private int StorageFolderCount => Math.Min(8, storageReport?.Folders.Count ?? 0);
    private string storageRoot = "", storageError = "";
    private bool storageReindex;
    private DateTime nextStorageMaintenance = DateTime.UtcNow.AddSeconds(30);

    private void StartStorage(bool clean = false, bool cache = false)
    {
        if (storageTask is not null) return;
        string root = Path.GetFullPath(LibrarySettings.Workspace), songs = LibrarySettings.Songs;
        if (storageRoot != root) { storageReport = null; workspaceScroll = 0; }
        storageRoot = root; storageError = ""; storageReindex = false;
        if (clean || cache) nextStorageMaintenance = DateTime.UtcNow.AddDays(1);
        string[] livePaths = difficulties.SelectMany(d => new[] { d.History.Document.AudioPath, d.History.Document.SourcePath }).OfType<string>().ToArray();
        storageTask = Task.Run(() =>
        {
            var report = clean || cache ? WorkspaceStorage.Clean(root, cache, protectedPaths: livePaths) : WorkspaceStorage.Inspect(root);
            if (cache && !report.RecoveryPending)
            {
                storageReindex = true;
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
            { libraryProjectsNeedReindex = true; if (scanTask is { IsCompleted: false }) libraryRescanRequested = true; else StartLibraryScan(); }
            storageReindex = false;
        }
        if (fileMonitor is not null && LibraryVisible && WorkspaceSession is null && storageTask is null
            && scanTask is null && searchTask is null && DateTime.UtcNow >= nextStorageMaintenance)
        {
            nextStorageMaintenance = DateTime.UtcNow.AddDays(1);
            StartStorage(clean: true);
        }
    }

    private void DrawStorage(ICanvas c, float sectionY)
    {
        float x = SettingsContentX, y = sectionY - 22, w = SettingsRight - x - 32;
        c.Text(L.Get("storage.title"), x, sectionY, 18, Foreground, w, true);
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
            foreach (var folder in report.Folders.Take(8))
            {
                folderY += 20;
                if (folderY + 20 < workspaceScrollBounds.Y || folderY > workspaceScrollBounds.Bottom) continue;
                c.Text(folder.Name, x, folderY, 11, Muted, w * .6f);
                c.Text(Size(folder.Bytes) + "  " + (report.TotalBytes == 0 ? 0 : (double)folder.Bytes / report.TotalBytes).ToString("P1"), x + w * .62f, folderY, 11, Foreground, w * .38f);
            }
        }
        float messageY = y + 250 + StorageFolderCount * 20;
        string message = storageTask is not null ? L.Get("storage.working") : storageError.Length > 0 ? storageError
            : storageReport?.RecoveryPending == true ? L.Get("storage.pending") : L.Get("storage.reclaimed", Size(storageReport?.ReclaimedBytes ?? 0));
        c.Text(message, x, messageY, 11, storageError.Length > 0 ? Error : Muted, w);
        bool available = storageTask is null && !SynchronizationBusy && !SynchronizationVisible;
        float buttonWidth = (w - 24) / 4;
        SettingsButton(c, new(x, messageY + 26, buttonWidth, 30), L.Get("storage.refresh"), () => StartStorage(), enabled: storageTask is null);
        SettingsButton(c, new(x + 2 * (buttonWidth + 8), messageY + 26, buttonWidth, 30), L.Get("storage.cleanHistory"), () => StartStorage(clean: true), enabled: available);
        SettingsButton(c, new(x + 3 * (buttonWidth + 8), messageY + 26, buttonWidth, 30), L.Get("storage.clearCache"), () => StartStorage(cache: true), enabled: available);
        SettingsButton(c, new(x + buttonWidth + 8, messageY + 26, buttonWidth, 30), L.Get("storage.openFolder"),
            () => RequestOpenExternalPath?.Invoke(LibrarySettings.Workspace), enabled: RequestOpenExternalPath is not null && Directory.Exists(LibrarySettings.Workspace));
    }

    private void DrawWorkspaceSettings(ICanvas c)
    {
        workspaceScrollBounds = new(SettingsContentX, SettingsTop + 122, SettingsRight - SettingsContentX - 20, SettingsBounds.Height - 252);
        workspaceScroll = Math.Clamp(workspaceScroll, 0, Math.Max(0, WorkspaceContentHeight - workspaceScrollBounds.Height));
        float top = SettingsTop - workspaceScroll;
        int firstHit = hits.Count;
        c.Clip(workspaceScrollBounds);
        c.Text(L.Get("library.settingsDescription"), SettingsContentX, top + 128, SettingsTextSize, Muted, SettingsRight - SettingsContentX - 32);
        LibraryTextField(c, 0, L.Get("library.workspace"), draftWorkspace, top + 180);
        LibraryTextField(c, 1, L.Get("library.songs"), draftOsuRoot, top + 284);
        c.Line(SettingsContentX, top + 367, SettingsRight - 32, top + 367, Grid);
        DrawStorage(c, top + 384);
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--)
        {
            var bounds = hits[i].Bounds;
            float start = Math.Max(bounds.Y, workspaceScrollBounds.Y), end = Math.Min(bounds.Bottom, workspaceScrollBounds.Bottom);
            if (end <= start) hits.RemoveAt(i);
            else hits[i] = hits[i] with { Bounds = bounds with { Y = start, Height = end - start } };
        }
        workspaceScrollTrack = new(SettingsRight - 16, workspaceScrollBounds.Y, 6, workspaceScrollBounds.Height);
        float thumbHeight = Math.Min(workspaceScrollTrack.Height, Math.Max(24, workspaceScrollTrack.Height * workspaceScrollBounds.Height / WorkspaceContentHeight));
        float travel = workspaceScrollTrack.Height - thumbHeight, maximum = Math.Max(0, WorkspaceContentHeight - workspaceScrollBounds.Height);
        workspaceScrollThumb = new(workspaceScrollTrack.X, workspaceScrollTrack.Y + (maximum > 0 ? workspaceScroll / maximum * travel : 0), 6, thumbHeight);
        c.Fill(workspaceScrollTrack, Surface, 3); c.Fill(workspaceScrollThumb, Muted, 3);
    }

    private void ScrollStorage(float x, float y, float delta)
    {
        if (settingsCategory == SettingsCategory.Workspace && workspaceScrollBounds.Contains(x, y))
        {
            libraryField = -1;
            workspaceScroll = Math.Clamp(workspaceScroll - delta / 120 * 64, 0, Math.Max(0, WorkspaceContentHeight - workspaceScrollBounds.Height));
        }
    }

    private bool BeginWorkspaceScroll(float x, float y, int button)
    {
        if (settingsCategory != SettingsCategory.Workspace || button != 0 || !workspaceScrollTrack.Contains(x, y)) return false;
        workspaceScrollGrab = workspaceScrollThumb.Contains(x, y) ? y - workspaceScrollThumb.Y : workspaceScrollThumb.Height / 2;
        workspaceScrollDragging = true; libraryField = -1; MoveWorkspaceScroll(y); return true;
    }

    private void MoveWorkspaceScroll(float y)
    {
        float fraction = Math.Clamp((y - workspaceScrollTrack.Y - workspaceScrollGrab) / Math.Max(1, workspaceScrollTrack.Height - workspaceScrollThumb.Height), 0, 1);
        workspaceScroll = fraction * Math.Max(0, WorkspaceContentHeight - workspaceScrollBounds.Height);
    }

    private static string Size(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):F2} GiB" : $"{bytes / (1024d * 1024):F1} MiB";
}
