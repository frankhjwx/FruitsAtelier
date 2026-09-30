namespace FruitsAtelier.Core;

public static class BeatGrid
{
    public static double Snap(double time, double offset, double beatLength, int divisor)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (!double.IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset));
        if (!double.IsFinite(beatLength) || beatLength <= 0) throw new ArgumentOutOfRangeException(nameof(beatLength));
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        double step = beatLength / divisor;
        double index = Math.Floor((time - offset) / step + 0.5);
        double snapped = offset + index * beatLength / divisor;
        if (!double.IsFinite(snapped)) throw new ArgumentOutOfRangeException(nameof(time));
        return snapped;
    }
}
