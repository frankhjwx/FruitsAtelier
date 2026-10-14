namespace FruitsAtelier.Core;

public sealed class EditorDockLayouts
{
    public int Version { get; set; } = 1;
    public DockWorkspaceLayout? Mapping { get; set; }
    public DockWorkspaceLayout? Timing { get; set; }
}

public sealed class DockWorkspaceLayout
{
    public DockGroupLayout? Left { get; set; }
    public DockGroupLayout? Right { get; set; }
    public int PreviewMod { get; set; }
    public int PreviewDisplayMode { get; set; }
    public float? MovementX { get; set; }
    public float? MovementY { get; set; }
}

public sealed class DockGroupLayout
{
    public string[]? Panels { get; set; }
    public string? Active { get; set; }
    public bool Collapsed { get; set; }
    public float Width { get; set; }
}
