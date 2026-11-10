using System.Text.Json;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string savedDockLayout = "";
    internal EditorDockLayouts CaptureDockLayouts() => new()
    {
        Version = 3, Mapping = Capture(mappingDockWorkspace), Timing = Capture(timingDockWorkspace), Hitsounding = Capture(hitsoundingDockWorkspace)
    };
    private static DockWorkspaceLayout Capture(DockWorkspace workspace) => new()
    {
        Left = Capture(workspace.Left), Right = Capture(workspace.Right),
        PreviewMod = workspace.PreviewMod, PreviewDisplayMode = workspace.PreviewDisplayMode,
        MovementX = workspace.MovementPosition?.X, MovementY = workspace.MovementPosition?.Y,
        MovementScale = workspace.MovementScale, PreciseHitsounds = workspace.PreciseHitsounds, ShowHitsoundText = workspace.ShowHitsoundText
    };
    private static DockGroupLayout Capture(DockGroup group) => new()
    {
        Panels = group.Panels.Select(p => p.ToString()).ToArray(), Active = group.Active.ToString(),
        Width = group.Width, Collapsed = group.Collapsed, Split = group.Split
    };
    private void SaveDockLayoutIfChanged()
    {
        // Persist completed gestures, never intermediate splitter or tab drag positions.
        if (hitsoundingSplitDrag is not null || dockPressed is not null || drag != DragKind.None || movementPanelPointer) return;
        var layout = CaptureDockLayouts();
        string serialized = JsonSerializer.Serialize(layout);
        if (serialized == savedDockLayout) return;
        savedDockLayout = serialized; LibrarySettings.DockLayouts = layout;
        RequestViewPreference?.Invoke();
    }
    internal void RestoreDockLayouts()
    {
        if (LibrarySettings.DockLayouts is { Version: 1 or 2 or 3 } layouts)
        {
            Restore(mappingDockWorkspace, layouts.Mapping);
            if (layouts.Version < 3)
            {
                if (!mappingDockWorkspace.Left.Panels.Contains(DockPanel.Testplay) && !mappingDockWorkspace.Right.Panels.Contains(DockPanel.Testplay))
                    mappingDockWorkspace.Right.Panels.Add(DockPanel.Testplay);
                if (LibrarySettings.TestplayMods == CatchMods.None)
                    LibrarySettings.TestplayMods = layouts.Mapping?.PreviewMod switch { 1 => CatchMods.Easy, 2 => CatchMods.HardRock, _ => CatchMods.None };
            }
            Restore(timingDockWorkspace, layouts.Timing);
            Restore(hitsoundingDockWorkspace, layouts.Hitsounding, hitsounding: true);
            if (layouts.Mapping?.Left?.Active == "OsuWeb" || layouts.Mapping?.Right?.Active == "OsuWeb")
                LibrarySettings.DiscussionWebMode = true;
        }
        LibrarySettings.DockLayouts = CaptureDockLayouts();
        savedDockLayout = JsonSerializer.Serialize(LibrarySettings.DockLayouts);
        RefreshLoadedAimod();
    }
    private static void Restore(DockWorkspace workspace, DockWorkspaceLayout? layout, bool hitsounding = false)
    {
        if (layout is null) return;
        var seen = new HashSet<DockPanel>();
        if (layout.Right?.Active == "OsuWeb")
        {
            RestoreGroup(workspace.Right, layout.Right);
            RestoreGroup(workspace.Left, layout.Left);
        }
        else
        {
            RestoreGroup(workspace.Left, layout.Left);
            RestoreGroup(workspace.Right, layout.Right);
        }
        workspace.PreciseHitsounds = layout.PreciseHitsounds;
        workspace.ShowHitsoundText = layout.ShowHitsoundText;
        workspace.PreviewMod = Math.Clamp(layout.PreviewMod, 0, 2);
        workspace.PreviewDisplayMode = Math.Clamp(layout.PreviewDisplayMode, 0, 2);
        workspace.MovementPosition = layout.MovementX is { } x && float.IsFinite(x)
            && layout.MovementY is { } y && float.IsFinite(y) ? (x, y) : null;
        workspace.MovementScale = float.IsFinite(layout.MovementScale) ? Math.Clamp(layout.MovementScale, .6f, 3) : 1;
        void RestoreGroup(DockGroup group, DockGroupLayout? saved)
        {
            var names = saved?.Panels ?? group.Panels.Select(p => p.ToString()).ToArray();
            group.Panels.Clear();
            foreach (string savedName in names)
            {
                string name = savedName switch { "OsuWeb" => "Discussion", "MapSamplesets" or "HitsoundLibrary" => "HitsoundingConfig", _ => savedName };
                if (Enum.TryParse<DockPanel>(name, out var panel) && Enum.IsDefined(panel)
                    && panel.ToString() == name && DockPanelAllowed(panel, hitsounding) && seen.Add(panel)) group.Panels.Add(panel);
            }
            if (saved is not null)
            {
                group.Collapsed = saved.Collapsed;
                if (float.IsFinite(saved.Width) && saved.Width >= MinimumDockWidth) group.Width = Math.Min(saved.Width, 10000);
            }
            group.Active = Enum.TryParse<DockPanel>(saved?.Active switch { "OsuWeb" => "Discussion", "MapSamplesets" or "HitsoundLibrary" => "HitsoundingConfig", _ => saved?.Active }, out var active) && group.Panels.Contains(active)
                ? active : group.Panels.FirstOrDefault();
            group.Split = saved is not null && float.IsFinite(saved.Split) ? Math.Clamp(saved.Split, .25f, .75f) : .58f;
            group.TabScroll = 0; group.Revealed = null;
        }
    }
}
