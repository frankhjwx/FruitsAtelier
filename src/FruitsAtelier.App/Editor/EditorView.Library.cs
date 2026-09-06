using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public LibrarySettings LibrarySettings { get; private set; } = new();
    public WorkspaceSession? WorkspaceSession { get; set; }
    public bool LibraryVisible { get; private set; }
    public Action<bool>? RequestLibraryFolder { get; set; }
    public Action<bool>? RequestLibraryImport { get; set; }
    public Action<LibraryMap>? RequestLibraryOpen { get; set; }
    public Action<bool, string>? RequestWorkspaceExport { get; set; }
    private LibraryDatabase? libraryDatabase;
    private Task<LibraryScan>? scanTask;
    private bool libraryRescanRequested;
    private Task<IReadOnlyList<LibraryMap>>? searchTask;
    private Task<Dictionary<string, double?>>? ratingTask;
    private string searchTaskQuery = "", libraryQuery = "", libraryError = "", libraryNotice = "";
    private IReadOnlyList<LibraryMap> libraryMaps = [];
    private List<IGrouping<string, LibraryMap>> libraryGroups = [];
    private Dictionary<string, double?> libraryRatings = [];
    private string? selectedLibraryGroup;
    private bool librarySettingsOpen, libraryProjectsOnly, libraryReplace, exportPage, resourcePage;
    private int libraryField = -1, libraryScroll, libraryDiffScroll;
    private string exportName = "";
    private string draftWorkspace = "", draftSongs = "";
    private DateTime searchAfter, nextLibraryScan = DateTime.MinValue, nextResourceCheck;
    private IReadOnlyList<string> resourceErrors = [];

    public void InitializeLibrary(bool show, LibrarySettings? settings = null)
    {
        try { LibrarySettings = settings ?? LibrarySettings.Load(); }
        catch (Exception e) { libraryError = e.Message; }
        draftWorkspace = LibrarySettings.Workspace; draftSongs = LibrarySettings.Songs;
        LibraryVisible = show;
        librarySettingsOpen = false;
        StartLibraryScan();
    }
    public void SetLibraryFolder(bool workspace, string path)
    {
        if (workspace) draftWorkspace = path; else draftSongs = path;
    }
    public void ShowLibrary()
    {
        if (!PrepareFileOperation()) return;
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        LibraryVisible = true; exportPage = resourcePage = false; libraryField = -1;
        librarySettingsOpen = false;
        if (libraryDatabase is null) StartLibraryScan();
        else { QueueLibrarySearch(); if (DateTime.UtcNow >= nextLibraryScan) StartLibraryScan(); }
    }
    public void ShowWorkspaceExport()
    {
        if (!PrepareFileOperation()) return;
        if (WorkspaceSession is null) { RequestSave?.Invoke(); if (WorkspaceSession is null) return; }
        exportName = CurrentDifficultyName + " (FruitsAtelier)";
        exportPage = LibraryVisible = true; librarySettingsOpen = false; libraryField = -1;
    }
    public void CloseLibrary() { LibraryVisible = exportPage = resourcePage = false; libraryField = -1; }
    public void CheckWorkspaceResources()
    {
        resourceErrors = WorkspaceSession is null ? [] : WorkspaceProject.MissingResources(new BeatmapProject { Name = ProjectName, Difficulties = difficulties.Select(d => new ProjectDifficulty { Id = d.Id, Name = d.Name, Document = d.History.Document }).ToList() });
        nextResourceCheck = DateTime.UtcNow.AddSeconds(3);
    }
    public bool SaveWorkspace(bool copy = false)
    {
        if (!PrepareFileOperation()) return false;
        var project = CaptureProject();
        if (WorkspaceSession is null || copy) WorkspaceSession = WorkspaceProject.Create(LibrarySettings.Workspace, project, LibrarySettings.Songs);
        else WorkspaceProject.Save(WorkspaceSession, project);
        MarkSaved(); CheckWorkspaceResources();
        libraryDatabase?.ReindexProjects(); QueueLibrarySearch();
        SetNotice(L.Get("files.saved", WorkspaceSession.Directory));
        return true;
    }
    public void LoadWorkspace(WorkspaceSession session)
    {
        LoadProject(session.Project); WorkspaceSession = session; CheckWorkspaceResources();
        libraryDatabase?.ReindexProjects(); QueueLibrarySearch(); CloseLibrary();
    }
    public void LibraryExportFinished()
    {
        CheckWorkspaceResources(); StartLibraryScan(); CloseLibrary();
    }
    public void RefreshLibrary() { librarySettingsOpen = false; libraryRescanRequested = scanTask is { IsCompleted: false }; StartLibraryScan(); }
    private void StartLibraryScan()
    {
        if (scanTask is { IsCompleted: false }) return;
        try
        {
            libraryRescanRequested = false;
            libraryDatabase = new(LibrarySettings.Workspace, LibrarySettings.Songs);
            var db = libraryDatabase;
            scanTask = Task.Run(() => db.Scan());
            libraryNotice = L.Get("library.scanning");
            libraryError = "";
            QueueLibrarySearch(); nextLibraryScan = DateTime.UtcNow.AddMinutes(1);
        }
        catch (Exception e) { libraryError = e.Message; }
    }
    private void QueueLibrarySearch() { searchAfter = DateTime.UtcNow.AddMilliseconds(150); searchTaskQuery = "\0"; libraryScroll = 0; }
    private void PumpLibrary()
    {
        if (scanTask is { IsCompleted: true })
        {
            try { var result = scanTask.GetAwaiter().GetResult(); libraryNotice = L.Get("library.indexed", result.Count); libraryError = string.Join("\n", result.Errors.Take(3)); QueueLibrarySearch(); }
            catch (Exception e) { libraryError = e.Message; QueueLibrarySearch(); }
            scanTask = null;
            if (libraryRescanRequested) StartLibraryScan();
        }
        if (searchTask is { IsCompleted: true })
        {
            try
            {
                libraryMaps = searchTask.GetAwaiter().GetResult();
                libraryGroups = libraryMaps.GroupBy(m => libraryProjectsOnly ? m.ProjectPath ?? m.Directory : m.Directory).ToList();
                if (!libraryGroups.Any(g => g.Key == selectedLibraryGroup)) selectedLibraryGroup = libraryGroups.FirstOrDefault()?.Key;
            }
            catch (Exception e) { libraryError = e.Message; }
            searchTask = null;
        }
        if (searchTask is null && libraryDatabase is not null && DateTime.UtcNow >= searchAfter && searchTaskQuery != libraryQuery + libraryProjectsOnly)
        {
            var db = libraryDatabase; string query = libraryQuery; bool projects = libraryProjectsOnly;
            searchTaskQuery = libraryQuery + libraryProjectsOnly;
            searchTask = Task.Run(() => db.Search(query, projects));
        }
        if (ratingTask is { IsCompleted: true })
        {
            if (ratingTask.IsCompletedSuccessfully) foreach (var (path, stars) in ratingTask.Result) libraryRatings[path] = stars;
            ratingTask = null;
        }
        if (LibraryVisible && !librarySettingsOpen && !exportPage && libraryDatabase is not null && DateTime.UtcNow >= nextLibraryScan) StartLibraryScan();
        if (WorkspaceSession is not null && DateTime.UtcNow >= nextResourceCheck) CheckWorkspaceResources();
    }
    private void DrawLibrary(ICanvas c)
    {
        c.Fill(new(0, 0, width, height), Background);
        c.Fill(new(0, 0, width, 64), Panel);
        c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"), new(20, 13, 48, 36));
        c.Text(L.Get(resourcePage ? "library.referenceErrors" : exportPage ? "library.export" : "library.title"), 82, 21, 20, Foreground, width - 480, true);
        Button(c, new(width - 378, 16, 110, 32), L.Get("library.settings"), () => { draftWorkspace = LibrarySettings.Workspace; draftSongs = LibrarySettings.Songs; librarySettingsOpen = !librarySettingsOpen; exportPage = resourcePage = false; });
        Button(c, new(width - 254, 16, 110, 32), L.Get("ui.languageButton"), CycleLanguage);
        Button(c, new(width - 130, 16, 110, 32), L.Get("library.editor"), CloseLibrary);
        if (resourcePage)
        {
            c.Text(L.Get("library.referenceHelp"), 32, 96, 14, Muted, width - 64);
            float y = 144;
            c.Clip(new(24, 140, width - 48, height - 160));
            foreach (string error in resourceErrors.Skip(libraryScroll))
            {
                string remaining = error;
                while (remaining.Length > 0)
                {
                    int length = remaining.Length;
                    while (length > 1 && c.MeasureText(remaining[..length], 13) > width - 80) length--;
                    c.Text(remaining[..length], 32, y, 13, Error, width - 64); y += 22; remaining = remaining[length..];
                }
                y += 18;
            }
            c.Unclip(); return;
        }
        if (exportPage) { DrawExportPage(c); return; }
        if (librarySettingsOpen)
        {
            c.Text(L.Get("library.settingsDescription"), 32, 98, 15, Muted, width - 64);
            LibraryTextField(c, 0, L.Get("library.workspace"), draftWorkspace, 158);
            LibraryTextField(c, 1, L.Get("library.songs"), draftSongs, 256);
            Button(c, new(32, 366, 200, 38), L.Get("library.apply"), () =>
            {
                try { var settings = new LibrarySettings { Workspace = draftWorkspace, Songs = draftSongs }; settings.Save(); LibrarySettings = settings; librarySettingsOpen = false; libraryField = -1; libraryRatings.Clear(); StartLibraryScan(); }
                catch (Exception e) { libraryError = e.Message; }
            }, enabled: scanTask is null && searchTask is null);
            c.Text(libraryError, 32, 430, 14, Error, width - 64);
            return;
        }
        c.Fill(new(0, 64, 190, height - 64), Panel);
        Button(c, new(16, 88, 158, 36), L.Get("library.all"), () => { libraryProjectsOnly = false; QueueLibrarySearch(); }, !libraryProjectsOnly);
        Button(c, new(16, 134, 158, 36), L.Get("library.projects"), () => { libraryProjectsOnly = true; libraryDatabase?.ReindexProjects(); QueueLibrarySearch(); }, libraryProjectsOnly);
        Button(c, new(16, 208, 158, 36), L.Get("library.refresh"), StartLibraryScan, enabled: scanTask is null);
        Button(c, new(16, 254, 158, 36), L.Get("library.new"), () => RequestNewProject?.Invoke());
        Button(c, new(16, 314, 158, 36), L.Get("library.importFolder"), () => RequestLibraryImport?.Invoke(true), enabled: scanTask is null);
        Button(c, new(16, 360, 158, 36), L.Get("library.importFile"), () => RequestLibraryImport?.Invoke(false), enabled: scanTask is null);
        c.Text(libraryNotice, 16, height - 96, 12, Muted, 158);
        float listWidth = width - 558;
        var queryRect = new Rect(214, 84, width - 238, 40);
        c.Fill(queryRect, Surface, 6); c.Stroke(queryRect, libraryField == 2 ? Accent : Grid, radius: 6);
        c.Text(libraryQuery.Length == 0 ? L.Get("library.search") : libraryQuery + (libraryField == 2 ? "│" : ""), 226, 95, 14, libraryQuery.Length == 0 ? Muted : Foreground, queryRect.Width - 24);
        hits.Add(new(queryRect, () => { libraryField = 2; libraryReplace = false; }, true));
        c.Text(L.Get("library.results", libraryGroups.Count), 214, 140, 12, Muted, listWidth);
        int visible = Math.Max(1, (int)(height - 222) / 86);
        libraryScroll = Math.Clamp(libraryScroll, 0, Math.Max(0, libraryGroups.Count - visible));
        for (int i = libraryScroll; i < Math.Min(libraryGroups.Count, libraryScroll + visible); i++)
        {
            var group = libraryGroups[i]; var map = group.First(); float y = 170 + (i - libraryScroll) * 86;
            var rect = new Rect(214, y, listWidth, 78);
            c.Fill(rect, selectedLibraryGroup == group.Key ? 0x304445u : Surface, 6);
            c.Clip(rect);
            if (map.Background.Length > 0 && File.Exists(map.Background)) c.Image(map.Background, new(224, y + 9, 76, 60));
            string title = string.IsNullOrWhiteSpace(map.TitleUnicode) ? map.Title : map.TitleUnicode;
            c.Text(title, 314, y + 10, 16, Foreground, listWidth - 116, true);
            c.Text(map.ArtistUnicode.Length > 0 ? map.ArtistUnicode : map.Artist, 314, y + 33, 12, Muted, listWidth - 116);
            c.Text(L.Get(map.ProjectPath is null ? "library.diffCount" : "library.projectCount", group.Count()), 314, y + 53, 11, Accent, listWidth - 116);
            c.Unclip();
            hits.Add(new(rect, () => { selectedLibraryGroup = group.Key; libraryDiffScroll = 0; libraryField = -1; }, true));
        }
        DrawLibraryDetails(c, width - 320);
        if (libraryGroups.Count == 0) c.Text(L.Get(string.IsNullOrWhiteSpace(LibrarySettings.Songs) && !libraryProjectsOnly ? "library.unboundEmpty" : "library.empty"), 226, 204, 15, Muted, listWidth - 24);
        if (libraryError.Length > 0) c.Text(libraryError.Replace('\n', ' '), 214, height - 34, 12, Error, width - 238);
    }
    private void DrawLibraryDetails(ICanvas c, float x)
    {
        var group = libraryGroups.FirstOrDefault(g => g.Key == selectedLibraryGroup);
        if (group is null) return;
        var map = group.First();
        c.Text(map.Title, x, 170, 15, Foreground, 288, true);
        c.Text(map.Artist + " · " + map.Creator, x, 201, 12, Muted, 288);
        c.Text(map.Tags, x, 228, 12, Muted, 288);
        c.Text(map.Directory, x, 250, 11, Muted, 288);
        Button(c, new(x, 272, 288, 38), L.Get(map.ProjectPath is null ? "library.start" : "library.continue"), () => RequestLibraryOpen?.Invoke(map));
        var entries = group.ToArray(); int count = Math.Max(1, (int)(height - 390) / 40);
        libraryDiffScroll = Math.Clamp(libraryDiffScroll, 0, Math.Max(0, entries.Length - count));
        for (int i = libraryDiffScroll; i < Math.Min(entries.Length, libraryDiffScroll + count); i++)
        {
            var diff = entries[i]; float y = 334 + (i - libraryDiffScroll) * 40;
            libraryRatings.TryGetValue(diff.Path, out var stars);
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "osu", "RulesetCatch.png"), new(x, y, 22, 22), DifficultyColour(stars));
            c.Text(diff.Difficulty, x + 32, y + 3, 13, Foreground, 200);
            c.Text(stars is null ? "—" : stars.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "★", x + 238, y + 3, 12, DifficultyColour(stars), 60);
        }
        if (ratingTask is null)
        {
            var pending = entries.Where(m => !libraryRatings.ContainsKey(m.Path) && m.Path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pending.Length > 0) ratingTask = Task.Run(() =>
            {
                var result = new Dictionary<string, double?>();
                foreach (var entry in pending)
                {
                    try { var d = OsuBeatmapReader.ReadFile(entry.Path); var converted = CatchStreamConverter.Convert(d); result[entry.Path] = converted.Success ? CatchDifficultyCalculator.Calculate(converted.Objects, d.CircleSize).StarRating : null; }
                    catch (Exception) { result[entry.Path] = null; }
                }
                return result;
            });
        }
    }
    private void LibraryTextField(ICanvas c, int index, string label, string value, float y)
    {
        c.Text(label, 32, y, 14, Foreground, width - 64, true);
        var rect = new Rect(32, y + 28, width - (index < 2 ? 216 : 64), 42);
        c.Fill(rect, Surface, 5); c.Stroke(rect, libraryField == index ? Accent : Grid, radius: 5);
        c.Text(value + (libraryField == index ? "│" : ""), 44, y + 40, 14, Foreground, rect.Width - 24);
        hits.Add(new(rect, () => { libraryField = index; libraryReplace = false; }, true));
        if (index < 2) Button(c, new(width - 168, y + 28, 136, 42), L.Get("library.browse"), () => RequestLibraryFolder?.Invoke(index == 0));
    }
    private void DrawExportPage(ICanvas c)
    {
        c.Text(L.Get("library.exportDescription", CurrentDifficultyName), 32, 98, 16, Foreground, width - 64);
        bool bound = !string.IsNullOrWhiteSpace(LibrarySettings.Songs);
        c.Text(bound ? LibrarySettings.Songs : L.Get("library.bindForExport"), 32, 132, 13, Muted, width - 64);
        LibraryTextField(c, 3, L.Get("library.newDifficultyName"), exportName, 190);
        Button(c, new(32, 310, 240, 42), L.Get("library.exportNew"), () => RequestWorkspaceExport?.Invoke(false, exportName), enabled: bound);
        Button(c, new(292, 310, 240, 42), L.Get("library.exportOverride"), () => RequestWorkspaceExport?.Invoke(true, exportName), enabled: bound);
        var entry = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulties[activeDifficulty].Id);
        c.Text(L.Get("library.overrideTarget", entry?.ExportTarget ?? entry?.Source ?? L.Get("library.noTarget")), 32, 382, 13, Muted, width - 64);
        c.Text(L.Get("library.exportConflictHint"), 32, 414, 13, Gold, width - 64);
        if (resourceErrors.Count > 0) c.Text(L.Get("library.missingResources", string.Join("\n", resourceErrors)), 32, 466, 14, Error, width - 64);
    }
    public bool LibraryLoading => scanTask is { IsCompleted: false } || searchTask is { IsCompleted: false } || ratingTask is { IsCompleted: false };
    public bool LibraryTextFocused => LibraryVisible && libraryField >= 0;
    public void PasteLibraryText(string text)
    {
        if (!LibraryTextFocused) return;
        string value = new(text.Where(c => !char.IsControl(c)).ToArray());
        LibraryFieldValue = new string(((libraryReplace ? "" : LibraryFieldValue) + value).Take(4096).ToArray());
        libraryReplace = false;
    }
    private void LibraryKey(int key, bool ctrl)
    {
        if (key == 27) { if (libraryField >= 0) libraryField = -1; else CloseLibrary(); return; }
        if (key == 116) { StartLibraryScan(); return; }
        if (ctrl && key == 70) { libraryField = 2; libraryReplace = true; return; }
        if (libraryField < 0) return;
        if (ctrl && key == 65) { libraryReplace = true; return; }
        string value = LibraryFieldValue;
        if (key == 8) { var positions = System.Globalization.StringInfo.ParseCombiningCharacters(value); LibraryFieldValue = libraryReplace || positions.Length == 0 ? "" : value[..positions[^1]]; libraryReplace = false; }
        if (key == 46) { LibraryFieldValue = ""; libraryReplace = false; }
        if (key == 13) libraryField = -1;
    }
    private string LibraryFieldValue
    {
        get => libraryField switch { 0 => draftWorkspace, 1 => draftSongs, 2 => libraryQuery, 3 => exportName, _ => "" };
        set { switch (libraryField) { case 0: draftWorkspace = value; break; case 1: draftSongs = value; break; case 2: libraryQuery = value; QueueLibrarySearch(); break; case 3: exportName = value; break; } }
    }
}
