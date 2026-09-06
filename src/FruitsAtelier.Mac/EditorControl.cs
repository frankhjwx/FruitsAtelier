using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using FruitsAtelier.App.Editor;

namespace FruitsAtelier.Mac;

public static class MacInput
{
    public static bool Control(KeyModifiers modifiers) => (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
    public static int VirtualKey(Key key, bool editingText = true) => key switch
    {
        >= Key.A and <= Key.Z => 65 + key - Key.A,
        Key.Back => editingText ? 8 : 46, Key.Tab => 9, Key.Enter => 13, Key.Escape => 27,
        Key.Space => 32, Key.Home => 36, Key.Delete => 46, _ => 0
    };
}
internal sealed class EditorControl : Control, IDisposable
{
    internal EditorView View { get; } = new();
    private readonly ImageCache images = new();
    internal Action? Changed;
    public EditorControl() { Focusable = true; ClipToBounds = true; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var canvas = new MacCanvas(context, images);
        View.Render(canvas, (float)Bounds.Width, (float)Bounds.Height);
    }
    internal void Refresh() { InvalidateVisual(); Changed?.Invoke(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus(); var p = e.GetPosition(this); var state = e.GetCurrentPoint(this).Properties;
        int button = state.IsRightButtonPressed ? 2 : state.IsMiddleButtonPressed ? 1 : 0;
        if (e.ClickCount == 2 && button == 0) View.PointerDoubleClick((float)p.X, (float)p.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift), MacInput.Control(e.KeyModifiers));
        else View.PointerDown((float)p.X, (float)p.Y, button, e.KeyModifiers.HasFlag(KeyModifiers.Shift), MacInput.Control(e.KeyModifiers));
        if (View.WantsCapture) e.Pointer.Capture(this);
        e.Handled = true; Refresh();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        View.PointerMove((float)p.X, (float)p.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift), MacInput.Control(e.KeyModifiers));
        Refresh();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var p = e.GetPosition(this);
        View.PointerUp((float)p.X, (float)p.Y, e.InitialPressMouseButton == MouseButton.Right ? 2 : e.InitialPressMouseButton == MouseButton.Middle ? 1 : 0);
        if (!View.WantsCapture) e.Pointer.Capture(null);
        e.Handled = true; Refresh();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { if (View.WantsCapture) View.CancelInteraction(); Refresh(); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var p = e.GetPosition(this); View.Wheel((float)p.X, (float)p.Y, (float)e.Delta.Y * 120, MacInput.Control(e.KeyModifiers));
        e.Handled = true; Refresh();
    }
    protected override async void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.V && MacInput.Control(e.KeyModifiers) && View.LibraryTextFocused)
        {
            e.Handled = true;
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                try { View.PasteLibraryText(await clipboard.TryGetTextAsync() ?? ""); }
                catch (Exception error) { View.SetNotice(error.Message); }
            }
            Refresh(); return;
        }
        View.KeyDown(MacInput.VirtualKey(e.Key, View.IsEditingText), MacInput.Control(e.KeyModifiers), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = e.Key is Key.Tab or Key.Space or Key.Back or Key.Delete or Key.Enter or Key.Escape || MacInput.Control(e.KeyModifiers);
        Refresh();
    }
    protected override void OnTextInput(TextInputEventArgs e)
    {
        foreach (char c in e.Text ?? "") View.TextInput(c);
        e.Handled = true; Refresh();
    }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e) { View.CancelInteraction(); Refresh(); }
    public void Dispose() => images.Dispose();
}
