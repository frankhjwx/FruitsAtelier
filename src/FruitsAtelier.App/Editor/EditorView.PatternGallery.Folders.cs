using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private IReadOnlyList<PatternFolder> galleryFolders = [];
    private Guid galleryFolderId = PatternFolder.DefaultId;
    private Rect galleryCategoryBounds, galleryCategoryListBounds;
    private float galleryCategoryScroll, galleryCategoryMaxScroll;
    private bool galleryCategoriesFocused;
    private readonly HashSet<Guid> galleryFolderSelection = [];
    private readonly List<(Guid Id, Rect Bounds)> galleryFolderCards = [];
    private Guid gallerySelectionAnchor, galleryFolderAnchor;
    private Guid[] galleryClipboard = [];
    private bool galleryClipboardCut, galleryFocused;
    private float galleryCardZoom = 1;
    private Guid[]? galleryExportFolders;
    private void RequestGalleryExport(Guid[]? folders = null, bool selectedOnly = false)
    {
        galleryExportFolders = folders;
        RequestPatternExport?.Invoke(selectedOnly);
    }
    internal Guid PatternGalleryFolderId => galleryFolderId;
    internal Rect PatternGalleryCategoryBounds => galleryCategoryBounds;
    internal int PatternGallerySelectionCount => gallerySelection.Count;
    internal Rect PatternGalleryContextItemBounds(int row) => ContextItemBounds(row);
    internal Rect PatternGalleryFolderBounds(Guid id) => galleryFolderCards.FirstOrDefault(f => f.Id == id).Bounds;

    private void OpenGalleryFolder(Guid id)
    {
        CancelPatternGalleryInteraction();
        galleryFolderId = id; galleryScroll = 0; galleryFocused = true;
        gallerySelection.Clear(); gallerySelectionAnchor = Guid.Empty;
        galleryCategoriesFocused = true;
        textEditor.Field = "";
        ApplyGalleryFilter();
    }

    private float GalleryMaximumCardZoom => Math.Max(1, (galleryGridBounds.Width - GalleryCardGap - 6) / (2 * GalleryCardWidth));

    private void SetGalleryCardZoom(float zoom)
    {
        zoom = Math.Clamp(zoom, .5f, GalleryMaximumCardZoom);
        if (galleryCardZoom == zoom) return;
        galleryCardZoom = zoom;
        galleryScroll = 0;
        galleryTagLayouts.Clear();
    }

    private void RequestGalleryFolder()
    {
        if (!CanEditGalleryTags || galleryStore is not { } store) return;
        RequestGalleryText(L.Get("gallery.newFolder"), "", name =>
        {
            if (string.IsNullOrWhiteSpace(name) || !CanEditGalleryTags) return;
            StartGalleryIo(() => { store.CreateFolder(name); return ReloadGallery(store, "", true); });
        });
    }

    private void DrawGalleryCategories(ICanvas c, Rect bounds)
    {
        galleryCategoryBounds = bounds;
        c.Text(L.Get("gallery.category"), bounds.X + 4, bounds.Y + 3, 12, Muted, bounds.Width - 8, true);
        galleryCategoryListBounds = new(bounds.X, bounds.Y + 26, bounds.Width, Math.Max(0, bounds.Height - 26));
        galleryCategoryMaxScroll = Math.Max(0, galleryFolders.Count * 48 - galleryCategoryListBounds.Height);
        galleryCategoryScroll = Math.Clamp(galleryCategoryScroll, 0, galleryCategoryMaxScroll);
        c.Clip(galleryCategoryListBounds);
        for (int i = Math.Max(0, (int)(galleryCategoryScroll / 48)); i < galleryFolders.Count; i++)
        {
            var folder = galleryFolders[i];
            var row = new Rect(bounds.X, galleryCategoryListBounds.Y + i * 48 - galleryCategoryScroll, bounds.Width - 4, 44);
            if (row.Y >= galleryCategoryListBounds.Bottom) break;
            galleryFolderCards.Add((folder.Id, row));
            if (folder.Id == galleryFolderId) c.Fill(row, Accent, 3, .16f);
            if (galleryFolderSelection.Contains(folder.Id)) c.Stroke(row, Accent, radius: 3);
            c.Text(folder.Name, row.X + 5, row.Y + 5, 12, Foreground, row.Width - 10);
            c.Text(L.Get("gallery.folderCount", folder.PatternIds.Count), row.X + 5, row.Y + 25, 10, Muted, row.Width - 10);
        }
        c.Unclip();
        DrawGalleryScrollbar(c, galleryCategoryListBounds, galleryCategoryScroll, galleryCategoryMaxScroll);
    }

    private void ShowGalleryCategoryMenu(float x, float y)
    {
        contextItems.Clear(); menu = -1;
        contextItems.Add(new(L.Get("gallery.newFolder"), RequestGalleryFolder, CanEditGalleryTags && RequestPatternName is not null));
        PositionGalleryMenu(x, y);
    }

    private void SelectGalleryRange(Guid id, bool ctrl, bool shift, bool folders = false)
    {
        var selection = folders ? galleryFolderSelection : gallerySelection;
        var ordered = folders ? galleryFolders.Select(f => f.Id).ToArray() : galleryFilteredPatterns.Select(p => p.Id).ToArray();
        Guid anchor = folders ? galleryFolderAnchor : gallerySelectionAnchor;
        int start = Array.IndexOf(ordered, anchor), end = Array.IndexOf(ordered, id);
        if (shift && start >= 0 && end >= 0)
        {
            if (!ctrl) selection.Clear();
            for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++) selection.Add(ordered[i]);
        }
        else
        {
            if (!ctrl) selection.Clear();
            if (!selection.Add(id) && ctrl) selection.Remove(id);
            if (folders) galleryFolderAnchor = id; else gallerySelectionAnchor = id;
        }
    }

    private void GalleryFolderPointerDown(Guid id, float x, float y, int button, bool ctrl, bool shift)
    {
        galleryCategoriesFocused = true;
        galleryPopupId = Guid.Empty;
        if (button == 2)
        {
            if (!galleryFolderSelection.Contains(id)) SelectGalleryRange(id, false, false, true);
            contextItems.Clear(); menu = -1;
            contextItems.Add(new(L.Get("gallery.renameFolder"), () =>
            {
                var folder = galleryFolders.First(f => f.Id == id);
                RequestGalleryText(L.Get("gallery.renameFolder"), folder.Name, name =>
                {
                    if (string.IsNullOrWhiteSpace(name) || !CanEditGalleryTags || galleryStore is not { } store) return;
                    StartGalleryIo(() => { store.RenameFolder(id, name); return ReloadGallery(store, "", true); });
                });
            }, CanEditGalleryTags && id != PatternFolder.DefaultId && RequestPatternName is not null));
            if (id != PatternFolder.DefaultId)
                contextItems.Add(new(L.Get("gallery.deleteFolder"), () =>
                {
                    if (!CanEditGalleryTags || galleryStore is not { } store) return;
                    StartGalleryIo(() => { store.DeleteFolder(id); return ReloadGallery(store, ""); });
                }, CanEditGalleryTags));
            if (galleryFolderSelection.Count >= 2)
                contextItems.Add(new(L.Get("gallery.mergeFolders"), () => ShowGalleryMergeFolders(), CanEditGalleryTags));
            contextItems.Add(new(L.Get("gallery.export"), () => RequestGalleryExport(galleryFolderSelection.ToArray())));
            if (galleryClipboard.Length > 0)
                contextItems.Add(new(L.Get("editor.command.paste"), () => PasteGalleryPatterns(id), CanEditGalleryTags));
            PositionGalleryMenu(x, y);
            return;
        }
        if (!ctrl && !shift) OpenGalleryFolder(id);
        SelectGalleryRange(id, ctrl, shift, true);
    }

    private void ShowGalleryMergeFolders(int page = 0)
    {
        var selected = galleryFolders.Where(f => galleryFolderSelection.Contains(f.Id)).ToArray();
        ShowGalleryFolderChoices(selected, page, folder =>
        {
            if (!CanEditGalleryTags || galleryStore is not { } store) return;
            var ids = selected.Select(f => f.Id).ToArray();
            StartGalleryIo(() => { store.MergeFolders(ids, folder.Id); return ReloadGallery(store, "", true); });
        }, "gallery.mergeInto", ShowGalleryMergeFolders);
    }

    private void ShowGalleryImportDestination(string path, int page = 0)
    {
        if (galleryStore is null || galleryIoTask is not null) return;
        ShowGalleryFolderChoices(galleryFolders, page, folder => ImportGalleryInto(path, folder.Id), "gallery.importInto",
            next => ShowGalleryImportDestination(path, next));
        contextItems.Insert(0, new(L.Get("gallery.importNewFolders"), () => ImportGalleryInto(path, null), Active: true));
        PositionGalleryMenu(galleryBounds.X, galleryBounds.Y + 64);
    }

    private void ShowGalleryFolderChoices(IReadOnlyList<PatternFolder> folders, int page, Action<PatternFolder> choose, string label, Action<int> next)
    {
        contextItems.Clear(); menu = -1;
        foreach (var folder in folders.Skip(page * 8).Take(8))
            contextItems.Add(new(L.Get(label, folder.Name), () => choose(folder)));
        if (page > 0) contextItems.Add(new(L.Get("gallery.previousTags"), () => next(page - 1)));
        if ((page + 1) * 8 < folders.Count) contextItems.Add(new(L.Get("gallery.nextTags"), () => next(page + 1)));
        PositionGalleryMenu(galleryBounds.X, galleryBounds.Y + 64);
    }

    private void ShowGalleryExportMenu()
    {
        contextItems.Clear(); menu = -1;
        contextItems.Add(new(L.Get("gallery.exportSelected"), () => RequestGalleryExport(selectedOnly: true), gallerySelection.Count > 0));
        contextItems.Add(new(L.Get("gallery.exportFolder"), () => RequestGalleryExport([galleryFolderId])));
        contextItems.Add(new(L.Get("gallery.exportAll"), () => RequestGalleryExport()));
        PositionGalleryMenu(galleryBounds.X, galleryBounds.Y + 98);
    }

    private void CopyGalleryPatterns(bool cut)
    {
        if (!CanEditGalleryTags || gallerySelection.Count == 0) return;
        galleryClipboard = galleryFilteredPatterns.Where(p => gallerySelection.Contains(p.Id)).Select(p => p.Id).ToArray();
        galleryClipboardCut = cut;
        galleryPopupId = Guid.Empty;
    }

    private void PasteGalleryPatterns(Guid destination)
    {
        if (!CanEditGalleryTags || galleryStore is not { } store || galleryClipboard.Length == 0) return;
        var ids = galleryClipboard; bool cut = galleryClipboardCut;
        galleryCategoriesFocused = false;
        StartGalleryIo(() => { store.TransferPatterns(ids, destination, !cut); return ReloadGallery(store, "", true); });
        // A failed move can be retried: IDs remain valid until publication succeeds.
        galleryPendingCut = cut ? ids : null;
    }

    private Guid[]? galleryPendingCut;
}
