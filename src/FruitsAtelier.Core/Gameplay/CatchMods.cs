namespace FruitsAtelier.Core;

[Flags]
public enum CatchMods { None = 0, Easy = 1, HardRock = 2, Hidden = 4, DoubleTime = 8, HalfTime = 16, Flashlight = 32 }

public static class CatchModRules
{
    public const CatchMods Supported = CatchMods.Easy | CatchMods.HardRock | CatchMods.Hidden | CatchMods.DoubleTime | CatchMods.HalfTime | CatchMods.Flashlight;

    public static CatchMods Normalize(CatchMods mods)
    {
        mods &= Supported;
        if (mods.HasFlag(CatchMods.HardRock)) mods &= ~CatchMods.Easy;
        if (mods.HasFlag(CatchMods.DoubleTime)) mods &= ~CatchMods.HalfTime;
        return mods;
    }

    public static CatchMods Toggle(CatchMods mods, CatchMods mod)
    {
        if (mods.HasFlag(mod)) return mods & ~mod;
        mods &= mod switch
        {
            CatchMods.Easy => ~CatchMods.HardRock, CatchMods.HardRock => ~CatchMods.Easy,
            CatchMods.DoubleTime => ~CatchMods.HalfTime, CatchMods.HalfTime => ~CatchMods.DoubleTime,
            _ => Supported
        };
        return Normalize(mods | mod);
    }

    public static double ClockRate(CatchMods mods) => mods.HasFlag(CatchMods.DoubleTime) ? 1.5 : mods.HasFlag(CatchMods.HalfTime) ? .75 : 1;

    // ppy/osu 48c4800e, CatchModHidden: fade from 60% to 44% of the remaining preempt.
    // MIT license: LICENSE.osu.txt.
    public static float HiddenOpacity(double remainingMs, double approachRate)
        => (float)Math.Clamp((remainingMs / CatchScrollTiming.PreemptMs(approachRate) - .44) / .16, 0, 1);
}
