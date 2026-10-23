using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private ContextItem? contextSubmenu;
    private Rect contextSubmenuBounds, contextSubmenuBridge;
    private int contextSubmenuFirst;
    private int ContextSubmenuRows => Math.Max(1, Math.Min(10, (int)((height - 28) / 32)));
    internal Rect ContextSubmenuBounds => contextSubmenu is null ? default : contextSubmenuBounds;

    private void OpenContextSubmenu(ContextItem item, Rect row)
    {
        if (!ReferenceEquals(contextSubmenu, item)) contextSubmenuFirst = 0;
        contextSubmenu = item;
        float childWidth = Math.Min(240, width - 16);
        float childHeight = 12 + Math.Min(ContextSubmenuRows, item.Children!.Length) * 32;
        float x = contextBounds.Right + childWidth <= width - 8 ? contextBounds.Right : contextBounds.X - childWidth;
        contextSubmenuBounds = new(Math.Clamp(x, 8, Math.Max(8, width - childWidth - 8)),
            Math.Clamp(row.Y - 6, 8, Math.Max(8, height - childHeight - 8)), childWidth, childHeight);
        contextSubmenuBridge = new(Math.Min(row.X, contextSubmenuBounds.X), row.Y,
            Math.Max(row.Right, contextSubmenuBounds.Right) - Math.Min(row.X, contextSubmenuBounds.X), row.Height);
    }

    private void UpdateContextSubmenu()
    {
        if (contextSubmenu is not null && !contextItems.Any(item => ReferenceEquals(item, contextSubmenu))) contextSubmenu = null;
        for (int i = 0; i < contextItems.Count; i++)
            if (contextItems[i] is { Enabled: true, Children: not null } item && ContextItemBounds(i).Contains(mouseX, mouseY))
            { OpenContextSubmenu(item, ContextItemBounds(i)); return; }
        if (contextSubmenu is not null && !contextSubmenuBounds.Contains(mouseX, mouseY) && !contextSubmenuBridge.Contains(mouseX, mouseY))
            contextSubmenu = null;
    }

    private Rect ContextSubmenuRow(int index) => new(contextSubmenuBounds.X + 6,
        contextSubmenuBounds.Y + 6 + (index - contextSubmenuFirst) * 32, contextSubmenuBounds.Width - 12, 30);

    private void DrawContextSubmenu(ICanvas c)
    {
        if (contextSubmenu?.Children is not { } children) return;
        c.Fill(new(contextSubmenuBounds.X + 3, contextSubmenuBounds.Y + 4, contextSubmenuBounds.Width, contextSubmenuBounds.Height), 0x11151B, 5);
        c.Fill(contextSubmenuBounds, Surface, 5); c.Stroke(contextSubmenuBounds, Grid, 1, 5);
        for (int i = contextSubmenuFirst; i < Math.Min(children.Length, contextSubmenuFirst + ContextSubmenuRows); i++)
            DrawContextAction(c, children[i], ContextSubmenuRow(i));
        if (children.Length > ContextSubmenuRows)
        {
            float track = contextSubmenuBounds.Height - 12, thumb = track * ContextSubmenuRows / children.Length;
            c.Fill(new(contextSubmenuBounds.Right - 4, contextSubmenuBounds.Y + 6 + (track - thumb) * contextSubmenuFirst / (children.Length - ContextSubmenuRows), 2, thumb), Muted, 1);
        }
    }

    private bool ScrollContextSubmenu(float x, float y, float delta)
    {
        if (contextSubmenu?.Children is not { } children || !contextSubmenuBounds.Contains(x, y)) return false;
        contextSubmenuFirst = Math.Clamp(contextSubmenuFirst - (int)(delta / 120 * 3), 0, Math.Max(0, children.Length - ContextSubmenuRows));
        return true;
    }

    private bool ActivateContextSubmenu(float x, float y)
    {
        if (contextSubmenu?.Children is not { } children || !contextSubmenuBounds.Contains(x, y)) return false;
        for (int i = contextSubmenuFirst; i < Math.Min(children.Length, contextSubmenuFirst + ContextSubmenuRows); i++)
            if (ContextSubmenuRow(i).Contains(x, y) && children[i].Enabled)
            {
                var action = children[i].Action;
                contextSubmenu = null; contextItems.Clear(); action(); return true;
            }
        return true;
    }
}
