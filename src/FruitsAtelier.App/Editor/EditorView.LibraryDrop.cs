namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<string[]>? RequestLibraryDrop { get; set; }
    public bool CanDropLibraryFiles => LibraryVisible && !librarySettingsOpen && !resourcePage && !updatesPage
        && !ExportVisible && !ErrorVisible && !DiscardConfirmationVisible && !SliderDialogVisible;

    public static bool IsLibraryArchive(string path)
        => Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".osk", StringComparison.OrdinalIgnoreCase);

    public void DropLibraryFiles(IEnumerable<string> paths)
    {
        if (!CanDropLibraryFiles) return;
        var archives = paths.Where(IsLibraryArchive).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (archives.Length == 0 || !PrepareFileOperation()) return;
        libraryField = -1; contextItems.Clear(); languageMenuOpen = false;
        RequestLibraryDrop?.Invoke(archives);
    }
}
