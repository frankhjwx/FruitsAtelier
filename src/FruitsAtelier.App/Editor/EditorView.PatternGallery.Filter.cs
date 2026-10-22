using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string galleryQuery = "";
    private Rect gallerySearchBounds;
    private (PatternSnapshot Pattern, string Search)[] gallerySearchIndex = [];
    private PatternTag[] galleryTags = [];
    private readonly HashSet<string> galleryTagFilter = new(StringComparer.OrdinalIgnoreCase);
    private bool GallerySearchFocused => textEditor.Field == "gallery:search" && PatternGalleryInputAvailable;
    private bool CanSavePatternSelection => CanCopySelection && !PatternEditingActive && !PatternEditingBusy
        && galleryIoTask is null && RequestPatternName is not null;
    private bool CanEditGalleryTags => galleryStore is not null && galleryIoTask is null && !PatternEditingActive && !PatternEditingBusy;

    private void AddGalleryCaptureContextItem()
        => contextItems.Add(new(L.Get("gallery.saveToGallery"), RequestSavePattern, CanSavePatternSelection));

    private void RefreshGallerySearchIndex()
    {
        galleryTags = galleryPatterns.SelectMany(p => p.Tags).DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        galleryTagFilter.IntersectWith(galleryTags.Select(t => t.Name));
        gallerySearchIndex = galleryPatterns.Select(p => (p, LibraryDatabase.Normalize(string.Join(" ",
            p.Name, p.Source.Artist, p.Source.Title, p.Source.Creator, p.Source.Difficulty, string.Join(" ", p.Tags.Select(t => t.Name)))))).ToArray();
        ApplyGalleryFilter(false);
    }

    private void ApplyGalleryFilter(bool resetScroll = true)
    {
        var words = LibraryDatabase.Normalize(galleryQuery).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        galleryFilteredPatterns = gallerySearchIndex.Where(p => words.All(w => p.Search.Contains(w, StringComparison.Ordinal))
            && galleryTagFilter.All(tag => p.Pattern.Tags.Any(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase))))
            .Select(p => p.Pattern).ToArray();
        if (resetScroll) galleryScroll = 0;
        if (!galleryFilteredPatterns.Any(p => p.Id == galleryPopupId)) galleryPopupId = Guid.Empty;
    }

    private void GallerySearchText(string text)
    {
        string value = new(text.Where(c => !char.IsControl(c)).ToArray());
        if (value.Length == 0) return;
        galleryQuery = InsertInput("gallery:search", galleryQuery, value, 4096);
        ApplyGalleryFilter();
    }

    private void DrawGallerySearchAndFilters(ICanvas c, Rect bounds, bool ready)
    {
        gallerySearchBounds = new(bounds.X, bounds.Y, bounds.Width, 28);
        c.Fill(gallerySearchBounds, Surface, 4); c.Stroke(gallerySearchBounds, GallerySearchFocused ? Accent : Grid, radius: 4);
        DrawInputText(c, new(bounds.X + 9, bounds.Y + 6, bounds.Width - 18, 20), galleryQuery, 12, GallerySearchFocused, "gallery:search");
        if (galleryQuery.Length == 0 && !GallerySearchFocused)
            c.Text(L.Get("library.search"), bounds.X + 9, bounds.Y + 6, 12, Muted, bounds.Width - 18);
        hits.Add(new(gallerySearchBounds, () => FocusInput("gallery:search", galleryQuery, mouseX), true));
        float half = (bounds.Width - 5) / 2;
        Button(c, new(bounds.X, bounds.Y + 34, half, 28), L.Get("gallery.filterTags", galleryTagFilter.Count),
            () => ShowGalleryTagFilter(), enabled: ready, active: galleryTagFilter.Count > 0, fontSize: 11);
        Button(c, new(bounds.X + half + 5, bounds.Y + 34, half, 28), L.Get("gallery.manageTags"),
            () => ShowGalleryTagManager(), enabled: ready && CanEditGalleryTags, fontSize: 11);
    }

    private void ShowGalleryTagFilter(int page = 0)
    {
        contextItems.Clear(); menu = -1;
        contextItems.Add(new(L.Get("gallery.allTags"), () => { galleryTagFilter.Clear(); ApplyGalleryFilter(); }));
        foreach (var tag in GalleryTagPage(page))
            contextItems.Add(new(tag.Name, () =>
            {
                if (!galleryTagFilter.Add(tag.Name)) galleryTagFilter.Remove(tag.Name);
                ApplyGalleryFilter(); ShowGalleryTagFilter(page);
            }, Color: tag.Colour, Active: galleryTagFilter.Contains(tag.Name)));
        GalleryTagPaging(page, ShowGalleryTagFilter);
        PositionGalleryMenu(galleryBounds.X, galleryBounds.Y + 64);
    }

    private IEnumerable<PatternTag> GalleryTagPage(int page) => galleryTags.Skip(page * 8).Take(8);
    private void GalleryTagPaging(int page, Action<int> open)
    {
        if (page > 0) contextItems.Add(new(L.Get("gallery.previousTags"), () => open(page - 1)));
        if ((page + 1) * 8 < galleryTags.Length) contextItems.Add(new(L.Get("gallery.nextTags"), () => open(page + 1)));
    }

    private void ShowGalleryTagAssignments(Guid id) => OpenGalleryTags(id);

    private bool ValidGalleryTagName(string? name)
    {
        if (name is null) return false;
        if (!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 64 && !name.Any(char.IsControl)) return true;
        StatusMessage = L.Get("core.pattern.invalid"); return false;
    }

    private void SaveGalleryTags(PatternSnapshot pattern, PatternTag[] tags)
    {
        if (!CanEditGalleryTags || galleryStore is not { } store) return;
        StartGalleryIo(() => { store.Replace(pattern.WithTags(tags)); return new(store.Load(), "", true); });
    }

    private void ShowGalleryTagManager() => OpenGalleryTags(Guid.Empty);

    private void EditGalleryTag(PatternTag tag, PatternTag? replacement)
    {
        if (!CanEditGalleryTags || galleryStore is not { } store) return;
        StartGalleryIo(() => { store.EditTag(tag.Name, replacement); return new(store.Load(), "", true); });
    }

    private void RequestGalleryText(string title, string initial, Action<string?> completion)
    {
        int generation = galleryGeneration;
        RequestPatternName?.Invoke(title, value => { if (generation == galleryGeneration) completion(value); });
        if (PatternNameVisible) { patternNameText = initial; SelectInput("gallery:name", initial); }
    }
}
