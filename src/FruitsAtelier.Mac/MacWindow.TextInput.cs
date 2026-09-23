using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FruitsAtelier.App.Editor;
using System.Globalization;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private readonly Canvas textLayer = new();
    private readonly TextBox nativeText = new()
    {
        IsVisible = false, BorderThickness = new(0), Padding = new(0),
        Background = new SolidColorBrush(Color.Parse("#282F3A")),
        Foreground = new SolidColorBrush(Color.Parse("#E7EBF2")),
        FontFamily = new FontFamily("Arial, PingFang SC"),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private string? nativeTextKey;
    private string? nativeModelText;
    private bool syncingNativeText;
    private bool nativeNeedsReset;
    private long nativeTextRenderVersion;
    private Avalonia.Point? nativeTextClick;

    private Control CreateEditorSurface()
    {
        var surface = new Grid();
        surface.Children.Add(editor);
        surface.Children.Add(textLayer);
        textLayer.Children.Add(nativeText);
        editor.TextFieldRendered = field =>
        {
            long version = ++nativeTextRenderVersion;
            Dispatcher.UIThread.Post(() =>
            {
                if (version == nativeTextRenderVersion) SyncNativeText(field);
            }, DispatcherPriority.Render);
        };
        editor.TextFieldPointerPressed = point => nativeTextClick = point;
        nativeText.TextChanged += (_, _) =>
        {
            if (syncingNativeText || nativeTextKey is null) return;
            string text = nativeText.Text ?? "";
            if (View.SetNativeText(nativeTextKey, text)) nativeModelText = text;
            else nativeNeedsReset = true;
            editor.Refresh();
        };
        nativeText.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape or Key.Tab)) return;
            View.KeyDown(MacInput.VirtualKey(e.Key), MacInput.Control(e.KeyModifiers),
                e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            editor.Refresh();
        }, RoutingStrategies.Tunnel);
        nativeText.GotFocus += (_, _) => View.SetTextInputFocus(true);
        return surface;
    }

    private void SyncNativeText(NativeTextField? field)
    {
        if (field is null)
        {
            nativeTextKey = null;
            nativeModelText = null;
            nativeText.IsVisible = false;
            editor.NativeTextActive = false;
            nativeTextClick = null;
            if (nativeText.IsFocused) editor.Focus();
            return;
        }
        var active = field.Value;
        bool changedField = nativeTextKey != active.Id;
        nativeTextKey = active.Id;
        Canvas.SetLeft(nativeText, active.Bounds.X);
        Canvas.SetTop(nativeText, active.Bounds.Y);
        nativeText.Width = active.Bounds.Width;
        nativeText.Height = active.Bounds.Height;
        nativeText.FontSize = active.FontSize;
        if ((changedField || nativeNeedsReset || nativeModelText != active.Text) && nativeText.Text != active.Text)
        {
            int start = nativeText.SelectionStart, end = nativeText.SelectionEnd;
            syncingNativeText = true;
            try { nativeText.Text = active.Text; }
            finally { syncingNativeText = false; }
            if (!changedField)
            {
                nativeText.SelectionStart = Math.Min(start, active.Text.Length);
                nativeText.SelectionEnd = Math.Min(end, active.Text.Length);
            }
        }
        nativeModelText = active.Text;
        nativeNeedsReset = false;
        nativeText.IsVisible = true;
        if (changedField)
        {
            editor.NativeTextActive = true;
            nativeText.Focus();
            if (nativeTextClick is { } click)
            {
                int caret = CaretAt(active, click.X);
                nativeText.SelectionStart = nativeText.SelectionEnd = nativeText.CaretIndex = caret;
            }
            else if (active.SelectAll) nativeText.SelectAll();
            else nativeText.CaretIndex = active.Text.Length;
        }
        nativeTextClick = null;
    }

    private int CaretAt(NativeTextField field, double x)
    {
        if (field.Text.Length == 0) return 0;
        double Measure(string text) => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(nativeText.FontFamily), field.FontSize, nativeText.Foreground).WidthIncludingTrailingWhitespace;
        double fullWidth = Measure(field.Text);
        double target = Math.Clamp(x - field.Bounds.X + Math.Max(0, fullWidth - field.Bounds.Width + 2), 0, fullWidth);
        int[] starts = StringInfo.ParseCombiningCharacters(field.Text);
        int low = 0, high = starts.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            int end = middle == starts.Length ? field.Text.Length : starts[middle];
            if (Measure(field.Text[..end]) < target) low = middle + 1;
            else high = middle;
        }
        int after = low == starts.Length ? field.Text.Length : starts[low];
        int before = low == 0 ? 0 : starts[low - 1];
        return target - Measure(field.Text[..before]) <= Measure(field.Text[..after]) - target ? before : after;
    }
}
