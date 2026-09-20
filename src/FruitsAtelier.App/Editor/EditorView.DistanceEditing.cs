using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private (Guid Source, int Event)? distanceObject;
    private readonly List<Rect> distanceLabelBounds = [];
    public IReadOnlyList<Rect> DistanceLabelBounds => distanceLabelBounds;
    public Rect? PreviousDistanceFieldBounds { get; private set; }
    public Rect? NextDistanceFieldBounds { get; private set; }

    private double BaseDistanceVelocity(double time) => 100 * Document.SliderMultiplier / renderedTiming!.At(time).BeatLengthMs;
    private double? BaseDistanceRatio(ConvertedCatchObject from, ConvertedCatchObject to)
        => DistanceSnap.Ratio(new(from.TimeMs, from.X), new(to.TimeMs, to.X), BaseDistanceVelocity(from.TimeMs));

    private ConvertedCatchObject? SelectedDistanceObject()
    {
        if (distanceObject is not { } selected || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return null;
        var ids = FlagTargets();
        if (ids.Length != 1 || ids[0] != selected.Source) return null;
        return conversion!.Objects.FirstOrDefault(o => o.SourceId == selected.Source && o.EventIndex == selected.Event);
    }

    private (ConvertedCatchObject? Previous, ConvertedCatchObject? Next) DistanceNeighbours(ConvertedCatchObject target, IReadOnlyList<ConvertedCatchObject>? objects = null)
    {
        objects ??= conversion!.Objects;
        EnsureMovementStates(objects);
        int index = Array.FindIndex(movementIndices, i => objects[i].SourceId == target.SourceId && objects[i].EventIndex == target.EventIndex);
        if (index < 0) return (null, null);
        return (index > 0 ? objects[movementIndices[index - 1]] : null,
            index + 1 < movementIndices.Length ? objects[movementIndices[index + 1]] : null);
    }

    private void DrawDistanceFields(ICanvas c, Rect panel)
    {
        var target = PlacementGhostPoint() is null ? SelectedDistanceObject() : null;
        var neighbours = target is not null ? DistanceNeighbours(target) : default;
        float width = (panel.Width - 30) / 2;
        PreviousDistanceFieldBounds = Draw(new(panel.X + 8, panel.Y + 44, width, 22), false, DistanceReadout.Previous, neighbours.Previous);
        NextDistanceFieldBounds = Draw(new(panel.Right - width - 8, panel.Y + 44, width, 22), true, DistanceReadout.Next, neighbours.Next);
        if (editField >= 0 && fieldError.Length > 0)
        {
            c.Fill(new(panel.X, panel.Y - 30, panel.Width, 28), Panel, 4);
            c.Text(fieldError, panel.X + 6, panel.Y - 24, 11, Error, panel.Width - 12);
        }

        Rect? Draw(Rect bounds, bool next, double? ratio, ConvertedCatchObject? reference)
        {
            string key = next ? "assist.next" : "assist.previous";
            string display = L.Get(key, ratio is { } number ? L.Get("assist.ratio", number) : "—");
            bool editable = target is not null && reference is not null && ratio.HasValue && !notesLocked && drag == DragKind.None;
            if (!editable)
            {
                c.Text(display, bounds.X + 4, bounds.Y + 3, 11, Muted, bounds.Width - 8);
                return null;
            }
            int index = fields.Count;
            bool focused = editField == index;
            fields.Add(new(bounds, L.Get(next ? "distance.nextField" : "distance.previousField"), ratio!.Value,
                value => ApplyDistanceRatio(target!, reference!, next, value), false));
            if (focused || bounds.Contains(mouseX, mouseY))
            {
                c.Fill(bounds, 0x273638, 3, .8f);
                c.Stroke(bounds, focused && fieldError.Length > 0 ? Error : Accent, 1, 3);
            }
            if (focused) DrawInputText(c, new(bounds.X + 4, bounds.Y + 3, bounds.Width - 8, 16), editBuffer, 11, true, replaceText);
            else c.Text(display, bounds.X + 4, bounds.Y + 3, 11, Foreground, bounds.Width - 8);
            return bounds;
        }
    }

    private void ApplyDistanceRatio(ConvertedCatchObject target, ConvertedCatchObject reference, bool next, double value)
    {
        try { DistanceSpacingEditing.Apply(Document, target, reference, next, value, compensateTinyDroplets); }
        catch (InvalidOperationException error) { throw new ArgumentException(error.Message, error); }
        EnsureConversion();
        var updated = conversion!.Objects.FirstOrDefault(o => o.SourceId == target.SourceId && o.Kind == target.Kind && Math.Abs(o.TimeMs - target.TimeMs) < .001);
        if (updated is not null) distanceObject = (updated.SourceId, updated.EventIndex);
    }

    private void DrawMovementDistanceLabels(ICanvas c)
    {
        if (!movementAnalysis) return;
        var objects = placementMovementObjects ?? conversion!.Objects;
        EnsureMovementStates(objects);
        double endTime = viewStart + plot.Height / pixelsPerMs;
        double margin = Math.Max(200, CatchSize.FruitRadius(Document.CircleSize) * Playfield.Width / 512 * 4 / pixelsPerMs);
        int low = 0, high = objects.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (objects[mid].TimeMs < viewStart - margin) low = mid + 1; else high = mid;
        }
        var occupied = new List<Rect>();
        for (int i = low; i < objects.Count && objects[i].TimeMs <= endTime + margin; i++)
        {
            var bounds = CatchHitBounds(objects[i]);
            if (Intersects(bounds, plot)) occupied.Add(bounds);
        }
        if (PlacementGhostPoint() is not null || FlagTargets().Length == 1)
        {
            float panelWidth = Math.Min(340, plot.Width - 12);
            occupied.Add(new(Math.Clamp(Playfield.X + Playfield.Width / 2 - panelWidth / 2, plot.X + 6, plot.Right - panelWidth - 6),
                plot.Bottom - 78, panelWidth, 70));
        }
        foreach (var shower in Document.BananaShowers) occupied.Add(BananaRectangle(shower));
        for (int i = FirstVisibleMovement(objects); i < movementIndices.Length; i++)
        {
            var from = objects[movementIndices[i - 1]];
            var to = objects[movementIndices[i]];
            if (from.TimeMs > endTime) break;
            if (to.TimeMs < viewStart || from.TimeMs >= to.TimeMs
                || Document.BananaShowers.Any(s => s.TimeMs <= to.TimeMs && s.EndTimeMs >= from.TimeMs)) continue;
            if (BaseDistanceRatio(from, to) is not { } ratio) continue;
            string label = L.Get("assist.ratio", ratio);
            float width = c.MeasureText(label, 11) + 8;
            double start = Math.Max(viewStart, from.TimeMs), end = Math.Min(endTime, to.TimeMs);
            bool placed = false;
            foreach (double fraction in new[] { .5, .25, .75 })
            {
                double time = start + (end - start) * fraction;
                var p = Screen(new(time, from.X + (to.X - from.X) * (time - from.TimeMs) / (to.TimeMs - from.TimeMs)));
                foreach (int side in new[] { 1, -1 })
                {
                    var bounds = new Rect(side > 0 ? p.X + 8 : p.X - width - 8, p.Y - 9, width, 18);
                    if (bounds.X < plot.X || bounds.Right > plot.Right || bounds.Y < plot.Y || bounds.Bottom > plot.Bottom
                        || occupied.Any(r => Intersects(r, bounds))) continue;
                    c.Fill(bounds, Background, 3, .8f);
                    c.Text(label, bounds.X + 4, bounds.Y + 2, 11, Foreground, width - 8);
                    occupied.Add(new(bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6));
                    distanceLabelBounds.Add(bounds);
                    placed = true;
                    break;
                }
                if (placed) break;
            }
        }
    }
}
