using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private float CanvasCornerRight => canvas.Right - 26;
    private float CanvasCornerBottom => IsOsuCanvas ? OsuDefaultSvBounds.Bottom + 42 : plot.Y + 60;

    private void DrawCoordinateReadout(ICanvas c)
    {
        bool pointerInside = plot.Contains(mouseX, mouseY);
        var osuPoint = IsOsuCanvas && pointerInside ? OsuCanvasPoint(mouseX, mouseY) : ((double X, double Y)?)null;
        double? x = IsOsuCanvas ? osuPoint?.X
            : pointerInside ? Math.Clamp(Transform.ToMap(mouseX, mouseY).X, 0, 512) : null;
        double? previous = null, next = null;
        bool selected = false;
        var ids = IsOsuCanvas ? null : FlagTargets().ToHashSet();
        if (ids is { Count: > 0 } && draftTrack == Guid.Empty && draftBanana == Guid.Empty)
        {
            if (DistanceReadoutObject() is { } target)
            {
                selected = true;
                x = target.X;
                var neighbours = DistanceNeighbours(target, ReferenceEquals(target, clickedCoordinate) ? playableObjects : null);
                previous = neighbours.Previous is { } before ? BaseDistanceRatio(before, target) : null;
                next = neighbours.Next is { } after ? BaseDistanceRatio(target, after) : null;
            }
            else
            {
                EnsureDistanceReferences();
                DistanceSnap.Reference? first = null, last = null;
                foreach (var item in distanceReferences)
                {
                    if (!ids.Contains(item.Id)) continue;
                    first ??= item;
                    last = item;
                }
                if (first is not null && last is not null)
                {
                    selected = true;
                    x = first.Start.X;
                    var before = PreviousReference(first.Start.TimeMs, ids);
                    var after = distanceReferences.FirstOrDefault(r => !ids.Contains(r.Id) && r.Start.TimeMs >= last.End.TimeMs);
                    previous = before is null ? null : DistanceSnap.Ratio(before.End, first.Start, BaseDistanceVelocity(before.End.TimeMs));
                    next = after is null ? null : DistanceSnap.Ratio(last.End, after.Start, BaseDistanceVelocity(last.End.TimeMs));
                }
            }
        }

        string coordinateX = x?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        string coordinate = IsOsuCanvas
            ? L.Get("coordinate.readoutXY", coordinateX, osuPoint?.Y.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "—")
            : L.Get("coordinate.readout", coordinateX);
        string prev = L.Get("assist.previous", previous is { } p ? L.Get("assist.ratio", p) : "—");
        string following = L.Get("assist.next", next is { } n ? L.Get("assist.ratio", n) : "—");
        const float font = 12, lineHeight = 18;
        float textWidth = c.MeasureText(coordinate, font);
        float previousWidth = c.MeasureText(prev, font), nextWidth = c.MeasureText(following, font);
        if (selected) textWidth = Math.Max(textWidth, previousWidth + 16 + nextWidth);
        var bounds = new Rect(CanvasCornerRight - textWidth - 16, IsOsuCanvas ? OsuDefaultSvBounds.Bottom + 8 : plot.Y + 8, textWidth + 16, selected ? 44 : 26);
        c.Fill(bounds, Panel, 4, .85f);
        c.Text(coordinate, bounds.X + 8, bounds.Y + 5, font, selected ? Gold : Foreground, textWidth);
        if (!selected) return;
        c.Text(prev, bounds.X + 8, bounds.Y + 5 + lineHeight, font, Gold, previousWidth);
        c.Text(following, bounds.Right - 8 - nextWidth, bounds.Y + 5 + lineHeight, font, Gold, nextWidth);
    }
}
