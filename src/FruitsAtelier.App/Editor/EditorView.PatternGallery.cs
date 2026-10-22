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
    private Rect galleryBounds, galleryGridBounds, galleryPopupBounds, galleryLargePreviewBounds;
    private Guid galleryPopupId, galleryPressedId;
    private float galleryPressedX, galleryPressedY, galleryScrollOrigin;
    private bool galleryPreviewPanning;
    private long galleryUseCounter;
    private int galleryPreviewBuildCount;
    private sealed record GalleryLoad(IReadOnlyList<PatternSnapshot> Patterns, string Notice);
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
    public bool PatternGalleryNeedsRedraw => PatternEditingNeedsRedraw || PatternSourceNeedsRedraw || galleryIoTask is not null || galleryPreviewTask is not null
        || galleryPlacementTask is not null || galleryDropTask is not null || galleryDragging && !galleryDropChoosing || galleryPendingDirectory is not null
        || PatternGalleryAvailable && (GalleryFullPreviewMissing || galleryVisibleIds.Any(GalleryPreviewMissing));
    private bool GalleryPreviewMissing(Guid id) => id != Guid.Empty && !galleryPreviews.ContainsKey(id) && !galleryPreviewErrors.ContainsKey(id);
    private bool GalleryFullPreviewMissing => galleryPopupId != Guid.Empty && (galleryFullPreviewId != galleryPopupId || galleryFullPreview is null) && !galleryPreviewErrors.ContainsKey(galleryPopupId);
    internal int PatternGalleryThumbnailCost => galleryPreviews.Values.Sum(p => p.Cost);
    internal int PatternGalleryFullPreviewObjectCount => galleryFullPreview?.Objects.Count ?? 0;
    internal int PatternGalleryVisibleCardCount => galleryVisibleIds.Count;
    internal int PatternGalleryCount => galleryPatterns.Count;
    internal int PatternGalleryPreviewCacheCount => galleryPreviews.Count;
    internal int PatternGalleryPreviewBuildCount => galleryPreviewBuildCount;
    internal Rect PatternGalleryGridBounds => galleryGridBounds;
    internal Rect PatternGalleryPreviewBounds => galleryLargePreviewBounds;
    internal Rect PatternGalleryCardBounds(Guid id) => galleryCards.FirstOrDefault(card => card.Id == id).Bounds;
    internal bool PatternGalleryPreviewVisible => galleryPopupId != Guid.Empty;
    internal float PatternGalleryPreviewScroll => galleryPreviewScroll;
    internal float PatternGalleryGridScroll => galleryScroll;

    public void ConfigurePatternGallery(string directory)
    {
        if (PatternEditingActive || PatternEditingBusy || galleryIoTask is not null)
        { galleryPendingDirectory = directory; return; }
        galleryPendingDirectory = null;
        galleryGeneration++;
        galleryStore = new PatternGalleryStore(directory);
        galleryPatterns = []; gallerySelection.Clear(); galleryPreviews.Clear(); galleryPreviewErrors.Clear(); galleryFullPreview = null; galleryFullPreviewId = Guid.Empty;
        galleryCards.Clear(); galleryVisibleIds.Clear();
        galleryIoTask = null; galleryPreviewTask = null;
        galleryLoaded = galleryLoadRequested = false; galleryScroll = 0;
        CancelPatternGalleryInteraction();
    }

    private void EnsurePatternGalleryLoaded() => galleryLoadRequested = true;

    private void StartGalleryIo(Func<GalleryLoad> action)
    {
        if (galleryIoTask is not null || PatternEditingBusy) return;
        galleryIoGeneration = galleryGeneration;
        galleryIoTask = Task.Run(action);
    }

    public void BeginPatternImport(string path)
    {
        if (galleryStore is not { } store || galleryIoTask is not null) return;
        StartGalleryIo(() =>
        {
            int count = store.Import(path);
            return new(store.Load(), L.Get("gallery.imported", count));
        });
    }

    public void BeginPatternExport(string path, bool selectedOnly)
    {
        if (galleryStore is not { } store || galleryIoTask is not null) return;
        Guid[]? ids = selectedOnly ? gallerySelection.ToArray() : null;
        if (ids is { Length: 0 }) return;
        StartGalleryIo(() =>
        {
            store.Export(path, ids);
            return new(store.Load(), L.Get("gallery.exported"));
        });
    }

    public void SaveSelectionToPatternGallery(string name)
    {
        if (galleryStore is not { } store || galleryIoTask is not null || !CanCopySelection) return;
        var source = Document.DeepClone();
        var ids = ClipboardSelectedParentIds().ToArray();
        string? sourcePath = GallerySourcePath();
        StartGalleryIo(() =>
        {
            var snapshot = PatternSnapshot.Capture(source, ids, name);
            store.Add(snapshot); store.RememberSource(snapshot.Id, sourcePath);
            return new(store.Load(), L.Get("gallery.saved"));
        });
    }

    private void RequestSavePattern()
    {
        if (!CanCopySelection || galleryStore is not { } store || galleryIoTask is not null) return;
        // The name prompt may outlive the current selection or difficulty.
        var source = Document.DeepClone();
        var ids = ClipboardSelectedParentIds().ToArray();
        string? sourcePath = GallerySourcePath();
        int generation = galleryGeneration;
        RequestPatternName?.Invoke(L.Get("gallery.namePrompt"), name =>
        {
            if (string.IsNullOrWhiteSpace(name) || generation != galleryGeneration || galleryIoTask is not null) return;
            StartGalleryIo(() =>
            {
                var snapshot = PatternSnapshot.Capture(source, ids, name.Trim());
                store.Add(snapshot); store.RememberSource(snapshot.Id, sourcePath);
                return new(store.Load(), L.Get("gallery.saved"));
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
                    galleryPatterns = result.Patterns; galleryLoaded = true;
                    galleryPreviews.Clear(); galleryPreviewErrors.Clear(); galleryFullPreview = null; galleryFullPreviewId = Guid.Empty;
                    galleryGeneration++;
                    gallerySelection.IntersectWith(galleryPatterns.Select(p => p.Id));
                    if (!galleryPatterns.Any(p => p.Id == galleryPopupId)) galleryPopupId = Guid.Empty;
                    if (result.Notice.Length > 0) StatusMessage = result.Notice;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { galleryLoaded = true; StatusMessage = L.Get("gallery.failed", error.Message); }
        }
        if (galleryLoadRequested && !galleryLoaded && galleryIoTask is null && galleryStore is { } store)
            StartGalleryIo(() => new(store.Load(), ""));
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
        bool ready = galleryStore is not null && galleryLoaded && galleryIoTask is null && !PatternEditingBusy;
        float gap = 5, unit = Math.Max(20, (bounds.Width - gap * 2) / 3);
        Button(c, new(bounds.X, bounds.Y, unit, 28), L.Get("gallery.save"), RequestSavePattern,
            enabled: ready && CanCopySelection && RequestPatternName is not null, fontSize: 11);
        Button(c, new(bounds.X + unit + gap, bounds.Y, unit, 28), L.Get("gallery.import"),
            () => RequestPatternImport?.Invoke(), enabled: ready, fontSize: 11);
        Button(c, new(bounds.X + (unit + gap) * 2, bounds.Y, unit, 28), L.Get("gallery.export"),
            ShowGalleryExportMenu, enabled: ready && galleryPatterns.Count > 0, fontSize: 11);
        galleryGridBounds = new(bounds.X, bounds.Y + 36, bounds.Width, Math.Max(0, bounds.Height - 62));
        if (galleryPatterns.Count == 0)
        {
            c.Text(L.Get(ready ? "gallery.empty" : "gallery.loading"), bounds.X + 4, bounds.Y + 54, 12, Muted, bounds.Width - 8);
            return;
        }
        int columns = Math.Clamp((int)(bounds.Width / (GalleryCardWidth + GalleryCardGap)), 1, 24);
        const float cellWidth = GalleryCardWidth, cellHeight = GalleryCardHeight;
        // Extremely tall layouts scroll a bounded viewport without changing card or raster sizes.
        galleryGridBounds = galleryGridBounds with
        {
            Height = Math.Min(galleryGridBounds.Height, (48 / columns - 1) * (cellHeight + GalleryCardGap))
        };
        int rows = (galleryPatterns.Count + columns - 1) / columns;
        galleryMaxScroll = Math.Max(0, rows * (cellHeight + 8) - 8 - galleryGridBounds.Height);
        galleryScroll = Math.Clamp(galleryScroll, 0, galleryMaxScroll);
        int firstRow = Math.Max(0, (int)(galleryScroll / (cellHeight + 8)));
        int lastRow = Math.Min(rows, (int)Math.Ceiling((galleryScroll + galleryGridBounds.Height) / (cellHeight + GalleryCardGap)));
        c.Clip(galleryGridBounds);
        for (int row = firstRow; row < lastRow; row++)
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (index >= galleryPatterns.Count) break;
                var pattern = galleryPatterns[index];
                var card = new Rect(bounds.X + column * (cellWidth + 8), galleryGridBounds.Y + row * (cellHeight + 8) - galleryScroll, cellWidth, cellHeight);
                galleryCards.Add((pattern.Id, card)); galleryVisibleIds.Add(pattern.Id);
                c.Fill(card, Surface, 5);
                c.Stroke(card, gallerySelection.Contains(pattern.Id) ? Accent : Grid, gallerySelection.Contains(pattern.Id) ? 2 : 1, 5);
                var image = new Rect(card.X + 6, card.Y + 6, card.Width - 12, cellHeight - 41);
                if (galleryPreviews.TryGetValue(pattern.Id, out var preview))
                {
                    preview.LastUsed = ++galleryUseCounter;
                    c.CachedPreview((preview.CacheIdentity, skin), image, (target, local) => DrawGalleryPreview(target, preview.Data, local, true, 0));
                }
                else c.Text(galleryPreviewErrors.ContainsKey(pattern.Id) ? "!" : "…", image.X + image.Width / 2 - 5, image.Y + image.Height / 2 - 10, 18, Muted);
                c.Text(pattern.Name, card.X + 7, card.Bottom - 23, 12, Foreground, card.Width - 14);
            }
        c.Unclip();
        DrawGalleryScrollbar(c, galleryGridBounds, galleryScroll, galleryMaxScroll);
        c.Text(L.Get("gallery.dragHint"), bounds.X, bounds.Bottom - 19, 10, Muted, bounds.Width);
    }

    private void ShowGalleryExportMenu()
    {
        galleryPopupId = Guid.Empty;
        contextItems.Clear(); menu = -1;
        contextItems.Add(new(L.Get("gallery.exportAll"), () => RequestPatternExport?.Invoke(false)));
        contextItems.Add(new(L.Get("gallery.exportSelected"), () => RequestPatternExport?.Invoke(true), gallerySelection.Count > 0));
        float menuWidth = Math.Min(250, Math.Max(140, width - 24));
        contextBounds = new(Math.Clamp(galleryBounds.Right - menuWidth, 8, Math.Max(8, width - menuWidth - 8)),
            galleryBounds.Y + 30, menuWidth, ContextMenuHeight);
    }

    private void DrawPatternGalleryOverlay(ICanvas c)
    {
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
        c.Text(pattern.Name, left + 16, top + 14, 16, Foreground, popupWidth - 65, true);
        Button(c, new(left + popupWidth - 36, top + 8, 28, 28), "×", () => galleryPopupId = Guid.Empty);
        float metadataTop = top + popupHeight - 126;
        galleryLargePreviewBounds = new(left + 12, top + 46, popupWidth - 24, Math.Max(50, metadataTop - top - 56));
        c.Fill(galleryLargePreviewBounds, Surface, 3);
        if (galleryPreviews.TryGetValue(pattern.Id, out var preview))
        {
            preview.LastUsed = ++galleryUseCounter;
            if (galleryFullPreviewId == pattern.Id && galleryFullPreview is { } full)
            {
                double timeScale = CatchScrollTiming.PixelsPerMs(full.ApproachRate, Math.Max(10, galleryLargePreviewBounds.Width - 28));
                float padding = GalleryPreviewPadding(full, Math.Max(10, galleryLargePreviewBounds.Width - 28));
                galleryPreviewMaxScroll = Math.Max(0, (float)((full.EndTimeMs - full.StartTimeMs) * timeScale) + padding * 2 - galleryLargePreviewBounds.Height);
                galleryPreviewScroll = Math.Clamp(galleryPreviewScroll, 0, galleryPreviewMaxScroll);
                DrawGalleryPreview(c, full, galleryLargePreviewBounds, false, galleryPreviewScroll);
                DrawGalleryScrollbar(c, galleryLargePreviewBounds, galleryPreviewScroll, galleryPreviewMaxScroll);
            }
            else c.Text(L.Get("gallery.loading"), left + 25, top + 70, 12, Muted, popupWidth - 50);
            var detailLines = L.Get("gallery.details", pattern.ObjectCount, Number(preview.TickRate), preview.Streams, preview.Stacks).Split('\n');
            for (int line = 0; line < Math.Min(2, detailLines.Length); line++)
                c.Text(detailLines[line], left + 16, metadataTop + line * 17, 11, Foreground, popupWidth - 32);
        }
        else c.Text(galleryPreviewErrors.GetValueOrDefault(pattern.Id) ?? L.Get("gallery.loading"), left + 25, top + 70, 12, Muted, popupWidth - 50);
        c.Text(pattern.Source.Artist + " — " + pattern.Source.Title, left + 16, metadataTop + 36, 11, Muted, popupWidth - 32);
        c.Text(pattern.Source.Difficulty + " · " + pattern.Source.Creator, left + 16, metadataTop + 54, 11, Muted, popupWidth - 32);
        c.Text(L.Get("gallery.scrollHint"), left + 16, metadataTop + 74, 10, Muted, popupWidth - 32);
        float footerWidth = (popupWidth - 30) / 2;
        Button(c, new(left + 12, top + popupHeight - 36, footerWidth, 26), L.Get("gallery.jumpSource"),
            () => RequestPatternSource?.Invoke(pattern), enabled: RequestPatternSource is not null, fontSize: 11);
        Button(c, new(left + 18 + footerWidth, top + popupHeight - 36, footerWidth, 26), L.Get("gallery.exportSelected"),
            () => { gallerySelection.Clear(); gallerySelection.Add(pattern.Id); RequestPatternExport?.Invoke(true); }, enabled: galleryIoTask is null && !PatternEditingBusy, fontSize: 11);
    }

    private void DrawGalleryPreview(ICanvas c, PatternPreviewData data, Rect bounds, bool thumbnail, float scroll)
    {
        float fieldWidth = Math.Max(10, bounds.Width - 28);
        double duration = Math.Max(1, data.EndTimeMs - data.StartTimeMs);
        if (thumbnail)
            fieldWidth = (float)Math.Min(fieldWidth, Math.Max(1, bounds.Height - 8)
                / (duration * CatchScrollTiming.PixelsPerMs(data.ApproachRate, 512)
                    + CatchSize.FruitRadius(data.CircleSize) * 2.4) * 512);
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
