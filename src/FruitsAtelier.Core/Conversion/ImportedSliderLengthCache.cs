namespace FruitsAtelier.Core;

// History snapshots retain IDs, but geometry can change in place or through undo.
internal sealed class ImportedSliderLengthCache
{
    private readonly Dictionary<Guid, (ImportedSlider Snapshot, double Length)> entries = new();

    public double EndTimeMs(MapDocument document, ImportedSlider slider, TimingMap.Lookup timingLookup)
    {
        if (!entries.TryGetValue(slider.Id, out var entry)
            || entry.Snapshot.X != slider.X || entry.Snapshot.Y != slider.Y
            || entry.Snapshot.PathType != slider.PathType || entry.Snapshot.PixelLength != slider.PixelLength
            || !entry.Snapshot.ControlPoints.SequenceEqual(slider.ControlPoints))
        {
            entry = (slider.DeepClone(), new ImportedSliderGeometry(slider).Distance);
            entries[slider.Id] = entry;
        }
        var timing = timingLookup.At(slider.TimeMs);
        return slider.TimeMs + entry.Length / LegacyCatchRules.Velocity(timing.BeatLengthMs,
            document.SliderMultiplier, timing.SliderVelocityMultiplier) * slider.SpanCount;
    }

    public void Retain(MapDocument document)
    {
        var ids = document.ImportedSliders.Select(s => s.Id).ToHashSet();
        foreach (var id in entries.Keys.Where(id => !ids.Contains(id)).ToArray()) entries.Remove(id);
    }
}
