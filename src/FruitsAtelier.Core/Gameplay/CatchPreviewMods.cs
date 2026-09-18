// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f,
// CatchBeatmapProcessor.ApplyPositionOffsets and LegacyRandom.NextBool.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public static class CatchPreviewMods
{
    public static IReadOnlyList<ConvertedCatchObject> HardRock(MapDocument document, CatchConversionResult conversion)
    {
        var rng = new CatchLegacyRandom(1337);
        uint bits = 0; int bitIndex = 32;
        float? previous = null; double previousTime = 0;
        var imported = document.ImportedSliders.ToDictionary(slider => slider.Id);
        var paths = conversion.Sliders.ToDictionary(slider => slider.SourceId);
        var order = document.Fruits.Select(item => (item.Id, item.SourceOrder))
            .Concat(document.Tracks.Select(item => (item.Id, item.SourceOrder)))
            .Concat(document.ImportedSliders.Select(item => (item.Id, item.SourceOrder)))
            .Concat(document.BananaShowers.Select(item => (item.Id, item.SourceOrder))).ToDictionary(item => item.Id, item => item.SourceOrder);
        var result = new List<ConvertedCatchObject>(conversion.Objects.Count);
        foreach (var group in conversion.Objects.GroupBy(item => item.SourceId).OrderBy(group => group.Min(item => item.TimeMs)).ThenBy(group => order.GetValueOrDefault(group.Key)))
        {
            var first = group.First();
            if (first.IsStandalone)
            {
                float x = (float)first.X;
                int elapsed = (int)(first.TimeMs - previousTime);
                if (previous is null or 0 || elapsed > 1000) { previous = x; previousTime = first.TimeMs; }
                else
                {
                    float difference = x - previous.Value;
                    if (difference == 0)
                    {
                        if (bitIndex == 32) { rng.Next(); bits = rng.LastUInt; bitIndex = 0; }
                        bool right = (bits & 1) != 0; bits >>= 1; bitIndex++;
                        float offset = Math.Min(20, (int)(rng.NextDouble() * Math.Max(0, elapsed / 4d)));
                        float direction = right ? 1 : -1;
                        x += x + direction * offset is >= 0 and <= 512 ? direction * offset : -direction * offset;
                    }
                    else
                    {
                        if (Math.Abs(difference) < elapsed / 3 && x + difference is > 0 and < 512) x += difference;
                        previous = x; previousTime = first.TimeMs;
                    }
                }
                result.Add(first with { X = x, RandomOffset = x - first.PathX });
                continue;
            }
            if (paths.TryGetValue(group.Key, out var slider))
            {
                previous = imported.TryGetValue(group.Key, out var original) && original.ControlPoints.Count > 0
                    ? (float)original.ControlPoints[^1].X : (float)slider.Path[^1].X;
                previousTime = slider.StartTimeMs;
            }
            foreach (var item in group)
            {
                double x = item.X;
                if (item.Kind == CatchObjectKind.Banana) { x = (float)(rng.NextDouble() * 512); rng.Next(); rng.Next(); rng.Next(); }
                else if (item.Kind == CatchObjectKind.TinyDroplet) x = Math.Clamp(item.PathX + rng.NextTinyOffset(), 0, 512);
                else if (item.Kind == CatchObjectKind.Droplet) rng.Next();
                result.Add(item with { X = x, RandomOffset = x - item.PathX });
            }
        }
        return result.OrderBy(item => item.TimeMs).ToArray();
    }
}
