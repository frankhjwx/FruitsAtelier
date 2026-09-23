using System.ComponentModel;
using System.Text;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private const int NativeTextId = 0x711;
    private nint nativeText, nativeTextFont, nativeTextBrush;
    private string? nativeTextKey;
    private string? nativeModelText;
    private bool syncingNativeText;
    private bool nativeNeedsReset;
    private bool nativeTextVisible;
    private (int X, int Y, int Width, int Height)? nativeTextBounds;
    private (float X, float Y)? nativeTextClick;

    private void SyncNativeText()
    {
        var active = view.ActiveTextField;
        if (active is null)
        {
            nativeTextClick = null;
            if (nativeText != 0 && nativeTextVisible)
            {
                nativeTextKey = null;
                nativeModelText = null;
                Native.ShowWindow(nativeText, 0);
                nativeTextVisible = false;
                if (Native.GetFocus() == nativeText) Native.SetFocus(hwnd);
            }
            return;
        }
        var field = active.Value;
        if (nativeText == 0)
        {
            nativeText = Native.CreateWindowEx(0, "EDIT", "", 0x40000000 | 0x10000000 | 0x80 | 0x100,
                0, 0, 1, 1, hwnd, NativeTextId, Native.GetModuleHandle(null), 0);
            if (nativeText == 0) throw new Win32Exception();
            nativeTextBrush = Native.CreateSolidBrush(0x3A2F28);
        }
        bool changedField = nativeTextKey != field.Id;
        if (changedField) nativeTextKey = field.Id;
        int left = (int)Math.Round(field.Bounds.X * dpi / 96);
        int top = (int)Math.Round(field.Bounds.Y * dpi / 96);
        int width = Math.Max(1, (int)Math.Round(field.Bounds.Width * dpi / 96));
        int height = Math.Max(1, (int)Math.Round(field.Bounds.Height * dpi / 96));
        var bounds = (left, top, width, height);
        if (nativeTextBounds != bounds)
        {
            Native.SetWindowPos(nativeText, 0, left, top, width, height, 0x0004 | 0x0010);
            nativeTextBounds = bounds;
        }
        int fontHeight = Math.Max(1, (int)Math.Round(field.FontSize * dpi / 96));
        if (nativeTextFont == 0 || fontHeight != nativeTextFontHeight)
        {
            nint font = Native.CreateFont(-fontHeight, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            if (font != 0)
            {
                Native.SendMessage(nativeText, 0x0030, (nuint)font, 1); // WM_SETFONT
                if (nativeTextFont != 0) Native.DeleteObject(nativeTextFont);
                nativeTextFont = font; nativeTextFontHeight = fontHeight;
            }
        }
        string current = ReadNativeText();
        if ((changedField || nativeNeedsReset || nativeModelText != field.Text) && current != field.Text)
        {
            nint selection = Native.SendMessage(nativeText, 0x00B0, 0, 0); // EM_GETSEL
            int start = (int)(selection.ToInt64() & 0xFFFF), end = (int)((selection.ToInt64() >> 16) & 0xFFFF);
            syncingNativeText = true;
            try { Native.SetWindowText(nativeText, field.Text); }
            finally { syncingNativeText = false; }
            if (!changedField) Native.SendMessage(nativeText, 0x00B1, (nuint)Math.Min(start, field.Text.Length), Math.Min(end, field.Text.Length));
        }
        nativeModelText = field.Text;
        nativeNeedsReset = false;
        if (!nativeTextVisible) { Native.ShowWindow(nativeText, 5); nativeTextVisible = true; }
        if (changedField || Native.GetFocus() != nativeText)
        {
            Native.SetFocus(nativeText);
            if (nativeTextClick is { } click)
            {
                int x = Math.Clamp((int)Math.Round((click.X - field.Bounds.X) * dpi / 96), 0, width - 1);
                int y = Math.Clamp((int)Math.Round((click.Y - field.Bounds.Y) * dpi / 96), 0, height - 1);
                nint point = (nint)((y << 16) | (x & 0xFFFF));
                Native.SendMessage(nativeText, 0x0201, 1, point);
                Native.SendMessage(nativeText, 0x0202, 0, point);
            }
            else if (field.SelectAll) Native.SendMessage(nativeText, 0x00B1, 0, -1);
            else Native.SendMessage(nativeText, 0x00B1, (nuint)field.Text.Length, field.Text.Length);
        }
        nativeTextClick = null;
    }

    private int nativeTextFontHeight;
    private string ReadNativeText()
    {
        var text = new StringBuilder(Math.Max(1, Native.GetWindowTextLength(nativeText) + 1));
        Native.GetWindowText(nativeText, text, text.Capacity);
        return text.ToString();
    }

    private bool HandleNativeTextCommand(nuint wParam, nint lParam)
    {
        if (nativeText == 0 || lParam != nativeText || (int)(wParam & 0xFFFF) != NativeTextId) return false;
        if ((wParam >> 16) != 0x0300 || syncingNativeText || nativeTextKey is null) return true; // EN_CHANGE
        string text = ReadNativeText();
        if (view.SetNativeText(nativeTextKey, text)) nativeModelText = text;
        else nativeNeedsReset = true;
        Invalidate();
        return true;
    }

    private void DisposeNativeText()
    {
        if (nativeText != 0) { Native.DestroyWindow(nativeText); nativeText = 0; }
        if (nativeTextFont != 0) { Native.DeleteObject(nativeTextFont); nativeTextFont = 0; }
        if (nativeTextBrush != 0) { Native.DeleteObject(nativeTextBrush); nativeTextBrush = 0; }
    }
}
