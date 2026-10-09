using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private CatchConversionResult? draftBaseConversion;
    private IReadOnlyList<ConvertedCatchObject> draftBaseObjects = [];
    private OsuWriteResult? draftBaseExport;
    private MapDocument? draftPreviewSnapshot;
    private CatchConversionCache draftConversionCache = new();
    private bool deferredConversion, deferredTestplay;
    public bool ConversionRefreshing => deferredConversion;
    public bool ConversionNeedsRedraw => deferredConversion || deferredConversionTask is { IsCompleted: true };
    private Task<DeferredConversion>? deferredConversionTask;
    private sealed record DeferredConversion(MapDocument Snapshot, EditorHistory Owner, bool Compensation,
        string Language, OsuWriteCache Cache, CatchConversionResult Conversion, OsuWriteResult? Export);

    private void BeginDraftConversion()
    {
        deferredTestplay = false;
        EnsureConversion();
        draftBaseConversion = conversion;
        draftBaseObjects = playableObjects;
        draftBaseExport = playableExport;
        draftPreviewSnapshot = null;
        draftConversionCache = new();
    }

    private MapDocument DraftCandidate(CurveTrack track)
    {
        var candidate = new MapDocument
        {
            DurationMs = Document.DurationMs, BeatLengthMs = Document.BeatLengthMs,
            TimingOffsetMs = Document.TimingOffsetMs, ApproachRate = Document.ApproachRate,
            CircleSize = Document.CircleSize, SliderMultiplier = Document.SliderMultiplier,
            SliderTickRate = Document.SliderTickRate, DerandomizeDroplets = Document.DerandomizeDroplets,
            DerandomizeFSliderDroplets = Document.DerandomizeFSliderDroplets, DerandomizeDropletsForHardRock = Document.DerandomizeDropletsForHardRock, RandomizeDropletStrength = Document.RandomizeDropletStrength, RandomizeDropletSeed = Document.RandomizeDropletSeed
        };
        candidate.TimingPoints.AddRange(Document.TimingPoints);
        if (track.Nodes.Count >= 2) candidate.Tracks.Add(track);
        else if (track.Nodes.Count == 1)
        {
            // A single anchor has no convertible slider yet, but its placed head remains visible.
            var head = track.Nodes[0];
            candidate.Fruits.Add(new Fruit { Id = track.Id, TimeMs = head.TimeMs, X = head.X });
        }
        return candidate;
    }

    private void UpdateDraftConversion()
    {
        var track = Document.Tracks.First(t => t.Id == draftTrack);
        var candidate = DraftCandidate(track);
        if (draftPreviewSnapshot?.ContentEquals(candidate) == true) return;
        bool first = draftPreviewSnapshot is null;
        draftPreviewSnapshot = candidate.DeepClone();
        var local = CatchStreamConverter.Convert(candidate, compensateTinyDroplets, draftConversionCache);
        // Draft events are visual feedback only: downstream RNG and export quantization wait for completion.
        conversion = new()
        {
            Sliders = draftBaseConversion!.Sliders.Concat(local.Sliders).ToArray(),
            Objects = MergeDraftObjects(draftBaseConversion.Objects, local.Objects),
            Diagnostics = local.Diagnostics, Success = local.Success,
            MaxTickError = local.MaxTickError, MaxTinyError = local.MaxTinyError
        };
        playableObjects = MergeDraftObjects(draftBaseObjects, local.Objects);
        if (first) RefreshConversionPresentation();
        UpdatePlacementMovement(playableObjects);
        hyperdashObjects = placementHyperdash;
    }

    private static IReadOnlyList<ConvertedCatchObject> MergeDraftObjects(
        IReadOnlyList<ConvertedCatchObject> baseline, IReadOnlyList<ConvertedCatchObject> local)
    {
        if (local.Count == 0) return baseline;
        var merged = new ConvertedCatchObject[baseline.Count + local.Count];
        int a = 0, b = 0, n = 0;
        while (a < baseline.Count && b < local.Count)
            merged[n++] = baseline[a].TimeMs <= local[b].TimeMs ? baseline[a++] : local[b++];
        while (a < baseline.Count) merged[n++] = baseline[a++];
        while (b < local.Count) merged[n++] = local[b++];
        return merged;
    }

    private void EndDraftConversion(bool cancelled)
    {
        if (draftBaseConversion is null) return;
        if (cancelled)
        {
            conversion = draftBaseConversion;
            playableObjects = draftBaseObjects;
            playableExport = draftBaseExport;
            RefreshConversionPresentation();
        }
        else deferredConversion = true;
        draftBaseConversion = null;
        draftBaseObjects = [];
        draftBaseExport = null;
        draftPreviewSnapshot = null;
    }

    private void ResetDeferredConversion()
    {
        deferredConversion = deferredTestplay = false;
        draftBaseConversion = null; draftBaseObjects = []; draftBaseExport = null; draftPreviewSnapshot = null;
    }

    private void PumpDeferredConversion()
    {
        if (!deferredConversion)
        {
            if (deferredConversionTask is { IsCompleted: true } retired)
            { _ = retired.Exception; deferredConversionTask = null; }
            return;
        }
        if (draftTrack != Guid.Empty || history.HasActiveTransaction) return;
        if (deferredConversionTask is { IsCompleted: false }) return;
        if (deferredConversionTask is { } completed)
        {
            deferredConversionTask = null;
            var result = completed.GetAwaiter().GetResult();
            if (ReferenceEquals(result.Owner, history)) editorWriteCache = result.Cache;
            if (ReferenceEquals(result.Owner, history) && result.Compensation == compensateTinyDroplets
                && result.Language == L.Language && result.Snapshot.ContentEquals(Document))
            {
                convertedSnapshot = result.Snapshot;
                convertedWithCompensation = result.Compensation;
                conversion = result.Conversion;
                playableExport = result.Export;
                playableObjects = result.Export?.PlayableObjects ?? result.Conversion.Objects;
                contentDragPreview = deferredConversion = false;
                renderedTiming = SnapTiming();
                RefreshConversionPresentation();
                if (deferredTestplay)
                {
                    deferredTestplay = false;
                    if (result.Export is not null) StartTestplay();
                }
                return;
            }
        }
        // One worker owns its cache; superseded edits are captured only after it retires.
        var snapshot = Document.DeepClone();
        var owner = history;
        bool compensation = compensateTinyDroplets;
        string language = L.Language;
        var cache = editorWriteCache;
        editorWriteCache = new();
        deferredConversionTask = Task.Run(() =>
        {
            CatchConversionResult? output = null;
            OsuWriteResult? exported = null;
            try
            {
                output = cache.Convert(snapshot, compensation);
                if (output.Success)
                {
                    var candidate = OsuBeatmapWriter.Serialize(snapshot, compensation, cache);
                    if (candidate.ObjectSequenceMatches) exported = candidate;
                    else throw new InvalidDataException(string.Join("\n", candidate.Diagnostics));
                }
            }
            catch (Exception error)
            {
                output = new() { Success = false, Sliders = output?.Sliders ?? [], Objects = output?.Objects ?? [],
                    Diagnostics = [L.Reformat(error.Message)] };
            }
            return new DeferredConversion(snapshot, owner, compensation, language, cache, output, exported);
        });
    }
}
