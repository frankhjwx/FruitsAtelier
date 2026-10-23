using System.Text.Json;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string savedDockLayout = "";
    internal EditorDockLayouts CaptureDockLayouts() => new()
    {
        Version = 2, Mapping = Capture(mappingDockWorkspace), Timing = Capture(timingDockWorkspace)
    };
    private static DockWorkspaceLayout Capture(DockWorkspace workspace) => new()
    {
        Left = Capture(workspace.Left), Right = Capture(workspace.Right),
        PreviewMod = workspace.PreviewMod, PreviewDisplayMode = workspace.PreviewDisplayMode,
        MovementX = workspace.MovementPosition?.X, MovementY = workspace.MovementPosition?.Y
    };
    private static DockGroupLayout Capture(DockGroup group) => new()
    {
        Panels = group.Panels.Select(p => p.ToString()).ToArray(), Active = group.Active.ToString(),
        Width = group.Width, Collapsed = group.Collapsed
    };
    private void SaveDockLayoutIfChanged()
    {
        // Persist completed gestures, never intermediate splitter or tab drag positions.
        if (dockPressed is not null || drag != DragKind.None || movementPanelPointer) return;
        var layout = CaptureDockLayouts();
        string serialized = JsonSerializer.Serialize(layout);
        if (serialized == savedDockLayout) return;
        savedDockLayout = serialized; LibrarySettings.DockLayouts = layout;
        RequestViewPreference?.Invoke();
    }
    internal void RestoreDockLayouts()
    {
        if (LibrarySettings.DockLayouts is { Version: 1 or 2 } layouts)
        {
            Restore(mappingDockWorkspace, layouts.Mapping);
            Restore(timingDockWorkspace, layouts.Timing);
            if (layouts.Version == 1 && !mappingDockWorkspace.Left.Panels.Contains(DockPanel.OsuWeb)
                && !mappingDockWorkspace.Right.Panels.Contains(DockPanel.OsuWeb))
                mappingDockWorkspace.Right.Panels.Add(DockPanel.OsuWeb);
        }
        savedDockLayout = JsonSerializer.Serialize(CaptureDockLayouts());
        RefreshLoadedAimod();
    }
    private static void Restore(DockWorkspace workspace, DockWorkspaceLayout? layout)
    {
        if (layout is null) return;
        var seen = new HashSet<DockPanel>();
        RestoreGroup(workspace.Left, layout.Left);
        RestoreGroup(workspace.Right, layout.Right);
        workspace.PreviewMod = Math.Clamp(layout.PreviewMod, 0, 2);
        workspace.PreviewDisplayMode = Math.Clamp(layout.PreviewDisplayMode, 0, 2);
        workspace.MovementPosition = layout.MovementX is { } x && float.IsFinite(x)
            && layout.MovementY is { } y && float.IsFinite(y) ? (x, y) : null;
        void RestoreGroup(DockGroup group, DockGroupLayout? saved)
        {
            var names = saved?.Panels ?? group.Panels.Select(p => p.ToString()).ToArray();
            group.Panels.Clear();
            foreach (string name in names)
                if (Enum.TryParse<DockPanel>(name, out var panel) && Enum.IsDefined(panel)
                    && panel.ToString() == name && seen.Add(panel)) group.Panels.Add(panel);
            if (saved is not null)
            {
                group.Collapsed = saved.Collapsed;
                if (float.IsFinite(saved.Width) && saved.Width >= MinimumDockWidth) group.Width = Math.Min(saved.Width, 10000);
            }
            group.Active = Enum.TryParse<DockPanel>(saved?.Active, out var active) && group.Panels.Contains(active)
                ? active : group.Panels.FirstOrDefault();
            group.TabScroll = 0; group.Revealed = null;
        }
    }
}
