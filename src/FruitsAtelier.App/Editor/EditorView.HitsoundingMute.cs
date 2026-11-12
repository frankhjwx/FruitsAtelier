using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly bool[] hitsoundingMuted = new bool[7];
    private readonly Rect[] hitsoundingMuteButtons = new Rect[7];
    internal Rect HitsoundingMuteBounds(int lane) => hitsoundingMuteButtons[lane];
    private bool HitsoundingLaneMuted(int lane) => HitsoundingPageVisible && !IsTestplaying && hitsoundingMuted[lane];
    private static int HitsoundingSoundLane(Hitsound sound) => sound.Name switch
    { "hitwhistle" => 2, "hitfinish" => 3, "hitclap" => 4, "slidertick" => 5, "storyboard" => 6, _ => 1 };

    private void DrawHitsoundingMute(ICanvas c, Rect bounds, int lane)
    {
        hitsoundingMuteButtons[lane] = bounds;
        bool muted = hitsoundingMuted[lane], hover = bounds.Contains(mouseX, mouseY);
        c.Fill(bounds, !muted ? SelectedSurface : hover ? HoverSurface : Surface, 4);
        if (!muted) c.StrokeOpacity(bounds, Accent, 1, 4, .7f);
        uint color = muted ? Muted : Accent;
        float x = bounds.X + 5, y = bounds.Y + 12;
        c.Fill(new(x, y - 3, 4, 6), color, 1);
        c.Line(x + 4, y - 3, x + 9, y - 7, color, 1.5f);
        c.Line(x + 9, y - 7, x + 9, y + 7, color, 1.5f);
        c.Line(x + 9, y + 7, x + 4, y + 3, color, 1.5f);
        if (muted) c.Line(x - 1, y + 8, x + 15, y - 8, color, 1.5f);
        else { c.Line(x + 12, y - 4, x + 15, y, color, 1.5f); c.Line(x + 15, y, x + 12, y + 4, color, 1.5f); }
        hits.Add(new(bounds, () =>
        {
            hitsoundingMuted[lane] = !hitsoundingMuted[lane];
            if (AudioPlaying) PrimeScheduledHitsounds(playhead); else ResetHitsounds();
        }, true));
    }

    private void DrawHitsoundingMuteTooltip(ICanvas c)
    {
        for (int lane = 1; lane <= 6; lane++)
            if (hitsoundingMuteButtons[lane].Contains(mouseX, mouseY))
                DrawPaletteTooltip(c, L.Get(hitsoundingMuted[lane] ? "hitsounding.unmuteLane" : "hitsounding.muteLane", L.Get("hitsounding.lane" + lane)),
                    hitsoundingMuteButtons[lane], left: false, above: true);
    }
}
