using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public LibrarySettings LibrarySettings { get; private set; } = new();
    public WorkspaceSession? WorkspaceSession { get; set; }
    public bool LibraryVisible { get; private set; }
    public bool ExportVisible => exportPage;
    public Action<bool>? RequestLibraryFolder { get; set; }
    public Action<bool>? RequestLibraryImport { get; set; }
    public Action<LibraryMap>? RequestLibraryOpen { get; set; }
    public Action<string>? RequestOsuExport { get; set; }
    public Action<bool, string>? RequestWorkspaceExport { get; set; }
    private LibraryDatabase? libraryDatabase;
    private Task<(LibraryDatabase Database, LibraryScan Scan)>? scanTask;
    private bool libraryRescanRequested;
    private bool libraryProjectsNeedReindex;
    private LibraryScanProgress? libraryScanProgress;
    private Task<LibraryBrowser>? searchTask;
    private Task<Dictionary<string, double?>>? ratingTask;
    private string searchTaskQuery = "", libraryQuery = "", libraryError = "", libraryNotice = "";
    private string runningSearchQuery = "";
    private string? runningSearchSelection;
    private float runningSearchScroll;
    private bool runningSearchProjects, libraryResultsReady;
    private LibraryBrowser? libraryBrowser;
    private int LibrarySetCount => libraryBrowser?.Count ?? 0;
    public int LibraryCachedRows => libraryBrowser?.CachedRows ?? 0;
    public int LibrarySetTotal => LibrarySetCount;
    private readonly List<(Rect Bounds, LibraryMap Map)> libraryCards = [];
    private Dictionary<string, double?> libraryRatings = [];
    private string? selectedLibraryGroup;
    private bool librarySettingsOpen, libraryProjectsOnly, libraryReplace, exportPage, resourcePage;
    private int libraryField = -1, libraryDiffScroll;
    private float libraryScroll;
    private string exportName = "";
    private int exportMode;
    private string draftWorkspace = "", draftOsuRoot = "", draftDefaultSkin = "";
    private DateTime searchAfter, nextLibraryScan = DateTime.MinValue, nextResourceCheck;
    private IReadOnlyList<string> resourceErrors = [];

    public void InitializeLibrary(bool show, LibrarySettings? settings = null)
    {
        try { LibrarySettings = settings ?? LibrarySettings.Load(); }
        catch (Exception e) { libraryError = e.Message; }
        playbackLineFromBottom = LibrarySettings.PlaybackLineFromBottom;
        ApplyAudioVolume();
        draftWorkspace = LibrarySettings.Workspace; draftOsuRoot = LibrarySettings.OsuRoot; draftDefaultSkin = LibrarySettings.DefaultSkin ?? "";
        LibraryVisible = show;
        librarySettingsOpen = false; updatesPage = false;
        LoadLibraryMemory();
        StartLibraryScan();
    }
    public void SetLibraryFolder(bool workspace, string path)
    {
        if (workspace) draftWorkspace = path; else draftOsuRoot = path;
    }
    public void ShowLibrary()
    {
        if (DiscardConfirmationVisible || !PrepareFileOperation()) return;
        if (HasEditorProject && IsDirty)
        {
            ShowDiscardConfirmation(answer =>
            {
                if (answer == 2) return;
                try
                {
                    if (answer == 7 || answer == 6 && SaveWorkspace()) LeaveEditor();
                }
                catch (Exception error) { ShowError(L.Reformat(error.Message)); }
            });
            return;
        }
        LeaveEditor();
    }
    private void LeaveEditor()
    {
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        if (WorkspaceSession is { } session)
        {
            string? source = session.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulties[activeDifficulty].Id)?.Source;
            if (source is null && !libraryProjectsOnly) SwitchLibraryCategory(true);
            string? current = libraryProjectsOnly ? session.Directory : source is null ? null : Path.GetDirectoryName(source);
            if (current is not null)
            {
                if (libraryQuery.Length > 0 && libraryBrowser?.Selected?.Key != current)
                { libraryQuery = ""; libraryResultsReady = false; }
                selectedLibraryGroup = current;
            }
        }
        if (HasEditorProject)
        {
            ResetHitsounds();
            LoadProject(BeatmapProject.FromDocuments([new MapDocument { IsDemo = false }]));
            HasEditorProject = false;
        }
        hits.Clear(); fields.Clear();
        revealLibrarySelection = true;
        LibraryVisible = true; exportPage = resourcePage = false; libraryField = -1;
        librarySettingsOpen = false; updatesPage = false;
        if (libraryDatabase is null) StartLibraryScan();
        else { QueueLibrarySearch(); if (DateTime.UtcNow >= nextLibraryScan) StartLibraryScan(); }
    }
    public void ShowWorkspaceExport()
    {
        if (!HasEditorProject || ExportVisible) return;
        if (!PrepareFileOperation()) return;
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        exportName = CurrentDifficultyName + " (FruitsAtelier)";
        exportMode = 0;
        exportPage = true; LibraryVisible = false; librarySettingsOpen = false; updatesPage = false; libraryField = -1;
        menu = -1; contextItems.Clear(); hits.Clear(); fields.Clear();
    }
    public void CloseLibrary()
    {
        if (LibraryVisible && !exportPage) { RememberLibraryPosition(); SaveLibraryMemory(); }
        LibraryVisible = !HasEditorProject; exportPage = resourcePage = false; librarySettingsOpen = false; updatesPage = false;
        libraryField = -1; libraryPointerActive = false; contextItems.Clear(); languageMenuOpen = false;
    }
    public bool TryResumeLibraryProject(LibraryMap map)
    {
        if (WorkspaceSession is not { } session || map.ProjectPath is null || session.Directory != map.ProjectPath) return false;
        CloseLibrary();
        OfferAdditionalDifficulties();
        return true;
    }
    public void CheckWorkspaceResources()
    {
        if (WorkspaceSession is null) resourceErrors = [];
        else
        {
            var snapshot = ResourceSnapshot();
            resourceReferences ??= WorkspaceProject.ResourceReferences(snapshot);
            resourceErrors = resourceReferences.FindMissing();
        }
        nextResourceCheck = DateTime.UtcNow.AddSeconds(3);
    }
    public bool SaveWorkspace()
    {
        if (!PrepareFileOperation()) return false;
        var project = CaptureProject();
        if (WorkspaceSession is null) WorkspaceSession = WorkspaceProject.Create(LibrarySettings.Workspace, project, LibrarySettings.Songs);
        else WorkspaceProject.Save(WorkspaceSession, project);
        MarkSaved(); CheckWorkspaceResources();
        libraryProjectsNeedReindex = true; QueueLibrarySearch();
        SetNotice(L.Get("files.saved", WorkspaceSession.Directory));
        return true;
    }
    public bool CurrentDifficultyHasExport => WorkspaceSession?.Manifest.Difficulties
        .Any(d => d.Id == difficulties[activeDifficulty].Id && d.ExportTarget is not null && d.ExportHash is not null) == true;
    public bool ProjectInSongs => !string.IsNullOrWhiteSpace(LibrarySettings.Songs) && (WorkspaceSession is { } session
        ? WorkspaceProject.HasExistingSongsFile(session.Manifest, LibrarySettings.Songs)
        : Document.SourcePath is { } path && WorkspaceProject.Within(LibrarySettings.Songs, path) && File.Exists(path));
    public void SaveCurrentDifficulty()
    {
        if (DiscardConfirmationVisible || ExportVisible || !PrepareFileOperation()) return;
        if (string.IsNullOrWhiteSpace(LibrarySettings.Songs)) { SaveWorkspace(); return; }
        if (!ProjectInSongs)
        {
            if (!SaveWorkspace()) return;
            ShowDiscardConfirmation(answer => { if (answer == 6) ShowWorkspaceExport(); });
            offerSongsExport = true;
            return;
        }
        var entry = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulties[activeDifficulty].Id);
        if (CurrentDifficultyHasExport)
            RequestWorkspaceExport?.Invoke(true, CurrentDifficultyName);
        else if (entry?.Source is not null || Document.SourcePath is { } source && Path.GetExtension(source).Equals(".osu", StringComparison.OrdinalIgnoreCase))
            ShowWorkspaceExport();
        else SaveWorkspace();
    }
    public void LoadWorkspace(WorkspaceSession session, bool checkAdditionalDifficulties = false)
    {
        LoadProject(session.Project); WorkspaceSession = session; CheckWorkspaceResources();
        libraryProjectsNeedReindex = true; QueueLibrarySearch(); CloseLibrary();
        if (session.IsNewImport) OfferSliderConversion(true);
        else if (checkAdditionalDifficulties) OfferAdditionalDifficulties();
    }
    private void OfferAdditionalDifficulties()
    {
        if (WorkspaceSession is not { } session) return;
        var additions = FruitsAtelier.App.Platform.LibraryOperations.MissingDifficulties(session, CaptureProject());
        if (additions.Count == 0) return;
        ShowDiscardConfirmation(answer =>
        {
            if (answer != 6) return;
            try
            {
                var imported = BeatmapProject.FromDocuments(additions.Select(m => OsuBeatmapReader.ReadFile(m.Path)).ToArray());
                var candidate = CaptureProject();
                candidate.Difficulties.AddRange(imported.Difficulties);
                candidate.Validate();
                difficulties.AddRange(imported.Difficulties.Select(d => new DifficultySession(d)));
                projectStructureDirty = true;
                PreloadProjectHitsounds();
            }
            catch (Exception e) { ShowError(e.Message); }
        });
        additionalDifficulties = additions;
    }
    public void LibraryExportFinished(WorkspaceExportPlan plan)
    {
        if (plan.ExpectedHash is null && WorkspaceSession is { } session)
        {
            LoadWorkspace(WorkspaceProject.Open(session.Directory));
            int index = WorkspaceSession!.Manifest.Difficulties.FindIndex(d => string.Equals(d.Source, plan.Target, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) SwitchDifficulty(index);
        }
        CheckWorkspaceResources(); StartLibraryScan(); CloseLibrary();
    }
    public void RefreshLibrary() { librarySettingsOpen = false; updatesPage = false; libraryRescanRequested = scanTask is { IsCompleted: false }; StartLibraryScan(); }
    private LibraryDatabase? scanningDatabase;
    private DateTime nextScanRefresh;
    private long searchedLibraryRevision = -1;
    private void StartLibraryScan()
    {
        if (scanTask is { IsCompleted: false }) return;
        try
        {
            libraryRescanRequested = false;
            string workspace = LibrarySettings.Workspace, songs = LibrarySettings.Songs;
            Volatile.Write(ref scanningDatabase, null);
            searchedLibraryRevision = -1;
            Volatile.Write(ref libraryScanProgress, new(0, 0, 0));
            // Schema initialization can wait on another scan's SQLite write lock as well.
            scanTask = Task.Run(() =>
            {
                var db = new LibraryDatabase(workspace, songs);
                Volatile.Write(ref scanningDatabase, db);
                return (db, db.Scan(progress: p => Volatile.Write(ref libraryScanProgress, p)));
            });
            libraryNotice = L.Get("library.scanning");
            libraryError = "";
            QueueLibrarySearch(); nextLibraryScan = DateTime.UtcNow.AddMinutes(1);
        }
        catch (Exception e) { libraryError = e.Message; }
    }
    private void QueueLibrarySearch() { searchAfter = DateTime.UtcNow.AddMilliseconds(150); searchTaskQuery = "\0"; }
    private void PumpLibrary()
    {
        libraryBrowser?.Pump();
        if (libraryBrowser?.Error is { } browserError) libraryError = browserError;
        if (libraryMemoryDirty && DateTime.UtcNow >= libraryMemorySaveAfter) SaveLibraryMemory();
        if (scanTask is { IsCompleted: false } && Volatile.Read(ref scanningDatabase) is { } scanning && DateTime.UtcNow >= nextScanRefresh && scanning.Revision != searchedLibraryRevision)
        {
            libraryDatabase = scanning;
            searchedLibraryRevision = scanning.Revision;
            searchTaskQuery = "\0";
            nextScanRefresh = DateTime.UtcNow.AddSeconds(5);
        }
        if (scanTask is { IsCompleted: false } && Volatile.Read(ref libraryScanProgress) is { } progress)
            libraryNotice = L.Get("library.scanProgress", progress.Files, progress.Indexed, progress.Errors);
        if (scanTask is { IsCompleted: true })
        {
            try { var (db, result) = scanTask.GetAwaiter().GetResult(); libraryDatabase = db; libraryNotice = L.Get("library.indexed", result.Count); libraryError = ""; QueueLibrarySearch(); }
            catch (Exception e) { libraryError = e.Message; QueueLibrarySearch(); }
            scanTask = null;
            if (libraryRescanRequested) StartLibraryScan();
        }
        if (searchTask is { IsCompleted: true })
        {
            try
            {
                var result = searchTask.GetAwaiter().GetResult();
                if (runningSearchQuery != libraryQuery || runningSearchProjects != libraryProjectsOnly
                    || runningSearchSelection != selectedLibraryGroup || runningSearchScroll != libraryScroll)
                { result.Retire(); searchTask = null; QueueLibrarySearch(); return; }
                float fraction = libraryScroll - (int)libraryScroll;
                libraryBrowser?.Retire(); libraryBrowser = result;
                if (result.TopIndex >= 0 && !libraryPointerActive) libraryScroll = result.TopIndex + fraction;
                if (selectedLibraryGroup is null || scanTask is not { IsCompleted: false }) selectedLibraryGroup = result.Selected?.Key;
                libraryResultsReady = scanTask is not { IsCompleted: false };
                if (libraryResultsReady) RememberLibraryPosition();
            }
            catch (Exception e) { libraryError = e.Message; }
            searchTask = null;
        }
        if (searchTask is null && libraryDatabase is not null && DateTime.UtcNow >= searchAfter && searchTaskQuery != libraryQuery + libraryProjectsOnly)
        {
            var db = libraryDatabase; string query = libraryQuery; bool projects = libraryProjectsOnly;
            runningSearchQuery = query; runningSearchProjects = projects;
            searchTaskQuery = libraryQuery + libraryProjectsOnly;
            bool reindex = libraryProjectsNeedReindex;
            libraryProjectsNeedReindex = false;
            string? selected = selectedLibraryGroup, top = libraryResultsReady ? libraryBrowser?.Get((int)libraryScroll)?.Key : null;
            int offset = (int)libraryScroll;
            runningSearchSelection = selected; runningSearchScroll = libraryScroll;
            searchTask = Task.Run(() =>
            {
                if (reindex) db.ReindexProjects();
                return LibraryBrowser.Create(db, query, projects, selected, top, offset);
            });
        }
        if (ratingTask is { IsCompleted: true })
        {
            if (ratingTask.IsCompletedSuccessfully)
                foreach (var (path, stars) in ratingTask.Result)
                {
                    if (libraryRatings.Count >= 512) libraryRatings.Remove(libraryRatings.Keys.First());
                    libraryRatings[path] = stars;
                }
            ratingTask = null;
        }
        if (LibraryVisible && !librarySettingsOpen && !exportPage && libraryDatabase is not null && DateTime.UtcNow >= nextLibraryScan) StartLibraryScan();
        PumpWorkspaceResources();
    }
    private void OpenLibraryCard(float x, float y)
    {
        if (librarySettingsOpen || exportPage || resourcePage) return;
        foreach (var card in libraryCards)
        {
            if (!card.Bounds.Contains(x, y)) continue;
            OpenSelectedLibraryMap(card.Map);
            return;
        }
    }
    private void DrawLibrary(ICanvas c)
    {
        libraryCards.Clear();
        libraryScrollTrack = libraryDiffTrack = default;
        c.Fill(new(0, 0, width, height), Background);
        if (librarySettingsOpen) { DrawSettings(c); return; }
        DrawHeader(c);
        c.Text(L.Get(resourcePage ? "library.referenceErrors" : exportPage ? "library.export" : "library.title"), 109, 11, 13, Foreground, width - 535, true);
        Button(c, new(HeaderNavigationBounds.X - 116, 6, 110, 28), L.Get("library.settings"), OpenSettings);
        if (HasEditorProject) Button(c, HeaderNavigationBounds, L.Get("library.editor"), CloseLibrary);
        if (updatesPage) { DrawUpdates(c); return; }
        if (resourcePage)
        {
            c.Text(L.Get("library.referenceHelp"), 32, 96, 14, Muted, width - 64);
            float y = 144;
            c.Clip(new(24, 140, width - 48, height - 160));
            foreach (string error in resourceErrors.Skip((int)libraryScroll))
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
        c.Fill(new(0, HeaderHeight, 190, height - HeaderHeight), Panel);
        Button(c, new(16, 88, 158, 36), L.Get("library.all"), () => SwitchLibraryCategory(false), !libraryProjectsOnly);
        Button(c, new(16, 134, 158, 36), L.Get("library.projects"), () => SwitchLibraryCategory(true), libraryProjectsOnly);
        var noticeLines = libraryNotice.Split('\n');
        for (int i = 0; i < noticeLines.Length; i++) c.Text(noticeLines[i], 16, height - 96 + i * 19, 12, Muted, 158);
        if (SkinName is { } skinName) c.Text(L.Get("skin.selector", skinName), 16, height - 126, 12, Muted, 158);
        float listWidth = width - 558;
        var queryRect = new Rect(214, 84, width - 238, 40);
        c.Fill(queryRect, Surface, 6); c.Stroke(queryRect, libraryField == 2 ? Accent : Grid, radius: 6);
        if (libraryQuery.Length == 0 && libraryField != 2)
            c.Text(L.Get("library.search"), 226, 95, 14, Muted, queryRect.Width - 24);
        else DrawInputText(c, new(226, 95, queryRect.Width - 24, 20), libraryQuery, 14, libraryField == 2, libraryReplace);
        hits.Add(new(queryRect, () => { libraryField = 2; libraryReplace = false; }, true));
        c.Text(L.Get("library.results", LibrarySetCount), 214, 140, 12, Muted, listWidth);
        libraryListBounds = new(214, 170, listWidth, Math.Max(86, height - 212));
        if (revealLibrarySelection && LibrarySetCount > 0)
        {
            int selected = libraryBrowser?.Selected is { } selectedRow && selectedRow.Key == selectedLibraryGroup ? selectedRow.Index : -1;
            if (selected >= 0)
            {
                if (selected < libraryScroll) libraryScroll = selected;
                else if (selected + 1 > libraryScroll + LibraryVisibleRows) libraryScroll = selected + 1 - LibraryVisibleRows;
                revealLibrarySelection = false;
            }
        }
        if (libraryResultsReady) libraryScroll = Math.Clamp(libraryScroll, 0, LibraryMaxScroll);
        libraryBrowser?.RequestVisible((int)libraryScroll, (int)Math.Ceiling(libraryScroll + LibraryVisibleRows) + 2);
        c.Clip(libraryListBounds);
        for (int i = (int)libraryScroll; i < Math.Min(LibrarySetCount, Math.Ceiling(libraryScroll + LibraryVisibleRows)); i++)
        {
            float y = 170 + (i - libraryScroll) * 86;
            var rect = new Rect(214, y, listWidth, 78);
            var group = libraryBrowser?.Get(i);
            if (group is null) { c.Fill(rect, Surface, 6); continue; }
            var map = group.Map;
            libraryCards.Add((new(rect.X, Math.Max(rect.Y, libraryListBounds.Y), rect.Width,
                Math.Max(0, Math.Min(rect.Bottom, libraryListBounds.Bottom) - Math.Max(rect.Y, libraryListBounds.Y))), map));
            bool outsideSongs = group.InSongs == false;
            bool selected = selectedLibraryGroup == group.Key;
            uint cardColour = outsideSongs ? 0x303449u : selected ? 0x304445u : Surface;
            c.Fill(rect, cardColour, 6);
            if (outsideSongs && selected) c.Stroke(rect, Accent, 2, 6);
            c.Clip(rect);
            if (map.Background.Length > 0) c.Thumbnail(map.Background, new(224, y + 9, 76, 60));
            string title = DisplayMetadata(map.Title, map.TitleUnicode);
            c.Text(title, 314, y + 10, 16, Foreground, listWidth - 116, true);
            string artist = DisplayMetadata(map.Artist, map.ArtistUnicode);
            c.Text(map.Creator.Length > 0 ? L.Get("library.artistMapper", artist, map.Creator) : artist, 314, y + 33, 12, Muted, listWidth - 116);
            float countWidth = listWidth - 116;
            if (outsideSongs)
            {
                string presence = L.Get("library.notInSongs");
                float badgeWidth = c.MeasureText(presence, 11) + 16;
                var badge = new Rect(rect.Right - badgeWidth - 12, y + 49, badgeWidth, 22);
                c.Fill(badge, 0x41465Fu, 4);
                c.Text(presence, badge.X + 8, y + 53, 11, Foreground, badgeWidth - 16);
                countWidth = Math.Max(0, badge.X - 324);
            }
            c.Text(L.Get(libraryProjectsOnly ? "library.projectCount" : "library.diffCount", group.Count), 314, y + 53, 11, Accent, countWidth);
            c.Unclip();
        }
        c.Unclip();
        DrawLibraryScrollbar(c, new(libraryListBounds.Right + 6, 170, 10, libraryListBounds.Height), libraryScroll, LibrarySetCount, LibraryVisibleRows, false);
        DrawLibraryDetails(c, width - 320);
        if (LibrarySetCount == 0) c.Text(L.Get(string.IsNullOrWhiteSpace(LibrarySettings.Songs) && !libraryProjectsOnly ? "library.unboundEmpty" : "library.empty"), 226, 204, 15, Muted, listWidth - 24);
        if (libraryError.Length > 0) c.Text(libraryError.Replace('\n', ' '), 214, height - 34, 12, Error, width - 238);
        else c.Text(L.Get("library.dropHint"), 214, height - 34, 12, Muted, width - 238);
    }
    private void DrawLibraryDetails(ICanvas c, float x)
    {
        var group = libraryBrowser?.Selected;
        if (group is null) return;
        var map = group.Map;
        c.Text(DisplayMetadata(map.Title, map.TitleUnicode), x, 170, 15, Foreground, 288, true);
        c.Text(DisplayMetadata(map.Artist, map.ArtistUnicode) + " · " + map.Creator, x, 201, 12, Muted, 288);
        c.Text(map.Tags, x, 228, 12, Muted, 288);
        c.Text(map.Directory, x, 250, 11, Muted, 288);
        Button(c, new(x, 272, 288, 38), L.Get(map.ProjectPath is null ? "library.start" : "library.continue"), () => OpenSelectedLibraryMap(map));
        int count = LibraryVisibleDifficulties;
        libraryDiffScroll = Math.Clamp(libraryDiffScroll, 0, Math.Max(0, group.Count - count));
        libraryBrowser!.RequestDetails(libraryDiffScroll);
        var pending = new List<LibraryMap>();
        for (int i = libraryDiffScroll; i < Math.Min(group.Count, libraryDiffScroll + count); i++)
        {
            var diff = libraryBrowser.Detail(i);
            if (diff is null) continue;
            if (!libraryRatings.ContainsKey(diff.Path) && (diff.Path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)
                || diff.Path.EndsWith(".catchdiff", StringComparison.OrdinalIgnoreCase))) pending.Add(diff);
            float y = 334 + (i - libraryDiffScroll) * 40;
            libraryRatings.TryGetValue(diff.Path, out var stars);
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "osu", "RulesetCatch.png"), new(x, y, 22, 22), DifficultyColour(stars));
            c.Text(diff.Difficulty, x + 32, y + 3, 13, Foreground, 200);
            c.Text(stars is null ? "—" : stars.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "★", x + 238, y + 3, 12, DifficultyColour(stars), 60);
        }
        DrawLibraryScrollbar(c, new(width - 16, 334, 8, count * 40), libraryDiffScroll, group.Count, count, true);
        if (ratingTask is null && !libraryPointerActive)
        {
            if (pending.Count > 0) ratingTask = Task.Run(() =>
            {
                var result = new Dictionary<string, double?>();
                foreach (var entry in pending)
                {
                    try { var d = entry.Path.EndsWith(".catchdiff", StringComparison.OrdinalIgnoreCase) ? ProjectSerializer.ReadFile(entry.Path) : OsuBeatmapReader.ReadFile(entry.Path); var converted = CatchStreamConverter.Convert(d); result[entry.Path] = converted.Success ? CatchDifficultyCalculator.Calculate(converted.Objects, d.CircleSize).StarRating : null; }
                    catch (Exception) { result[entry.Path] = null; }
                }
                return result;
            });
        }
    }
    private void LibraryTextField(ICanvas c, int index, string label, string value, float y)
    {
        float x = librarySettingsOpen ? SettingsContentX : 32;
        c.Text(label, x, y, 14, Foreground, width - x - 32, true);
        var rect = new Rect(x, y + 28, width - x - (index < 2 || index == 4 ? 184 : 32), 42);
        c.Fill(rect, Surface, 5); c.Stroke(rect, libraryField == index ? Accent : Grid, radius: 5);
        DrawInputText(c, new(x + 12, y + 40, rect.Width - 24, 20), value, 14, libraryField == index, libraryReplace);
        hits.Add(new(rect, () => { libraryField = index; libraryReplace = false; }, true));
        if (index == 4) Button(c, new(width - 168, y + 28, 136, 42), L.Get("library.browse"), () => RequestDefaultSkinArchive?.Invoke());
        if (index < 2) Button(c, new(width - 168, y + 28, 136, 42), L.Get("library.browse"), () => RequestLibraryFolder?.Invoke(index == 0));
    }
    public bool LibraryLoading => scanTask is { IsCompleted: false } || searchTask is { IsCompleted: false } || ratingTask is { IsCompleted: false } || libraryBrowser is { Loading: true };
    public bool LibraryTextFocused => (LibraryVisible || ExportVisible) && libraryField >= 0 && !ErrorVisible && !DiscardConfirmationVisible;
    public void PasteLibraryText(string text)
    {
        if (!LibraryTextFocused) return;
        ResetTextCaret();
        string value = new(text.Where(c => !char.IsControl(c)).ToArray());
        LibraryFieldValue = new string(((libraryReplace ? "" : LibraryFieldValue) + value).Take(4096).ToArray());
        libraryReplace = false;
    }
    private void LibraryKey(int key, bool ctrl)
    {
        if (updatesPage) { if (key == 27) updatesPage = false; return; }
        if (key == 27) FinishVolumeDrag();
        if (key == 27) { if (contextItems.Count > 0) contextItems.Clear(); else if (libraryField >= 0) libraryField = -1; else if (librarySettingsOpen) CloseSettings(); else resourcePage = false; return; }
        if (key == 116) { StartLibraryScan(); return; }
        if (!librarySettingsOpen && ctrl && key == 70) { libraryField = 2; libraryReplace = true; return; }
        if (key == 13 && libraryField < 0 && !librarySettingsOpen && !resourcePage && libraryBrowser?.Selected?.Map is { } map)
        { OpenSelectedLibraryMap(map); return; }
        if (libraryField < 0) return;
        if (ctrl && key == 65) { libraryReplace = true; return; }
        string value = LibraryFieldValue;
        if (key == 8) { var positions = System.Globalization.StringInfo.ParseCombiningCharacters(value); LibraryFieldValue = libraryReplace || positions.Length == 0 ? "" : value[..positions[^1]]; libraryReplace = false; }
        if (key == 46) { LibraryFieldValue = ""; libraryReplace = false; }
        if (key == 13) libraryField = -1;
    }
    private string LibraryFieldValue
    {
        get => libraryField switch { 0 => draftWorkspace, 1 => draftOsuRoot, 2 => libraryQuery, 3 => exportName, 4 => draftDefaultSkin, _ => "" };
        set { switch (libraryField) { case 0: draftWorkspace = value; break; case 1: draftOsuRoot = value; break; case 2: libraryQuery = value; libraryScroll = 0; libraryResultsReady = false; QueueLibrarySearch(); RememberLibraryPosition(); break; case 3: exportName = value; break; case 4: draftDefaultSkin = value; break; } }
    }
}
