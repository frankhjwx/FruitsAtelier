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
    public Action? RequestRegisterFaz { get; set; }
    private FazPackage? fazPackage;
    private Task? fazTask;
    private Action? fazCompletion;
    private Action? fazOpened;
    private string?[] fazTargets = [];
    private bool[] fazIndependent = [];
    private int fazIndex, fazMode;
    private bool FazVisible => fazPackage is not null || fazTask is not null;

    public void BeginFazOpen(string path, Action opened)
    {
        if (!PrepareFileOperation() || FazVisible) return;
        RequestPausePlayback?.Invoke();
        var settings = new LibrarySettings { Workspace = LibrarySettings.Workspace, Songs = LibrarySettings.Songs };
        var task = Task.Run(() => FazArchive.Prepare(path, settings));
        fazOpened = opened;
        StartFazTask(task, () =>
        {
            fazPackage = task.GetAwaiter().GetResult();
            fazTargets = fazPackage.Project.Difficulties.Select((d, i) => FazArchive.Recommend(d, fazPackage.Candidates[i], fazPackage.Manifest.SourceHashes.GetValueOrDefault(d.Id), fazPackage.CandidateHashes)).ToArray();
            fazIndependent = new bool[fazTargets.Length];
            fazIndex = 0;
            fazMode = fazPackage.Manifest.Complete && Directory.Exists(settings.Songs)
                ? fazTargets.All(p => p is not null) ? 1 : 2 : 0;
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
    private void StartFazTask(Task task, Action completion)
    {
        CancelInteraction(); menu = -1; contextItems.Clear(); hits.Clear(); fields.Clear();
        fazTask = task; fazCompletion = completion;
    }
    private void PumpFaz()
    {
        if (fazTask is not { IsCompleted: true } task) return;
        fazTask = null; var completed = fazCompletion; fazCompletion = null;
        try { task.GetAwaiter().GetResult(); completed?.Invoke(); }
        catch (Exception error) { ShowError(L.Reformat(error.Message)); }
    }
    private void CloseFaz()
    {
        if (fazTask is not null) return;
        fazPackage?.Dispose(); fazPackage = null; hits.Clear(); fields.Clear();
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
        });
    }

    private void DrawFaz(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        c.Fill(new(0, 0, width, height), Background, opacity: .8f);
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
        c.Text(L.Get(fazMode == 0 ? "faz.independentHelp" : fazMode == 1 ? "faz.linkHelp" : "faz.installHelp"), x + 20, y + 134, 13, Muted, w - 40);
        c.Text(LibrarySettings.Workspace, x + 20, y + 162, 12, Muted, w - 40);
        c.Text(string.IsNullOrWhiteSpace(LibrarySettings.Songs) ? L.Get("faz.configureSongs") : LibrarySettings.Songs, x + 20, y + 184, 12, Muted, w - 40);
        if (fazMode == 1)
        {
            var diff = package.Project.Difficulties[fazIndex];
            c.Text(L.Get("faz.difficulty", fazIndex + 1, fazTargets.Length, diff.Name), x + 20, y + 222, 16, Foreground, w - 40);
            c.Text(fazTargets[fazIndex] ?? L.Get(fazIndependent[fazIndex] ? "faz.independent" : "faz.noMatch"), x + 20, y + 252, 12, Foreground, w - 40);
            Button(c, new(x + 20, y + 285, 90, 32), "←", () => fazIndex--, enabled: fazIndex > 0);
            Button(c, new(x + 120, y + 285, 90, 32), "→", () => fazIndex++, enabled: fazIndex + 1 < fazTargets.Length);
            Button(c, new(x + 220, y + 285, w - 240, 32), L.Get("faz.nextCandidate", package.Candidates[fazIndex].Count), () =>
            {
                var candidates = package.Candidates[fazIndex];
                int i = candidates.ToList().FindIndex(m => m.Path == fazTargets[fazIndex]);
                fazTargets[fazIndex] = candidates[(i + 1) % candidates.Count].Path; fazIndependent[fazIndex] = false;
            }, enabled: package.Candidates[fazIndex].Count > 0);
            Button(c, new(x + 20, y + 330, (w - 50) / 2, 34), L.Get("sync.chooseFile"), () =>
            {
                int i = fazIndex;
                RequestSyncFile?.Invoke(path => { if (path is not null && fazPackage == package) { fazTargets[i] = path; fazIndependent[i] = false; } });
            });
            Button(c, new(x + 30 + (w - 50) / 2, y + 330, (w - 50) / 2, 34), L.Get("faz.independent"), () => { fazTargets[fazIndex] = null; fazIndependent[fazIndex] = true; });
            c.Text(L.Get("faz.reviewHelp"), x + 20, y + 379, 12, Muted, w - 40);
        }
        string? existing = fazMode == 1 && fazTargets[fazIndex] is { } selected ? package.Owners.GetValueOrDefault(selected) : null;
        existing ??= package.ExistingProject;
        if (existing is not null)
            Button(c, new(x + 20, y + h - 100, w - 40, 32), L.Get("faz.openExisting"), () => ImportFaz(existing));
        Button(c, new(x + 20, y + h - 52, (w - 50) / 2, 34), L.Get("mac.cancel"), CloseFaz);
        Button(c, new(x + 30 + (w - 50) / 2, y + h - 52, (w - 50) / 2, 34), L.Get("faz.import"), () => ImportFaz(), true,
            fazMode != 1 || fazTargets.Select((p, i) => p is null ? fazIndependent[i] : !package.Owners.ContainsKey(p)).All(v => v));
    }
}
