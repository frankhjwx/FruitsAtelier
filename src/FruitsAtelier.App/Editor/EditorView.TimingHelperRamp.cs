using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int timingHelperRampDivisor = 1;
    internal (Rect Interval, Rect Generate) TimingHelperRampControls { get; private set; }
    private (TimingPoint Start, TimingPoint End)? TimingHelperRampEndpoints()
    {
        if (hitsoundingTimingSelection.Count != 2) return null;
        var points = hitsoundingTimingSelection.OrderBy(p => p.TimeMs).ToArray();
        int first = Array.IndexOf(hitsoundingGreenPoints, points[0]);
        return first < 0 || first + 1 >= hitsoundingGreenPoints.Length || !ReferenceEquals(hitsoundingGreenPoints[first + 1], points[1]) || points[0].TimeMs >= points[1].TimeMs
            ? null : (points[0], points[1]);
    }
    private void DrawTimingHelperRamp(ICanvas c, float x, ref float y, float w)
    {
        c.Text(L.Get("hitsounding.rampTitle"), x, y, 12, Foreground, w, true);
        y += 24;
        float labelWidth = Math.Min(w * .4f, c.MeasureText(L.Get("hitsounding.rampInterval"), 11) + 8);
        float generateWidth = Math.Min(w * .32f, c.MeasureText(L.Get("hitsounding.generate"), 11) + 16);
        c.Text(L.Get("hitsounding.rampInterval"), x, y + 8, 11, Muted, labelWidth);
        var selector = new Rect(x + labelWidth, y, Math.Max(1, w - labelWidth - generateWidth - 6), 30);
        var generate = new Rect(x + w - generateWidth, y, generateWidth, 30);
        TimingHelperRampControls = (selector, generate);
        Button(c, selector, "1/" + timingHelperRampDivisor + " ▾", () =>
        {
            if (!CommitField()) return;
            contextItems.Clear();
            foreach (int division in SnapDivisors)
                contextItems.Add(new("1/" + division, () => timingHelperRampDivisor = division, Active: timingHelperRampDivisor == division));
            contextBounds = new(selector.X, Math.Max(0, Math.Min(selector.Bottom, height - ContextMenuHeight - 8)), selector.Width, ContextMenuHeight);
        }, fontSize: 11);
        Button(c, generate, L.Get("hitsounding.generate"), () =>
        {
            if (!CommitField() || TimingHelperRampEndpoints() is not { } endpoints) return;
            if (!Edit(L.Get("hitsounding.generateRamp"), () => TimingEditing.GenerateVolumeRamp(Document, endpoints.Start, endpoints.End, timingHelperRampDivisor))) return;
            hitsoundingTimingSelection.Clear();
            hitsoundingTimingSelection.UnionWith(Document.TimingPoints.Where(p => !p.Uninherited && p.TimeMs >= endpoints.Start.TimeMs && p.TimeMs <= endpoints.End.TimeMs));
            hitsoundingSelectedTiming = endpoints.End;
            inspectorConversion = null;
        }, fontSize: 11);
        y += 40;
        c.Line(x, y, x + w, y, Grid); y += 12;
    }
}
