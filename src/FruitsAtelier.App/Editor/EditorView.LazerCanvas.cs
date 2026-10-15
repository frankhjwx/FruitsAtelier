// Adapted from ppy/osu 7318ef41b01bdc618d1bc1ec86de617223705077:
// CatchHitObjectComposer, CatchDistanceSnapGrid and LegacyHitExplosion.
// Copyright (c) ppy Pty Ltd. MIT Licence; see Core/Gameplay/LICENSE.osu.txt.
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private IReadOnlyList<ConvertedCatchObject>? lazerVisualSource;
    private CatchAutoPreview? lazerAutoplay;
    private CatchLightingEvent[] lazerLighting = [], previewLighting = [];
    private readonly Dictionary<(Guid SourceId, int EventIndex), bool> lazerCaught = [];
    private CatchConversionResult? lazerDistanceConversion;
    private DistanceSnap.Reference[] lazerDistanceReferences = [];
    private readonly Dictionary<Guid, double> lazerDistanceParentEnds = [];
    private sealed record LazerDistanceSnapshot(DistanceSnap.Reference[] References, IReadOnlyDictionary<Guid, double> Ends);
    private LazerDistanceSnapshot? lazerGestureDistance;
    private static readonly double[] LazerGuideVelocities = [0, 1, -1, .5, -.5];
    internal int LazerDistanceGuideCount { get; private set; }

    private static CatchLightingEvent[] BuildLighting(IReadOnlyList<ConvertedCatchObject> objects, CatchAutoPreview autoplay,
        double? circleSize = null)
    {
        int combo = 0;
        List<CatchLightingEvent> result = [];
        foreach (var item in objects)
        {
            double offset = item.X - autoplay.At(item.TimeMs).X;
            bool hit = circleSize is null || Math.Abs(offset) <= CatchSize.CatchWidth(circleSize.Value) / 2;
            if (item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet) combo = hit ? combo + 1 : 0;
            if (hit) result.Add(new(item, offset, combo));
        }
        return result.ToArray();
    }

    private void EnsureLazerCanvasVisuals()
    {
        if (ReferenceEquals(lazerVisualSource, playableObjects)) return;
        lazerVisualSource = playableObjects;
        var traversal = BuildLazerAutoplayTraversal();
        lazerAutoplay = new(playableObjects, Document.CircleSize, traversal, lazerEditor: true);
        lazerLighting = BuildLighting(playableObjects, lazerAutoplay, Document.CircleSize);
        lazerCaught.Clear();
        double halfWidth = CatchSize.CatchWidth(Document.CircleSize) / 2;
        foreach (var item in playableObjects)
            lazerCaught[(item.SourceId, item.EventIndex)] = Math.Abs(item.X - lazerAutoplay.At(item.TimeMs).X) <= halfWidth;
    }

    private ConvertedCatchObject[] BuildLazerAutoplayTraversal()
    {
        var parents = ComboEntries().Select((entry, order) => (entry.Id, entry.EventIndex, order)).ToArray();
        var orderByEvent = parents.ToDictionary(entry => (entry.Id, entry.EventIndex), entry => entry.order);
        var orderByParent = parents.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.First().order);
        var segmentRanks = bananaSegments.ToDictionary(group => group.Key, group => group.Value.Select((segment, index) =>
            (Start: OsuBeatmapWriter.QuantizeTime(segment.TimeMs), Rank: orderByEvent.GetValueOrDefault((group.Key, index),
                orderByParent.GetValueOrDefault(group.Key, int.MaxValue)))).ToArray());
        return playableObjects.OrderBy(Rank).ThenBy(item => item.EventIndex).ToArray();

        int Rank(ConvertedCatchObject item)
        {
            if (item.Kind == CatchObjectKind.Banana && segmentRanks.TryGetValue(item.SourceId, out var segments) && segments.Length > 0)
            {
                // Banana event indices span the whole shower; composer parent indices identify its segments.
                int lo = 0, hi = segments.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (segments[mid].Start <= item.TimeMs) lo = mid + 1; else hi = mid;
                }
                return segments[Math.Max(0, lo - 1)].Rank;
            }
            return orderByEvent.GetValueOrDefault((item.SourceId, item.EventIndex),
                orderByParent.GetValueOrDefault(item.SourceId, int.MaxValue));
        }
    }

    private float LazerCanvasObjectOpacity(ConvertedCatchObject item)
    {
        double age = LazerPresentationTime - item.TimeMs;
        if (age < 0) return 1;
        if (lazerCaught.GetValueOrDefault((item.SourceId, item.EventIndex), true)) return 0;
        return (float)Math.Clamp(1 - age / 250, 0, 1);
    }

    private void DrawLazerCanvasCatcher(ICanvas c)
    {
        if (!IsLazerEditing) return;
        EnsureLazerCanvasVisuals();
        double time = LazerPresentationTime;
        var field = Playfield;
        var frame = lazerAutoplay!.At(time);
        for (double at = Math.Max(0, Math.Ceiling((time - 800) / 16) * 16); at <= time; at += 16)
        {
            var past = lazerAutoplay.At(at);
            bool hyper = lazerAutoplay.HyperDashingAt(at);
            if (past.Dashing || hyper)
                DrawCatcherTrail(c, new(at, past.X, lazerAutoplay.FacingLeftAt(at), hyper, false),
                    field.X, field.Width, LazerCatchLineY, time, Document.CircleSize);
        }
        foreach (double start in lazerAutoplay.HyperStarts(time - 1200, time))
            DrawCatcherTrail(c, new(start, lazerAutoplay.At(start).X, lazerAutoplay.FacingLeftAt(start), true, true),
                field.X, field.Width, LazerCatchLineY, time, Document.CircleSize);
        float x = field.X + (float)frame.X * field.Width / 512;
        DrawCatcherBody(c, x, LazerCatchLineY, field.Width, 0xFFFFFF, 1, false,
            lazerAutoplay.FacingLeftAt(time), circleSize: Document.CircleSize);
        float tint = (float)lazerAutoplay.HyperTintAt(time);
        if (tint > .001f) DrawCatcherBody(c, x, LazerCatchLineY, field.Width, HyperDashColour, tint, false,
            lazerAutoplay.FacingLeftAt(time), circleSize: Document.CircleSize);
        DrawCatchLighting(c, lazerLighting, time, x, field.Width, LazerCatchLineY, Document.CircleSize);
    }

    private void DrawCatchLighting(ICanvas c, IReadOnlyList<CatchLightingEvent> entries, double time,
        float catcherX, float fieldWidth, float catchY, double circleSize)
    {
        if (!HitLightingEnabled) return;
        int lo = 0, hi = entries.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (entries[mid].Object.TimeMs < time - CatchHitLighting.Lifetime) lo = mid + 1; else hi = mid;
        }
        float catcherScale = CatchSize.Scale(circleSize) * 2;
        float scale = .5f * catcherScale * fieldWidth / 512;
        float halfWidth = CatchSize.CatchWidth(circleSize) / 2;
        for (int i = lo; i < entries.Count && entries[i].Object.TimeMs <= time; i++)
        {
            var entry = entries[i];
            var state = CatchHitLighting.At(entry, time);
            float offset = (float)Math.Clamp(entry.Offset, -halfWidth + .3f, halfWidth - .3f);
            float x = catcherX + offset * catcherScale * fieldWidth / 512;
            uint colour = ObjectColour(entry.Object);
            Draw("scoreboard-explosion-2@2x.png", 84, 146, state.TallScaleX, state.TallScaleY, state.TallOpacity);
            Draw("scoreboard-explosion-1@2x.png", 78, 210, state.WideScaleX, state.WideScaleY, state.WideOpacity);
            void Draw(string name, float width, float height, float sx, float sy, float opacity)
            {
                if (opacity <= 0) return;
                float w = width / 2 * scale * sx, h = height / 2 * scale * sy;
                c.SpriteImage(Path.Combine(AppContext.BaseDirectory, "assets", "ui", "catch", name),
                    new(x - w / 2, catchY - w / 2 - h / 2, w, h), colour,
                    new(0, 0, width, height), opacity, -90, true);
            }
        }
    }

    private void EnsureLazerDistanceReferences()
    {
        EnsureConversion();
        if (ReferenceEquals(lazerDistanceConversion, conversion)) return;
        EnsureDistanceReferences();
        lazerDistanceParentEnds.Clear();
        foreach (var reference in distanceReferences) lazerDistanceParentEnds[reference.Id] = reference.End.TimeMs;
        var tails = conversion!.Objects.Where(item => item.Kind is not (CatchObjectKind.TinyDroplet or CatchObjectKind.Banana))
            .GroupBy(item => item.SourceId).ToDictionary(group => group.Key, group => group.Last());
        lazerDistanceReferences = distanceReferences.Select(reference =>
            tails.TryGetValue(reference.Id, out var tail)
                ? reference with { End = new(tail.TimeMs, tail.X) } : reference).ToArray();
        lazerDistanceConversion = conversion;
    }

    private DistanceSnap.Reference? LazerDistanceSnapSource(double time, ISet<Guid>? excluded = null)
    {
        EnsureLazerDistanceReferences();
        return FindLazerDistanceReference(time, lazerDistanceReferences, lazerDistanceParentEnds, excluded);
    }

    private DistanceSnap.Reference? FindLazerDistanceReference(double time, DistanceSnap.Reference[] references,
        IReadOnlyDictionary<Guid, double> ends, ISet<Guid>? excluded)
    {
        int lo = 0, hi = references.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (references[mid].Start.TimeMs < time) lo = mid + 1; else hi = mid;
        }
        for (int i = lo - 1; i >= 0; i--)
        {
            var reference = references[i];
            if (reference.Id != draftTrack && ends[reference.Id] < time
                && excluded?.Contains(reference.Id) != true) return reference;
        }
        return null;
    }

    private void CaptureLazerGestureDistance()
    {
        if (!IsLazerEditing) { lazerGestureDistance = null; return; }
        EnsureLazerDistanceReferences();
        lazerGestureDistance = new(lazerDistanceReferences, new Dictionary<Guid, double>(lazerDistanceParentEnds));
    }

    private MapPoint LazerPlacementPoint(float x, float y)
    {
        var point = Transform.ToMap(x, y);
        double time = snap ? SnapTiming().Snap(point.TimeMs, divisor) : point.TimeMs;
        point = new(Math.Clamp(time, 0, EditableDurationMs), Math.Clamp(point.X, 0, 512));
        return SnapDistance(point);
    }

    private bool TryLazerGestureDistanceSnap(MapPoint candidate, out MapPoint snapped,
        ISet<Guid>? excluded = null, double minX = 0, double maxX = 512)
    {
        snapped = candidate;
        if (!IsLazerEditing || !DistanceSnapEnabled || lazerGestureDistance is not { } snapshot) return false;
        var source = FindLazerDistanceReference(candidate.TimeMs, snapshot.References, snapshot.Ends, excluded);
        return SnapLazerDistanceFromSource(candidate, source, minX, maxX, out snapped);
    }

    private bool TryLazerDistanceSnap(MapPoint candidate, out MapPoint snapped,
        ISet<Guid>? excluded = null, double minX = 0, double maxX = 512)
    {
        snapped = candidate;
        if (!IsLazerEditing || !DistanceSnapEnabled || draftTrack != Guid.Empty) return false;
        var source = LazerDistanceSnapSource(candidate.TimeMs, excluded);
        return SnapLazerDistanceFromSource(candidate, source, minX, maxX, out snapped);
    }

    private bool SnapLazerDistanceFromSource(MapPoint candidate, DistanceSnap.Reference? source,
        double minX, double maxX, out MapPoint snapped)
    {
        snapped = candidate;
        if (source is null) return false;
        double best = 50;
        foreach (double velocity in LazerGuideVelocities)
        {
            double x = source.End.X + (candidate.TimeMs - source.End.TimeMs) * velocity;
            if (x < minX || x > maxX) continue;
            double distance = Math.Abs(x - candidate.X) * Playfield.Width / 512;
            if (distance >= best) continue;
            best = distance;
            snapped = candidate with { X = x };
        }
        return best < 50;
    }

    private void DrawLazerDistanceGuides(ICanvas c)
    {
        LazerDistanceGuideCount = 0;
        if (!IsLazerEditing || !DistanceSnapEnabled) return;
        double time;
        ISet<Guid>? excluded = null;
        if (tool == Tool.Select)
        {
            if (objectSelection.Count == 0) return;
            RefreshTimelineSources();
            time = timelineSources.Where(source => objectSelection.Contains(source.Id)).Select(source => source.Start).DefaultIfEmpty(double.NaN).Min();
            excluded = objectSelection;
        }
        else
        {
            if (tool is not (Tool.Fruit or Tool.Slider) || !plot.Contains(mouseX, mouseY)
                || draftTrack != Guid.Empty) return;
            time = Transform.ToMap(mouseX, mouseY).TimeMs;
        }
        if (!double.IsFinite(time)) return;
        var source = LazerDistanceSnapSource(time, excluded);
        if (source is null) return;
        double topTime = Transform.ToMap(Playfield.X, plot.Y).TimeMs;
        foreach (double velocity in LazerGuideVelocities)
        {
            double end = Math.Min(topTime, source.End.TimeMs + (velocity > 0 ? (512 - source.End.X) / velocity
                : velocity < 0 ? source.End.X / -velocity : 1024));
            end = Math.Max(end, source.End.TimeMs);
            var from = Screen(source.End);
            var to = Screen(new(end, source.End.X + (end - source.End.TimeMs) * velocity));
            if (end > source.End.TimeMs) LazerDistanceGuideCount++;
            c.Line(from.X, from.Y, to.X, to.Y, 0xFFFFFF, 4 * LazerCanvasScale, .5f);
        }
    }
}
