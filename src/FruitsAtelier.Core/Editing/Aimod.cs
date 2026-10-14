namespace FruitsAtelier.Core;

public readonly record struct NoteOverlap(Guid FirstId, Guid SecondId, double FirstTimeMs, double SecondTimeMs,
    double FirstEndTimeMs)
{
    // Integer division rounds the boundary midpoint toward the next object's start, as in stable.
    public int TimeMs => ((int)FirstEndTimeMs - (int)SecondTimeMs) / 2 + (int)SecondTimeMs;
}

public static partial class Aimod
{
    public static IReadOnlyList<NoteOverlap> FindOverlaps(MapDocument document)
    {
        var timing = new TimingMap.Lookup(document);
        double SliderEnd(ImportedSlider slider)
        {
            var state = timing.At(slider.TimeMs);
            return slider.TimeMs + slider.PixelLength * slider.SpanCount * state.BeatLengthMs
                / (100 * document.EffectiveSliderMultiplier * state.SliderVelocityMultiplier);
        }
        var notes = document.Fruits.Select(f => (f.Id, Time: f.TimeMs, End: f.TimeMs, f.SourceOrder))
            .Concat(document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Time: t.Nodes[0].TimeMs, End: CurveMath.EndTimeMs(t), t.SourceOrder)))
            .Concat(document.ImportedSliders.Select(s => (s.Id, Time: s.TimeMs, End: SliderEnd(s), s.SourceOrder)))
            .Concat(document.BananaShowers.Select(s => (s.Id, Time: s.TimeMs, End: s.EndTimeMs, s.SourceOrder)))
            .OrderBy(n => n.Time).ThenBy(n => n.SourceOrder).ToArray();
        var errors = new List<NoteOverlap>();
        // Adjacent pairs identify every involved object without quadratic output for a stack.
        for (int i = 1; i < notes.Length; i++)
            // Stable compares integer object boundaries, including slider repeats and spinner tails.
            if ((int)notes[i].Time - (int)notes[i - 1].End < 10)
                errors.Add(new(notes[i - 1].Id, notes[i].Id, notes[i - 1].Time, notes[i].Time, notes[i - 1].End));
        return errors;
    }
}
