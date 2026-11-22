using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace FruitsAtelier.App.Platform;

[ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEditorFileDropTarget
{
    [PreserveSig] int DragEnter(ComDataObject data, uint keys, long point, ref uint effect);
    [PreserveSig] int DragOver(uint keys, long point, ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(ComDataObject data, uint keys, long point, ref uint effect);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class EditorFileDropTarget : IEditorFileDropTarget, IDisposable
{
    private readonly nint window;
    private readonly Func<string[], int, int, bool> preview;
    private readonly Action clear;
    private readonly Action<string[], int, int> drop;
    private string[] paths = [];
    private bool registered, initialized;
    internal EditorFileDropTarget(nint window, Func<string[], int, int, bool> preview, Action clear, Action<string[], int, int> drop)
    {
        this.window = window; this.preview = preview; this.clear = clear; this.drop = drop;
        int result = OleInitialize(0);
        initialized = result >= 0;
        if (initialized) registered = RegisterDragDrop(window, this) == 0;
    }
    internal bool Registered => registered;
    public int DragEnter(ComDataObject data, uint keys, long point, ref uint effect)
    {
        paths = ReadPaths(data);
        return DragOver(keys, point, ref effect);
    }
    public int DragOver(uint keys, long point, ref uint effect)
    {
        try
        {
            var p = ClientPoint(point);
            effect = (effect & 1) != 0 && preview(paths, p.X, p.Y) ? 1u : 0;
        }
        catch (Exception error) { AppLog.Write(error.ToString()); effect = 0; }
        return 0;
    }
    public int DragLeave()
    {
        paths = [];
        try { clear(); } catch (Exception error) { AppLog.Write(error.ToString()); }
        return 0;
    }
    public int Drop(ComDataObject data, uint keys, long point, ref uint effect)
    {
        try
        {
            var p = ClientPoint(point);
            paths = ReadPaths(data);
            if ((effect & 1) != 0 && preview(paths, p.X, p.Y)) { drop(paths, p.X, p.Y); effect = 1; }
            else effect = 0;
        }
        catch (Exception error) { AppLog.Write(error.ToString()); effect = 0; }
        finally { DragLeave(); }
        return 0;
    }
    private Native.Point ClientPoint(long point)
    {
        var p = new Native.Point { X = unchecked((int)point), Y = unchecked((int)(point >> 32)) };
        Native.ScreenToClient(window, ref p); return p;
    }
    private static string[] ReadPaths(ComDataObject data)
    {
        var format = new FORMATETC { cfFormat = 15, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        try
        {
            if (data.QueryGetData(ref format) != 0) return [];
            data.GetData(ref format, out var medium);
            try { return Native.ReadDroppedFiles(medium.unionmember); }
            finally { ReleaseStgMedium(ref medium); }
        }
        catch (Exception error) when (error is COMException or ExternalException) { return []; }
    }
    public void Dispose()
    {
        if (registered) { RevokeDragDrop(window); registered = false; }
        if (initialized) { OleUninitialize(); initialized = false; }
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint window, IEditorFileDropTarget target);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint window);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
