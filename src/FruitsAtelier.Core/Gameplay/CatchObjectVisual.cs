// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f:
// DrawableFruit, DrawableDroplet, DrawableBanana and StatelessRNG.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public readonly record struct CatchObjectVisual(float Rotation, float Scale)
{
    public static CatchObjectVisual At(ConvertedCatchObject item, double time, double approachRate, bool caught = false)
    {
        if (caught) time = Math.Min(time, item.TimeMs);
        float Random(int series) => RandomSingle((int)item.TimeMs, series);
        double preempt = CatchScrollTiming.PreemptMs(approachRate);
        double elapsed = time - item.TimeMs + preempt;
        if (item.Kind == CatchObjectKind.Banana)
        {
            double progress = caught ? Math.Min(1, elapsed / preempt) : elapsed / preempt;
            float start = .6f + 1.6f * Random(3);
            float from = 180 * (Random(1) * 2 - 1), to = 180 * (Random(2) * 2 - 1);
            return new((float)(from + (to - from) * progress), Math.Max(0, (float)(start + (.6f - start) * progress)) / .6f);
        }
        if (item.Kind is CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet)
            return new(Random(1) * 20 + (float)(720 * elapsed / (preempt + 2000)), 1);
        return new((Random(1) - .5f) * 40, 1);
    }

    public static float RandomSingle(int seed, int series)
        => (float)(RandomBits(seed, series) & ((1 << 24) - 1)) / (1 << 24);

    public static uint BananaColour(double time) => (RandomBits((int)time, 0) % 3) switch
    { 1 => 0xFFC000, 2 => 0xD6DD1C, _ => 0xFFF000 };

    private static ulong RandomBits(int seed, int series)
    {
        unchecked
        {
            ulong x = (((ulong)(uint)series << 32) | (uint)seed) ^ 0x12345678;
            x ^= x >> 33; x *= 0xff51afd7ed558ccd;
            x ^= x >> 33; x *= 0xc4ceb9fe1a85ec53; x ^= x >> 33;
            return x;
        }
    }
}
