// Tick generation adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f, SliderEventGenerator.
// Copyright (c) ppy Pty Ltd. MIT licence: LICENSE.osu.txt. See docs/CATCH_DIFFICULTY.md.
namespace FruitsAtelier.Core;

public static class CatchDifficultyObjects
{
    /// <summary>Builds lazer difficulty positions from an exported/read-back document.</summary>
    public static IReadOnlyList<ConvertedCatchObject> Create(MapDocument document, bool hardRock = false)
    {
        var conversion = CatchStreamConverter.Convert(document);
        if (!conversion.Success) throw new InvalidDataException(string.Join("\n", conversion.Diagnostics));
        var objects = hardRock ? CatchPreviewMods.HardRock(document, conversion) : conversion.Objects;
        var ticks = new Dictionary<Guid, Queue<(double Time, double X)>>();
        var timing = new TimingMap.Lookup(document);
        foreach (var slider in document.ImportedSliders)
        {
            var path = slider.GetGeometry();
            double velocity = ImportedSliderConverter.VelocityAt(document, slider, timing);
            double duration = path.Distance / velocity;
            var state = SliderBpm.Apply(document, timing.At(slider.TimeMs), slider.BpmMultiplier);
            double sv = ImportedSliderConverter.SliderVelocityAt(document, slider, timing);
            double length = Math.Min(LegacyCatchRules.MaximumPathLength, path.Distance);
            double spacing = Math.Clamp(LegacyCatchRules.TickDistance(document, velocity, state.BeatLengthMs, sv), 0, length);
            var queue = new Queue<(double, double)>();
            if (spacing > 0)
            {
                var progress = new List<double>();
                for (double distance = spacing; distance < length - velocity * 10; distance += spacing)
                    progress.Add(distance / length);
                for (int span = 0; span < slider.SpanCount; span++)
                {
                    bool reversed = span % 2 == 1;
                    foreach (double p in reversed ? progress.AsEnumerable().Reverse() : progress)
                        queue.Enqueue((slider.TimeMs + span * duration + (reversed ? 1 - p : p) * duration,
                            Math.Clamp(Math.Clamp((float)slider.X, 0, 512) + path.PositionAt(p).X, 0, 512)));
                }
            }
            ticks[slider.Id] = queue;
        }
        // Gameplay retains stable's integer tick timing. Difficulty uses lazer's fractional tick positions.
        return objects.Select(item =>
        {
            if (item.Kind != CatchObjectKind.Droplet || !ticks.TryGetValue(item.SourceId, out var queue)) return item;
            var tick = queue.Dequeue();
            return item with { TimeMs = tick.Time, X = tick.X };
        }).OrderBy(item => item.TimeMs).ToArray();
    }
}
