using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool IsLazerEditing => LibrarySettings.LazerEditing;
    public bool HitLightingEnabled => LibrarySettings.CatchHitLighting == CatchHitLightingMode.On
        || LibrarySettings.CatchHitLighting == CatchHitLightingMode.FollowMode && IsLazerEditing;
}
