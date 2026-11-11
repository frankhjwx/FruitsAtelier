using System.Globalization;
using FruitsAtelier.App.Media;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<bool, Action<string?>>? RequestMediaFile { get; set; }
    internal Func<string, StoryboardImageCache.Pixels?>? DecodePaletteImage { get; set; }
    private Task<Action>? mediaEditTask;
    private Task<uint[]>? mediaPaletteTask;
    private EditorHistory? mediaPaletteOwner, mediaOffsetOwner;
    private string? mediaPalettePath;
    private MediaFileVersion mediaPaletteVersion;
    private uint[] mediaPalette = [];
    private int mediaTab, mediaColourCount = 4;
    private string mediaOffsetText = "", mediaPanelError = "";
    private bool mediaOffsetEditing;
    private float mediaPanelScroll;
    private Rect mediaPanelBounds;
    private MapDocument? mediaPanelSnapshot;
    private BeatmapMedia mediaPanelValues;
    private bool mediaBackgroundExists, mediaVideoExists;
    private Task<string[]>? mediaFilesTask;
    private string? mediaFilesDirectory, mediaFilesRequestedDirectory;
    private string[] mediaFiles = [];
    private float mediaPanelContentHeight = 600;
    private bool MediaOffsetFocused => mediaOffsetEditing && ReferenceEquals(mediaOffsetOwner, history)
        && DockContentInputAvailable && PanelVisible(DockPanel.Media);
    internal bool MediaEditingBusy => mediaEditTask is not null;
    internal bool MediaPaletteBusy => mediaPaletteTask is not null;
    internal Rect MediaOffsetBounds { get; private set; }
    internal Rect MediaPanelViewport => mediaPanelBounds;
    internal Rect MediaSyncBounds { get; private set; }
    internal Rect MediaGenerateBounds { get; private set; }
    internal Rect MediaApplyBounds { get; private set; }
    public static bool IsBeatmapVideo(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".avi" or ".flv" or ".mov" or ".wmv" or ".m4v" or ".mpg" or ".mpeg" or ".mkv" or ".webm";

    internal void ShowMediaPanel()
    {
        if (!CommitField()) return;
        AddDockPanel(DockPanel.Media, false);
        var group = PanelGroup(DockPanel.Media); group.Active = DockPanel.Media; group.Collapsed = false;
        menu = -1; contextItems.Clear();
    }

    private string? MediaPath(string? name, MapDocument? document = null)
    {
        if (name is null || StoryboardMediaDirectory(document ?? Document) is not { } directory) return null;
        return Path.GetFullPath(Path.Combine(directory, name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
    }

    private void DrawMediaPanel(ICanvas c, Rect bounds)
    {
        if (!ReferenceEquals(mediaPanelSnapshot, convertedSnapshot))
        {
            mediaPanelSnapshot = convertedSnapshot; mediaPanelValues = BeatmapMedia.Read(Document);
            mediaBackgroundExists = File.Exists(MediaPath(mediaPanelValues.Background));
            mediaVideoExists = File.Exists(MediaPath(mediaPanelValues.Video));
            if (!ReferenceEquals(mediaOffsetOwner, history)) mediaOffsetEditing = false;
        }
        mediaPanelBounds = bounds;
        string? directory = StoryboardMediaDirectory(Document);
        if (directory != mediaFilesDirectory && mediaFilesTask is null) RefreshMediaFiles();
        mediaPanelScroll = Math.Clamp(mediaPanelScroll, 0, Math.Max(0, mediaPanelContentHeight - bounds.Height));
        float y = bounds.Y - mediaPanelScroll;
        int firstHit = hits.Count;
        c.Clip(bounds);
        float half = (bounds.Width - 6) / 2;
        Button(c, new(bounds.X, y, half, 32), L.Get("media.files"), () => { if (CommitMediaOffset()) { mediaTab = 0; mediaPanelScroll = 0; } }, mediaTab == 0, fontSize: 12);
        Button(c, new(bounds.X + half + 6, y, half, 32), L.Get("media.palette"), () => { if (CommitMediaOffset()) { mediaTab = 1; mediaPanelScroll = 0; } }, mediaTab == 1, fontSize: 12);
        y += 44;
        if (mediaTab == 0)
        {
            Slot(false, mediaPanelValues.Background, mediaBackgroundExists);
            Slot(true, mediaPanelValues.Video, mediaVideoExists);
            c.Text(L.Get("media.offset"), bounds.X, y, 12, Muted, bounds.Width); y += 24;
            var field = new Rect(bounds.X, y, bounds.Width, 32);
            MediaOffsetBounds = field;
            c.Fill(field, Surface, 4);
            DrawInputText(c, new(field.X + 8, field.Y + 7, field.Width - 16, 20),
                MediaOffsetFocused ? mediaOffsetText : mediaPanelValues.OffsetMs.ToString(CultureInfo.InvariantCulture), 13, MediaOffsetFocused, "media:offset");
            hits.Add(new(field, () =>
            {
                if (mediaPanelValues.Video is null || !CommitField()) return;
                mediaOffsetOwner = history; mediaOffsetEditing = true;
                mediaOffsetText = BeatmapMedia.Read(Document).OffsetMs.ToString(CultureInfo.InvariantCulture);
                SelectInput("media:offset", mediaOffsetText);
            }, mediaPanelValues.Video is not null && !MediaEditingBusy));
            y += 44;
            MediaSyncBounds = WrappedButton(L.Get("media.sync"), SynchronizeMedia, difficulties.Count > 1 && !SynchronizationBusy && !MediaEditingBusy);
            foreach (var line in WrapSyncText(c, L.Get("media.dropHint"), bounds.Width))
            { c.Text(line.Text, bounds.X, y, 12, Muted, bounds.Width); y += 18; }
            y += 16;
            Button(c, new(bounds.X, y, bounds.Width, 30), L.Get("media.folderFiles"), RefreshMediaFiles, enabled: mediaFilesTask is null, fontSize: 12); y += 40;
            foreach (string file in mediaFilesDirectory == directory ? mediaFiles : [])
            {
                if (y + 60 < bounds.Y || y > bounds.Bottom) { y += 60; continue; }
                string name = Path.GetRelativePath(directory!, file);
                bool bound = MediaFileVersion.SamePath(file, MediaPath(mediaPanelValues.Background)) || MediaFileVersion.SamePath(file, MediaPath(mediaPanelValues.Video));
                c.Text(name, bounds.X, y, 12, Foreground, bounds.Width); y += 20;
                Button(c, new(bounds.X, y, bounds.Width, 28), L.Get(bound ? "media.bound" : "media.bind"),
                    () => ImportMediaFile(file, IsBeatmapVideo(file)), active: bound, enabled: !bound && !MediaEditingBusy, fontSize: 12); y += 40;
            }
        }
        else
        {
            c.Text(L.Get("media.colourCount", mediaColourCount), bounds.X, y, 12, Foreground, bounds.Width); y += 26;
            for (int n = 3; n <= 8; n++)
            {
                int count = n; float cell = (bounds.Width - 25) / 6;
                Button(c, new(bounds.X + (n - 3) * (cell + 5), y, cell, 30), n.ToString(),
                    () => { mediaColourCount = count; mediaPalette = []; }, mediaColourCount == n, enabled: !MediaPaletteBusy, fontSize: 12);
            }
            y += 42;
            MediaGenerateBounds = WrappedButton(L.Get(MediaPaletteBusy ? "media.generating" : "media.generate"), GenerateMediaPalette,
                mediaBackgroundExists && !MediaPaletteBusy && DecodePaletteImage is not null);
            bool current = ReferenceEquals(mediaPaletteOwner, history) && mediaPalettePath == MediaPath(mediaPanelValues.Background);
            if (current && mediaPalette.Length > 0)
            {
                for (int i = 0; i < mediaPalette.Length; i++)
                {
                    uint colour = mediaPalette[i];
                    var swatch = new Rect(bounds.X + i % 2 * (half + 6), y + i / 2 * 40, half, 32);
                    c.Fill(swatch, colour, 4);
                    c.Text($"#{colour:X6}", swatch.X + 8, swatch.Y + 8, 12, BackgroundPalette.PerceivedBrightness(colour) > 145 ? 0x101010u : 0xFFFFFFu, swatch.Width - 16);
                }
                y += (mediaPalette.Length + 1) / 2 * 40 + 8;
                MediaApplyBounds = WrappedButton(L.Get("media.apply"), ApplyMediaPalette, !MediaPaletteBusy);
            }
            foreach (var line in WrapSyncText(c, L.Get("media.paletteHint"), bounds.Width))
            { c.Text(line.Text, bounds.X, y, 12, Muted, bounds.Width); y += 18; }
        }
        c.Text(MediaEditingBusy ? L.Get("media.copying") : mediaPanelError, bounds.X, y + 12, 12, MediaEditingBusy ? Muted : Error, bounds.Width);
        mediaPanelContentHeight = y + 44 - (bounds.Y - mediaPanelScroll);
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--)
            if (hits[i].Bounds.Y < bounds.Y || hits[i].Bounds.Bottom > bounds.Bottom) hits.RemoveAt(i);
        if (mediaPanelContentHeight > bounds.Height)
        {
            float thumb = Math.Max(20, bounds.Height * bounds.Height / mediaPanelContentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + mediaPanelScroll / (mediaPanelContentHeight - bounds.Height) * (bounds.Height - thumb), 3, thumb), Muted, 1);
        }

        Rect WrappedButton(string label, Action action, bool enabled)
        {
            var lines = WrapSyncText(c, label, bounds.Width - 18);
            float height = Math.Max(34, lines.Length * 18 + 12);
            var button = new Rect(bounds.X, y, bounds.Width, height);
            Button(c, button, "", action, enabled: enabled);
            for (int i = 0; i < lines.Length; i++) c.Text(lines[i].Text, bounds.X + 9, y + 6 + i * 18, 12, enabled ? Foreground : Muted, bounds.Width - 18);
            y += height + 14;
            return button;
        }

        void Slot(bool video, string? name, bool exists)
        {
            c.Text(L.Get(video ? "media.video" : "media.background"), bounds.X, y, 13, Foreground, bounds.Width, true); y += 24;
            c.Text(name ?? L.Get("media.unbound"), bounds.X, y, 12, name is null || exists ? Muted : Error, bounds.Width); y += 24;
            if (name is not null && !exists) { c.Text(L.Get("media.missing"), bounds.X, y, 12, Error, bounds.Width); y += 22; }
            Button(c, new(bounds.X, y, half, 30), L.Get("media.choose"), () => ChooseMedia(video), enabled: !MediaEditingBusy && RequestMediaFile is not null, fontSize: 12);
            Button(c, new(bounds.X + half + 6, y, half, 30), L.Get("media.clear"), () =>
            {
                if (!CommitField()) return;
                var value = BeatmapMedia.Read(Document);
                SetMedia(video ? value with { Video = null, OffsetMs = 0 } : value with { Background = null });
            }, enabled: name is not null && !MediaEditingBusy, fontSize: 12);
            y += 46;
        }
    }

    private void ChooseMedia(bool video)
    {
        if (!PrepareFileOperation() || MediaEditingBusy) return;
        var owner = history;
        RequestMediaFile?.Invoke(video, path =>
        {
            if (path is not null && ReferenceEquals(owner, history) && CanDropBackground)
                ImportMediaFile(path, video);
        });
    }

    private void SetMedia(BeatmapMedia value)
    {
        if (value == BeatmapMedia.Read(Document)) return;
        if (Edit(L.Get("media.title"), () => value.Apply(Document)))
        { mediaPanelSnapshot = null; mediaFilesDirectory = null; RefreshBeatmapBackground(); }
    }

    private bool CommitMediaOffset()
    {
        if (!mediaOffsetEditing) return true;
        if (!ReferenceEquals(mediaOffsetOwner, history)) { mediaOffsetEditing = false; return true; }
        if (!int.TryParse(mediaOffsetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset))
        { mediaPanelError = L.Get("media.invalidOffset"); return false; }
        mediaOffsetEditing = false; mediaPanelError = "";
        var value = BeatmapMedia.Read(Document);
        if (value.Video is not null) SetMedia(value with { OffsetMs = offset });
        return true;
    }
    private void MediaOffsetText(string text) => mediaOffsetText = InsertInput("media:offset", mediaOffsetText,
        new string(text.Where(c => char.IsAsciiDigit(c) || c is '-' or '+').ToArray()), 12);
    private void MediaOffsetKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { mediaOffsetEditing = false; mediaPanelError = ""; return; }
        if (key is 13 or 9) { CommitMediaOffset(); return; }
        InputKey("media:offset", ref mediaOffsetText, key, ctrl, shift, 12, RequestPasteField);
    }

    internal void ImportMediaFile(string path, bool video)
    {
        if (MediaEditingBusy || !PrepareFileOperation()) return;
        if (!(video ? IsBeatmapVideo(path) : IsBackgroundImage(path))) { ShowError(L.Get("media.unsupported")); return; }
        bool needsResourceRoot = NeedsMediaResourceRoot(Document);
        if (needsResourceRoot && WorkspaceSession is null)
        { BeginWorkspaceSave(saved => { if (saved) ImportMediaFile(path, video); }); return; }
        string directory = needsResourceRoot
            ? MediaResourceRoot(difficulties[activeDifficulty].Id)
            : StoryboardMediaDirectory(Document)!;
        var owner = history;
        var resources = needsResourceRoot ? CaptureMediaResources(Document) : null;
        mediaPanelError = "";
        mediaEditTask = Task.Run<Action>(() =>
        {
            if (resources is not null) CopyExistingMediaResources(resources, directory);
            string name = CopyMediaFile(path, directory, reuseIdentical: needsResourceRoot);
            string marker = Path.Combine(directory, ".fa-resource");
            if (needsResourceRoot && !File.Exists(marker)) File.WriteAllText(marker, "FruitsAtelier resource root");
            return () =>
            {
                if (!ReferenceEquals(owner, history)) return;
                var before = BeatmapMedia.Read(Document);
                var value = video ? before with { Video = name, OffsetMs = 0 } : before with { Background = name };
                if (needsResourceRoot)
                {
                    if (Edit(L.Get("media.title"), () => { Document.SourcePath = marker; value.Apply(Document); }))
                    { mediaPanelSnapshot = null; RefreshBeatmapBackground(); }
                }
                else SetMedia(value);
            };
        });
    }

    private bool NeedsMediaResourceRoot(MapDocument document)
    {
        string? origin = document.SourcePath ?? document.AudioPath;
        return origin is null || !(Within(LibrarySettings.Workspace) || Within(LibrarySettings.Songs));
        bool Within(string root) => !string.IsNullOrWhiteSpace(root) && WorkspaceProject.Within(root, origin!);
    }
    private string MediaResourceRoot(Guid difficultyId) => Path.Combine(LibrarySettings.Workspace, "Resources", "Media-" + difficultyId.ToString("N"));
    private static MapDocument CaptureMediaResources(MapDocument document)
    {
        var resources = new MapDocument { SourcePath = document.SourcePath, AudioPath = document.AudioPath };
        SongSetup.Set(resources, "General", "AudioFilename", SongSetup.Get(document, "General", "AudioFilename"));
        return resources;
    }
    private static void CopyExistingMediaResources(MapDocument document, string directory)
    {
        WorkspaceProject.RejectLinks(directory);
        Directory.CreateDirectory(directory);
        Platform.BeatmapResources.Copy(document, directory, document);
        Platform.LibraryOperations.CopyArchiveExtras(document, directory);
    }

    internal static string CopyMediaFile(string path, string directory, bool reuseIdentical = false)
    {
        path = Path.GetFullPath(path); directory = Path.GetFullPath(directory);
        WorkspaceProject.RejectLinks(path); WorkspaceProject.RejectLinks(directory);
        if (!File.Exists(path)) throw new FileNotFoundException(L.Get("resource.missing", path), path);
        string name = Path.GetFileName(path);
        Platform.BeatmapArchive.ValidateRelativePath(name);
        if (name.IndexOfAny(['"', '\r', '\n']) >= 0) throw new InvalidDataException(L.Get("media.unsupported"));
        if (WorkspaceProject.Within(directory, path)) return Path.GetRelativePath(directory, path);
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, name);
        string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
        for (int suffix = 1; File.Exists(target) || Directory.Exists(target); suffix++)
        {
            WorkspaceProject.RejectLinks(target);
            if (reuseIdentical && File.Exists(target) && new FileInfo(path).Length == new FileInfo(target).Length
                && WorkspaceProject.Hash(path) == WorkspaceProject.Hash(target)) return Path.GetFileName(target);
            target = Path.Combine(directory, $"{stem} ({suffix}){extension}");
        }
        File.Copy(path, target, overwrite: false);
        return Path.GetFileName(target);
    }

    private void SynchronizeMedia()
    {
        if (!PrepareFileOperation() || MediaEditingBusy || SynchronizationBusy || difficulties.Any(d => d.History.HasActiveTransaction)) return;
        var owner = history; var source = BeatmapMedia.Read(Document);
        string? background = MediaPath(source.Background), video = MediaPath(source.Video);
        var targets = difficulties.Where(d => d.History != owner).Select(d =>
            (Owner: d.History, Before: BeatmapMedia.Read(d.History.Document), OriginalPath: d.History.Document.SourcePath,
                BackgroundLines: CaptureMediaLines(d.History.Document, false), VideoLines: CaptureMediaLines(d.History.Document, true),
                NeedsRoot: NeedsMediaResourceRoot(d.History.Document), Resources: CaptureMediaResources(d.History.Document),
                Directory: NeedsMediaResourceRoot(d.History.Document) ? MediaResourceRoot(d.Id) : StoryboardMediaDirectory(d.History.Document))).ToArray();
        mediaEditTask = Task.Run<Action>(() =>
        {
            foreach (var target in targets)
                if (target.NeedsRoot && (background is not null || video is not null)) CopyExistingMediaResources(target.Resources, target.Directory!);
            var prepared = targets.Select(t => (t.Owner, t.Before, t.OriginalPath, t.BackgroundLines, t.VideoLines,
                NewPath: t.NeedsRoot && (background is not null || video is not null) ? Path.Combine(t.Directory!, ".fa-resource") : t.OriginalPath,
                After: new BeatmapMedia(
                background is null ? null : CopyMediaFile(background, t.Directory ?? throw new InvalidOperationException(L.Get("media.noDirectory")), reuseIdentical: true),
                video is null ? null : CopyMediaFile(video, t.Directory ?? throw new InvalidOperationException(L.Get("media.noDirectory")), reuseIdentical: true), source.OffsetMs))).ToArray();
            foreach (var target in prepared)
                if (target.NewPath != target.OriginalPath && !File.Exists(target.NewPath)) File.WriteAllText(target.NewPath!, "FruitsAtelier resource root");
            return () =>
            {
                if (!ReferenceEquals(owner, history) || !prepared.Any(t => t.Before != t.After)) return;
                history.EditRelatedDocuments(L.Get("media.sync"), forward =>
                {
                    foreach (var target in prepared)
                        if (difficulties.Any(d => d.History == target.Owner))
                        {
                            var expected = forward ? target.Before : target.After;
                            var value = forward ? target.After : target.Before;
                            var current = BeatmapMedia.Read(target.Owner.Document);
                            bool replaceBackground = current.Background == expected.Background;
                            bool replaceVideo = current.Video == expected.Video && current.OffsetMs == expected.OffsetMs;
                            string? expectedPath = forward ? target.OriginalPath : target.NewPath;
                            string? newPath = forward ? target.NewPath : target.OriginalPath;
                            bool replacePath = target.Owner.Document.SourcePath == expectedPath;
                            target.Owner.RebaseSharedMetadata(d =>
                            {
                                var media = BeatmapMedia.Read(d);
                                (media with { Background = replaceBackground ? value.Background : media.Background,
                                    Video = replaceVideo ? value.Video : media.Video, OffsetMs = replaceVideo ? value.OffsetMs : media.OffsetMs }).Apply(d);
                                if (!forward)
                                {
                                    if (replaceBackground) RestoreMediaLines(d, target.BackgroundLines, false);
                                    if (replaceVideo) RestoreMediaLines(d, target.VideoLines, true);
                                }
                                if (replacePath && replaceBackground && replaceVideo) d.SourcePath = newPath;
                            });
                        }
                });
            };
        });
    }

    private static bool MediaEventLine(string line, bool video)
    {
        int comma = line.IndexOf(',');
        if (comma < 0) return false;
        string kind = line[..comma].Trim();
        return video ? kind is "1" or "Video" : kind is "0" or "Background";
    }
    private static string[] CaptureMediaLines(MapDocument document, bool video) => document.OriginalSections
        .Where(s => s.Name == "Events").SelectMany(s => s.Lines).Where(line => MediaEventLine(line, video)).ToArray();
    private static void RestoreMediaLines(MapDocument document, string[] lines, bool video)
    {
        foreach (var section in document.OriginalSections.Where(s => s.Name == "Events"))
            section.Lines.RemoveAll(line => MediaEventLine(line, video));
        if (lines.Length == 0) return;
        var events = document.OriginalSections.First(s => s.Name == "Events");
        for (int i = lines.Length - 1; i >= 0; i--) events.Lines.Insert(0, lines[i]);
    }

    private void GenerateMediaPalette()
    {
        if (MediaPaletteBusy || DecodePaletteImage is not { } decode || MediaPath(BeatmapMedia.Read(Document).Background) is not { } path) return;
        mediaPaletteOwner = history; mediaPalettePath = path; mediaPaletteVersion = MediaFileVersion.Read(path);
        mediaPalette = []; mediaPanelError = ""; int count = mediaColourCount;
        mediaPaletteTask = Task.Run(() => decode(path) is { } pixels ? BackgroundPalette.Generate(pixels, count) : []);
    }
    private void ApplyMediaPalette()
    {
        if (!CommitField() || mediaPalette.Length == 0 || !ReferenceEquals(mediaPaletteOwner, history)
            || mediaPalettePath != MediaPath(BeatmapMedia.Read(Document).Background)) return;
        if (mediaPaletteVersion != MediaFileVersion.Read(mediaPalettePath!))
        { mediaPalette = []; mediaPanelError = L.Get("media.regenerate"); return; }
        Edit(L.Get("media.apply"), () => SongSetup.SetColours(Document, mediaPalette));
    }
    private void PumpMediaPanel()
    {
        if (mediaFilesTask is { IsCompleted: true } files)
        {
            mediaFilesTask = null; mediaFilesDirectory = mediaFilesRequestedDirectory;
            try { mediaFiles = files.GetAwaiter().GetResult(); }
            catch (Exception e) { mediaFiles = []; mediaPanelError = e.Message; }
        }
        if (mediaEditTask is { IsCompleted: true } edit)
        {
            mediaEditTask = null;
            try { edit.GetAwaiter().GetResult()(); }
            catch (Exception e) { ShowError(e.Message); }
        }
        if (mediaPaletteTask is { IsCompleted: true } palette)
        {
            mediaPaletteTask = null;
            try
            {
                var result = palette.GetAwaiter().GetResult();
                if (ReferenceEquals(mediaPaletteOwner, history) && mediaPalettePath == MediaPath(BeatmapMedia.Read(Document).Background)
                    && mediaPaletteVersion == MediaFileVersion.Read(mediaPalettePath!))
                { mediaPalette = result; if (result.Length == 0) mediaPanelError = L.Get("media.noColours"); }
            }
            catch (Exception e) { mediaPanelError = e.Message; }
        }
    }

    private void RefreshMediaFiles()
    {
        if (mediaFilesTask is not null) return;
        mediaPanelSnapshot = null;
        string? directory = StoryboardMediaDirectory(Document);
        mediaFilesRequestedDirectory = directory;
        mediaFilesTask = Task.Run(() => directory is null || !Directory.Exists(directory) ? []
            : Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                .Where(p => IsBackgroundImage(p) || IsBeatmapVideo(p)).Take(1000).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
