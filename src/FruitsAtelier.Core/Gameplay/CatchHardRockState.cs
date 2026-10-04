// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f,
// CatchBeatmapProcessor.ApplyPositionOffsets and LegacyRandom.NextBool.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

internal struct CatchHardRockState
{
    internal CatchLegacyRandom Random = new(1337);
    private uint bits;
    private int bitIndex = 32;
    private float? previous;
    private double previousTime;

    public CatchHardRockState() { }

    internal void Slider(float endpoint, double time) { previous = endpoint; previousTime = time; }

    internal float Fruit(float x, double time)
    {
        int elapsed = (int)(time - previousTime);
        if (previous is null or 0 || elapsed > 1000) { previous = x; previousTime = time; return x; }
        float difference = x - previous.Value;
        if (difference == 0)
        {
            if (bitIndex == 32) { Random.Next(); bits = Random.LastUInt; bitIndex = 0; }
            bool right = (bits & 1) != 0; bits >>= 1; bitIndex++;
            float offset = Math.Min(20, (int)(Random.NextDouble() * Math.Max(0, elapsed / 4d)));
            float direction = right ? 1 : -1;
            x += x + direction * offset is >= 0 and <= 512 ? direction * offset : -direction * offset;
        }
        else
        {
            if (Math.Abs(difference) < elapsed / 3 && x + difference is > 0 and < 512) x += difference;
            previous = x; previousTime = time;
        }
        return x;
    }
}
