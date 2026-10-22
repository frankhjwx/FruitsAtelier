using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal enum DockPanel { MovementStrain, Preview, PatternEditor, Notes, SongSetup, RandomizeDroplets, SliderInspector, Aimod, CollaborationMapping, PatternGallery }
    private sealed class DockGroup(DockPanel panel, float width)
    {
        public readonly List<DockPanel> Panels = [panel];
        public DockPanel Active = panel;
        public bool Collapsed = true;
        public float Width = width;
        public float TabScroll, TabViewportWidth, TabScrollMaximum;
        public DockPanel? Revealed;
    }
    private sealed class DockWorkspace
    {
        public readonly DockGroup Left = new(DockPanel.MovementStrain, DifficultyCurveWidth);
        public readonly DockGroup Right = CreateRightDock();
        private static DockGroup CreateRightDock()
        { var group = new DockGroup(DockPanel.Preview, 360); group.Panels.AddRange([DockPanel.Notes, DockPanel.SongSetup, DockPanel.PatternGallery]); return group; }
        public (float X, float Y)? MovementPosition;
        public int PreviewMod, PreviewDisplayMode;
    }
    // Each workspace owns its layout so switching views cannot overwrite the other view's groups.
    private readonly DockWorkspace mappingDockWorkspace = new(), timingDockWorkspace = new();
    private DockWorkspace DockWorkspaceState => TimingPageVisible ? timingDockWorkspace : mappingDockWorkspace;
    private DockGroup PanelGroup(DockPanel panel) => DockWorkspaceState.Left.Panels.Contains(panel)
        ? DockWorkspaceState.Left : DockWorkspaceState.Right;
    private bool DockContentInputAvailable => !GalleryTagsVisible && !PatternNameVisible && !ErrorVisible && !DiscardConfirmationVisible && !LibraryVisible
        && !ExportVisible && !SongSetupVisible && !SliderDialogVisible && !TimeJumpVisible && !TimingModal
        && !BananaEditVisible && !StreamDialogVisible && !MergeDialogVisible && !VolumeDialogVisible
        && !DistanceSnapDialogVisible && !HitsoundCopierVisible && !VersionHistoryVisible
        && !IsTestplaying && !SynchronizationBlocksInput && !SynchronizationWaitVisible;
    private bool PanelVisible(DockPanel panel) => !TimingPageVisible && DockPanelExists(panel) && !PanelGroup(panel).Collapsed
        && PanelGroup(panel).Active == panel;
    private Rect DockBounds(bool left) => new(left ? leftPanel.X : rightPanel.X, canvas.Y + 38,
        left ? leftPanel.Width : rightPanel.Width, Math.Max(0, canvas.Height - 38));
    private Rect PanelBounds(DockPanel panel) => DockBounds(ReferenceEquals(PanelGroup(panel), DockWorkspaceState.Left));
    private readonly List<(DockPanel Panel, Rect Bounds)> dockTabs = [];
    private readonly List<(DockPanel Panel, Rect Bounds)> dockAllTabs = [];
    private DockPanel? dockPressed;
    private bool dockDragging;
    private bool drawingDockContent;
    private float dockStartX, dockStartY;
    private Rect leftDockResizeBounds, rightDockResizeBounds;
    private bool dockResizeLeft;
    private int dockReorderIndex;
    private readonly List<(DockPanel Panel, Rect Bounds)> dockOriginalTabs = [];
    private const float MinimumDockWidth = 180;
    private float AvailableDockWidth => Math.Max(24, width - 109 - (MinimumPlayfieldWidth + 198));
    private float MaximumDockWidth => Math.Max(MinimumDockWidth, width * .45f);
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
    private bool WheelDockTabs(float x, float y, float delta)
    {
        if (!DockContentInputAvailable || TimingPageVisible || dockDragging) return false;
        return Scroll(DockWorkspaceState.Left, true) || Scroll(DockWorkspaceState.Right, false);

        bool Scroll(DockGroup group, bool left)
        {
            if (group.Collapsed || group.Panels.Count == 0) return false;
            var bounds = DockBounds(left);
            if (!new Rect(bounds.X, bounds.Y, bounds.Width, 38).Contains(x, y)) return false;
            group.TabScroll = Math.Clamp(group.TabScroll - delta / 120 * 48, 0, group.TabScrollMaximum);
            return true;
        }
    }
    private static string DockTitle(DockPanel panel) => L.Get(panel switch
    {
        DockPanel.PatternGallery => "dock.gallery", DockPanel.Aimod => "aimod.title", DockPanel.Preview => "dock.preview", DockPanel.PatternEditor => "dock.patternEditor",
        DockPanel.Notes => "dock.notes", DockPanel.SongSetup => "song.title", DockPanel.CollaborationMapping => "collab.title",
        DockPanel.RandomizeDroplets => "randomize.title", DockPanel.SliderInspector => "inspector.title", _ => "difficulty.curve.title"
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
        if (panel == DockPanel.Aimod) RefreshAimod();
        if (panel == DockPanel.PatternGallery) EnsurePatternGalleryLoaded();
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
        state.Right.Panels.Add(DockPanel.PatternGallery);
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
        contextItems.Add(new(L.Get("dock.panels"), () => { }, Enabled: false, Heading: true));
        foreach (var panel in Enum.GetValues<DockPanel>())
        {
            bool exists = DockPanelExists(panel);
            contextItems.Add(new((exists ? "✓ " : "") + DockTitle(panel), () =>
            {
                if (DockPanelExists(panel)) RemoveDockPanel(panel);
                else AddDockPanel(panel, left);
            }, Active: exists));
        }
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 282)),
            Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 282, ContextMenuHeight);
        return true;
    }

    private void LayoutDockPanels()
    {
        var state = DockWorkspaceState;
        float available = AvailableDockWidth;
        bool leftOpen = !state.Left.Collapsed && state.Left.Panels.Count > 0;
        bool rightOpen = TimingPageVisible || !state.Right.Collapsed && state.Right.Panels.Count > 0;
        float leftWidth = leftOpen ? Math.Clamp(state.Left.Width, MinimumDockWidth, MaximumDockWidth) : 24;
        float rightWidth = rightOpen ? TimingPageVisible
            ? Math.Min(Math.Max(MinimumPreviewWidth, state.Right.Width), Math.Max(0, available - leftWidth))
            : Math.Clamp(state.Right.Width, MinimumDockWidth, MaximumDockWidth) : 0;
        if (!TimingPageVisible && leftWidth + rightWidth > available)
        {
            if (dockResizeLeft)
            {
                rightWidth = rightOpen ? Math.Max(MinimumDockWidth, available - leftWidth) : 0;
                leftWidth = leftOpen ? Math.Max(MinimumDockWidth, Math.Min(leftWidth, available - rightWidth)) : 24;
            }
            else
            {
                leftWidth = leftOpen ? Math.Max(MinimumDockWidth, available - rightWidth) : 24;
                rightWidth = rightOpen ? Math.Max(MinimumDockWidth, Math.Min(rightWidth, available - leftWidth)) : 0;
            }
        }
        float bodyHeight = Math.Max(180, TransportTop - 84);
        leftPanel = new(0, 84, leftWidth, bodyHeight);
        rightPanel = new(width - (rightOpen ? rightWidth : 340), 84, rightOpen ? rightWidth : 340, rightOpen ? bodyHeight : 38);
        canvas = new(108 + leftWidth, 84, Math.Max(120, width - rightWidth - leftWidth - 109), bodyHeight);
        dockTabs.Clear(); dockAllTabs.Clear();
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
                var panels = DisplayDockPanels(group);
                float total = panels.Sum(p => c.MeasureText(DockTitle(p), 12, true) + 24);
                bool overflow = total > bounds.Width - 12;
                float arrows = overflow ? 24 : 0;
                var strip = new Rect(bounds.X + 6 + arrows, bounds.Y + 6, bounds.Width - 12 - arrows * 2, 32);
                group.TabScrollMaximum = Math.Max(0, total - strip.Width);
                group.TabScroll = Math.Clamp(group.TabScroll, 0, Math.Max(0, total - strip.Width));
                if ((group.Revealed != group.Active || group.TabViewportWidth != strip.Width) && !dockDragging)
                {
                    float start = panels.TakeWhile(p => p != group.Active).Sum(p => c.MeasureText(DockTitle(p), 12, true) + 24);
                    float end = start + c.MeasureText(DockTitle(group.Active), 12, true) + 24;
                    group.TabScroll = Math.Clamp(start < group.TabScroll ? start : end > group.TabScroll + strip.Width ? end - strip.Width : group.TabScroll, 0, Math.Max(0, total - strip.Width));
                    group.Revealed = group.Active; group.TabViewportWidth = strip.Width;
                }
                c.Clip(strip);
                float x = strip.X - group.TabScroll;
                float available = Math.Max(0, bounds.Width - 12);
                foreach (var panel in panels)
                {
                    string title = DockTitle(panel);
                    float tabWidth = c.MeasureText(title, 12, true) + 24;
                    var tab = new Rect(x, bounds.Y + 6, tabWidth, 32);
                    dockAllTabs.Add((panel, tab));
                    float clippedX = Math.Max(tab.X, strip.X), clippedRight = Math.Min(tab.Right, strip.Right);
                    if (clippedRight > clippedX) dockTabs.Add((panel, new(clippedX, tab.Y, clippedRight - clippedX, tab.Height)));
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
                c.Unclip();
                if (overflow)
                {
                    Button(c, new(bounds.X + 2, bounds.Y + 6, 24, 32), "‹", () => group.TabScroll = Math.Max(0, group.TabScroll - strip.Width * .7f), enabled: group.TabScroll > 0);
                    Button(c, new(bounds.Right - 26, bounds.Y + 6, 24, 32), "›", () => group.TabScroll = Math.Min(total - strip.Width, group.TabScroll + strip.Width * .7f), enabled: group.TabScroll < total - strip.Width);
                }
                c.Line(bounds.X, bounds.Y + 38, bounds.Right, bounds.Y + 38, Grid);
                drawingDockContent = true;
                try
                {
                    if (group.Active == DockPanel.MovementStrain)
                    {
                        DifficultyCurveGraphBounds = new(bounds.X + 18, bounds.Y + 58,
                            Math.Max(0, bounds.Width - 40), Math.Max(0, bounds.Height - 78));
                        DrawDifficultyCurve(c, DifficultyCurveGraphBounds);
                    }
                    else if (group.Active == DockPanel.PatternGallery)
                        DrawPatternGallery(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                    else if (group.Active == DockPanel.PatternEditor)
                        DrawPatternDock(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                    else if (group.Active == DockPanel.RandomizeDroplets)
                        DrawRandomDock(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                    else if (group.Active == DockPanel.SliderInspector)
                        DrawSliderInspector(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                    else if (group.Active == DockPanel.SongSetup)
                        DrawSongDock(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                    else if (group.Active == DockPanel.CollaborationMapping)
                        DrawCollaborationPanel(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                    else if (group.Active == DockPanel.Aimod)
                        DrawAimodPanel(c, new(bounds.X + 8, bounds.Y + 44, Math.Max(0, bounds.Width - 16), Math.Max(0, bounds.Height - 50)));
                    else if (group.Active == DockPanel.Notes)
                        DrawNotesDock(c, new(bounds.X + 12, bounds.Y + 44, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 50)));
                    else
                    {
                        DrawPreview(c, new(bounds.X + 16, bounds.Y + 38, Math.Max(0, bounds.Width - 32), Math.Max(0, bounds.Height - 52)));
                    }
                    }
                finally { drawingDockContent = false; }
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
            if (!CommitCollaborationField()) return true;
            if (editField >= 0 && !CommitField()) return true;
            dockPressed = tab.Panel; dockDragging = false; dockStartX = x; dockStartY = y;
            dockOriginalTabs.Clear();
            dockOriginalTabs.AddRange(dockAllTabs.Where(t => PanelGroup(tab.Panel).Panels.Contains(t.Panel)));
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
        var other = dockResizeLeft ? DockWorkspaceState.Right : DockWorkspaceState.Left;
        bool otherOpen = !other.Collapsed && other.Panels.Count > 0;
        float otherMinimum = otherOpen ? MinimumDockWidth : dockResizeLeft ? 0 : 24;
        float maximum = Math.Max(MinimumDockWidth, Math.Min(MaximumDockWidth, AvailableDockWidth - otherMinimum));
        group.Width = Math.Clamp(dockResizeLeft ? x : width - x, MinimumDockWidth, maximum);
        if (otherOpen) other.Width = Math.Max(MinimumDockWidth, Math.Min(other.Width, AvailableDockWidth - group.Width));
    }
}
