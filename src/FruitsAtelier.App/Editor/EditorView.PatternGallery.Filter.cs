using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string galleryQuery = "";
    private Rect gallerySearchBounds, galleryTagsBounds;
    private (PatternSnapshot Pattern, string Search)[] gallerySearchIndex = [];
    private PatternTag[] galleryTags = [];
    private readonly HashSet<string> galleryTagFilter = new(StringComparer.OrdinalIgnoreCase);
    internal Rect PatternGalleryManageTagsMenuBounds => contextItems.Count > 0 && contextItems[0].Actions is { Length: 3 }
        ? ContextActionBounds(0, 2) : default;
    private bool GallerySearchFocused => textEditor.Field == "gallery:search" && PatternGalleryInputAvailable;
    private bool CanSavePatternSelection => CanCopySelection && !PatternEditingActive && !PatternEditingBusy
        && galleryIoTask is null && RequestPatternName is not null;
    private bool CanEditGalleryTags => galleryStore is not null && galleryIoTask is null && !PatternEditingActive && !PatternEditingBusy;

    private void AddGalleryCaptureContextItem()
        => contextItems.Add(new(L.Get("gallery.saveToGallery"), RequestSavePattern, CanSavePatternSelection));

    private void RefreshGallerySearchIndex()
    {
        galleryTags = galleryTags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        galleryTagFilter.IntersectWith(galleryTags.Select(t => t.Name));
        gallerySearchIndex = galleryPatterns.Select(p => (p, LibraryDatabase.Normalize(string.Join(" ",
            p.Name, p.Source.Artist, p.Source.Title, p.Source.Creator, p.Source.Difficulty, string.Join(" ", p.Tags.Select(t => t.Name)))))).ToArray();
        ApplyGalleryFilter(false);
    }

    private void ApplyGalleryFilter(bool resetScroll = true)
    {
        var words = LibraryDatabase.Normalize(galleryQuery).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        galleryFilteredPatterns = gallerySearchIndex.Where(p => words.All(w => p.Search.Contains(w, StringComparison.Ordinal))
            && (galleryTagFilter.Count == 0 || p.Pattern.Tags.Any(t => galleryTagFilter.Contains(t.Name))))
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
        float tagsWidth = Math.Min(Math.Max(120, c.MeasureText(L.Get("gallery.tagFilters"), 11) + 18), bounds.Width * .5f);
        gallerySearchBounds = new(bounds.X, bounds.Y, Math.Max(20, bounds.Width - tagsWidth - 5), 28);
        c.Fill(gallerySearchBounds, Surface, 4); c.Stroke(gallerySearchBounds, GallerySearchFocused ? Accent : Grid, radius: 4);
        DrawInputText(c, new(bounds.X + 9, bounds.Y + 6, gallerySearchBounds.Width - 18, 20), galleryQuery, 12, GallerySearchFocused, "gallery:search");
        if (galleryQuery.Length == 0 && !GallerySearchFocused)
            c.Text(L.Get("library.search"), bounds.X + 9, bounds.Y + 6, 12, Muted, gallerySearchBounds.Width - 18);
        hits.Add(new(gallerySearchBounds, () => FocusInput("gallery:search", galleryQuery, mouseX), true));
        galleryTagsBounds = new(gallerySearchBounds.Right + 5, bounds.Y, tagsWidth, 28);
        Button(c, galleryTagsBounds, L.Get("gallery.tagFilters"),
            () => ShowGalleryTagFilter(), enabled: ready, active: galleryTagFilter.Count > 0, fontSize: 11);
    }

    private void ShowGalleryTagFilter(int page = 0)
    {
        contextItems.Clear(); menu = -1;
        contextItems.Add(new("", () => { }, Actions:
        [
            new(L.Get("gallery.allTags"), () => { galleryTagFilter.UnionWith(galleryTags.Select(t => t.Name)); ApplyGalleryFilter(); ShowGalleryTagFilter(page); }),
            new(L.Get("gallery.noTags"), () => { galleryTagFilter.Clear(); ApplyGalleryFilter(); ShowGalleryTagFilter(page); }),
            new(L.Get("gallery.manageTags"), ShowGalleryTagManager, CanEditGalleryTags, Weight: 2)
        ]));
        AddContextSeparator();
        foreach (var tag in GalleryTagPage(page))
            contextItems.Add(new(tag.Name, () =>
            {
                if (!galleryTagFilter.Add(tag.Name)) galleryTagFilter.Remove(tag.Name);
                ApplyGalleryFilter(); ShowGalleryTagFilter(page);
            }, Color: tag.Colour, Active: galleryTagFilter.Contains(tag.Name)));
        GalleryTagPaging(page, ShowGalleryTagFilter);
        PositionGalleryMenu(gallerySearchBounds.Right + 5, galleryBounds.Y + 30);
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
        StartGalleryIo(() => { store.Replace(pattern.WithTags(tags)); return ReloadGallery(store, "", true); });
    }

    private void ShowGalleryTagManager() => OpenGalleryTags(Guid.Empty);

    private void EditGalleryTag(PatternTag tag, PatternTag? replacement)
    {
        if (!CanEditGalleryTags || galleryStore is not { } store) return;
        StartGalleryIo(() => { store.EditTag(tag.Name, replacement); return ReloadGallery(store, "", true); });
    }

    private void RequestGalleryText(string title, string initial, Action<string?> completion)
    {
        int generation = galleryGeneration;
        RequestPatternName?.Invoke(title, value => { if (generation == galleryGeneration) completion(value); });
        if (PatternNameVisible) { patternNameText = initial; SelectInput("gallery:name", initial); }
    }
}
