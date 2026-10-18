using Avalonia.Controls;
using FruitsAtelier.Core;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private double normalWidth = 1440, normalHeight = 900;
    private WindowState lastWindowState;

    private void RestoreWindowPreferences()
    {
        if (View.StartupDialogVisible || View.LibrarySettings.MacWindow is not { } saved) return;
        if (saved.HasValidSize())
        {
            var work = Screens.ScreenFromWindow(this)?.WorkingArea;
            Width = Math.Clamp(saved.Width, Math.Min(MinWidth, (work?.Width ?? 1920) / RenderScaling), (work?.Width ?? 1920) / RenderScaling);
            Height = Math.Clamp(saved.Height, Math.Min(MinHeight, (work?.Height ?? 1080) / RenderScaling), (work?.Height ?? 1080) / RenderScaling);
            normalWidth = Width; normalHeight = Height;
            if (work is { } area) Position = new Avalonia.PixelPoint(area.X + (area.Width - (int)(Width * RenderScaling)) / 2,
                area.Y + (area.Height - (int)(Height * RenderScaling)) / 2);
        }
        if (saved.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowPreferences()
    {
        if (View.StartupDialogVisible || SystemDecorations == SystemDecorations.None) return;
        var state = WindowState == WindowState.Minimized ? lastWindowState : WindowState;
        View.LibrarySettings.MacWindow = new EditorWindowPreferences
        {
            Width = normalWidth, Height = normalHeight,
            Maximized = (state == WindowState.FullScreen ? windowedState : state) == WindowState.Maximized
        };
        View.LibrarySettings.Save();
    }
}
