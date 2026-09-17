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
        PrepareOwner(owner);
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
        PrepareOwner(owner);
        IFileDialog? dialog = null;
        IShellItem? selected = null;
        try
        {
            dialog = (IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"))!)!;
            dialog.GetOptions(out uint options);
            // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR.
            dialog.SetOptions(options | 0x20 | 0x40 | 0x800 | 0x8);
            dialog.SetTitle(title);
            AppLog.Write($"Folder dialog opening: owner={owner}");
            int result = dialog.Show(owner);
            AppLog.Write($"Folder dialog closed: HRESULT=0x{result:X8}");
            if (result == unchecked((int)0x800704C7)) return null;
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out selected);
            selected.GetDisplayName(0x80058000, out nint path); // SIGDN_FILESYSPATH
            try { return Marshal.PtrToStringUni(path); }
            finally { Marshal.FreeCoTaskMem(path); }
        }
        catch (COMException error)
        {
            throw new InvalidOperationException(L.Get("dialog.fileFailed", error.ErrorCode), error);
        }
        finally
        {
            if (selected is not null) Marshal.ReleaseComObject(selected);
            if (dialog is not null) Marshal.ReleaseComObject(dialog);
        }
    }
    private static void PrepareOwner(nint owner)
    {
        if (Native.GetCapture() == owner) Native.ReleaseCapture();
        if (Native.IsIconic(owner)) Native.ShowWindow(owner, 9);
        Native.SetForegroundWindow(owner);
    }

    // Preserve native vtable order, including methods preceding those used by folder selection.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(nint owner);
        void SetFileTypes(uint count, nint filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(nint folder);
        void SetFolder(nint folder);
        void GetFolder(out nint folder);
        void GetCurrentSelection(out nint item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out nint name);
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
