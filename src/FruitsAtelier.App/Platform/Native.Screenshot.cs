using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FruitsAtelier.App.Platform;

internal static partial class Native
{
    internal static void WriteClipboardImage(nint owner, int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4))
            throw new ArgumentException(nameof(pixels));
        var header = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0), 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
        // Top-down BGRA preserves the renderer's row order; CF_DIB carries no BMP file header.
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), -height);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14), 32);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), pixels.Length);
        nint memory = GlobalAlloc(2, (nuint)checked(header.Length + pixels.Length));
        if (memory == 0) throw new Win32Exception();
        bool opened = false;
        try
        {
            nint pointer = GlobalLock(memory);
            if (pointer == 0) throw new Win32Exception();
            try
            {
                Marshal.Copy(header, 0, pointer, header.Length);
                Marshal.Copy(pixels, 0, pointer + header.Length, pixels.Length);
            }
            finally { GlobalUnlock(memory); }
            // Clipboard consumers briefly lock it after a change; tolerate that bounded hand-off.
            for (int attempt = 0; attempt < 5 && !(opened = OpenClipboard(owner)); attempt++)
                if (attempt < 4) Thread.Sleep(10);
            if (!opened) throw new Win32Exception();
            if (!EmptyClipboard() || SetClipboardData(8, memory) == 0) throw new Win32Exception();
            memory = 0;
        }
        finally
        {
            if (opened) CloseClipboard();
            if (memory != 0) GlobalFree(memory);
        }
    }
}
