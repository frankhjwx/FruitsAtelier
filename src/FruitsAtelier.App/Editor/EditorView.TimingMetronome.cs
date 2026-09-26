using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int metronomeDivisor;
    private MapDocument? metronomeSnapshot;
    private TimingMap.Lookup? metronomeLookup;
    private void UpdateTimingMetronome(double position, bool playing)
    {
        if (!playing || !metronomeEnabled || TimingModal || !double.IsFinite(position))
        { ResetHitsounds(); return; }
        int subdivision = placementCtrl ? divisor : 1;
        if (metronomeSnapshot is null || !Document.ContentEquals(metronomeSnapshot) || subdivision != metronomeDivisor)
        {
            ResetHitsounds(); metronomeSnapshot = Document.DeepClone(); metronomeLookup = new(Document); metronomeDivisor = subdivision;
        }
        double previous = hitsoundPosition ?? position - .001;
        if (position < previous || position - previous > 250) { ResetHitsounds(); previous = position - .001; }
        hitsoundPosition = position;
        bool scheduled = RequestScheduleHitsound is not null;
        double start = scheduled ? scheduledThrough ?? previous : previous;
        double end = scheduled ? position + HitsoundLookaheadMs : position;
        foreach (var tick in metronomeLookup!.Grid(start, end, subdivision).Where(t => t.TimeMs > start && t.TimeMs >= 0))
        {
            var sound = new Hitsound(CatchObjectKind.Droplet, null, tick.IsMeasure ? 1 : .7f,
                tick.IsMeasure ? "metronome-downbeat" : "metronome-tick");
            if (scheduled) RequestScheduleHitsound!(sound, tick.TimeMs); else RequestHitsound?.Invoke(sound);
        }
        if (scheduled) scheduledThrough = end;
    }
}
