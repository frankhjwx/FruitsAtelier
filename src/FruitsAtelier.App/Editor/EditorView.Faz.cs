using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<bool>? RequestFazExport { get; set; }
    public Action<LibraryMap, bool>? RequestLibraryFazExport { get; set; }
    public Action<string>? RequestFazOpen { get; set; }
    private FazPackage? fazPackage;
    private Task? fazTask;
    private Task<FazAssociations>? fazSearch;
    private CancellationTokenSource? fazSearchCancellation;
    private Action? fazCompletion;
    private Action? fazFailure;
    private Action? fazOpened;
    private string?[] fazTargets = [];
    private int fazPage, fazMode;
    private FazProjectCandidate? fazSelected;
    private Action<bool>? fazExportChoice;
    private bool FazVisible => fazPackage is not null || fazTask is not null || fazExportChoice is not null;

    public void ShowFazExportChoice(LibraryMap? map = null)
    {
        if (!PrepareFileOperation()) return;
        menu = -1; contextItems.Clear();
        fazExportChoice = complete =>
        {
            if (map is null) RequestFazExport?.Invoke(complete);
            else RequestLibraryFazExport?.Invoke(map, complete);
        };
    }

    private void ChooseFazExport(bool complete)
    {
        var choice = fazExportChoice; fazExportChoice = null;
        hits.Clear(); fields.Clear(); choice?.Invoke(complete);
    }

    public void BeginFazOpen(string path, Action opened)
    {
        if (!PrepareFileOperation() || FazVisible) return;
        RequestPausePlayback?.Invoke();
        var settings = new LibrarySettings { Workspace = LibrarySettings.Workspace, Songs = LibrarySettings.Songs };
        var task = Task.Run(() => FazArchive.Prepare(path, settings, discoverAssociations: false));
        fazOpened = opened;
        StartFazTask(task, () =>
        {
            fazPackage = task.GetAwaiter().GetResult();
            fazSelected = fazPackage.Projects.Count == 1 ? fazPackage.Projects[0] : null;
            fazTargets = fazSelected?.Targets.Cast<string?>().ToArray() ?? new string?[fazPackage.Project.Difficulties.Count];
            fazPage = 0;
            fazMode = fazPackage.Manifest.Complete && Directory.Exists(settings.Songs) ? 2 : 0;
            if (Directory.Exists(settings.Songs))
            {
                var package = fazPackage;
                var scan = scanTask;
                var token = (fazSearchCancellation = new()).Token;
                fazSearch = Task.Run(async () =>
                {
                    if (scan is not null) await scan.WaitAsync(token);
                    token.ThrowIfCancellationRequested();
                    return FazArchive.FindAssociations(package, settings);
                });
            }
        });
    }

    public void BeginFazExport(string path, bool complete, LibraryMap? map = null)
    {
        if (!PrepareFileOperation() || FazVisible) return;
        var snapshot = map is null ? CaptureProject() : null;
        bool compensate = CompensateTinyDroplets; string workspace = LibrarySettings.Workspace;
        StartFazTask(Task.Run(() => FazArchive.Export(snapshot ?? LibraryOperations.ExportProject(map!), path, complete, compensate, workspace)),
            () => SetNotice(L.Get("library.exported", path)));
    }
    private void StartFazTask(Task task, Action completion, Action? failure = null)
    {
        CancelInteraction(); menu = -1; contextItems.Clear(); hits.Clear(); fields.Clear();
        fazTask = task; fazCompletion = completion; fazFailure = failure;
    }
    private void PumpFaz()
    {
        if (fazSearch is { IsCompleted: true } search)
        {
            fazSearch = null;
            fazSearchCancellation?.Dispose(); fazSearchCancellation = null;
            try
            {
                var result = search.GetAwaiter().GetResult();
                if (fazPackage is { } package)
                {
                    FazArchive.ApplyAssociations(package, result);
                    if (fazSelected is null && package.Projects.Count == 1)
                    {
                        fazSelected = package.Projects[0]; fazTargets = fazSelected.Targets.Cast<string?>().ToArray();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { ShowError(L.Reformat(error.Message)); }
        }
        if (fazTask is not { IsCompleted: true } task) return;
        fazTask = null; var completed = fazCompletion; fazCompletion = null;
        var failed = fazFailure; fazFailure = null;
        try { task.GetAwaiter().GetResult(); completed?.Invoke(); }
        catch (Exception error) { failed?.Invoke(); ShowError(L.Reformat(error.Message)); }
    }
    public void CleanupFazOnExit()
    {
        fazSearchCancellation?.Cancel();
        var package = fazPackage;
        var task = fazTask;
        string workspace = LibrarySettings.Workspace;
        _ = Task.WhenAll(task ?? Task.CompletedTask, fazSearch ?? Task.CompletedTask).ContinueWith(completed =>
        {
            _ = completed.Exception;
            try
            {
                if (package is not null) package.Dispose();
                else if (task is Task<FazPackage> { IsCompletedSuccessfully: true } prepared) prepared.Result.Dispose();
            }
            finally { FazArchive.CleanupTemporaryFiles(workspace); }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        FazArchive.CleanupTemporaryFiles(workspace);
    }

    private void CloseFaz()
    {
        if (fazTask is not null) return;
        fazExportChoice = null;
        var package = fazPackage;
        fazSearchCancellation?.Cancel(); fazSearchCancellation?.Dispose(); fazSearchCancellation = null;
        if (fazSearch is { } search)
            _ = search.ContinueWith(t => { _ = t.Exception; package?.Dispose(); }, TaskScheduler.Default);
        else package?.Dispose();
        fazSearch = null; fazPackage = null; hits.Clear(); fields.Clear();
    }
    private void ImportFaz(string? existing = null)
    {
        if (fazPackage is not { } package) return;
        var settings = new LibrarySettings { Workspace = LibrarySettings.Workspace, Songs = LibrarySettings.Songs };
        bool install = fazMode == 2, compensate = CompensateTinyDroplets;
        var targets = fazMode == 1 ? fazTargets.ToArray() : new string?[fazTargets.Length];
        var task = Task.Run(() => existing is not null ? WorkspaceProject.Open(existing)
            : FazArchive.Import(package, settings, targets, install, compensate));
        StartFazTask(task, () =>
        {
            var session = task.GetAwaiter().GetResult();
            CloseFaz(); LoadWorkspace(session); RefreshLibrary(); fazOpened?.Invoke(); fazOpened = null;
            if (targets.Any(p => p is not null) || existing is not null) RefreshSynchronization(ReviewNextFazDifference);
        });
    }

    private void ReviewNextFazDifference()
    {
        for (int i = 0; i < difficulties.Count; i++)
            if (ShowSyncProblem(i)) return;
    }

    private void DrawFaz(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        c.Fill(new(0, 0, width, height), Background, opacity: .8f);
        if (fazExportChoice is not null)
        {
            float cw = Math.Min(640, width - 24), ch = 300, cx = (width - cw) / 2, cy = (height - ch) / 2;
            c.Fill(new(cx, cy, cw, ch), Panel, 8); c.Stroke(new(cx, cy, cw, ch), Accent, 2, 8);
            c.Text(L.Get("faz.export"), cx + 20, cy + 18, 20, Foreground, cw - 40, true);
            Button(c, new(cx + 20, cy + 64, cw - 40, 36), L.Get("faz.exportComplete"), () => ChooseFazExport(true));
            c.Text(L.Get("faz.completeDescription"), cx + 24, cy + 110, 12, Muted, cw - 48);
            Button(c, new(cx + 20, cy + 151, cw - 40, 36), L.Get("faz.exportProject"), () => ChooseFazExport(false));
            c.Text(L.Get("faz.projectDescription"), cx + 24, cy + 197, 12, Muted, cw - 48);
            Button(c, new(cx + 20, cy + 247, cw - 40, 32), L.Get("mac.cancel"), CloseFaz);
            return;
        }
        float w = Math.Min(720, width - 24), h = Math.Min(530, height - 24), x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get(fazTask is not null ? "faz.working" : "faz.import"), x + 20, y + 18, 20, Foreground, w - 40, true);
        if (fazTask is not null) return;
        if (fazPackage is not { } package) return;
        c.Text(package.Project.Name, x + 20, y + 53, 16, Foreground, w - 40);
        float bw = (w - 48) / 3;
        Button(c, new(x + 16, y + 88, bw, 34), L.Get("faz.independent"), () => fazMode = 0, fazMode == 0);
        Button(c, new(x + 24 + bw, y + 88, bw, 34), L.Get("faz.link"), () => fazMode = 1, fazMode == 1, Directory.Exists(LibrarySettings.Songs));
        Button(c, new(x + 32 + bw * 2, y + 88, bw, 34), L.Get("faz.installSongs"), () => fazMode = 2, fazMode == 2, package.Manifest.Complete && Directory.Exists(LibrarySettings.Songs));
        if (fazMode == 1)
        {
            c.Text(L.Get("faz.chooseProject"), x + 20, y + 134, 13, Muted, w - 40);
            if (package.Projects.Count == 0)
                c.Text(fazSearch is not null ? L.Get("library.scanning") : L.Get("faz.noMatch"), x + 20, y + 190, 14, Muted, w - 40);
            for (int i = fazPage * 2; i < Math.Min(package.Projects.Count, fazPage * 2 + 2); i++)
            {
                var candidate = package.Projects[i]; var map = candidate.Map;
                var rect = new Rect(x + 20, y + 164 + (i % 2) * 86, w - 40, 78);
                bool selected = ReferenceEquals(fazSelected, candidate);
                c.Fill(rect, selected ? 0x304445u : Surface, 6);
                if (selected) c.Stroke(rect, Accent, 2, 6);
                c.Clip(rect);
                if (map.Background.Length > 0) c.Thumbnail(map.Background, new(rect.X + 10, rect.Y + 9, 76, 60));
                float tx = rect.X + 100, tw = rect.Width - 112;
                c.Text(DisplayMetadata(map.Title, map.TitleUnicode), tx, rect.Y + 8, 16, Foreground, tw, true);
                c.Text(L.Get("library.artistMapper", DisplayMetadata(map.Artist, map.ArtistUnicode), map.Creator), tx, rect.Y + 31, 12, Muted, tw);
                c.Text(L.Get("library.diffCount", candidate.DifficultyCount) + " · " + Path.GetFileName(map.Directory), tx, rect.Y + 53, 11, Accent, tw);
                c.Unclip();
                hits.Add(new(rect, () => { fazSelected = candidate; fazTargets = candidate.Targets.Cast<string?>().ToArray(); }, true));
            }
            Button(c, new(x + 20, y + 344, w - 230, 32), L.Get("faz.locateProject"), () =>
                RequestSyncFile?.Invoke(path =>
                {
                    if (path is null || fazPackage != package) return;
                    var settings = new LibrarySettings { Workspace = LibrarySettings.Workspace, Songs = LibrarySettings.Songs };
                    var task = Task.Run(() => FazArchive.MatchProject(package, settings, Path.GetDirectoryName(path)!));
                    StartFazTask(task, () =>
                    {
                        var match = task.GetAwaiter().GetResult();
                        if (match is null) { ShowError(L.Get("faz.incompleteProject")); return; }
                        var candidate = package.Projects.FirstOrDefault(p => WorkspaceSynchronization.Paths.Equals(p.Map.Directory, match.Map.Directory));
                        if (candidate is null) { candidate = match; package.Projects.Add(candidate); }
                        fazSelected = candidate; fazTargets = candidate.Targets.Cast<string?>().ToArray();
                        fazPage = package.Projects.IndexOf(candidate) / 2;
                    });
                }), enabled: fazSearch is null);
            Button(c, new(x + w - 200, y + 344, 80, 32), "←", () => fazPage--, enabled: fazPage > 0);
            Button(c, new(x + w - 110, y + 344, 90, 32), "→", () => fazPage++, enabled: (fazPage + 1) * 2 < package.Projects.Count);
        }
        else
            c.Text(L.Get(fazMode == 0 ? "faz.independentHelp" : "faz.installHelp"), x + 20, y + 150, 13, Muted, w - 40);
        string? existing = fazMode == 1 ? fazTargets.Where(t => t is not null).Select(t => package.Owners.GetValueOrDefault(t!)).FirstOrDefault(p => p is not null) : null;
        existing ??= package.ExistingProject;
        if (existing is not null)
            Button(c, new(x + 20, y + h - 100, w - 40, 32), L.Get("faz.openExisting"), () => ImportFaz(existing));
        Button(c, new(x + 20, y + h - 52, (w - 50) / 2, 34), L.Get("mac.cancel"), CloseFaz);
        Button(c, new(x + 30 + (w - 50) / 2, y + h - 52, (w - 50) / 2, 34), L.Get("faz.import"), () => ImportFaz(), true,
            fazMode != 1 || fazSelected is not null && fazTargets.All(p => p is not null && !package.Owners.ContainsKey(p)));
    }
}
