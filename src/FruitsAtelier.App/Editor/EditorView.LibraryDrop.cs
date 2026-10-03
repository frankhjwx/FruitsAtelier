namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<string[]>? RequestLibraryDrop { get; set; }
    public Action<string>? RequestAudioProject { get; set; }
    public bool CanDropAudio => !librarySettingsOpen && !resourcePage && !updatesPage && !IsTestplaying
        && !ExportVisible && !ErrorVisible && !DiscardConfirmationVisible && !SongSetupVisible
        && !SliderDialogVisible && !SynchronizationVisible && !VersionHistoryVisible && !TimingModal
        && !AimodVisible && !DistanceSnapDialogVisible && !VolumeDialogVisible && !StreamDialogVisible
        && !MergeDialogVisible && !SynchronizationBlocksInput;
    public bool CanDropLibraryFiles => LibraryVisible && !librarySettingsOpen && !resourcePage && !updatesPage
        && !ExportVisible && !ErrorVisible && !DiscardConfirmationVisible && !SliderDialogVisible && !SongSetupVisible;

    public bool CanDropFile(string path) => CanDropAudio && Platform.LibraryOperations.IsProjectAudio(path)
        || CanDropLibraryFiles && IsLibraryArchive(path);

    public static bool IsLibraryArchive(string path)
        => Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".osk", StringComparison.OrdinalIgnoreCase);

    public void DropLibraryFiles(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var audio = files.Where(Platform.LibraryOperations.IsProjectAudio).ToArray();
        if (CanDropAudio && audio.Length > 0)
        {
            if (audio.Length != 1 || files.Any(IsLibraryArchive)) { ShowError(FruitsAtelier.Localization.Strings.Get("audioProject.oneFile")); return; }
            if (PrepareFileOperation()) RequestAudioProject?.Invoke(audio[0]);
            return;
        }
        if (!CanDropLibraryFiles) return;
        var archives = files.Where(IsLibraryArchive).ToArray();
        if (archives.Length == 0 || !PrepareFileOperation()) return;
        libraryField = -1; contextItems.Clear(); languageMenuOpen = false;
        RequestLibraryDrop?.Invoke(archives);
    }
}
