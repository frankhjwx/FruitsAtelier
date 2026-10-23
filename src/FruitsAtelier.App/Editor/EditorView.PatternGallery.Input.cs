using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<PatternSnapshot>? RequestPatternSource { get; set; }
    private bool galleryDragging, galleryDropRequested, galleryDropChoosing;
    private PatternSnapshot? galleryDraggedPattern;
    private MapDocument? galleryDragTarget;
    private EditorHistory? galleryDragHistory;
    private MapDocument? galleryDragConversion;
    private Task<GalleryPlacementResult>? galleryPlacementTask;
    private Task<GalleryDropResult>? galleryDropTask;
    private GalleryPlacementResult? galleryPlacement;
    private double galleryRequestedTime = double.NaN, galleryStartedTime = double.NaN, galleryRequestedX;
    private int galleryDragVersion;
    private bool galleryDragSpeed;
    private sealed record GalleryPlacementResult(int Version, double Time, PreparedPatternPlacement? Placement,
        PatternPreviewData? Preview, string? Error);
    private sealed record GalleryDropResult(int Version, MapDocument? Document, Guid[] Ids, OsuWriteCache? Cache,
        CatchConversionResult? Conversion, OsuWriteResult? Export, bool Compensation, string Language, string? Error);

    private bool PatternGalleryPointerDown(float x, float y, int button, bool shift, bool ctrl)
    {
        if (!PatternGalleryInputAvailable || menu >= 0 || contextItems.Count > 0) return false;
        if (GallerySearchFocused && !gallerySearchBounds.Contains(x, y)) textEditor.Field = "";
        if (galleryPopupId != Guid.Empty && galleryPopupBounds.Contains(x, y))
        {
            if (button == 0 && galleryLargePreviewBounds.Contains(x, y))
            {
                galleryPreviewPanning = true; galleryPressedY = y; galleryScrollOrigin = galleryPreviewScroll;
            }
            else if (button == 0) InvokeGalleryHit(x, y);
            return true;
        }
        if (!galleryBounds.Contains(x, y))
        {
            galleryPopupId = Guid.Empty;
            return false;
        }
        if (button is not (0 or 2)) return true;
        foreach (var card in galleryCards)
        {
            if (!galleryGridBounds.Contains(x, y) || !card.Bounds.Contains(x, y)) continue;
            if (button == 2)
            {
                ShowGalleryPatternMenu(card.Id, x, y);
                return true;
            }
            if (ctrl)
            {
                if (!gallerySelection.Add(card.Id)) gallerySelection.Remove(card.Id);
                galleryPopupId = Guid.Empty;
                return true;
            }
            if (!shift) gallerySelection.Clear();
            gallerySelection.Add(card.Id);
            galleryPressedId = card.Id; galleryPressedX = x; galleryPressedY = y;
            return true;
        }
        if (button == 0) InvokeGalleryHit(x, y);
        return true;
    }

    private void ShowGalleryPatternMenu(Guid id, float x, float y)
    {
        if (galleryIoTask is not null || PatternEditingActive || PatternEditingBusy
            || galleryPatterns.FirstOrDefault(p => p.Id == id) is not { } pattern) return;
        CancelPatternGalleryInteraction();
        gallerySelection.Clear(); gallerySelection.Add(id);
        contextItems.Clear(); menu = -1;
        contextItems.Add(new(L.Get("gallery.rename"), () =>
        {
            int generation = galleryGeneration;
            RequestPatternName?.Invoke(L.Get("gallery.rename"), name =>
            {
                if (string.IsNullOrWhiteSpace(name) || generation != galleryGeneration
                    || galleryIoTask is not null || galleryStore is not { } store) return;
                StartGalleryIo(() =>
                {
                    store.Replace(pattern.WithName(name));
                    return ReloadGallery(store, "", true);
                });
            });
            if (PatternNameVisible)
            {
                patternNameText = pattern.Name;
                SelectInput("gallery:name", patternNameText);
            }
        }, RequestPatternName is not null));
        contextItems.Add(new(L.Get("gallery.delete"), () =>
        {
            if (galleryIoTask is not null || galleryStore is not { } store) return;
            StartGalleryIo(() => { store.Delete(id); return ReloadGallery(store, ""); });
        }));
        PositionGalleryMenu(x, y);
    }

    private void PositionGalleryMenu(float x, float y)
    {
        float menuWidth = Math.Min(300, Math.Max(140, width - 24));
        contextBounds = new(Math.Clamp(x, 8, Math.Max(8, width - menuWidth - 8)),
            Math.Clamp(y, 8, Math.Max(8, height - ContextMenuHeight - 8)), menuWidth, ContextMenuHeight);
    }

    private void InvokeGalleryHit(float x, float y)
    {
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y))
            { if (hits[i].Enabled) hits[i].Action(); break; }
    }

    private bool PatternGalleryPointerMove(float x, float y)
    {
        if (galleryDropChoosing) return false;
        if (!PatternGalleryInputAvailable && (galleryPreviewPanning || galleryPressedId != Guid.Empty))
        { CancelPatternGalleryInteraction(); return false; }
        if (galleryPreviewPanning || galleryPressedId != Guid.Empty) { mouseX = x; mouseY = y; }
        if (galleryPreviewPanning)
        {
            galleryPreviewScroll = Math.Clamp(galleryScrollOrigin + y - galleryPressedY, 0, galleryPreviewMaxScroll);
            return true;
        }
        if (galleryPressedId == Guid.Empty) return false;
        if (!galleryDragging && Math.Abs(x - galleryPressedX) + Math.Abs(y - galleryPressedY) > 6)
        {
            galleryPopupId = Guid.Empty;
            if (!CanStartGalleryPlacement) { galleryPressedId = Guid.Empty; return true; }
            galleryDraggedPattern = galleryPatterns.FirstOrDefault(p => p.Id == galleryPressedId);
            if (galleryDraggedPattern is null) { galleryPressedId = Guid.Empty; return true; }
            galleryDragging = true; galleryDragSpeed = false;
            galleryDragHistory = history; galleryDragConversion = convertedSnapshot;
            galleryDragVersion++; galleryRequestedTime = galleryStartedTime = double.NaN;
        }
        if (galleryDragging)
        {
            var point = Transform.ToMap(x, y);
            galleryRequestedTime = Math.Max(0, SnapTiming().Snap(point.TimeMs, divisor));
            galleryRequestedX = point.X - 256;
        }
        return true;
    }

    private bool CanStartGalleryPlacement => ClipboardInteractionReady && !notesLocked && !IsOsuCanvas
        && !history.HasActiveTransaction && !SliderConversionBusy && !deferredConversion && galleryDropTask is null;

    private bool PatternGalleryPointerUp(float x, float y, int button)
    {
        if (button != 0) return false;
        if (!PatternGalleryInputAvailable && (galleryPreviewPanning || galleryPressedId != Guid.Empty))
        { CancelPatternGalleryInteraction(); return false; }
        if (galleryPreviewPanning) { galleryPreviewPanning = false; return true; }
        if (galleryPressedId == Guid.Empty) return false;
        if (galleryDragging)
        {
            PatternGalleryPointerMove(x, y);
            galleryPressedId = Guid.Empty;
            if (!plot.Contains(x, y) || !CanStartGalleryPlacement)
            { CancelPatternGalleryInteraction(); return true; }
            galleryDropChoosing = true;
            contextItems.Clear(); menu = -1;
            contextItems.Add(new(L.Get("gallery.keepWidth"), () => ConfirmGalleryDrop(false)));
            contextItems.Add(new(L.Get("gallery.preserveSpeed"), () => ConfirmGalleryDrop(true)));
            contextItems.Add(new(L.Get("mac.cancel"), CancelPatternGalleryInteraction));
            PositionGalleryMenu(x, y);
        }
        else
        {
            if (galleryGridBounds.Contains(x, y))
            {
                galleryPopupId = galleryPressedId;
                galleryPreviewScroll = 0;
                galleryPreviewZoom = 1;
            }
            galleryPressedId = Guid.Empty;
        }
        return true;
    }

    private void ConfirmGalleryDrop(bool preserveSpeed)
    {
        if (!galleryDropChoosing) return;
        galleryDropChoosing = false; galleryDropRequested = true;
        if (galleryDragSpeed != preserveSpeed)
        {
            galleryDragSpeed = preserveSpeed; galleryDragVersion++;
            galleryPlacement = null; galleryStartedTime = double.NaN;
        }
        StatusMessage = L.Get("gallery.preparing");
    }

    private bool PatternGalleryWheel(float x, float y, float delta, bool alt)
    {
        if (!PatternGalleryInputAvailable || menu >= 0 || contextItems.Count > 0) return false;
        if (galleryPopupId != Guid.Empty && galleryPopupBounds.Contains(x, y))
        {
            if (alt)
            {
                if (galleryLargePreviewBounds.Contains(x, y) && galleryFullPreviewId == galleryPopupId && galleryFullPreview is { } data)
                {
                    UpdateGalleryPreviewViewport(data);
                    float fieldWidth = Math.Max(10, galleryLargePreviewBounds.Width - 28) * (float)galleryPreviewZoom;
                    double time = (galleryLargePreviewBounds.Bottom - GalleryPreviewPadding(data, fieldWidth) + galleryPreviewScroll - y)
                        / CatchScrollTiming.PixelsPerMs(data.ApproachRate, fieldWidth);
                    galleryPreviewZoom = Math.Clamp(galleryPreviewZoom * Math.Pow(1.16, delta / 120),
                        GalleryPreviewFitZoom(data, galleryLargePreviewBounds), 1);
                    fieldWidth = Math.Max(10, galleryLargePreviewBounds.Width - 28) * (float)galleryPreviewZoom;
                    galleryPreviewScroll = (float)(time * CatchScrollTiming.PixelsPerMs(data.ApproachRate, fieldWidth)
                        - galleryLargePreviewBounds.Bottom + GalleryPreviewPadding(data, fieldWidth) + y);
                    UpdateGalleryPreviewViewport(data);
                }
                return true;
            }
            galleryPreviewScroll = Math.Clamp(galleryPreviewScroll
                - delta / 120 * 64 * (LibrarySettings.ReverseCanvasScroll ? -1 : 1), 0, galleryPreviewMaxScroll);
            return true;
        }
        if (!galleryBounds.Contains(x, y)) return false;
        galleryScroll = Math.Clamp(galleryScroll - delta / 120 * 96, 0, galleryMaxScroll);
        return true;
    }

    private bool PatternGalleryKeyDown(int key, bool ctrl, bool shift)
    {
        if (GallerySearchFocused)
        {
            if (key is 27 or 13) { textEditor.Field = ""; return true; }
            string previous = galleryQuery;
            if (InputKey("gallery:search", ref galleryQuery, key, ctrl, shift, 4096, RequestPasteField) && previous != galleryQuery) ApplyGalleryFilter();
            return true;
        }
        if (PatternGalleryInputAvailable && ctrl && key == 70)
        { SelectInput("gallery:search", galleryQuery); return true; }
        if (!PatternGalleryInputAvailable || IsEditingText || menu >= 0 || contextItems.Count > 0) return false;
        if (key == 27 && (galleryDragging || galleryPopupId != Guid.Empty || galleryPressedId != Guid.Empty || galleryDropTask is not null))
        { CancelPatternGalleryInteraction(); return true; }
        return false;
    }

    private void CancelPatternGalleryInteraction()
    {
        galleryDragVersion++;
        galleryPressedId = galleryPopupId = Guid.Empty;
        galleryFullPreview = null; galleryFullPreviewId = Guid.Empty;
        galleryPreviewZoom = 1;
        if (galleryDropChoosing) contextItems.Clear();
        galleryPreviewPanning = galleryDragging = galleryDropRequested = galleryDropChoosing = false;
        galleryDraggedPattern = null; galleryDragTarget = null; galleryDragHistory = null;
        galleryPlacement = null; galleryRequestedTime = galleryStartedTime = double.NaN;
        galleryPopupBounds = galleryLargePreviewBounds = default;
    }

    private void PumpGalleryPlacement()
    {
        if (galleryDropChoosing && contextItems.Count == 0) CancelPatternGalleryInteraction();
        if (galleryDragging && (!PatternGalleryInputAvailable || !ReferenceEquals(galleryDragHistory, history)
            || !ReferenceEquals(galleryDragConversion, convertedSnapshot))) CancelPatternGalleryInteraction();
        if (galleryPlacementTask is { IsCompleted: true } task)
        {
            galleryPlacementTask = null;
            var result = task.GetAwaiter().GetResult();
            if (result.Version == galleryDragVersion && galleryDragging) galleryPlacement = result;
        }
        if (galleryDropTask is { IsCompleted: true } dropTask)
        {
            galleryDropTask = null;
            var result = dropTask.GetAwaiter().GetResult();
            if (result.Version == galleryDragVersion && galleryDragTarget is { } baseline
                && ReferenceEquals(galleryDragHistory, history) && !history.HasActiveTransaction
                && !notesLocked && result.Compensation == compensateTinyDroplets && result.Language == L.Language
                && Document.ContentEquals(baseline))
            {
                if (result.Document is { } candidate && result.Cache is { } cache
                    && result.Conversion is { } preparedConversion && result.Export is { } export)
                {
                    history.Begin(L.Get("gallery.insert"));
                    if (history.TryCommitPreparedTransaction(history.ActiveTransactionVersion, baseline, candidate))
                    {
                        editorWriteCache = cache; convertedSnapshot = Document.DeepClone();
                        convertedWithCompensation = result.Compensation; conversion = preparedConversion;
                        playableExport = export; playableObjects = export.PlayableObjects;
                        contentDragPreview = false; clickedCoordinate = null; renderedTiming = SnapTiming();
                        RefreshConversionPresentation();
                        SelectObjects(result.Ids, result.Ids[0]); tool = Tool.Select;
                        StatusMessage = L.Get("gallery.inserted", result.Ids.Length);
                    }
                    else history.Cancel();
                }
                else StatusMessage = L.Get("gallery.failed", result.Error ?? "");
            }
            CancelPatternGalleryInteraction();
        }
        if (!galleryDragging || galleryDropChoosing || galleryDropTask is not null || galleryDraggedPattern is null || !double.IsFinite(galleryRequestedTime)) return;
        // Capture the full target once per gesture. Pointer updates only replace a time/X request.
        galleryDragTarget ??= Document.DeepClone();
        if (galleryPlacementTask is null && galleryStartedTime != galleryRequestedTime)
        {
            galleryStartedTime = galleryRequestedTime;
            var snapshot = galleryDraggedPattern; var target = galleryDragTarget;
            double time = galleryRequestedTime; bool speed = galleryDragSpeed; int version = galleryDragVersion;
            galleryPlacementTask = Task.Run(() =>
            {
                try
                {
                    var prepared = PatternPlacement.Prepare(snapshot, target, time, speed);
                    var preview = PatternPreview.Create(prepared.CreateDocument());
                    return new GalleryPlacementResult(version, time, prepared, preview, null);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                { return new GalleryPlacementResult(version, time, null, null, error.Message); }
            });
        }
        if (!galleryDropRequested || galleryPlacement is not { } placement || placement.Time != galleryRequestedTime) return;
        if (placement.Placement is null)
        {
            StatusMessage = L.Get("gallery.failed", placement.Error ?? "");
            CancelPatternGalleryInteraction();
            return;
        }
        var preparedPlacement = placement.Placement;
        double offset = preparedPlacement.ClampOffsetX(galleryRequestedX);
        int order;
        try { order = NextClipboardSourceOrder(galleryDraggedPattern.ObjectCount); }
        catch (InvalidOperationException error) { StatusMessage = error.Message; CancelPatternGalleryInteraction(); return; }
        int dropVersion = galleryDragVersion;
        var before = galleryDragTarget;
        bool compensation = compensateTinyDroplets;
        string language = L.Language;
        galleryDropTask = Task.Run(() =>
        {
            try
            {
                var inserted = preparedPlacement.CreateDocument(offset, order);
                var candidate = before.DeepClone();
                AppendGalleryObjects(candidate, inserted);
                var cache = new OsuWriteCache();
                var refresh = SliderRepresentationEditing.RefreshLegacyDroplets(candidate, compensation, before, cache);
                if (!refresh.Success) throw new InvalidDataException(string.Join("; ", refresh.Diagnostics));
                EditorHistory.PrepareDetachedCommit(before, candidate);
                var conversion = cache.Convert(candidate, compensation);
                var output = OsuBeatmapWriter.Serialize(candidate, compensation, cache);
                if (!conversion.Success || !output.ObjectSequenceMatches) throw new InvalidDataException(string.Join("; ", output.Diagnostics));
                return new GalleryDropResult(dropVersion, candidate, ClipboardParents(inserted).Select(p => p.Id).ToArray(),
                    cache, conversion, output, compensation, language, null);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { return new GalleryDropResult(dropVersion, null, [], null, null, null, compensation, language, error.Message); }
        });
    }

    private static void AppendGalleryObjects(MapDocument target, MapDocument inserted)
    {
        target.Fruits.AddRange(inserted.Fruits);
        target.Tracks.AddRange(inserted.Tracks);
        target.ImportedSliders.AddRange(inserted.ImportedSliders);
        target.BananaShowers.AddRange(inserted.BananaShowers);
        target.HitsoundOverrides.AddRange(inserted.HitsoundOverrides);
        target.DurationMs = Math.Max(target.DurationMs, inserted.DurationMs);
    }

    private void DrawGalleryGhost(ICanvas c)
    {
        if (!galleryDragging || galleryDropChoosing || !plot.Contains(mouseX, mouseY)) return;
        if (galleryPlacement?.Preview is not { } preview || galleryPlacement.Placement is not { } prepared
            || galleryPlacement.Time != galleryRequestedTime)
        {
            c.Text(galleryPlacement?.Error ?? L.Get("gallery.preparing"), mouseX + 14, mouseY + 14, 11, Muted, 300);
            return;
        }
        double offset = prepared.ClampOffsetX(galleryRequestedX) - prepared.ClampOffsetX(0);
        double visibleStart = Transform.ToMap(plot.X, plot.Bottom + 50).TimeMs;
        double visibleEnd = Transform.ToMap(plot.X, plot.Y - 50).TimeMs;
        c.Clip(plot);
        foreach (var path in preview.Paths)
            for (int i = Math.Max(1, GalleryPathLowerBound(path, visibleStart)); i < path.Count && path[i - 1].TimeMs <= visibleEnd; i++)
            {
                var a = Screen(path[i - 1] with { X = path[i - 1].X + offset });
                var b = Screen(path[i] with { X = path[i].X + offset });
                c.Line(a.X, a.Y, b.X, b.Y, 0xFFFFFF, 3, .45f);
            }
        for (int i = GalleryObjectLowerBound(preview.Objects, visibleStart); i < preview.Objects.Count && preview.Objects[i].TimeMs <= visibleEnd; i++)
        {
            var item = preview.Objects[i];
            var point = Screen(new(item.TimeMs, item.X + (item.Kind == CatchObjectKind.Banana ? 0 : offset)));
            if (point.Y < plot.Y - 50 || point.Y > plot.Bottom + 50) continue;
            DrawCatchObject(c, item, point.X, point.Y, Playfield.Width, .65f, preview.CircleSize,
                hyperStarts: preview.HyperdashStarts, colourOverride: 0xFFFFFF, skinIndexOverride: 0);
        }
        c.Unclip();
        c.Text(L.Get("gallery.placementScale", prepared.HorizontalScale), mouseX + 14, mouseY + 14, 11, Accent, 260);
    }
}
