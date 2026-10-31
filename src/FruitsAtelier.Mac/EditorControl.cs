using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FruitsAtelier.App.Editor;

namespace FruitsAtelier.Mac;

public static class MacInput
{
    public static bool Control(KeyModifiers modifiers) => (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
    public static int VirtualKey(Key key, bool editingText = true) => key switch
    {
        >= Key.A and <= Key.Z => 65 + key - Key.A,
        >= Key.D0 and <= Key.D9 => 48 + key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => 96 + key - Key.NumPad0,
        >= Key.F1 and <= Key.F24 => 112 + key - Key.F1,
        Key.Left => 37, Key.Right => 39, Key.LeftShift or Key.RightShift => 16, Key.End => 35,
        Key.LeftCtrl or Key.RightCtrl => 17, Key.LeftAlt or Key.RightAlt => 18,
        Key.CapsLock => 20, Key.Clear => 12, Key.PageUp => 33, Key.PageDown => 34, Key.Insert => 45,
        Key.Multiply => 106, Key.Add => 107, Key.Separator => 108, Key.Subtract => 109, Key.Decimal => 110, Key.Divide => 111,
        Key.NumLock => 144, Key.Scroll => 145,
        Key.Back => editingText ? 8 : 46, Key.Tab => 9, Key.Enter => 13, Key.Escape => 27,
        Key.OemSemicolon => 186, Key.OemOpenBrackets => 219, Key.OemCloseBrackets => 221, Key.OemQuotes => 222,
        Key.OemComma => 188, Key.OemPeriod => 190, Key.OemQuestion => 191, Key.OemTilde => 192,
        Key.OemPipe => 220, Key.Oem8 => 223, Key.OemBackslash => 226,
        Key.Space => 32, Key.Home => 36, Key.Delete => 46, Key.Up => 38, Key.Down => 40, Key.OemPlus => 187, Key.OemMinus => 189, _ => 0
    };
}
internal sealed class EditorControl : Control, IDisposable
{
    internal EditorView View { get; } = new(loadDemo: false);
    private readonly ImageCache images = new();
    private readonly MacUiBoundary boundary;
    internal void RunUi(Action action) => boundary.Run(action);
    internal void ReportUiFailure(Exception error) => boundary.Report(error);
    internal bool MediaNeedsRedraw => images.MediaNeedsRedraw;
    internal Action? Changed;
    internal Action<Exception>? RecoveryRendererFailed;
    private float uiScale = 1;
    private bool nativeFallbackRequested;
    private Point EditorPoint(PointerEventArgs e) => e.GetPosition(this) / uiScale;
    public EditorControl()
    {
        boundary = new(View, error => MacPaths.Log(error.ToString()), InvalidateVisual);
        Focusable = true; ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) => boundary.Run(() =>
        {
            e.DragEffects = !View.ErrorVisible && DroppedPaths(e).Any(View.CanDropFile)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }));
        DragDrop.AddDropHandler(this, (_, e) => boundary.Run(() =>
        {
            if (!View.ErrorVisible) View.DropLibraryFiles(DroppedPaths(e));
            e.Handled = true; Refresh();
        }));
        View.RequestPasteObjects = async () =>
        {
            var document = View.Document;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    string text = await clipboard.TryGetTextAsync() ?? "";
                    if (!View.ErrorVisible && ReferenceEquals(document, View.Document)) View.PasteClipboardText(text);
                }
            }
            catch (Exception error) { View.SetNotice(error.Message); }
            Refresh();
        };
        View.RequestPasteHitsoundName = async () =>
        {
            int session = View.HitsoundCopierInputSession;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    string text = await clipboard.TryGetTextAsync() ?? "";
                    if (!View.ErrorVisible) View.PasteHitsoundName(text,session);
                }
            }
            catch (Exception error) { View.SetNotice(error.Message); }
        };
        View.RequestPasteSongSetup = async () =>
        {
            int session = View.SongSetupInputSession;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    string text = await clipboard.TryGetTextAsync() ?? "";
                    if (!View.ErrorVisible) View.PasteSongSetupText(text, session);
                }
            }
            catch (Exception error) { View.SetNotice(error.Message); }
            Refresh();
        };
        View.RequestPasteTiming = async () =>
        {
            int session = View.TimingInputSession;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    string text = await clipboard.TryGetTextAsync() ?? "";
                    if (!View.ErrorVisible) View.PasteTimingText(text, session);
                }
            }
            catch (Exception error) { View.SetNotice(error.Message); }
            Refresh();
        };
        View.RequestTimingSampleHelp = () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://osu.ppy.sh/wiki/en/Beatmapping/Hitsound") { UseShellExecute = true });
        View.RequestPasteTime = async () =>
        {
            int session = View.TimeJumpSession;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    string text = await clipboard.TryGetTextAsync() ?? "";
                    if (!View.ErrorVisible) View.PasteTimeJumpText(text, session);
                }
            }
            catch (Exception error) { View.SetNotice(error.Message); }
            Refresh();
        };
        View.RequestPasteLibrary = async () =>
        {
            int field = View.LibraryInputField;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    string text = await clipboard.TryGetTextAsync() ?? "";
                    if (!View.ErrorVisible) View.PasteLibraryText(text, field);
                }
            }
            catch (Exception error) { View.SetNotice(error.Message); }
            Refresh();
        };
        View.RequestPasteField = async () =>
        {
            string field = View.ActiveTextInputField;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    string text = await clipboard.TryGetTextAsync() ?? "";
                    if (!View.ErrorVisible) View.PasteFieldText(text, field);
                }
            }
            catch (Exception error) { View.SetNotice(error.Message); }
            Refresh();
        };
        View.RequestCopyText = async text =>
        {
            try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text); }
            catch (Exception error) { View.SetNotice(error.Message); }
        };
    }
    public override void Render(DrawingContext context)
    {
        try { RenderEditor(context); }
        catch (Exception error) { boundary.Report(error); }
    }
    private void RenderEditor(DrawingContext context)
    {
        base.Render(context);
        if (!View.RecoveryActive) nativeFallbackRequested = false;
        uiScale = View.UiScaleFactor;
        try
        {
            using var transform = context.PushTransform(Matrix.CreateScale(uiScale, uiScale));
            using var canvas = new MacCanvas(context, images, (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * uiScale);
            View.Render(canvas, (float)Bounds.Width / uiScale, (float)Bounds.Height / uiScale);
        }
        catch (Exception error)
        {
            if (View.RecoveryActive)
            {
                if (!nativeFallbackRequested)
                {
                    nativeFallbackRequested = true;
                    MacPaths.Log(error.ToString());
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => boundary.Run(() => RecoveryRendererFailed?.Invoke(error)));
                }
            }
            else
            {
                boundary.Report(error);
            }
            return;
        }
        if (View.ErrorVisible) return;
        UpdatePointerCursor();
        if (View.IsTestplaying || View.LazerPresentationNeedsRedraw || View.SliderHoldNeedsRedraw || View.MarqueeScrollNeedsRedraw || View.CanvasBackgroundDimNeedsRedraw || View.BookmarkToolbarNeedsRedraw || View.BookmarkToolbarHoverPending || View.VolumePopoverNeedsRedraw || View.PatternGalleryNeedsRedraw)
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => boundary.Run(() => { if (!View.ErrorVisible && (View.IsTestplaying || View.LazerPresentationNeedsRedraw || View.SliderHoldNeedsRedraw || View.MarqueeScrollNeedsRedraw || View.CanvasBackgroundDimNeedsRedraw || View.BookmarkToolbarNeedsRedraw || View.BookmarkToolbarHoverPending || View.VolumePopoverNeedsRedraw || View.PatternGalleryNeedsRedraw)) InvalidateVisual(); }));
    }
    private StandardCursorType? pointerCursorType;
    private void UpdatePointerCursor()
    {
        var next = View.TestplayUsesCursor ? StandardCursorType.None : View.FloatingPanelResizeCursor is 1 or 4 ? StandardCursorType.TopLeftCorner : View.FloatingPanelResizeCursor is 2 or 3 ? StandardCursorType.TopRightCorner : View.TimelinePanelResizeCursor ? StandardCursorType.SizeNorthSouth : (View.DiscussionHandCursor || View.MapsetVerifierHandCursor) ? StandardCursorType.Hand : View.DiscussionTextCursor ? StandardCursorType.Ibeam : View.TimelineResizeCursor || View.PreviewResizeCursor || View.AimodColumnResizeCursor || View.SelectionScaleCursor ? StandardCursorType.SizeWestEast : StandardCursorType.Arrow;
        if (pointerCursorType == next) return;
        pointerCursorType = next;
        Cursor = new Cursor(next);
    }
    private static IEnumerable<string> DroppedPaths(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Select(f => f.TryGetLocalPath()).OfType<string>() ?? [];

    internal void Refresh()
    {
        try { RefreshEditor(); }
        catch (Exception error) { boundary.Report(error); }
    }
    private void RefreshEditor()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            var theme = View.LightEditorTheme ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
            if (window.RequestedThemeVariant != theme) window.RequestedThemeVariant = theme;
        }
        InvalidateVisual(); Changed?.Invoke();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        try
        {
            View.SetModifiers(e.KeyModifiers.HasFlag(KeyModifiers.Alt), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            Focus(); var p = EditorPoint(e); var state = e.GetCurrentPoint(this).Properties;
            int button = state.IsRightButtonPressed ? 2 : state.IsMiddleButtonPressed ? 1 : 0;
            if (e.ClickCount == 2 && button == 0) View.PointerDoubleClick((float)p.X, (float)p.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift), MacInput.Control(e.KeyModifiers));
            else View.PointerDown((float)p.X, (float)p.Y, button, e.KeyModifiers.HasFlag(KeyModifiers.Shift), MacInput.Control(e.KeyModifiers));
            if (View.WantsCapture) e.Pointer.Capture(this);
            e.Handled = true; Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
        if (View.RecoveryActive)
        {
            try { e.Pointer.Capture(null); e.Handled = true; }
            catch (Exception error) { boundary.Report(error); e.Handled = true; }
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        try
        {
            View.SetModifiers(e.KeyModifiers.HasFlag(KeyModifiers.Alt), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            var p = EditorPoint(e);
            View.PointerMove((float)p.X, (float)p.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift), MacInput.Control(e.KeyModifiers));
            UpdatePointerCursor();
            Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
        if (View.RecoveryActive)
        {
            try { e.Pointer.Capture(null); e.Handled = true; }
            catch (Exception error) { boundary.Report(error); e.Handled = true; }
        }
    }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        try
        {
            base.OnPointerExited(e);
            View.PointerLeave(); Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        try
        {
            var p = EditorPoint(e);
            View.PointerUp((float)p.X, (float)p.Y, e.InitialPressMouseButton == MouseButton.Right ? 2 : e.InitialPressMouseButton == MouseButton.Middle ? 1 : 0,
                e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            if (!View.WantsCapture) e.Pointer.Capture(null);
            e.Handled = true; Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
        if (View.RecoveryActive)
        {
            try { e.Pointer.Capture(null); e.Handled = true; }
            catch (Exception error) { boundary.Report(error); e.Handled = true; }
        }
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        try
        {
            if (!View.RecoveryActive && View.WantsCapture) View.CancelInteraction();
            Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        try
        {
            View.SetModifiers(e.KeyModifiers.HasFlag(KeyModifiers.Alt), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            var p = EditorPoint(e); View.Wheel((float)p.X, (float)p.Y, (float)e.Delta.Y * 120,
                MacInput.Control(e.KeyModifiers), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            e.Handled = true; Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    private readonly HashSet<Key> recoveryHeldKeys = [];
    protected override void OnKeyDown(KeyEventArgs e)
    {
        try
        {
            if (View.RecoveryActive && !recoveryHeldKeys.Add(e.Key)) { e.Handled = true; return; }
            View.SetModifiers(e.KeyModifiers.HasFlag(KeyModifiers.Alt), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            View.KeyDown(MacInput.VirtualKey(e.Key, View.IsEditingText || View.CapturingTestplayKey || View.IsTestplaying), MacInput.Control(e.KeyModifiers), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = View.IsTestplaying || View.CapturingTestplayKey || e.Key is Key.Tab or Key.Space or Key.Back or Key.Delete or Key.Enter or Key.Escape
                || MacInput.Control(e.KeyModifiers) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
            Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        try
        {
            recoveryHeldKeys.Remove(e.Key);
            base.OnKeyUp(e);
            View.KeyUp(MacInput.VirtualKey(e.Key, View.IsEditingText || View.CapturingTestplayKey || View.IsTestplaying));
            View.SetModifiers(e.KeyModifiers.HasFlag(KeyModifiers.Alt), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    protected override void OnTextInput(TextInputEventArgs e)
    {
        try
        {
            foreach (char c in e.Text ?? "") View.TextInput(c);
            e.Handled = true; Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        try { base.OnGotFocus(e); View.SetTextInputFocus(true); Refresh(); }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            recoveryHeldKeys.Clear(); base.OnLostFocus(e); View.SetTextInputFocus(false);
            if (!View.RecoveryActive) View.CancelInteraction(preserveTestplay: true);
            Refresh();
        }
        catch (Exception error) { boundary.Report(error); e.Handled = true; }
    }
    public void Dispose() { _ = View.CompleteLibraryCacheWritesAsync(); View.StopFileMonitoring(); View.StopDiscussions(); images.Dispose(); }
}
