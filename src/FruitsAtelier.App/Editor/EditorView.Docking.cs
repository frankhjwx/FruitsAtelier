using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal enum DockPanel { MovementStrain, Preview, PatternEditor, Notes, SongSetup }
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
        public readonly DockGroup Right = CreateRightDock();
        private static DockGroup CreateRightDock()
        { var group = new DockGroup(DockPanel.Preview, 360); group.Panels.AddRange([DockPanel.Notes, DockPanel.SongSetup]); return group; }
        public (float X, float Y)? MovementPosition;
        public int PreviewMod, PreviewDisplayMode;
    }
    // Each workspace owns its layout so switching views cannot overwrite the other view's groups.
    private readonly DockWorkspace mappingDockWorkspace = new(), timingDockWorkspace = new();
    private DockWorkspace DockWorkspaceState => TimingPageVisible ? timingDockWorkspace : mappingDockWorkspace;
    private DockGroup PanelGroup(DockPanel panel) => DockWorkspaceState.Left.Panels.Contains(panel)
        ? DockWorkspaceState.Left : DockWorkspaceState.Right;
    private bool DockContentInputAvailable => !ErrorVisible && !DiscardConfirmationVisible && !LibraryVisible
        && !ExportVisible && !SongSetupVisible && !SliderDialogVisible && !TimeJumpVisible && !TimingModal
        && !BananaEditVisible && !StreamDialogVisible && !MergeDialogVisible && !VolumeDialogVisible
        && !DistanceSnapDialogVisible && !AimodVisible && !HitsoundCopierVisible && !VersionHistoryVisible
        && !IsTestplaying && !SynchronizationBlocksInput && !SynchronizationWaitVisible;
    private bool PanelVisible(DockPanel panel) => !TimingPageVisible && DockPanelExists(panel) && !PanelGroup(panel).Collapsed
        && PanelGroup(panel).Active == panel;
    private Rect DockBounds(bool left) => new(left ? leftPanel.X : rightPanel.X, canvas.Y + 38,
        left ? leftPanel.Width : rightPanel.Width, Math.Max(0, canvas.Height - 38));
    private Rect PanelBounds(DockPanel panel) => DockBounds(ReferenceEquals(PanelGroup(panel), DockWorkspaceState.Left));
    private readonly List<(DockPanel Panel, Rect Bounds)> dockTabs = [];
    private DockPanel? dockPressed;
    private bool dockDragging;
    private float dockStartX, dockStartY;
    private Rect leftDockResizeBounds, rightDockResizeBounds;
    private bool dockResizeLeft;
    private int dockReorderIndex;
    private readonly List<(DockPanel Panel, Rect Bounds)> dockOriginalTabs = [];
    private const float MinimumDockWidth = 180;
    private float MaximumDockWidth => Math.Max(MinimumDockWidth, (width - 109 - (MinimumPlayfieldWidth + 198)) / 2);
    internal Rect DockResizeBounds(bool left) => left ? leftDockResizeBounds : rightDockResizeBounds;
    private bool DockReordering => dockDragging && dockPressed is { } panel
        && DockDropSide(mouseX, mouseY) is { } left
        && ReferenceEquals(PanelGroup(panel), left ? DockWorkspaceState.Left : DockWorkspaceState.Right);
    private List<DockPanel> DisplayDockPanels(DockGroup group)
    {
        if (!DockReordering || dockPressed is not { } panel || !group.Panels.Contains(panel)) return group.Panels;
        var panels = new List<DockPanel>(group.Panels);
        panels.Remove(panel);
        panels.Insert(Math.Clamp(dockReorderIndex, 0, panels.Count), panel);
        return panels;
    }
    internal Rect DockTabBounds(DockPanel panel) => dockTabs.FirstOrDefault(t => t.Panel == panel).Bounds;
    private static string DockTitle(DockPanel panel) => L.Get(panel switch
    {
        DockPanel.Preview => "dock.preview", DockPanel.PatternEditor => "dock.patternEditor",
        DockPanel.Notes => "dock.notes", DockPanel.SongSetup => "song.title", _ => "difficulty.curve.title"
    });

    private bool DockPanelExists(DockPanel panel) => DockWorkspaceState.Left.Panels.Contains(panel)
        || DockWorkspaceState.Right.Panels.Contains(panel);
    private void RemoveDockPanel(DockPanel panel)
    {
        if (!DockPanelExists(panel)) return;
        var group = PanelGroup(panel);
        group.Panels.Remove(panel);
        if (group.Active == panel && group.Panels.Count > 0) group.Active = group.Panels[0];
    }
    private void AddDockPanel(DockPanel panel, bool left)
    {
        if (TimingPageVisible || DockPanelExists(panel)) return;
        var group = left ? DockWorkspaceState.Left : DockWorkspaceState.Right;
        group.Panels.Add(panel); group.Active = panel; group.Collapsed = false;
    }
    private void ResetDockLayout()
    {
        if (TimingPageVisible) return;
        var state = DockWorkspaceState;
        state.Left.Panels.Clear(); state.Right.Panels.Clear();
        state.Left.Width = DifficultyCurveWidth; state.Right.Width = 360;
        AddDockPanel(DockPanel.MovementStrain, true); AddDockPanel(DockPanel.Preview, false);
        state.Right.Panels.Add(DockPanel.Notes);
        state.Right.Panels.Add(DockPanel.SongSetup);
    }
    private bool DockTabContext(float x, float y)
    {
        if (TimingPageVisible) return false;
        var tab = dockTabs.FirstOrDefault(t => t.Bounds.Contains(x, y));
        if (tab.Bounds.Width <= 0) return false;
        if (editField >= 0 && !CommitField()) return true;
        bool left = ReferenceEquals(PanelGroup(tab.Panel), DockWorkspaceState.Left);
        contextItems.Clear(); menu = -1;
        contextItems.Add(new(L.Get("dock.closeTab"), () => RemoveDockPanel(tab.Panel)));
        AddContextSeparator();
        foreach (var panel in Enum.GetValues<DockPanel>())
        {
            bool exists = DockPanelExists(panel);
            contextItems.Add(new((exists ? "✓ " : "") + L.Get("dock.addTab", DockTitle(panel)),
                () => AddDockPanel(panel, left), !exists));
        }
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 282)),
            Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 282, ContextMenuHeight);
        return true;
    }

    private void LayoutDockPanels()
    {
        const float minimumCanvasWidth = MinimumPlayfieldWidth + 198;
        var state = DockWorkspaceState;
        float available = Math.Max(24, width - 109 - minimumCanvasWidth);
        bool leftOpen = !state.Left.Collapsed && state.Left.Panels.Count > 0;
        bool rightOpen = TimingPageVisible || !state.Right.Collapsed && state.Right.Panels.Count > 0;
        float leftWidth = leftOpen ? Math.Clamp(state.Left.Width, MinimumDockWidth, MaximumDockWidth) : 24;
        float rightWidth = rightOpen ? TimingPageVisible
            ? Math.Min(Math.Max(MinimumPreviewWidth, state.Right.Width), Math.Max(0, available - leftWidth))
            : Math.Clamp(state.Right.Width, MinimumDockWidth, MaximumDockWidth) : 0;
        float bodyHeight = Math.Max(180, height - 204);
        leftPanel = new(0, 84, leftWidth, bodyHeight);
        rightPanel = new(width - (rightOpen ? rightWidth : 340), 84, rightOpen ? rightWidth : 340, rightOpen ? bodyHeight : 38);
        canvas = new(108 + leftWidth, 84, Math.Max(120, width - rightWidth - leftWidth - 109), bodyHeight);
        dockTabs.Clear();
        leftDockResizeBounds = rightDockResizeBounds = default;
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
                if (panel == DockPanel.MovementStrain) DifficultyCurveToggleBounds = toggle;
                else if (panel == DockPanel.Preview) PreviewToggleBounds = toggle;
            if (!group.Collapsed && bounds.Width > 0)
            {
                c.Fill(bounds, Panel);
                c.Clip(bounds);
                c.Fill(new(bounds.X, bounds.Y, bounds.Width, 38), Background);
                float x = bounds.X + 6;
                float available = Math.Max(0, bounds.Width - 12);
                foreach (var panel in DisplayDockPanels(group))
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
                else if (group.Active == DockPanel.PatternEditor)
                    DrawPatternDock(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                else if (group.Active == DockPanel.SongSetup)
                    DrawSongDock(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                else if (group.Active == DockPanel.Notes)
                    DrawNotesDock(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                else
                {
                    DrawPreview(c, new(bounds.X + 16, bounds.Y + 38, Math.Max(0, bounds.Width - 32), Math.Max(0, bounds.Height - 52)));
                }
                c.Unclip();
                float edge = left ? bounds.Right : bounds.X;
                var resize = new Rect(edge - 4, bounds.Y, 8, bounds.Height);
                if (left) leftDockResizeBounds = resize; else rightDockResizeBounds = resize;
                if (group.Panels.Contains(DockPanel.Preview)) PreviewResizeBounds = resize;
                c.Fill(new(edge - 1, resize.Y, 2, resize.Height),
                    resize.Contains(mouseX, mouseY) || drag == DragKind.PreviewResize && dockResizeLeft == left ? Accent : Grid);
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
            dockOriginalTabs.Clear();
            dockOriginalTabs.AddRange(dockTabs.Where(t => PanelGroup(tab.Panel).Panels.Contains(t.Panel)));
            dockReorderIndex = PanelGroup(tab.Panel).Panels.IndexOf(tab.Panel);
            return true;
        }
        return false;
    }

    private void MoveDockPointer(float x, float y)
    {
        mouseX = x; mouseY = y;
        if (Math.Abs(x - dockStartX) >= 4 || Math.Abs(y - dockStartY) >= 4) dockDragging = true;
        if (DockReordering)
            dockReorderIndex = dockOriginalTabs.Count(t => t.Panel != dockPressed && x > t.Bounds.X + t.Bounds.Width / 2);
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
                source.Panels.Remove(panel);
                if (source.Panels.Count > 0 && source.Active == panel) source.Active = source.Panels[0];
                int index = ReferenceEquals(source, destination) ? dockReorderIndex : before.Bounds.Width > 0
                    ? destination.Panels.IndexOf(before.Panel)
                    : -1;
                if (index >= 0) destination.Panels.Insert(index, panel); else destination.Panels.Add(panel);
                destination.Active = panel; destination.Collapsed = false;
            }
        }
        dockPressed = null; dockDragging = false;
    }

    private void DrawDockDrag(ICanvas c)
    {
        if (!dockDragging || dockPressed is not { } panel || TimingPageVisible) return;
        if (DockReordering) return;
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
        var group = dockResizeLeft ? DockWorkspaceState.Left : DockWorkspaceState.Right;
        group.Width = Math.Clamp(dockResizeLeft ? x : width - x, MinimumDockWidth, MaximumDockWidth);
    }
}
