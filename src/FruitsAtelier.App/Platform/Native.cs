using System.Runtime.InteropServices;

namespace FruitsAtelier.App.Platform;

internal static class Native
{
    [DllImport("shell32.dll")] internal static extern void DragAcceptFiles(nint window, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(nint drop, uint index, System.Text.StringBuilder? path, uint capacity);
    [DllImport("shell32.dll")] private static extern void DragFinish(nint drop);

    internal static string[] TakeDroppedFiles(nint drop)
    {
        try
        {
            uint count = DragQueryFile(drop, uint.MaxValue, null, 0);
            var paths = new string[count];
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(drop, i, null, 0);
                var path = new System.Text.StringBuilder(checked((int)length + 1));
                DragQueryFile(drop, i, path, (uint)path.Capacity);
                paths[i] = path.ToString();
            }
            return paths;
        }
        finally { DragFinish(drop); }
    }

    internal const uint WindowStyle = 0x00CF0000;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        internal uint Size, Style;
        internal WindowProc Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        internal string? MenuName;
        internal string ClassName;
        internal nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Paint
    {
        internal nint Dc;
        internal int Erase;
        internal Rectangle Rect;
        internal int Restore, IncUpdate;
        internal fixed byte Reserved[32];
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMaxInfo { internal Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    [DllImport("user32.dll")] internal static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] internal static extern bool CloseClipboard();
    [DllImport("user32.dll")] internal static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] internal static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(nint memory);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    internal static void WriteClipboardText(nint owner, string text)
    {
        if (!OpenClipboard(owner)) return;
        nint memory = 0;
        try
        {
            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
            memory = GlobalAlloc(2, (nuint)bytes.Length);
            if (memory == 0) return;
            nint pointer = GlobalLock(memory);
            if (pointer == 0) return;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            if (EmptyClipboard() && SetClipboardData(13, memory) != 0) memory = 0;
        }
        finally { if (memory != 0) GlobalFree(memory); CloseClipboard(); }
    }
    internal static string ReadClipboardText(nint owner)
    {
        if (!OpenClipboard(owner)) return "";
        try
        {
            nint data = GetClipboardData(13); if (data == 0) return "";
            nint pointer = GlobalLock(data); if (pointer == 0) return "";
            try { return Marshal.PtrToStringUni(pointer) ?? ""; }
            finally { GlobalUnlock(data); }
        }
        finally { CloseClipboard(); }
    }
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] internal static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("imm32.dll")] internal static extern uint ImmGetVirtualKey(nint window);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern void PostQuitMessage(int exitCode);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rectangle rect);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rectangle rect);
    [DllImport("user32.dll")] internal static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] internal static extern bool ValidateRect(nint hwnd, nint rect);
    [DllImport("user32.dll")] internal static extern bool GetUpdateRect(nint hwnd, out Rectangle rect, bool erase);
    [DllImport("user32.dll")] internal static extern nint BeginPaint(nint hwnd, out Paint paint);
    [DllImport("user32.dll")] internal static extern bool EndPaint(nint hwnd, ref Paint paint);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] internal static extern bool AdjustWindowRectExForDpi(ref Rectangle rect, uint style, bool menu, uint extended, uint dpi);
    [DllImport("user32.dll")] internal static extern bool SystemParametersInfo(uint action, uint param, out Rectangle value, uint flags);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetCapture();
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetFocus();
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint LoadCursor(nint instance, nint name);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint hwnd, string text, string title, uint flags);
    internal static void ShowError(nint owner, string text, string title)
    {
        using var modal = new NativeModalScope(owner);
        if (GetCapture() != 0) ReleaseCapture();
        if (owner != 0) { ShowWindow(owner, IsIconic(owner) ? 9 : 5); SetForegroundWindow(owner); }
        // Fatal startup/render failures cannot rely on the editor canvas to display their error.
        MessageBox(owner, text, title, 0x00050010); // MB_SETFOREGROUND | MB_TOPMOST | MB_ICONERROR
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetWindowText(nint hwnd, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint SendMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("gdi32.dll")] internal static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] internal static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] internal static extern uint SetBkColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] internal static extern nint CreateFont(int height, int width, int escapement, int orientation,
        int weight, uint italic, uint underline, uint strikeout, uint charset, uint outputPrecision,
        uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? module);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    internal static bool Control => GetKeyState(0x11) < 0;
    internal static bool Shift => GetKeyState(0x10) < 0;
    internal static bool Alt => GetKeyState(0x12) < 0;
}
