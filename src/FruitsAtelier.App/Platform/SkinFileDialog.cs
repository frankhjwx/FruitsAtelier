using L = FruitsAtelier.Localization.Strings;
using System.Runtime.InteropServices;

namespace FruitsAtelier.App.Platform;

internal static class SkinFileDialog
{
    internal static string? SelectArchive(nint owner)
    {
        using var modal = new NativeModalScope(owner);
        MapFileDialog.PrepareOwner(owner);
        nint buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var dialog = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(), Owner = owner,
                Filter = L.Get("dialog.skinFilter") + "\0*.osk\0\0",
                FilterIndex = 1, File = buffer, MaxFile = 32768,
                Title = L.Get("dialog.skinTitle"),
                Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008
            };
            if (!GetOpenFileName(ref dialog))
            {
                uint error = CommDlgExtendedError();
                if (error != 0) throw new InvalidOperationException(L.Get("dialog.skinFailed", error));
                return null;
            }
            return Marshal.PtrToStringUni(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        internal int Size;
        internal nint Owner, Instance;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Filter;
        internal nint CustomFilter;
        internal int MaxCustomFilter, FilterIndex;
        internal nint File;
        internal int MaxFile;
        internal nint FileTitle;
        internal int MaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? InitialDirectory;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Title;
        internal uint Flags;
        internal ushort FileOffset, FileExtension;
        internal nint DefaultExtension, CustomData, Hook, TemplateName, Reserved;
        internal uint ReservedValue, FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);
    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();
}
