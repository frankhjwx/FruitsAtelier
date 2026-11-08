using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly bool[] hitsoundingLocked = [true, false, false, false, false, false, false];
    private readonly Rect[] hitsoundingLockButtons = new Rect[7];
    internal Rect HitsoundingLockBounds(int lane) => hitsoundingLockButtons[lane];

    private void DrawHitsoundingLockIcon(ICanvas c, Rect box, uint color, bool locked)
    {
        float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
        c.Stroke(new(x - 4, y - 8, 8, 9), color, 1.5f, 4);
        if (!locked) c.Fill(new(x, y - 5, 6, 6), Surface);
        c.Fill(new(x - 7, y - 1, 14, 10), color, 2);
        c.Line(x, y + 2, x, y + 6, Panel, 2);
    }

    private void DrawHitsoundingLock(ICanvas c, Rect bounds, int lane)
    {
        hitsoundingLockButtons[lane] = bounds;
        c.Fill(bounds, hitsoundingLocked[lane] ? SelectedSurface : Surface, 4);
        DrawHitsoundingLockIcon(c, bounds, hitsoundingLocked[lane] ? Accent : Muted, hitsoundingLocked[lane]);
        hits.Add(new(bounds, () =>
        {
            CancelInteraction();
            hitsoundingLocked[lane] = !hitsoundingLocked[lane];
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
            hitsoundingSelected = null; hitsoundingNotesSelection = false; SelectObjects([]);
        }, true));
    }

    private void DrawHitsoundingLockBackground(ICanvas c)
    {
        var g = hitsoundingGridBounds;
        float height = g.Height / 7;
        for (int lane = 0; lane < 7; lane++)
        {
            if (!hitsoundingLocked[lane]) continue;
            var bounds = new Rect(g.X, g.Y + lane * height, g.Width, height);
            c.Fill(bounds, 0x293649, opacity: .55f);
        }
    }
    private void DrawHitsoundingLockTooltip(ICanvas c)
    {
        for (int lane = 0; lane < 7; lane++)
            if (hitsoundingLockButtons[lane].Contains(mouseX, mouseY))
                DrawPaletteTooltip(c, L.Get(hitsoundingLocked[lane] ? "hitsounding.unlockLane" : "hitsounding.lockLane", L.Get("hitsounding.lane" + lane)),
                    hitsoundingLockButtons[lane], left: false, above: true);
    }
}
