using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int hitsoundingBrush = 2;
    private bool hitsoundingAllowNotes;
    private float hitsoundingToolsWidth;
    internal int HitsoundingBrush => hitsoundingBrush;
    internal bool HitsoundingDraw => hitsoundingDraw;
    internal bool HitsoundingBrushEnabled => hitsoundingDraw || hitsoundingMultiSelection.Count > 0
        || hitsoundingNotesSelection && objectSelection.Count > 0;
    internal Rect HitsoundingNotesToggleBounds { get; private set; }
    private void SetHitsoundingTool(bool draw)
    {
        CancelInteraction();
        hitsoundingDraw = draw;
    }
    private void SetHitsoundingBrush(int bank)
    {
        if (!HitsoundingBrushEnabled || hitsoundingPainting || hitsoundingNoteGesture != HitsoundingNoteGesture.None) return;
        hitsoundingBrush = bank;
    }
    private void DrawHitsoundingTools(ICanvas c, Rect bounds)
    {
        const float padding = 8, gap = 6, groupGap = 10;
        float size = Math.Min(bounds.Width - 8, Math.Min(72, Math.Max(18, (bounds.Height - 2 * padding - 4 * gap - groupGap) / 5)));
        float x = bounds.X + (bounds.Width - size) / 2, y = bounds.Y + padding;
        string[] names = ["select", "draw", "soft", "normal", "drum"];
        string[] keys = ["hitsounding.select", "hitsounding.draw", "timing.soft", "timing.normal", "timing.drum"];
        string[] shortcuts = ["1", "2", "A", "S", "D"];
        int[] banks = [0, 0, 2, 1, 3];
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            if (i == 2) { c.Line(x + 8, y + groupGap / 2, x + size - 8, y + groupGap / 2, Grid); y += groupGap; }
            var box = new Rect(x, y, size, size - 2);
            bool enabled = i < 2 || HitsoundingBrushEnabled;
            bool active = enabled && (i < 2 ? hitsoundingDraw == (i == 1) : hitsoundingBrush == banks[i]);
            uint color = !enabled ? Muted : i < 2 ? Accent : HitsoundingBankColor(banks[i]);
            if (i == 0)
                c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "tools", "select.png"), box, opacity: active ? 1 : .45f);
            else
            {
                c.Fill(box, active ? Surface : Panel, 6);
                c.StrokeOpacity(box, color, active ? 2 : 1, 6, active ? 1 : .35f);
                c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "hitsounding", names[i] + ".png"),
                    new(x + size * .22f, y + 3, size * .56f, size * .56f), opacity: !enabled ? .18f : active ? 1 : .55f);
            }
            c.Text(shortcuts[i], x + 5, y + 4, 10, color, 14, true);
            string label = L.Get(keys[i]);
            float font = Math.Clamp(size * .17f, 8, 11);
            c.Text(label, x + Math.Max(2, (size - c.MeasureText(label, font, true)) / 2), y + size * .73f,
                font, active ? Foreground : Muted, size - 4, true);
            if (enabled) hits.Add(new(box, () => { if (index < 2) SetHitsoundingTool(index == 1); else SetHitsoundingBrush(banks[index]); }, true));
            y += size + gap;
        }
    }
}
