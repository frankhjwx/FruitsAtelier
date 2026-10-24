using System.Runtime.InteropServices;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal static class SystemTrash
{
    internal static void Recycle(IReadOnlyList<string> paths)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RecycleOnSta(paths); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new IOException(L.Get("library.recycleFailed"), failure);
    }

    private static void RecycleOnSta(IReadOnlyList<string> paths)
    {
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
        var items = new List<object>();
        var sink = new RecycleOnlySink();
        try
        {
            // Require recycling, stop on errors, and never offer permanent deletion as a fallback.
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x00000400 | 0x00000010 | 0x00000004);
            foreach (string path in paths)
            {
                Guid iid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(path), 0, in iid, out IShellItem item));
                items.Add(item);
                operation.DeleteItem(item, sink);
            }
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out bool aborted);
            if (aborted || sink.Failed || paths.Any(Directory.Exists)) throw new IOException(L.Get("library.recycleFailed"));
        }
        finally
        {
            foreach (object item in items) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, in Guid iid, out IShellItem item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    // Unused entries preserve the native COM vtable order.
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, nint name, nint sink);
        void RenameItems(nint items, nint name);
        void MoveItem(nint item, nint destination, nint name, nint sink);
        void MoveItems(nint items, nint destination);
        void CopyItem(nint item, nint destination, nint name, nint sink);
        void CopyItems(nint items, nint destination);
        void DeleteItem(IShellItem item, IFileOperationProgressSink sink);
        void DeleteItems(nint items);
        void NewItem(nint destination, uint attributes, nint name, nint template, nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, nint name);
        [PreserveSig] int PostRenameItem(uint flags, nint item, nint name, int result, nint renamed);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint destination, nint name);
        [PreserveSig] int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint moved);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint destination, nint name);
        [PreserveSig] int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint copied);
        [PreserveSig] int PreDeleteItem(uint flags, nint item);
        [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint recycled);
        [PreserveSig] int PreNewItem(uint flags, nint destination, nint name);
        [PreserveSig] int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created);
        [PreserveSig] int UpdateProgress(uint total, uint complete);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleOnlySink : IFileOperationProgressSink
    {
        public bool Failed { get; private set; }
        public int PreDeleteItem(uint flags, nint item)
        {
            if ((flags & 0x80) != 0) return 0;
            Failed = true;
            return unchecked((int)0x80004005);
        }
        public int PostDeleteItem(uint flags, nint item, int result, nint recycled) { Failed |= result < 0 || recycled == 0; return 0; }
        public int StartOperations() => 0;
        public int FinishOperations(int result) { Failed |= result < 0; return 0; }
        public int PreRenameItem(uint flags, nint item, nint name) => 0;
        public int PostRenameItem(uint flags, nint item, nint name, int result, nint renamed) => 0;
        public int PreMoveItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostMoveItem(uint flags, nint item, nint destination, nint name, int result, nint moved) => 0;
        public int PreCopyItem(uint flags, nint item, nint destination, nint name) => 0;
        public int PostCopyItem(uint flags, nint item, nint destination, nint name, int result, nint copied) => 0;
        public int PreNewItem(uint flags, nint destination, nint name) => 0;
        public int PostNewItem(uint flags, nint destination, nint name, nint template, uint attributes, int result, nint created) => 0;
        public int UpdateProgress(uint total, uint complete) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
    }
}
