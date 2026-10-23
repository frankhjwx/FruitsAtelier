namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<string[]>? RequestLibraryDrop { get; set; }
    public Action<string>? RequestAudioProject { get; set; }
    public bool CanDropAudio => !collectionDialogOpen && !OsuLinkFallbackRequired && !AcknowledgementsVisible && !librarySettingsOpen && !resourcePage && !updatesPage && !IsTestplaying
        && !ExportVisible && !ErrorVisible && !DiscardConfirmationVisible && !SongSetupVisible
        && !SliderDialogVisible && !SynchronizationVisible && !VersionHistoryVisible && !TimingModal
        && !DistanceSnapDialogVisible && !VolumeDialogVisible && !BananaEditVisible && !StreamDialogVisible
        && !MergeDialogVisible && !SynchronizationBlocksInput;
    public bool CanDropLibraryFiles => !collectionDialogOpen && !AcknowledgementsVisible && LibraryVisible && !librarySettingsOpen && !resourcePage && !updatesPage
        && !ExportVisible && !ErrorVisible && !DiscardConfirmationVisible && !SliderDialogVisible && !SongSetupVisible;

    public bool CanDropBackground => !LibraryVisible && CanDropAudio;
    public bool CanDropPatterns => HasEditorProject && !LibraryVisible && !TimingPageVisible && CanDropAudio
        && !PatternNameVisible && !GalleryTagsVisible && !PatternEditingActive && !PatternEditingBusy && !WantsCapture
        && (galleryStore is not null || !string.IsNullOrWhiteSpace(LibrarySettings.Workspace))
        && galleryPendingImportPath is null && galleryIoTask is null;
    public static bool IsPatternArchive(string path) => Path.GetExtension(path).Equals(".fap", StringComparison.OrdinalIgnoreCase);
    public static bool IsBackgroundImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp";

    public bool CanDropFile(string path) => CanDropBackground && IsBackgroundImage(path)
        || CanDropPatterns && IsPatternArchive(path)
        || CanDropAudio && Platform.FadArchive.IsPackage(path)
        || CanDropAudio && Platform.FazArchive.IsPackage(path)
        || CanDropAudio && Platform.LibraryOperations.IsProjectAudio(path)
        || CanDropLibraryFiles && IsLibraryArchive(path);

    public static bool IsLibraryArchive(string path)
        => Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".osk", StringComparison.OrdinalIgnoreCase);

    public void DropLibraryFiles(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (CanDropPatterns && files.Any(IsPatternArchive))
        {
            if (files.Length != 1) { ShowError(FruitsAtelier.Localization.Strings.Get("gallery.oneFile")); return; }
            CancelInteraction();
            AddDockPanel(DockPanel.PatternGallery, false);
            var group = PanelGroup(DockPanel.PatternGallery);
            group.Active = DockPanel.PatternGallery; group.Collapsed = false;
            EnsurePatternGalleryLoaded();
            galleryPendingImportPath = files[0];
            return;
        }
        if (CanDropAudio && files.Any(Platform.FadArchive.IsPackage))
        {
            if (files.Length != 1) { ShowError(FruitsAtelier.Localization.Strings.Get("faz.oneFile")); return; }
            BeginFadImport(files[0]); return;
        }
        if (CanDropAudio && files.Any(Platform.FazArchive.IsPackage))
        {
            if (files.Length != 1) { ShowError(FruitsAtelier.Localization.Strings.Get("faz.oneFile")); return; }
            if (PrepareFileOperation()) RequestFazOpen?.Invoke(files[0]);
            return;
        }
        var images = files.Where(IsBackgroundImage).ToArray();
        if (CanDropBackground && images.Length > 0)
        {
            if (files.Length != 1) { ShowError(FruitsAtelier.Localization.Strings.Get("background.oneFile")); return; }
            if (PrepareFileOperation()) DropBackgroundImage(images[0]);
            return;
        }
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
