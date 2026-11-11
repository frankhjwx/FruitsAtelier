using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int timingHelperRampDivisor = 1;
    private (TimingPoint Start, TimingPoint End)? TimingHelperRampEndpoints()
    {
        if (hitsoundingTimingSelection.Count != 2) return null;
        var points = hitsoundingTimingSelection.OrderBy(p => p.TimeMs).ToArray();
        return points.Any(p => p.Uninherited || !Document.TimingPoints.Contains(p)) || points[0].TimeMs >= points[1].TimeMs
            ? null : (points[0], points[1]);
    }
    private void DrawTimingHelperRamp(ICanvas c, float x, ref float y, float w)
    {
        Field(c, x, ref y, w, L.Get("hitsounding.rampInterval"), timingHelperRampDivisor, value =>
        {
            if (!double.IsFinite(value) || value < 1 || value > 192 || value != Math.Truncate(value)) throw new ArgumentException(L.Get("timing.range"));
            timingHelperRampDivisor = (int)value;
        }, 110);
        fields[^1] = fields[^1] with { Preference = true };
        for (int i = 0; i < 3; i++)
        {
            int division = 1 << i;
            Button(c, new(x + i * w / 3, y, w / 3 - 3, 28), "1/" + division,
                () => timingHelperRampDivisor = division, timingHelperRampDivisor == division);
        }
        y += 34;
        Button(c, new(x, y, w, 32), L.Get("hitsounding.generateRamp"), () =>
        {
            if (!CommitField() || TimingHelperRampEndpoints() is not { } endpoints) return;
            Edit(L.Get("hitsounding.generateRamp"), () => TimingEditing.GenerateVolumeRamp(Document, endpoints.Start, endpoints.End, timingHelperRampDivisor));
        }, fontSize: 11);
        y += 42;
    }
}
