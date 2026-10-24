using System.Runtime.InteropServices;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal static class SystemTrash
{
    internal static void Recycle(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
            if (fa_trash_item(Path.GetFullPath(path)) == 0)
                throw new IOException(L.Get("library.recycleFailed"));
    }

    [DllImport("libFruitsAtelierAudio.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int fa_trash_item([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
}
