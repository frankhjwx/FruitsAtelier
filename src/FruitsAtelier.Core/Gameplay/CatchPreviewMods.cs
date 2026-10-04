// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f,
// CatchBeatmapProcessor.ApplyPositionOffsets and LegacyRandom.NextBool.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public static class CatchPreviewMods
{
    public static IReadOnlyList<ConvertedCatchObject> HardRock(MapDocument document, CatchConversionResult conversion)
    {
        var state = new CatchHardRockState();
        var imported = document.ImportedSliders.ToDictionary(slider => slider.Id);
        var paths = conversion.Sliders.ToDictionary(slider => slider.SourceId);
        var order = document.Fruits.Select(item => (item.Id, item.SourceOrder))
            .Concat(document.Tracks.Select(item => (item.Id, item.SourceOrder)))
            .Concat(document.ImportedSliders.Select(item => (item.Id, item.SourceOrder)))
            .Concat(document.BananaShowers.Select(item => (item.Id, item.SourceOrder))).ToDictionary(item => item.Id, item => item.SourceOrder);
        var result = new List<ConvertedCatchObject>(conversion.Objects.Count);
        foreach (var group in conversion.Objects.GroupBy(item => (item.SourceId, Event: item.IsStandalone ? item.EventIndex : -1)).OrderBy(group => group.Min(item => item.TimeMs)).ThenBy(group => order.GetValueOrDefault(group.Key.SourceId)))
        {
            var first = group.First();
            if (first.IsStandalone)
            {
                float x = state.Fruit((float)first.X, first.TimeMs);
                result.Add(first with { X = x, RandomOffset = x - first.PathX });
                continue;
            }
            if (paths.TryGetValue(group.Key.SourceId, out var slider))
            {
                float endpoint = imported.TryGetValue(group.Key.SourceId, out var original) && original.ControlPoints.Count > 0
                    ? (float)original.ControlPoints[^1].X : (float)slider.Path[^1].X;
                state.Slider(endpoint, slider.StartTimeMs);
            }
            foreach (var item in group)
            {
                double x = item.X;
                if (item.Kind == CatchObjectKind.Banana) { x = (float)(state.Random.NextDouble() * 512); state.Random.Next(); state.Random.Next(); state.Random.Next(); }
                else if (item.Kind == CatchObjectKind.TinyDroplet) x = Math.Clamp(item.PathX + state.Random.NextTinyOffset(), 0, 512);
                else if (item.Kind == CatchObjectKind.Droplet) state.Random.Next();
                result.Add(item with { X = x, RandomOffset = x - item.PathX });
            }
        }
        return result.OrderBy(item => item.TimeMs).ToArray();
    }
}
