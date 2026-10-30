using System.Text.Json;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed class LibraryPosition
    {
        public LibraryGrouping Group { get; set; }
        public LibraryOrder Sort { get; set; }
        public bool Descending { get; set; }
        public HashSet<string> Expanded { get; set; } = [];
        public string Query { get; set; } = "";
        public string? Selected { get; set; }
        public float Scroll { get; set; }
        public int DifficultyScroll { get; set; }
    }
    private sealed class LibraryMemory
    {
        public bool Recent { get; set; }
        public string? Collection { get; set; }
        public List<LibraryCollection> Collections { get; set; } = [];
        public Dictionary<string, long> Visits { get; set; } = new();
        public Dictionary<string, LibraryPosition> Positions { get; set; } = new();
        public bool Projects { get; set; }
        public bool Favourites { get; set; }
        public LibraryPosition FavouritePosition { get; set; } = new();
        public HashSet<string> FavouriteKeys { get; set; } = [];
        public LibraryPosition Songs { get; set; } = new();
        public LibraryPosition MyProjects { get; set; } = new();
    }
    private LibraryMemory libraryMemory = new();
    private readonly Dictionary<string, (LibraryBrowser? Browser, string Query)> libraryCategoryBrowsers = [];
    private string libraryBrowserQuery = "";
    private bool libraryFavouritesOnly, libraryRecentOnly;
    private string? libraryCollection;
    private LibraryGrouping libraryGrouping;
    private LibraryOrder librarySort;
    private bool libraryDescending;
    private HashSet<string> libraryExpanded = [];
    private bool LibraryUsesProjects => libraryMyMapsOnly || libraryFavouritesOnly || libraryRecentOnly || libraryCollection is not null && !IsOsuCollection;
    private int libraryFavouriteRevision;
    private int LibraryCategory => libraryCollection is not null ? 4 + Math.Max(0, AllLibraryCollectionIds.ToList().IndexOf(libraryCollection)) : libraryRecentOnly ? 3 : libraryFavouritesOnly ? 2 : libraryMyMapsOnly ? 1 : 0;
    private string LibrarySearchKey => libraryQuery + ":" + LibraryCategory + ":" + libraryFavouriteRevision + ":" + libraryGrouping + ":" + librarySort + ":" + libraryDescending + ":" + libraryCollection + (libraryGrouping is LibraryGrouping.DateAdded or LibraryGrouping.RecentVisited ? ":" + DateTime.Today.Ticks : "");
    private bool libraryMemoryLoaded, libraryMemoryDirty, revealLibrarySelection;
    private DateTime libraryMemorySaveAfter;
    private Rect libraryListBounds, libraryScrollTrack, libraryScrollThumb, libraryDiffTrack, libraryDiffThumb;
    private Rect libraryMapperSetupBounds;
    private bool libraryPointerActive, libraryPointerMoved, libraryDraggingThumb, libraryDraggingDifficulties;
    private float libraryPointerY, libraryPointerScroll, libraryThumbOffset;
    private LibraryMap? libraryPressedMap;
    private (Rect Bounds, LibraryMap Map)? libraryClickedCard;
    private float LibraryVisibleRows => Math.Max(1, libraryListBounds.Height / 86);
    private float LibraryMaxScroll => Math.Max(0, (libraryBrowser?.Extent ?? 0) - LibraryVisibleRows);

    private void LoadLibraryMemory()
    {
        libraryMemoryLoaded = true;
        try
        {
            string path = Path.Combine(LibrarySettings.Workspace, "library-view.json");
            libraryMemory = File.Exists(path) ? JsonSerializer.Deserialize<LibraryMemory>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { libraryMemory = new(); }
        libraryMyMapsOnly = libraryMemory.Projects;
        libraryFavouritesOnly = libraryMemory.Favourites;
        libraryMemory.FavouriteKeys = new(libraryMemory.FavouriteKeys ?? [], WorkspaceSynchronization.Paths);
        libraryMemory.Collections ??= [];
        foreach (var collection in libraryMemory.Collections) collection.Keys = new(collection.Keys ?? [], WorkspaceSynchronization.Paths);
        libraryMemory.Visits = new(libraryMemory.Visits ?? [], WorkspaceSynchronization.Paths);
        libraryMemory.Positions ??= new();
        libraryRecentOnly = libraryMemory.Recent;
        libraryCollection = (libraryMemory.Collection?.StartsWith("osu:", StringComparison.Ordinal) == true || libraryMemory.Collections.Any(c => c.Id == libraryMemory.Collection)) ? libraryMemory.Collection : null;
        RestoreLibraryPosition();
    }
    private void RememberLibraryPosition()
    {
        var position = new LibraryPosition { Group = libraryGrouping, Sort = librarySort, Descending = libraryDescending, Expanded = new(libraryExpanded), Query = libraryQuery, Selected = selectedLibraryGroup, Scroll = libraryScroll, DifficultyScroll = libraryDiffScroll };
        if (libraryRecentOnly || libraryCollection is not null) libraryMemory.Positions[libraryCollection ?? "recent"] = position;
        else if (libraryFavouritesOnly) libraryMemory.FavouritePosition = position;
        else if (libraryMyMapsOnly) libraryMemory.MyProjects = position; else libraryMemory.Songs = position;
        libraryMemory.Recent = libraryRecentOnly; libraryMemory.Collection = libraryCollection;
        libraryMemory.Projects = libraryMyMapsOnly;
        libraryMemory.Favourites = libraryFavouritesOnly;
        libraryMemoryDirty = true; libraryMemorySaveAfter = DateTime.UtcNow.AddMilliseconds(350);
    }
    public void SaveLibraryMemory()
    {
        if (!libraryMemoryLoaded || !libraryMemoryDirty) return;
        try
        {
            Directory.CreateDirectory(LibrarySettings.Workspace);
            string path = Path.Combine(LibrarySettings.Workspace, "library-view.json");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(libraryMemory)); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            libraryMemoryDirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { libraryMemoryDirty = false; libraryError = e.Message; }
    }
    private void RestoreLibraryPosition()
    {
        var position = (libraryRecentOnly || libraryCollection is not null ? libraryMemory.Positions.GetValueOrDefault(libraryCollection ?? "recent") ?? new LibraryPosition { Group = libraryRecentOnly ? LibraryGrouping.RecentVisited : LibraryGrouping.None, Sort = libraryRecentOnly ? LibraryOrder.RecentVisited : LibraryOrder.Title, Descending = libraryRecentOnly } : libraryFavouritesOnly ? libraryMemory.FavouritePosition : libraryMyMapsOnly ? libraryMemory.MyProjects : libraryMemory.Songs) ?? new();
        libraryGrouping = Enum.IsDefined(position.Group) ? position.Group : LibraryGrouping.None;
        librarySort = Enum.IsDefined(position.Sort) ? position.Sort : LibraryOrder.Title;
        libraryDescending = position.Descending; libraryExpanded = new(position.Expanded ?? []);
        libraryQuery = position.Query ?? ""; selectedLibraryGroup = position.Selected;
        libraryScroll = float.IsFinite(position.Scroll) ? Math.Max(0, position.Scroll) : 0;
        libraryDiffScroll = Math.Max(0, position.DifficultyScroll);
    }
    private void SwitchLibraryCategory(bool projects, bool favourites = false, bool recent = false, string? collection = null)
    {
        if (libraryMyMapsOnly == projects && libraryFavouritesOnly == favourites && libraryRecentOnly == recent && libraryCollection == collection) return;
        SaveLibraryPreviewOnLeave();
        libraryClickedCard = null;
        RememberLibraryPosition();
        libraryCategoryBrowsers[LibraryPreviewCategory] = (libraryBrowser, libraryBrowserQuery);
        libraryMyMapsOnly = projects; libraryFavouritesOnly = favourites; libraryRecentOnly = recent; libraryCollection = collection; RestoreLibraryPosition();
        var cached = libraryCategoryBrowsers.GetValueOrDefault(LibraryPreviewCategory);
        libraryCategoryBrowsers.Remove(LibraryPreviewCategory);
        libraryBrowser = cached.Browser; libraryBrowserQuery = cached.Query ?? "";
        if (libraryBrowserQuery != libraryQuery) { libraryBrowser?.Retire(); libraryBrowser = null; }
        if (libraryBrowser is null) LoadLibraryPreview();
        libraryCards.Clear(); libraryResultsReady = libraryBrowser is not null;
        libraryField = -1; contextItems.Clear();
        QueueLibrarySearch(); RememberLibraryPosition();
    }
    private bool IsLibraryFavourite(LibraryMap map)
        => libraryMemory.FavouriteKeys.Contains(map.Directory)
            || map.ProjectPath is { } project && libraryMemory.FavouriteKeys.Contains(project);

    private void ToggleLibraryFavourite(LibraryMap map)
    {
        if (IsLibraryFavourite(map))
        {
            libraryMemory.FavouriteKeys.Remove(map.Directory);
            if (map.ProjectPath is { } project) libraryMemory.FavouriteKeys.Remove(project);
        }
        else
        {
            libraryMemory.FavouriteKeys.Add(map.Directory);
            if (map.ProjectPath is { } project) libraryMemory.FavouriteKeys.Add(project);
        }
        libraryFavouriteRevision++;
        if (libraryCategoryBrowsers.Remove("favourites", out var cached)) cached.Browser?.Retire();
        QueueLibrarySearch(); RememberLibraryPosition(); SaveLibraryMemory();
    }

    private void OpenSelectedLibraryMap(LibraryMap map, bool selectDifficulty = false)
    {
        libraryBrowser?.SelectMap(map);
        selectedLibraryGroup = LibraryUsesProjects ? map.ProjectPath ?? map.Directory : map.Directory;
        libraryField = -1; RememberLibraryPosition(); SaveLibraryMemory();
        RequestLibraryOpen?.Invoke(map, selectDifficulty);
    }
    private void DrawLibraryScrollbar(ICanvas c, Rect track, float offset, float total, float visible, bool details)
    {
        float maximum = Math.Max(0, total - visible);
        offset = Math.Clamp(offset, 0, maximum);
        float size = maximum == 0 ? track.Height : Math.Max(28, track.Height * visible / total);
        var thumb = new Rect(track.X, track.Y + (maximum == 0 ? 0 : offset / maximum * (track.Height - size)), track.Width, size);
        if (details) { libraryDiffTrack = track; libraryDiffThumb = thumb; }
        else { libraryScrollTrack = track; libraryScrollThumb = thumb; }
        c.Fill(track, Panel, 5);
        c.Fill(thumb, track.Contains(mouseX, mouseY) || libraryPointerActive && libraryDraggingDifficulties == details ? Accent : Muted, 5);
    }
    private bool LibraryPointerDown(float x, float y, int button)
    {
        if (contextItems.Count > 0)
        {
            if (button == 0 && !librarySettingsOpen && !resourcePage && !collectionDialogOpen
                && !contextBounds.Contains(x, y) && ActivateLibraryOrderControl(x, y)) return true;
            if (button == 0) ActivateContextMenu(x, y); else contextItems.Clear();
            return true;
        }
        if (collectionDialogOpen) return CollectionPointerDown(x, y, button);
        if (librarySettingsOpen || resourcePage) return false;
        if (button == 0 && libraryMapperSetupBounds.Contains(x, y))
        { OpenSettings(); return true; }
        if (button == 0 && libraryWebButtons.FirstOrDefault(b => b.Bounds.Contains(x, y)) is var web && web.Url is not null)
        { RequestVisitOsuWeb?.Invoke(web.Url); return true; }
        if (button == 2 && ShowCollectionContext(x, y)) return true;
        if (button == 0 && libraryHeaders.FirstOrDefault(h => h.Bounds.Contains(x, y)) is var header && header.Key is not null)
        { ToggleLibraryGroup(header.Key); return true; }
        if (button == 2 && libraryListBounds.Contains(x, y))
        {
            libraryField = -1;
            var map = libraryCards.FirstOrDefault(card => card.Bounds.Contains(x, y)).Map;
            if (map is not null)
            {
                libraryBrowser?.SelectMap(map);
                selectedLibraryGroup = LibraryUsesProjects ? map.ProjectPath ?? map.Directory : map.Directory;
                libraryDiffScroll = 0;
                RememberLibraryPosition();
                string? project = map.ProjectPath;
                string? songsFolder = project is null ? map.Directory : null;
                if (project is not null && File.Exists(Path.Combine(project, WorkspaceProject.ManifestName)))
                {
                    try
                    {
                        var manifest = WorkspaceProject.ReadManifest(project);
                        songsFolder = manifest.SongsRoot is { } root && manifest.SourceDirectory is { } source
                            ? Path.GetFullPath(Path.Combine(root, source)) : manifest.ExternalSourceDirectory;
                        songsFolder ??= manifest.Difficulties.Select(d => d.Source ?? d.ExportTarget)
                            .Where(p => p is not null).Select(p => Path.GetDirectoryName(p!)).FirstOrDefault();
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
                    { libraryNotice = L.Reformat(error.Message); }
                }
                contextItems.Add(new(L.Get(IsLibraryFavourite(map) ? "library.removeFavourite" : "library.addFavourite"), () => ToggleLibraryFavourite(map)));
                contextItems.Add(new(L.Get("library.addCollection"), () => ShowCollectionDialog(map)));
                if (!IsOsuCollection && libraryCollection is { } collectionId)
                    contextItems.Add(new(L.Get("library.removeCollection"), () => SetCollectionMember(collectionId, map, false)));
                AddContextSeparator();
                contextItems.Add(new(L.Get(project is null ? "library.start" : "library.continue"), () => OpenSelectedLibraryMap(map)));
                AddContextSeparator();
                contextItems.Add(new(L.Get("library.openProjectFolder"), () => RequestOpenExternalPath?.Invoke(project!), Directory.Exists(project)));
                if (project is not null)
                {
                    contextItems.Add(new(L.Get("library.deleteProject"), () => RequestLibraryDelete?.Invoke(map)));
                }
                contextItems.Add(new(L.Get("library.new"), () => RequestNewProject?.Invoke()));
                AddContextSeparator();
                contextItems.Add(new(L.Get("project.openSongsFolder"), () => RequestOpenExternalPath?.Invoke(songsFolder!), Directory.Exists(songsFolder)));
                AddContextSeparator();
                contextItems.Add(new(L.Get("library.exportOsz"), () => RequestLibraryOszExport?.Invoke(map)));
                contextItems.Add(new(L.Get("faz.export"), () => ShowFazExportChoice(map)));
            }
            else
            {
                contextItems.Add(new(L.Get("library.new"), () => RequestNewProject?.Invoke()));
                AddContextSeparator();
            }
            contextItems.Add(new(L.Get("library.importFolder"), () => RequestLibraryImport?.Invoke(true)));
            contextItems.Add(new(L.Get("library.importFile"), () => RequestLibraryImport?.Invoke(false)));
            float menuHeight = ContextMenuHeight;
            contextBounds = new(Math.Clamp(x, 0, width - 270), Math.Clamp(y, 64, Math.Max(64, height - menuHeight)), 270, menuHeight);
            return true;
        }
        if (button != 0) return true;
        bool thumb = libraryScrollTrack.Contains(x, y), details = libraryDiffTrack.Contains(x, y);
        if (!thumb && !details && !libraryListBounds.Contains(x, y)) return false;
        libraryPointerActive = true; libraryPointerMoved = false; libraryDraggingThumb = thumb || details;
        libraryClickedCard = null;
        libraryDraggingDifficulties = details;
        libraryPointerY = y; libraryPointerScroll = details ? libraryDiffScroll : libraryScroll;
        libraryPressedMap = libraryCards.FirstOrDefault(card => card.Bounds.Contains(x, y)).Map;
        var handle = details ? libraryDiffThumb : libraryScrollThumb;
        libraryThumbOffset = handle.Contains(x, y) ? y - handle.Y : handle.Height / 2;
        if (libraryDraggingThumb) MoveLibraryPointer(y);
        return true;
    }
    private void MoveLibraryPointer(float y)
    {
        if (!libraryPointerActive) return;
        if (Math.Abs(y - libraryPointerY) > 3) libraryPointerMoved = true;
        if (libraryDraggingThumb)
        {
            var track = libraryDraggingDifficulties ? libraryDiffTrack : libraryScrollTrack;
            var thumb = libraryDraggingDifficulties ? libraryDiffThumb : libraryScrollThumb;
            float fraction = Math.Clamp((y - track.Y - libraryThumbOffset) / Math.Max(1, track.Height - thumb.Height), 0, 1);
            if (libraryDraggingDifficulties)
            {
                int count = libraryBrowser?.Selected?.Count ?? 0;
                libraryDiffScroll = (int)Math.Round(fraction * Math.Max(0, count - LibraryVisibleDifficulties));
            }
            else libraryScroll = fraction * LibraryMaxScroll;
        }
        else if (libraryPointerMoved) libraryScroll = Math.Clamp(libraryPointerScroll + (libraryPointerY - y) / 86, 0, LibraryMaxScroll);
        RememberLibraryPosition();
    }
    private void EndLibraryPointer(float x, float y)
    {
        if (!libraryPointerActive) return;
        MoveLibraryPointer(y);
        if (!libraryDraggingThumb && !libraryPointerMoved && libraryPressedMap is { } map && libraryListBounds.Contains(x, y))
        {
            libraryClickedCard = libraryCards.FirstOrDefault(card => card.Map == map);
            libraryBrowser?.SelectMap(map);
            selectedLibraryGroup = LibraryUsesProjects ? map.ProjectPath ?? map.Directory : map.Directory;
            libraryDiffScroll = 0; libraryField = -1;
        }
        libraryPointerActive = false; libraryPressedMap = null; RememberLibraryPosition(); SaveLibraryMemory();
    }
    private int LibraryVisibleDifficulties => Math.Max(1, (int)(height - 390) / 40);
}
