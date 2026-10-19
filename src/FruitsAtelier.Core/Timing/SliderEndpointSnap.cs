using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

// Null on the parent follows the global grid; null divisors choose the nearest supported grid.
public sealed record SliderEndpointSnap(int? StartDivisor = null, int? EndDivisor = null)
{
    public static ReadOnlySpan<int> Divisors => [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16, 24, 32];

    public void Validate()
    {
        if (StartDivisor is { } start && !Divisors.Contains(start)
            || EndDivisor is { } end && !Divisors.Contains(end))
            throw new InvalidDataException(L.Get("sliderSnap.invalid"));
    }

    public double Snap(TimingMap.Lookup timing, double time, bool end)
    {
        if ((end ? EndDivisor : StartDivisor) is { } divisor) return timing.Snap(time, divisor);
        double nearest = timing.Snap(time, 1), distance = Math.Abs(nearest - time);
        foreach (int candidate in Divisors)
        {
            double snapped = timing.Snap(time, candidate), next = Math.Abs(snapped - time);
            if (next < distance - 1e-7) { nearest = snapped; distance = next; }
        }
        return nearest;
    }
}
