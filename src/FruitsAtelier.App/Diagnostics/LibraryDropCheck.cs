using System.Runtime.InteropServices;
using System.Text;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;

namespace FruitsAtelier.App.Diagnostics;

internal static class LibraryDropCheck
{
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    internal static void Run(EditorView view, nint window)
    {
        var project = view.CaptureProject();
        var request = view.RequestLibraryDrop;
        string folder = Path.GetFullPath("artifacts/library-drop-check");
        Directory.CreateDirectory(folder);
        string[] paths = [Path.Combine(folder, "谱面.OSZ"), Path.Combine(folder, "皮肤.OSK")];
        foreach (string path in paths) File.WriteAllText(path, "native drop routing fixture");
        int count = 0;
        try
        {
            view.RequestLibraryDrop = received =>
            {
                if (!received.SequenceEqual(paths)) throw new InvalidOperationException("Native drop lost archive paths.");
                count++;
            };
            view.MarkSaved(); view.ShowLibrary();
            Drop();
            if (count != 1) throw new InvalidOperationException("Library did not receive WM_DROPFILES.");
            view.OpenSettings(); Drop(); view.KeyDown(27, false, false);
            if (count != 1) throw new InvalidOperationException("Settings accepted a library drop.");
            view.LoadProject(project); view.CloseLibrary(); Drop();
            if (count != 1) throw new InvalidOperationException("Editor accepted a library drop.");
        }
        finally { view.RequestLibraryDrop = request; view.LoadProject(project); view.CloseLibrary(); }

        void Drop()
        {
            byte[] names = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
            nint drop = GlobalAlloc(0x42, (nuint)(20 + names.Length));
            if (drop == 0) throw new OutOfMemoryException();
            nint data = Native.GlobalLock(drop);
            Marshal.WriteInt32(data, 0, 20);
            Marshal.WriteInt32(data, 16, 1);
            Marshal.Copy(names, 0, data + 20, names.Length);
            Native.GlobalUnlock(drop);
            // WM_DROPFILES transfers ownership; the handler releases the HDROP even when ignored.
            SendMessage(window, 0x0233, (nuint)drop, 0);
        }
    }
}
