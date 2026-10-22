using System.Text.Json;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record LibraryPreview(int Version, string Key, LibraryBrowser.SavedView View);
    private Task? libraryPreviewSave;
    private LibraryBrowser? savedPreviewBrowser;
    private long savedPreviewRevision = -1;
    private string savedPreviewSearchKey = "";
    private DateTime nextPreviewSave;
    private string LibraryPreviewPath => Path.Combine(LibrarySettings.Workspace, "library-preview.json");
    // Visits can change while editing; the live query reconciles their order without discarding the previous page.
    private string LibraryPreviewKey => JsonSerializer.Serialize(new
    {
        LibrarySettings.Songs, libraryMyMapsOnly, libraryFavouritesOnly, libraryRecentOnly, libraryCollection,
        libraryQuery, libraryGrouping, librarySort, libraryDescending,
        Expanded = libraryExpanded.Order().ToArray(),
        Creators = libraryMyMapsOnly ? LibrarySettings.MapperUsernames : null,
        Favourites = libraryFavouritesOnly ? libraryMemory.FavouriteKeys.Order().ToArray() : null,
        Collection = libraryMemory.Collections.FirstOrDefault(c => c.Id == libraryCollection),
        Day = libraryGrouping is FruitsAtelier.Core.LibraryGrouping.DateAdded or FruitsAtelier.Core.LibraryGrouping.RecentVisited ? DateTime.Today.Ticks : 0
    });

    private void LoadLibraryPreview()
    {
        try
        {
            var file = new FileInfo(LibraryPreviewPath);
            if (!file.Exists || file.Length > 2 * 1024 * 1024) return;
            var saved = JsonSerializer.Deserialize<LibraryPreview>(File.ReadAllText(file.FullName));
            if (saved is null || saved.Version != 1 || saved.Key != LibraryPreviewKey) return;
            libraryBrowser = LibraryBrowser.Restore(saved.View);
            libraryBrowserQuery = libraryQuery;
            libraryResultsReady = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { /* The database can rebuild a missing or damaged startup preview. */ }
    }

    private void SaveLibraryPreview()
    {
        if (libraryBrowser is not { IsPreview: false } browser || libraryBrowserQuery != libraryQuery
            || runningOrganizationKey != LibrarySearchKey || searchTaskQuery != LibrarySearchKey
            || searchTask is not null || libraryPreviewSave is { IsCompleted: false } || DateTime.UtcNow < nextPreviewSave) return;
        if (ReferenceEquals(savedPreviewBrowser, browser) && savedPreviewRevision == browser.ContentRevision && savedPreviewSearchKey == LibrarySearchKey) return;
        string key = LibraryPreviewKey;
        if (browser.SaveView() is not { } view) return;
        savedPreviewBrowser = browser; savedPreviewRevision = browser.ContentRevision; savedPreviewSearchKey = LibrarySearchKey;
        nextPreviewSave = DateTime.UtcNow.AddMilliseconds(350);
        string path = LibraryPreviewPath;
        libraryPreviewSave = Task.Run(() =>
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = JsonSerializer.Serialize(new LibraryPreview(1, key, view));
                if (System.Text.Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024) return;
                File.WriteAllText(temporary, json);
                File.Move(temporary, path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        });
    }
}
