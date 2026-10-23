using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const float GalleryCardWidth = 160, GalleryCardHeight = 189, GalleryCardGap = 8;
    public Action? RequestPatternImport { get; set; }
    public Action<bool>? RequestPatternExport { get; set; }
    public Action<string, Action<string?>>? RequestPatternName { get; set; }

    private PatternGalleryStore? galleryStore;
    private string? galleryPendingDirectory;
    private IReadOnlyList<PatternSnapshot> galleryPatterns = [];
    private IReadOnlyList<PatternSnapshot> galleryFilteredPatterns = [];
    private readonly HashSet<Guid> gallerySelection = [];
    private readonly Dictionary<Guid, GalleryPreview> galleryPreviews = [];
    private readonly Dictionary<Guid, string> galleryPreviewErrors = [];
    private readonly List<(Guid Id, Rect Bounds)> galleryCards = [];
    private readonly List<Guid> galleryVisibleIds = [];
    private Task<GalleryLoad>? galleryIoTask;
    private Task<GalleryPreviewResult>? galleryPreviewTask;
    private Guid galleryFullPreviewId;
    private PatternPreviewData? galleryFullPreview;
    private int galleryGeneration, galleryIoGeneration, galleryPreviewGeneration;
    private bool galleryLoadRequested, galleryLoaded;
    private float galleryScroll, galleryMaxScroll, galleryPreviewScroll, galleryPreviewMaxScroll;
    private double galleryPreviewZoom = 1;
    private Rect galleryBounds, galleryGridBounds, galleryPopupBounds, galleryLargePreviewBounds;
    private Guid galleryPopupId, galleryPressedId;
    private float galleryPressedX, galleryPressedY, galleryScrollOrigin;
    private bool galleryPreviewPanning;
    private long galleryUseCounter;
    private int galleryPreviewBuildCount;
    private sealed record GalleryLoad(IReadOnlyList<PatternSnapshot> Patterns, string Notice, bool PreservePreviews, IReadOnlyList<PatternTag> Tags, IReadOnlyList<PatternFolder> Folders);
    private static GalleryLoad ReloadGallery(PatternGalleryStore store, string notice, bool preservePreviews = false)
    {
        var contents = store.LoadContents();
        return new(contents.Patterns, notice, preservePreviews, contents.Tags, contents.Folders!);
    }
    private sealed record GalleryPreviewResult(Guid Id, GalleryPreview? Preview, PatternPreviewData? FullPreview, string? Error);
    private sealed class GalleryPreview(PatternPreviewData data, double circleSize, double tickRate, int streams, int stacks)
    {
        public object CacheIdentity { get; } = new();
        public PatternPreviewData Data { get; } = data;
        public double CircleSize { get; } = circleSize;
        public double TickRate { get; } = tickRate;
        public int Streams { get; } = streams;
        public int Stacks { get; } = stacks;
        public long LastUsed { get; set; }
        public int Cost => Data.Objects.Count + Data.Paths.Sum(p => p.Count);
    }

    public bool PatternGalleryWantsCapture => galleryPressedId != Guid.Empty || galleryPreviewPanning;
    public bool PatternGalleryNeedsRedraw => galleryTagReordering || GalleryTagAutosavePending || PatternEditingNeedsRedraw || PatternSourceNeedsRedraw || galleryIoTask is not null || galleryPreviewTask is not null
        || galleryPlacementTask is not null || galleryDropTask is not null || galleryDragging && !galleryDropChoosing || galleryPendingDirectory is not null || galleryPendingImportPath is not null
        || PatternGalleryAvailable && (GalleryFullPreviewMissing || galleryVisibleIds.Any(GalleryPreviewMissing));
    private bool GalleryPreviewMissing(Guid id) => id != Guid.Empty && !galleryPreviews.ContainsKey(id) && !galleryPreviewErrors.ContainsKey(id);
    private bool GalleryFullPreviewMissing => galleryPopupId != Guid.Empty && (galleryFullPreviewId != galleryPopupId || galleryFullPreview is null) && !galleryPreviewErrors.ContainsKey(galleryPopupId);
    internal int PatternGalleryThumbnailCost => galleryPreviews.Values.Sum(p => p.Cost);
    internal int PatternGalleryFullPreviewObjectCount => galleryFullPreview?.Objects.Count ?? 0;
    internal int PatternGalleryVisibleCardCount => galleryVisibleIds.Count;
    internal int PatternGalleryCount => galleryPatterns.Count;
    internal int PatternGalleryFilteredCount => galleryFilteredPatterns.Count;
    internal int PatternGalleryPreviewCacheCount => galleryPreviews.Count;
    internal int PatternGalleryPreviewBuildCount => galleryPreviewBuildCount;
    internal Rect PatternGalleryGridBounds => galleryGridBounds;
    internal Rect PatternGalleryPreviewBounds => galleryLargePreviewBounds;
    internal Rect PatternGalleryPopupBounds => galleryPopupBounds;
    internal Rect PatternGallerySearchBounds => gallerySearchBounds;
    internal Rect PatternGalleryTagsBounds => galleryTagsBounds;
    internal Rect PatternGalleryEditPatternBounds { get; private set; }
    internal Rect PatternGalleryEditTagsBounds { get; private set; }
    internal Rect PatternGalleryCardBounds(Guid id) => galleryCards.FirstOrDefault(card => card.Id == id).Bounds;
    internal bool PatternGalleryPreviewVisible => galleryPopupId != Guid.Empty;
    internal float PatternGalleryPreviewScroll => galleryPreviewScroll;
    internal float PatternGalleryGridScroll => galleryScroll;

    public void ConfigurePatternGallery(string directory)
    {
        if (PatternEditingActive || PatternEditingBusy || galleryIoTask is not null || GalleryTagAutosavePending)
        { FlushGalleryTagAutosave(); galleryPendingDirectory = directory; return; }
        galleryPendingDirectory = null;
        galleryTagLayouts.Clear();
        galleryTagEdits.Clear(); galleryCurrentTagEdit = null;
        CloseGalleryTags();
        galleryGeneration++;
        galleryStore = new PatternGalleryStore(directory);
        galleryPatterns = []; galleryFilteredPatterns = []; gallerySearchIndex = []; galleryTags = []; galleryTagFilter.Clear();
        galleryFolders = []; galleryFolderId = PatternFolder.DefaultId; galleryFolderSelection.Clear();
        galleryClipboard = []; galleryFocused = false; gallerySelectionAnchor = Guid.Empty;
        galleryPendingCut = null; galleryExportFolders = null;
        galleryCategoryScroll = 0; galleryCategoriesFocused = false;
        galleryQuery = ""; gallerySelection.Clear(); galleryPreviews.Clear(); galleryPreviewErrors.Clear(); galleryFullPreview = null; galleryFullPreviewId = Guid.Empty;
        galleryCards.Clear(); galleryVisibleIds.Clear();
        galleryIoTask = null; galleryPreviewTask = null;
        galleryLoaded = galleryLoadRequested = false; galleryScroll = 0;
        CancelPatternGalleryInteraction();
    }

    private void EnsurePatternGalleryLoaded() => galleryLoadRequested = true;
    private string? galleryPendingImportPath;

    private void StartGalleryIo(Func<GalleryLoad> action)
    {
        if (galleryIoTask is not null || PatternEditingBusy) return;
        galleryIoGeneration = galleryGeneration;
        galleryIoTask = Task.Run(action);
    }

    public void BeginPatternImport(string path)
        => ShowGalleryImportDestination(path);

    private void ImportGalleryInto(string path, Guid? folderId)
    {
        if (galleryStore is not { } store || galleryIoTask is not null) return;
        StartGalleryIo(() =>
        {
            int count = store.Import(path, folderId);
            return ReloadGallery(store, L.Get("gallery.imported", count));
        });
    }

    public void BeginPatternExport(string path, bool selectedOnly)
    {
        if (galleryStore is not { } store || galleryIoTask is not null) return;
        Guid[]? folders = galleryExportFolders;
        galleryExportFolders = null;
        Guid[]? ids = selectedOnly && folders is null ? gallerySelection.ToArray() : null;
        if (ids is { Length: 0 }) return;
        StartGalleryIo(() =>
        {
            store.Export(path, ids, folders);
            return ReloadGallery(store, L.Get("gallery.exported"));
        });
    }

    public void SaveSelectionToPatternGallery(string name)
    {
        if (galleryStore is not { } store || galleryIoTask is not null || !CanCopySelection) return;
        var source = Document.DeepClone();
        var ids = ClipboardSelectedParentIds().ToArray();
        string? sourcePath = GallerySourcePath();
        Guid folder = galleryFolderId;
        StartGalleryIo(() =>
        {
            var snapshot = PatternSnapshot.Capture(source, ids, name);
            store.Add(snapshot, folder); store.RememberSource(snapshot.Id, sourcePath);
            return ReloadGallery(store, L.Get("gallery.saved"));
        });
    }

    private void RequestSavePattern()
    {
        if (galleryStore is null && !string.IsNullOrWhiteSpace(LibrarySettings.Workspace))
            ConfigurePatternGallery(Path.Combine(LibrarySettings.Workspace, "Patterns"));
        if (!CanCopySelection || galleryStore is not { } store || galleryIoTask is not null) return;
        // The name prompt may outlive the current selection or difficulty.
        var source = Document.DeepClone();
        var ids = ClipboardSelectedParentIds().ToArray();
        string? sourcePath = GallerySourcePath();
        Guid folder = galleryFolderId;
        RequestPatternName?.Invoke(L.Get("gallery.namePrompt"), name =>
        {
            if (string.IsNullOrWhiteSpace(name) || !ReferenceEquals(store, galleryStore) || galleryIoTask is not null) return;
            StartGalleryIo(() =>
            {
                var snapshot = PatternSnapshot.Capture(source, ids, name.Trim());
                store.Add(snapshot, folder); store.RememberSource(snapshot.Id, sourcePath);
                return ReloadGallery(store, L.Get("gallery.saved"));
            });
        });
    }

    private string? GallerySourcePath()
    {
        var entry = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulties[activeDifficulty].Id);
        return entry is not null && WorkspaceSession is { } session
            ? Path.Combine(session.Directory, entry.File) : Document.SourcePath;
    }

    private void PumpPatternGallery()
    {
        if (galleryPendingImportPath is not null && (!HasEditorProject || LibraryVisible || !PanelVisible(DockPanel.PatternGallery)))
            galleryPendingImportPath = null;
        if (galleryPendingDirectory is { } pending && !PatternEditingActive && !PatternEditingBusy && galleryIoTask is null)
            ConfigurePatternGallery(pending);
        if (PatternGalleryAvailable)
        {
            if (galleryStore is null && !string.IsNullOrWhiteSpace(LibrarySettings.Workspace))
                ConfigurePatternGallery(Path.Combine(LibrarySettings.Workspace, "Patterns"));
            galleryLoadRequested = true;
        }
        if (galleryIoTask is { IsCompleted: true } io)
        {
            galleryIoTask = null;
            try
            {
                var result = io.GetAwaiter().GetResult();
                if (galleryIoGeneration == galleryGeneration)
                {
                    galleryPatterns = result.Patterns; galleryTags = result.Tags.ToArray(); galleryLoaded = true;
                    galleryFolders = result.Folders;
                    galleryFolderSelection.IntersectWith(galleryFolders.Select(f => f.Id));
                    if (!galleryFolders.Any(f => f.Id == galleryFolderId)) galleryFolderId = PatternFolder.DefaultId;
                    if (!result.PreservePreviews)
                    { galleryPreviews.Clear(); galleryPreviewErrors.Clear(); galleryFullPreview = null; galleryFullPreviewId = Guid.Empty; }
                    RefreshGallerySearchIndex();
                    galleryGeneration++;
                    gallerySelection.IntersectWith(galleryPatterns.Select(p => p.Id));
                    if (!galleryPatterns.Any(p => p.Id == galleryPopupId)) galleryPopupId = Guid.Empty;
                    if (result.Notice.Length > 0) StatusMessage = result.Notice;
                    CompleteGalleryTagAutosave(true);
                    if (galleryPendingCut is { } moved && ReferenceEquals(moved, galleryClipboard)) galleryClipboard = [];
                    galleryPendingCut = null;
                    var availableIds = galleryPatterns.Select(p => p.Id).ToHashSet();
                    galleryClipboard = galleryClipboard.Where(availableIds.Contains).ToArray();
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { galleryPendingCut = null; galleryLoaded = true; StatusMessage = L.Get("gallery.failed", error.Message); CompleteGalleryTagAutosave(false, StatusMessage); }
        }
        PumpGalleryTagAutosave();
        if (galleryPendingImportPath is { } importPath && galleryLoaded && galleryIoTask is null && PatternGalleryInputAvailable && galleryBounds.Width > 0)
        {
            galleryPendingImportPath = null;
            ShowGalleryImportDestination(importPath);
        }
        if (galleryLoadRequested && !galleryLoaded && galleryIoTask is null && !PatternNameVisible && galleryStore is { } store)
            StartGalleryIo(() => ReloadGallery(store, ""));
        if (galleryFullPreviewId != galleryPopupId || !PatternGalleryAvailable)
        { galleryFullPreview = null; galleryFullPreviewId = Guid.Empty; }
        if (galleryPreviewTask is { IsCompleted: true } previewTask)
        {
            galleryPreviewTask = null;
            var result = previewTask.GetAwaiter().GetResult();
            if (galleryPreviewGeneration == galleryGeneration)
            {
                if (result.Preview is { } preview)
                {
                    if (!galleryPreviews.ContainsKey(result.Id)) galleryPreviews[result.Id] = preview;
                    galleryPreviews[result.Id].LastUsed = ++galleryUseCounter;
                    if (result.Id == galleryPopupId) { galleryFullPreview = result.FullPreview; galleryFullPreviewId = result.Id; }
                    while (galleryPreviews.Count > 64)
                        galleryPreviews.Remove(galleryPreviews.MinBy(p => p.Value.LastUsed).Key);
                }
                else galleryPreviewErrors[result.Id] = result.Error ?? "";
            }
        }
        if (galleryPreviewTask is null && PatternGalleryAvailable)
        {
            Guid next = GalleryFullPreviewMissing ? galleryPopupId : galleryVisibleIds.FirstOrDefault(GalleryPreviewMissing);
            if (galleryPatterns.FirstOrDefault(p => p.Id == next) is { } snapshot)
            {
                galleryPreviewGeneration = galleryGeneration;
                galleryPreviewBuildCount++;
                galleryPreviewTask = Task.Run(() =>
                {
                    try
                    {
                        var document = snapshot.CreateDocument();
                        var full = PatternPreview.Create(snapshot);
                        return new GalleryPreviewResult(snapshot.Id, new(GalleryThumbnailData(full), document.CircleSize,
                            document.SliderTickRate, document.Tracks.Count(t => t.StreamSnapDivisor is not null && t.Stack is null),
                            document.Tracks.Count(t => t.Stack is not null)), full, null);
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    { return new GalleryPreviewResult(snapshot.Id, null, null, error.Message); }
                });
            }
        }
        PumpGalleryPlacement();
    }

    private bool PatternGalleryAvailable => HasEditorProject && !LibraryVisible && !TimingPageVisible
        && PanelVisible(DockPanel.PatternGallery);

    private bool PatternGalleryInputAvailable => PatternGalleryAvailable && DockContentInputAvailable
        && !librarySettingsOpen && !RotationVisible && !AcknowledgementsVisible && !CollaborationReportVisible
        && !updatesPage && !languageMenuOpen && pendingLanguage is null && !PatternEditingBusy;

    private void DrawPatternGallery(ICanvas c, Rect bounds)
    {
        galleryBounds = bounds;
        galleryCards.Clear(); galleryVisibleIds.Clear();
        galleryFolderCards.Clear();
        bool ready = galleryStore is not null && galleryLoaded && galleryIoTask is null && !PatternEditingBusy;
        DrawGallerySearchAndFilters(c, bounds, ready);
        float gap = 5, unit = Math.Max(20, (bounds.Width - gap * 2) / 3);
        Button(c, new(bounds.X, bounds.Y + 34, unit, 28), L.Get("gallery.save"), RequestSavePattern,
            enabled: ready && CanSavePatternSelection, fontSize: 11);
        Button(c, new(bounds.X + unit + gap, bounds.Y + 34, unit, 28), L.Get("gallery.import"),
            () => RequestPatternImport?.Invoke(), enabled: ready, fontSize: 11);
        Button(c, new(bounds.X + (unit + gap) * 2, bounds.Y + 34, unit, 28), L.Get("gallery.export"),
            ShowGalleryExportMenu, enabled: ready, fontSize: 11);
        float categoryWidth = Math.Min(Math.Clamp(bounds.Width * .28f, 40, 130), Math.Max(40, bounds.Width - GalleryCardWidth - 12));
        var content = new Rect(bounds.X, bounds.Y + 70, bounds.Width, Math.Max(0, bounds.Height - 96));
        DrawGalleryCategories(c, new(content.X, content.Y, categoryWidth, content.Height));
        float dividerX = content.X + categoryWidth + 4;
        c.Line(dividerX, content.Y, dividerX, content.Bottom, Grid);
        galleryGridBounds = new(dividerX + 8, content.Y, Math.Max(0, content.Right - dividerX - 8), content.Height);
        if (galleryFilteredPatterns.Count == 0)
        {
            galleryScroll = galleryMaxScroll = 0;
            bool emptyFolder = galleryFolders.FirstOrDefault(f => f.Id == galleryFolderId)?.PatternIds.Count is null or 0;
            c.Text(L.Get(!ready ? "gallery.loading" : emptyFolder ? "gallery.empty" : "gallery.noMatches"),
                galleryGridBounds.X + 4, galleryGridBounds.Y + 18, 12, Muted, galleryGridBounds.Width - 8);
            return;
        }
        SetGalleryCardZoom(galleryCardZoom);
        float cellWidth = GalleryCardWidth * galleryCardZoom, cellHeight = GalleryCardHeight * galleryCardZoom;
        int columns = Math.Clamp((int)((galleryGridBounds.Width + GalleryCardGap) / (cellWidth + GalleryCardGap)), 1, 24);
        // Extremely tall layouts scroll a bounded viewport without changing card or raster sizes.
        galleryGridBounds = galleryGridBounds with
        {
            Height = Math.Min(galleryGridBounds.Height, (48 / columns - 1) * (cellHeight + GalleryCardGap))
        };
        int rows = (galleryFilteredPatterns.Count + columns - 1) / columns;
        galleryMaxScroll = Math.Max(0, rows * (cellHeight + 8) - 8 - galleryGridBounds.Height);
        galleryScroll = Math.Clamp(galleryScroll, 0, galleryMaxScroll);
        int firstRow = Math.Max(0, (int)(galleryScroll / (cellHeight + 8)));
        int lastRow = Math.Min(rows, (int)Math.Ceiling((galleryScroll + galleryGridBounds.Height) / (cellHeight + GalleryCardGap)));
        c.Clip(galleryGridBounds);
        for (int row = firstRow; row < lastRow; row++)
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (index >= galleryFilteredPatterns.Count) break;
                var pattern = galleryFilteredPatterns[index];
                var card = new Rect(galleryGridBounds.X + column * (cellWidth + 8), galleryGridBounds.Y + row * (cellHeight + 8) - galleryScroll, cellWidth, cellHeight);
                galleryCards.Add((pattern.Id, card)); galleryVisibleIds.Add(pattern.Id);
                c.Fill(card, Surface, 5);
                c.Stroke(card, gallerySelection.Contains(pattern.Id) ? Accent : Grid, gallerySelection.Contains(pattern.Id) ? 2 : 1, 5);
                var image = new Rect(card.X + 6, card.Y + 6, card.Width - 12, cellHeight - (pattern.Tags.Count > 0 ? 61 : 41));
                if (galleryPreviews.TryGetValue(pattern.Id, out var preview))
                {
                    preview.LastUsed = ++galleryUseCounter;
                    c.CachedPreview((preview.CacheIdentity, skin), image, (target, local) => DrawGalleryPreview(target, preview.Data, local, true, 0));
                }
                else c.Text(galleryPreviewErrors.ContainsKey(pattern.Id) ? "!" : "…", image.X + image.Width / 2 - 5, image.Y + image.Height / 2 - 10, 18, Muted);
                c.Text(pattern.Name, card.X + 7, card.Bottom - 23, 12, Foreground, card.Width - 14);
                if (pattern.Tags.Count > 0)
                {
                    var tags = new Rect(card.X + 7, card.Bottom - 45, card.Width - 14, 18);
                    DrawGalleryTagChips(c, GalleryTagsLayout(c, pattern, tags.Width, false), tags);
                }
            }
        c.Unclip();
        DrawGalleryScrollbar(c, galleryGridBounds, galleryScroll, galleryMaxScroll);
    }

    private void DrawPatternGalleryOverlay(ICanvas c)
    {
        galleryPreviewTagsBounds = default;
        if (!PatternGalleryAvailable) { galleryPopupBounds = galleryLargePreviewBounds = default; return; }
        DrawGalleryGhost(c);
        if (galleryDragging || galleryPopupId == Guid.Empty) return;
        var pattern = galleryPatterns.FirstOrDefault(p => p.Id == galleryPopupId);
        if (pattern is null) return;
        float popupWidth = Math.Min(420, Math.Max(220, width - 24));
        float popupHeight = Math.Min(650, Math.Max(220, height - 200));
        float left = Math.Clamp(galleryBounds.X - popupWidth - 16, 12, Math.Max(12, width - popupWidth - 12));
        float top = Math.Clamp(galleryBounds.Y, 110, Math.Max(110, height - popupHeight - 72));
        galleryPopupBounds = new(left, top, popupWidth, popupHeight);
        c.Fill(new(left + 5, top + 5, popupWidth, popupHeight), 0x000000, 7, .35f);
        c.Fill(galleryPopupBounds, Background, 7); c.Stroke(galleryPopupBounds, Accent, 1, 7);
        galleryPopupHitStart = hits.Count;
        c.Text(pattern.Name, left + 16, top + 14, 16, Foreground, popupWidth - 65, true);
        hits.Add(new(new(left + 12, top + 8, popupWidth - 56, 30), () => RenameGalleryPattern(pattern), CanEditGalleryTags && RequestPatternName is not null));
        c.Text(L.Get("gallery.sourceSummary", pattern.Source.Artist, pattern.Source.Title, pattern.Source.Creator, pattern.Source.Difficulty),
            left + 16, top + 44, 11, Muted, popupWidth - 32);
        Button(c, new(left + popupWidth - 36, top + 8, 28, 28), "×", () => galleryPopupId = Guid.Empty);
        string editTags = L.Get("gallery.editTags");
        float editTagsWidth = MathF.Ceiling(c.MeasureText(editTags, 10)) + 14;
        PatternGalleryEditTagsBounds = new(left + popupWidth - 16 - editTagsWidth, top + 70, editTagsWidth, 18);
        float tagsHeight = DrawGalleryPopupTags(c, pattern, left + 16, top + 70,
            popupWidth - 40 - editTagsWidth, popupHeight);
        PopupButton(PatternGalleryEditTagsBounds, editTags, () => ShowGalleryTagAssignments(pattern.Id), CanEditGalleryTags, 10, 7);
        float previewTop = top + 70 + Math.Max(26, tagsHeight);
        float footerTop = top + popupHeight - 40;
        galleryLargePreviewBounds = new(left + 12, previewTop, popupWidth - 24, Math.Max(1, footerTop - previewTop - 12));
        c.Fill(galleryLargePreviewBounds, Surface, 3);
        if (galleryPreviews.TryGetValue(pattern.Id, out var preview))
        {
            preview.LastUsed = ++galleryUseCounter;
            if (galleryFullPreviewId == pattern.Id && galleryFullPreview is { } full)
            {
                UpdateGalleryPreviewViewport(full);
                DrawGalleryPreview(c, full, galleryLargePreviewBounds, false, galleryPreviewScroll);
                DrawGalleryScrollbar(c, galleryLargePreviewBounds, galleryPreviewMaxScroll - galleryPreviewScroll, galleryPreviewMaxScroll);
            }
            else c.Text(L.Get("gallery.loading"), left + 25, galleryLargePreviewBounds.Y + 14, 12, Muted, popupWidth - 50);
        }
        else c.Text(galleryPreviewErrors.GetValueOrDefault(pattern.Id) ?? L.Get("gallery.loading"), left + 25, galleryLargePreviewBounds.Y + 14, 12, Muted, popupWidth - 50);
        float footerWidth = (popupWidth - 32) / 2;
        PatternGalleryEditPatternBounds = new(left + 12, footerTop, footerWidth, 28);
        PopupButton(PatternGalleryEditPatternBounds, L.Get("gallery.editPattern"),
            () => BeginPatternEditing(pattern), galleryIoTask is null && !PatternEditingActive && !PatternEditingBusy);
        PopupButton(new(left + 20 + footerWidth, footerTop, footerWidth, 28), L.Get("gallery.jumpSource"),
            () => RequestPatternSource?.Invoke(pattern), RequestPatternSource is not null);
        galleryPopupHitEnd = hits.Count;

        void PopupButton(Rect bounds, string label, Action action, bool enabled, float fontSize = 11, float padding = 9)
        {
            c.Fill(bounds, Surface, 3);
            c.Stroke(bounds, Grid, 1, 3);
            Button(c, bounds, label, action, enabled: enabled, fontSize: fontSize, textPadding: padding);
        }
    }

    private void DrawGalleryPreview(ICanvas c, PatternPreviewData data, Rect bounds, bool thumbnail, float scroll)
    {
        float fieldWidth = Math.Max(10, bounds.Width - 28);
        fieldWidth *= (float)(thumbnail ? GalleryPreviewFitZoom(data, bounds) : galleryPreviewZoom);
        double scale = CatchScrollTiming.PixelsPerMs(data.ApproachRate, fieldWidth);
        float center = bounds.X + bounds.Width / 2;
        float bottom = bounds.Bottom - GalleryPreviewPadding(data, fieldWidth) + scroll;
        double visibleStart = data.StartTimeMs + (bottom - bounds.Bottom - 30) / scale;
        double visibleEnd = data.StartTimeMs + (bottom - bounds.Y + 30) / scale;
        c.Clip(bounds);
        foreach (var path in data.Paths)
            for (int i = Math.Max(1, GalleryPathLowerBound(path, visibleStart)); i < path.Count && path[i - 1].TimeMs <= visibleEnd; i++)
            {
                var a = path[i - 1]; var b = path[i];
                float ay = bottom - (float)((a.TimeMs - data.StartTimeMs) * scale);
                float by = bottom - (float)((b.TimeMs - data.StartTimeMs) * scale);
                if (Math.Max(ay, by) < bounds.Y || Math.Min(ay, by) > bounds.Bottom) continue;
                c.Line(center + (float)(a.X - data.CenterX) * fieldWidth / 512, ay,
                    center + (float)(b.X - data.CenterX) * fieldWidth / 512, by, 0xFFFFFF, Math.Max(1.5f, fieldWidth / 512 * 5), .6f);
            }
        for (int i = GalleryObjectLowerBound(data.Objects, visibleStart); i < data.Objects.Count && data.Objects[i].TimeMs <= visibleEnd; i++)
        {
            var item = data.Objects[i];
            float y = bottom - (float)((item.TimeMs - data.StartTimeMs) * scale);
            if (y < bounds.Y - 30 || y > bounds.Bottom + 30) continue;
            DrawCatchObject(c, item, center + (float)(item.X - data.CenterX) * fieldWidth / 512, y, fieldWidth,
                circleSize: data.CircleSize, hyperStarts: data.HyperdashStarts, colourOverride: 0xFFFFFF, skinIndexOverride: 0);
        }
        c.Unclip();
    }

    private static float GalleryPreviewPadding(PatternPreviewData data, float fieldWidth)
        => 4 + CatchSize.FruitRadius(data.CircleSize) * fieldWidth / 512 * 1.2f;

    private static double GalleryPreviewFitZoom(PatternPreviewData data, Rect bounds)
    {
        double fieldWidth = Math.Max(10, bounds.Width - 28);
        double extent = Math.Max(1, data.EndTimeMs - data.StartTimeMs) * CatchScrollTiming.PixelsPerMs(data.ApproachRate, fieldWidth)
            + CatchSize.FruitRadius(data.CircleSize) * 2.4 * fieldWidth / 512;
        return Math.Min(1, Math.Max(1, bounds.Height - 8) / extent);
    }

    private void UpdateGalleryPreviewViewport(PatternPreviewData data)
    {
        galleryPreviewZoom = Math.Clamp(galleryPreviewZoom, GalleryPreviewFitZoom(data, galleryLargePreviewBounds), 1);
        float fieldWidth = Math.Max(10, galleryLargePreviewBounds.Width - 28) * (float)galleryPreviewZoom;
        double extent = (data.EndTimeMs - data.StartTimeMs) * CatchScrollTiming.PixelsPerMs(data.ApproachRate, fieldWidth)
            + GalleryPreviewPadding(data, fieldWidth) * 2 - galleryLargePreviewBounds.Height;
        galleryPreviewMaxScroll = extent < .01 ? 0 : (float)extent;
        galleryPreviewScroll = Math.Clamp(galleryPreviewScroll, 0, galleryPreviewMaxScroll);
    }

    private static PatternPreviewData GalleryThumbnailData(PatternPreviewData full)
    {
        // Thumbnail sampling bounds idle cache memory independently of source pattern length.
        // The selected popup retains the full data; these samples never enter saved or placed geometry.
        var objects = GallerySamples(full.Objects, 512);
        return full with
        {
            Objects = objects,
            HyperdashStarts = objects.Where(o => full.HyperdashStarts.Contains((o.SourceId, o.EventIndex)))
                .Select(o => (o.SourceId, o.EventIndex)).ToHashSet(),
            Paths = GallerySamples(full.Paths, 256).Select(path => (IReadOnlyList<MapPoint>)GallerySamples(path, 10)).ToArray()
        };
    }

    private static T[] GallerySamples<T>(IReadOnlyList<T> source, int maximum)
    {
        int count = Math.Min(source.Count, maximum);
        var result = new T[count];
        for (int i = 0; i < count; i++)
            result[i] = source[count == 1 ? 0 : (int)((long)i * (source.Count - 1) / (count - 1))];
        return result;
    }

    private static int GalleryPathLowerBound(IReadOnlyList<MapPoint> points, double time)
    {
        int low = 0, high = points.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (points[middle].TimeMs < time) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static int GalleryObjectLowerBound(IReadOnlyList<ConvertedCatchObject> objects, double time)
    {
        int low = 0, high = objects.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (objects[middle].TimeMs < time) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static void DrawGalleryScrollbar(ICanvas c, Rect bounds, float scroll, float maximum)
    {
        if (maximum <= 0) return;
        float thumb = Math.Max(18, bounds.Height * bounds.Height / (bounds.Height + maximum));
        c.Fill(new(bounds.Right - 4, bounds.Y + scroll / maximum * (bounds.Height - thumb), 3, thumb), 0x75818D, 1);
    }
}
