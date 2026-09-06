using System.Runtime.InteropServices;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal static class MapFileDialog
{
    internal static string OpenFilter => L.Get("dialog.openFilter") + "\0*.osz;*.osu;*.catchproj;*.catchdiff\0\0";
    internal static string OsuFilter => L.Get("dialog.osuFilter") + "\0*.osu\0\0";
    internal static string ProjectFilter => L.Get("dialog.projectFilter") + "\0*.catchproj\0\0";
    internal static string AudioFilter => L.Get("dialog.audioFilter") + "\0*.mp3;*.ogg;*.wav\0\0";

    internal static string? Select(nint owner, bool save, string title, string filter, string? initialPath = null, string? extension = null)
    {
        nint buffer = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (save && initialPath is not null)
            {
                char[] name = (Path.GetFileName(initialPath) + '\0').ToCharArray();
                Marshal.Copy(name, 0, buffer, name.Length);
            }
            var dialog = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(), Owner = owner, Filter = filter,
                FilterIndex = 1, File = buffer, MaxFile = 32768, Title = title,
                InitialDirectory = initialPath is null ? null : Directory.Exists(initialPath) ? initialPath : Path.GetDirectoryName(initialPath),
                DefaultExtension = extension,
                Flags = 0x00080000 | 0x00000800 | 0x00000008 | (save ? 0x00000002u : 0x00001000u)
            };
            bool accepted = save ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog);
            if (!accepted)
            {
                uint error = CommDlgExtendedError();
                if (error != 0) throw new InvalidOperationException(L.Get("dialog.fileFailed", error));
                return null;
            }
            return Marshal.PtrToStringUni(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static string? SelectFolder(nint owner, string title)
    {
        nint display = Marshal.AllocHGlobal(32768 * sizeof(char));
        try
        {
            var info = new BrowseInfo { Owner = owner, DisplayName = display, Title = title, Flags = 0x41 };
            nint pidl = SHBrowseForFolder(ref info);
            if (pidl == 0) return null;
            try { return SHGetPathFromIDList(pidl, display) ? Marshal.PtrToStringUni(display) : null; }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        finally { Marshal.FreeHGlobal(display); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        internal nint Owner, Root, DisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Title;
        internal uint Flags;
        internal nint Callback, Param;
        internal int Image;
    }
    [DllImport("shell32.dll", EntryPoint = "SHBrowseForFolderW", CharSet = CharSet.Unicode)]
    private static extern nint SHBrowseForFolder(ref BrowseInfo info);
    [DllImport("shell32.dll", EntryPoint = "SHGetPathFromIDListW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SHGetPathFromIDList(nint pidl, nint path);

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
        [MarshalAs(UnmanagedType.LPWStr)] internal string? DefaultExtension;
        internal nint CustomData, Hook, TemplateName, Reserved;
        internal uint ReservedValue, FlagsEx;
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileName(ref OpenFileName dialog);
    [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileName(ref OpenFileName dialog);
    [DllImport("comdlg32.dll")] private static extern uint CommDlgExtendedError();
}
