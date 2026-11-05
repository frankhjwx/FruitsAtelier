using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal Rect TimingHelperNearbyBounds { get; private set; }
    private (int Start, int Count, int Current) TimingHelperNearbyRange()
    {
        if (overviewTiming.Length == 0) return (0, 0, -1);
        int lo = 0, hi = overviewTiming.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (overviewTiming[mid].TimeMs <= inspectorTime) lo = mid + 1; else hi = mid;
        }
        int current = Math.Max(0, lo - 1);
        if (hitsoundingSelectedTiming is { } selected)
            for (int i = current; i >= 0 && overviewTiming[i].TimeMs == selected.TimeMs; i--)
                if (ReferenceEquals(overviewTiming[i], selected)) { current = i; break; }
        int count = Math.Min(7, overviewTiming.Length);
        return (Math.Clamp(current - 3, 0, overviewTiming.Length - count), count, current);
    }

    private void DrawTimingHelperNearby(ICanvas c, Rect r, (int Start, int Count, int Current) range)
    {
        if (range.Count == 0) return;
        c.Line(r.X, r.Y, r.Right, r.Y, Grid);
        c.Clip(r);
        for (int row = 0; row < range.Count; row++)
        {
            int index = range.Start + row;
            var point = overviewTiming[index];
            bool current = index == range.Current;
            var bounds = new Rect(r.X, r.Y + 6 + row * 20, r.Width, 19);
            uint typeColor = point.Uninherited ? 0xE77785u : 0x72C686u;
            float strength = Math.Max(.3f, 1 - Math.Abs(index - range.Current) * .12f);
            uint color = current ? Accent : MixThemeColour(Panel, typeColor, strength);
            if (current) { c.Fill(bounds, SelectedSurface, 4); c.Stroke(bounds, Accent, 1, 4); }
            c.Text(Time(point.TimeMs), bounds.X + 4, bounds.Y + 3, 10, color, 74, current);
            c.Text(point.Volume + "%", bounds.Right - 38, bounds.Y + 3, 10, color, 34, current);
            string bank = point.SampleSet == 0 ? L.Get("timing.default") : BankCode(point.SampleSet);
            string info = bank + (point.SampleIndex > 0 ? ":" + point.SampleIndex : "");
            if (point.Uninherited) info = L.Get("timing.bpm") + " " + Number(60000 / point.BeatLengthMs) + " · " + info;
            c.Text(info, bounds.X + 80, bounds.Y + 3, 10, color, Math.Max(0, bounds.Width - 120));
        }
        c.Unclip();
    }
}
