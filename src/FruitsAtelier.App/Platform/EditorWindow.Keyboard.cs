namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private bool DispatchShortcutBeforeTranslation(Native.Message message)
    {
        // TranslateMessage can let the IME consume shortcuts before WM_KEYDOWN reaches the window.
        if (message.Window != hwnd || message.Id != 0x0100 || view.IsEditingText || view.CapturingTestplayKey) return false;
        uint key = ResolveVirtualKey(message.WParam, message.LParam, down: true);
        if (key is 0 or >= 0xE5) return false;
        view.SetModifiers(Native.Alt, Native.Shift);
        view.KeyDown((int)key, Native.Control, Native.Shift);
        if (!view.WantsCapture && Native.GetCapture() == hwnd) Native.ReleaseCapture();
        UpdateTitle(); Invalidate();
        return true;
    }

    private uint ResolveVirtualKey(nuint key, nint keyData, bool down)
    {
        if (key != 0xE5) return (uint)key;
        // ImmGetVirtualKey must precede TranslateMessage; key-up needs the physical scan code.
        uint original = down ? Native.ImmGetVirtualKey(hwnd) : 0;
        if (original is > 0 and < 0xE5) return original;
        uint scan = (uint)((long)keyData >> 16) & 255;
        if (((long)keyData & (1L << 24)) != 0) scan |= 0xE000;
        return Native.MapVirtualKey(scan, 3);
    }
}
