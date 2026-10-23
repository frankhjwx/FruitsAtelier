using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private IReadOnlyList<PatternFolder> galleryFolders = [];
    private Guid? galleryFolderId = PatternFolder.DefaultId;
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
    internal Guid? PatternGalleryFolderId => galleryFolderId;
    internal int PatternGallerySelectionCount => gallerySelection.Count;
    internal Rect PatternGalleryContextItemBounds(int row) => ContextItemBounds(row);
    internal Rect PatternGalleryFolderBounds(Guid id) => galleryFolderCards.FirstOrDefault(f => f.Id == id).Bounds;

    private void OpenGalleryFolder(Guid? id)
    {
        CancelPatternGalleryInteraction();
        galleryFolderId = id; galleryScroll = 0; galleryFocused = true;
        gallerySelection.Clear(); gallerySelectionAnchor = Guid.Empty;
        galleryFolderSelection.Clear(); galleryFolderAnchor = Guid.Empty;
        textEditor.Field = "";
        ApplyGalleryFilter();
    }

    private void DrawGalleryNavigation(ICanvas c, Rect bounds, bool ready)
    {
        float zoomWidth = 50, createWidth = Math.Min(95, bounds.Width / 3);
        string label = galleryFolderId is null ? L.Get("gallery.folders")
            : "‹ " + (galleryFolders.FirstOrDefault(f => f.Id == galleryFolderId)?.Name ?? PatternFolder.DefaultName);
        Button(c, new(bounds.X, bounds.Y, Math.Max(20, bounds.Width - createWidth - zoomWidth - 10), 28), label,
            () => OpenGalleryFolder(null), enabled: ready, fontSize: 11);
        Button(c, new(bounds.Right - createWidth - zoomWidth - 5, bounds.Y, createWidth, 28), L.Get("gallery.newFolder"),
            RequestGalleryFolder, enabled: ready && RequestPatternName is not null && CanEditGalleryTags, fontSize: 11);
        Button(c, new(bounds.Right - zoomWidth, bounds.Y, zoomWidth, 28), L.Get("gallery.zoom", galleryCardZoom * 100),
            () =>
            {
                contextItems.Clear(); menu = -1;
                foreach (float zoom in new[] { .5f, .75f, 1f })
                    contextItems.Add(new(L.Get("gallery.zoom", zoom * 100), () => SetGalleryCardZoom(zoom), Active: galleryCardZoom == zoom));
                PositionGalleryMenu(bounds.Right - 180, bounds.Y + 30);
            }, enabled: galleryFolderId is not null, fontSize: 11);
    }

    private void SetGalleryCardZoom(float zoom)
    {
        zoom = Math.Clamp(zoom, .5f, 1);
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
            OpenGalleryFolder(null);
        });
    }

    private void DrawGalleryFolders(ICanvas c, Rect bounds, bool ready)
    {
        gallerySearchBounds = galleryTagsBounds = default;
        float unit = (bounds.Width - 10) / 3;
        Button(c, new(bounds.X, bounds.Y, unit, 28), L.Get("gallery.import"), () => RequestPatternImport?.Invoke(), enabled: ready, fontSize: 11);
        Button(c, new(bounds.X + unit + 5, bounds.Y, unit, 28), L.Get("gallery.export"),
            () => RequestGalleryExport(galleryFolderSelection.Count == 0 ? null : galleryFolderSelection.ToArray()), enabled: ready, fontSize: 11);
        Button(c, new(bounds.X + (unit + 5) * 2, bounds.Y, unit, 28), L.Get("gallery.mergeFolders"),
            () => ShowGalleryMergeFolders(), enabled: ready && CanEditGalleryTags && galleryFolderSelection.Count >= 2, fontSize: 11);
        galleryGridBounds = new(bounds.X, bounds.Y + 36, bounds.Width, Math.Max(0, bounds.Height - 72));
        galleryMaxScroll = Math.Max(0, galleryFolders.Count * 54 - galleryGridBounds.Height);
        galleryScroll = Math.Clamp(galleryScroll, 0, galleryMaxScroll);
        c.Clip(galleryGridBounds);
        for (int i = Math.Max(0, (int)(galleryScroll / 54)); i < galleryFolders.Count; i++)
        {
            var folder = galleryFolders[i];
            var row = new Rect(bounds.X, galleryGridBounds.Y + i * 54 - galleryScroll, bounds.Width - 6, 48);
            if (row.Y >= galleryGridBounds.Bottom) break;
            galleryFolderCards.Add((folder.Id, row));
            c.Fill(row, Surface, 4); c.Stroke(row, galleryFolderSelection.Contains(folder.Id) ? Accent : Grid, radius: 4);
            c.Text(folder.Name, row.X + 10, row.Y + 6, 13, Foreground, row.Width - 20);
            c.Text(L.Get("gallery.folderCount", folder.PatternIds.Count), row.X + 10, row.Y + 28, 11, Muted, row.Width - 20);
        }
        c.Unclip();
        DrawGalleryScrollbar(c, galleryGridBounds, galleryScroll, galleryMaxScroll);
        Button(c, new(bounds.X, galleryGridBounds.Bottom + 5, bounds.Width, 28), L.Get("gallery.openFolder"),
            () => OpenGalleryFolder(galleryFolderSelection.Single()), enabled: ready && galleryFolderSelection.Count == 1, fontSize: 11);
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
        if (button == 2)
        {
            if (!galleryFolderSelection.Contains(id)) SelectGalleryRange(id, false, false, true);
            contextItems.Clear(); menu = -1;
            contextItems.Add(new(L.Get("gallery.openFolder"), () => OpenGalleryFolder(id)));
            contextItems.Add(new(L.Get("gallery.rename"), () =>
            {
                var folder = galleryFolders.First(f => f.Id == id);
                RequestGalleryText(L.Get("gallery.rename"), folder.Name, name =>
                {
                    if (string.IsNullOrWhiteSpace(name) || !CanEditGalleryTags || galleryStore is not { } store) return;
                    StartGalleryIo(() => { store.RenameFolder(id, name); return ReloadGallery(store, "", true); });
                });
            }, CanEditGalleryTags && id != PatternFolder.DefaultId && RequestPatternName is not null));
            contextItems.Add(new(L.Get("gallery.mergeFolders"), () => ShowGalleryMergeFolders(), CanEditGalleryTags && galleryFolderSelection.Count >= 2));
            contextItems.Add(new(L.Get("gallery.export"), () => RequestGalleryExport(galleryFolderSelection.ToArray())));
            contextItems.Add(new(L.Get("editor.command.paste"), () => PasteGalleryPatterns(id), CanEditGalleryTags && galleryClipboard.Length > 0));
            PositionGalleryMenu(x, y);
            return;
        }
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
        contextItems.Add(new(L.Get("gallery.exportFolder"), () => RequestGalleryExport([galleryFolderId!.Value])));
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
        StartGalleryIo(() => { store.TransferPatterns(ids, destination, !cut); return ReloadGallery(store, "", true); });
        // A failed move can be retried: IDs remain valid until publication succeeds.
        galleryPendingCut = cut ? ids : null;
    }

    private Guid[]? galleryPendingCut;
}
