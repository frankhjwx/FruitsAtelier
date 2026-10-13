using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal enum DockPanel { MovementStrain, Preview }
    private sealed class DockGroup(DockPanel panel, float width)
    {
        public readonly List<DockPanel> Panels = [panel];
        public DockPanel Active = panel;
        public bool Collapsed = true;
        public float Width = width;
    }
    private sealed class DockWorkspace
    {
        public readonly DockGroup Left = new(DockPanel.MovementStrain, DifficultyCurveWidth);
        public readonly DockGroup Right = new(DockPanel.Preview, 360);
        public (float X, float Y)? MovementPosition;
        public int PreviewMod, PreviewDisplayMode;
    }
    // Each workspace owns its layout so switching views cannot overwrite the other view's groups.
    private readonly DockWorkspace mappingDockWorkspace = new(), timingDockWorkspace = new();
    private DockWorkspace DockWorkspaceState => TimingPageVisible ? timingDockWorkspace : mappingDockWorkspace;
    private DockGroup PanelGroup(DockPanel panel) => DockWorkspaceState.Left.Panels.Contains(panel)
        ? DockWorkspaceState.Left : DockWorkspaceState.Right;
    private bool PanelVisible(DockPanel panel) => !TimingPageVisible && !PanelGroup(panel).Collapsed
        && PanelGroup(panel).Active == panel;
    private Rect DockBounds(bool left) => new(left ? leftPanel.X : rightPanel.X, canvas.Y + 38,
        left ? leftPanel.Width : rightPanel.Width, Math.Max(0, canvas.Height - 38));
    private Rect PanelBounds(DockPanel panel) => DockBounds(ReferenceEquals(PanelGroup(panel), DockWorkspaceState.Left));
    private readonly List<(DockPanel Panel, Rect Bounds)> dockTabs = [];
    private DockPanel? dockPressed;
    private bool dockDragging;
    private float dockStartX, dockStartY;
    internal Rect DockTabBounds(DockPanel panel) => dockTabs.FirstOrDefault(t => t.Panel == panel).Bounds;
    private static string DockTitle(DockPanel panel) => L.Get(panel == DockPanel.Preview ? "dock.preview" : "difficulty.curve.title");

    private void LayoutDockPanels()
    {
        const float minimumCanvasWidth = MinimumPlayfieldWidth + 198;
        var state = DockWorkspaceState;
        float available = Math.Max(24, width - 109 - minimumCanvasWidth);
        bool leftOpen = !state.Left.Collapsed && state.Left.Panels.Count > 0;
        bool rightOpen = TimingPageVisible || !state.Right.Collapsed && state.Right.Panels.Count > 0;
        float leftWidth = leftOpen ? Math.Min(state.Left.Width, available) : 24;
        float rightWidth = rightOpen ? Math.Min(Math.Max(MinimumPreviewWidth, state.Right.Width), Math.Max(0, available - leftWidth)) : 0;
        float bodyHeight = Math.Max(180, height - 204);
        leftPanel = new(0, 84, leftWidth, bodyHeight);
        rightPanel = new(width - (rightOpen ? rightWidth : 340), 84, rightOpen ? rightWidth : 340, rightOpen ? bodyHeight : 38);
        canvas = new(108 + leftWidth, 84, Math.Max(120, width - rightWidth - leftWidth - 109), bodyHeight);
        dockTabs.Clear();
        DifficultyCurveToggleBounds = DifficultyCurveGraphBounds = PreviewToggleBounds = PreviewResizeBounds = PreviewViewport = default;
        MovementOverlayBounds = null;
    }

    private void DrawDockPanels(ICanvas c)
    {
        DrawGroup(true, DockWorkspaceState.Left);
        DrawGroup(false, DockWorkspaceState.Right);
        void DrawGroup(bool left, DockGroup group)
        {
            if (group.Panels.Count == 0) return;
            var bounds = DockBounds(left);
            var toggle = new Rect(left ? leftPanel.Right - 22 : (group.Collapsed ? width : rightPanel.X) - 22,
                canvas.Y + canvas.Height / 2 - 22, 22, 44);
            foreach (var panel in group.Panels)
                if (panel == DockPanel.MovementStrain) DifficultyCurveToggleBounds = toggle; else PreviewToggleBounds = toggle;
            if (!group.Collapsed && bounds.Width > 0)
            {
                c.Fill(bounds, Panel);
                c.Clip(bounds);
                c.Fill(new(bounds.X, bounds.Y, bounds.Width, 38), Background);
                float x = bounds.X + 6;
                float available = Math.Max(0, bounds.Width - 12);
                foreach (var panel in group.Panels)
                {
                    string title = DockTitle(panel);
                    float tabWidth = Math.Min(c.MeasureText(title, 12, true) + 24, available / group.Panels.Count);
                    var tab = new Rect(x, bounds.Y + 6, tabWidth, 32);
                    dockTabs.Add((panel, tab));
                    bool selected = group.Active == panel;
                    if (selected || tab.Contains(mouseX, mouseY))
                    {
                        c.Fill(tab, selected ? Panel : Surface, 8);
                        c.Fill(new(tab.X, tab.Bottom - 8, tab.Width, 8), selected ? Panel : Surface);
                    }
                    c.Text(title, tab.X + 10, tab.Y + 8, 12, selected ? Foreground : Muted, Math.Max(0, tab.Width - 20), selected);
                    if (selected) c.Line(tab.X + 10, tab.Bottom - 1, tab.Right - 10, tab.Bottom - 1, Accent, 2);
                    x += tabWidth;
                }
                c.Line(bounds.X, bounds.Y + 38, bounds.Right, bounds.Y + 38, Grid);
                if (group.Active == DockPanel.MovementStrain)
                {
                    DifficultyCurveGraphBounds = new(bounds.X + 18, bounds.Y + 58,
                        Math.Max(0, bounds.Width - 40), Math.Max(0, bounds.Height - 78));
                    DrawDifficultyCurve(c, DifficultyCurveGraphBounds);
                }
                else
                {
                    DrawPreview(c, new(bounds.X + 16, bounds.Y + 38, Math.Max(0, bounds.Width - 32), Math.Max(0, bounds.Height - 52)));
                }
                c.Unclip();
                if (group.Panels.Contains(DockPanel.Preview))
                {
                    float edge = left ? bounds.Right : bounds.X;
                    PreviewResizeBounds = new(edge - 4, Math.Max(122, bounds.Y), 8, bounds.Bottom - Math.Max(122, bounds.Y));
                    c.Fill(new(edge - 1, PreviewResizeBounds.Y, 2, PreviewResizeBounds.Height),
                        PreviewResizeBounds.Contains(mouseX, mouseY) || drag == DragKind.PreviewResize ? Accent : Grid);
                }
            }
            c.Fill(toggle, Surface, 5);
            Button(c, toggle, left == group.Collapsed ? "›" : "‹", () => group.Collapsed = !group.Collapsed);
        }
    }

    private bool BeginDockPointer(float x, float y, int button)
    {
        if (TimingPageVisible || menu >= 0 || contextItems.Count > 0 || button != 0) return false;
        foreach (var tab in dockTabs)
        {
            if (!tab.Bounds.Contains(x, y)) continue;
            if (editField >= 0 && !CommitField()) return true;
            dockPressed = tab.Panel; dockDragging = false; dockStartX = x; dockStartY = y;
            return true;
        }
        return false;
    }

    private void MoveDockPointer(float x, float y)
    {
        mouseX = x; mouseY = y;
        if (Math.Abs(x - dockStartX) >= 4 || Math.Abs(y - dockStartY) >= 4) dockDragging = true;
    }

    private bool? DockDropSide(float x, float y)
    {
        if (TimingPageVisible || y < 122 || y >= canvas.Bottom) return null;
        if (x >= 0 && x < Math.Max(32, leftPanel.Right)) return true;
        var right = DockWorkspaceState.Right;
        if (x >= (!right.Collapsed && right.Panels.Count > 0 ? rightPanel.X : width - 32) && x < width) return false;
        return null;
    }

    private void EndDockPointer(float x, float y)
    {
        MoveDockPointer(x, y);
        if (dockPressed is { } panel && !TimingPageVisible)
        {
            var source = PanelGroup(panel);
            if (!dockDragging) source.Active = panel;
            else if (DockDropSide(x, y) is { } left)
            {
                var destination = left ? DockWorkspaceState.Left : DockWorkspaceState.Right;
                var before = dockTabs.FirstOrDefault(t => t.Bounds.Contains(x, y));
                int originalIndex = source.Panels.IndexOf(panel);
                source.Panels.Remove(panel);
                if (source.Panels.Count > 0 && source.Active == panel) source.Active = source.Panels[0];
                int index = before.Bounds.Width > 0
                    ? before.Panel == panel && ReferenceEquals(source, destination) ? originalIndex : destination.Panels.IndexOf(before.Panel)
                    : -1;
                if (index >= 0) destination.Panels.Insert(index, panel); else destination.Panels.Add(panel);
                destination.Active = panel; destination.Collapsed = false;
                if (panel == DockPanel.Preview) destination.Width = Math.Max(MinimumPreviewWidth, destination.Width);
            }
        }
        dockPressed = null; dockDragging = false;
    }

    private void DrawDockDrag(ICanvas c)
    {
        if (!dockDragging || dockPressed is not { } panel || TimingPageVisible) return;
        foreach (bool left in new[] { true, false })
        {
            var group = left ? DockWorkspaceState.Left : DockWorkspaceState.Right;
            var bounds = group.Collapsed || group.Panels.Count == 0
                ? new Rect(left ? 0 : width - 32, 122, 32, canvas.Bottom - 122) : DockBounds(left);
            bool target = DockDropSide(mouseX, mouseY) == left;
            c.Fill(bounds, Accent, opacity: target ? .2f : .07f);
            c.Stroke(bounds, Accent, target ? 2 : 1);
        }
        string title = DockTitle(panel);
        float w = c.MeasureText(title, 12, true) + 28;
        var ghost = new Rect(Math.Clamp(mouseX + 12, 0, Math.Max(0, width - w)), Math.Clamp(mouseY + 16, 0, Math.Max(0, height - 34)), w, 34);
        c.Fill(ghost, Surface, 6); c.Stroke(ghost, Accent, radius: 6);
        c.Text(title, ghost.X + 14, ghost.Y + 9, 12, Foreground, w - 28, true);
    }

    private void ResizePreviewDock(float x)
    {
        var group = PanelGroup(DockPanel.Preview);
        bool left = ReferenceEquals(group, DockWorkspaceState.Left);
        float otherWidth = left ? Math.Max(0, width - canvas.Right - 1) : leftPanel.Width;
        group.Width = Math.Clamp(left ? x : width - x, MinimumPreviewWidth,
            Math.Max(MinimumPreviewWidth, width - 109 - otherWidth - (MinimumPlayfieldWidth + 198)));
    }
}
