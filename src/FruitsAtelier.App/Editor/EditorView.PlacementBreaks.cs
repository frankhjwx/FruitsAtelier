using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private object? placementBreakIntervalSource;
    private Dictionary<Guid, (double Start, double End)> placementBreakIntervals = [];
    private IReadOnlyList<ConvertedCatchObject>? placementBreakObjects;
    private BreakPeriod[]? placementBreakSource;
    private double placementBreakApproach;
    private IReadOnlyList<BreakPeriod> placementBreakPreview = [];

    private IReadOnlyList<BreakPeriod> MovementBreakPeriods()
    {
        if (placementMovementObjects is null || placementGhost is null || cachedPlacementPoint is not { } point
            || breakPeriods.Length == 0) return breakPeriods;
        if (ReferenceEquals(placementBreakObjects, placementMovementObjects)
            && ReferenceEquals(placementBreakSource, breakPeriods) && placementBreakApproach == Document.ApproachRate)
            return placementBreakPreview;

        if (!ReferenceEquals(placementBreakIntervalSource, conversion))
        {
            RefreshTimelineSources();
            var incomplete = Document.Tracks.Where(track => track.Nodes.Count < 2).Select(track => track.Id).ToHashSet();
            placementBreakIntervals = timelineSources.Where(source => !incomplete.Contains(source.Id))
                .ToDictionary(source => source.Id, source => (source.Start, source.End));
            placementBreakIntervalSource = conversion;
        }
        var current = new Dictionary<Guid, (double Start, double End)>(placementBreakIntervals);
        bool removedParents = false;
        if (tool == Tool.Fruit && !IsOsuCanvas)
        {
            var removed = current.Where(pair => Math.Abs(pair.Value.Start - point.TimeMs) <= 2).Select(pair => pair.Key).ToHashSet();
            foreach (var id in removed) current.Remove(id);
            removedParents = Document.Fruits.Count(fruit => removed.Contains(fruit.Id)) > 1
                || Document.Tracks.Any(track => removed.Contains(track.Id))
                || Document.ImportedSliders.Any(slider => removed.Contains(slider.Id))
                || Document.BananaShowers.Any(shower => removed.Contains(shower.Id));
        }
        if (placementDraftTrack is { Nodes.Count: >= 2 } draft)
            current[draft.Id] = (draft.Nodes[0].TimeMs, CurveMath.EndTimeMs(draft));
        else current[placementId] = (point.TimeMs, point.TimeMs);

        placementBreakPreview = OsuTimeline.ReconcileBreaks(breakPeriods, placementBreakIntervals, current,
            CatchScrollTiming.PreemptMs(Document.ApproachRate), removedParents: removedParents);
        placementBreakObjects = placementMovementObjects;
        placementBreakSource = breakPeriods;
        placementBreakApproach = Document.ApproachRate;
        return placementBreakPreview;
    }
}
