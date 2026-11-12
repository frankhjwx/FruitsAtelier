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
    private bool mediaOffsetEditing, mediaLibraryExpanded;
    private float mediaPanelScroll;
    private bool mediaScrollbarDragging, mediaIncludeSubfolders, mediaFilesRequestedSubfolders;
    private float mediaScrollbarGrab;
    internal Rect MediaScrollTrack { get; private set; }
    internal Rect MediaScrollThumb { get; private set; }
    private Rect mediaPanelBounds;
    private MapDocument? mediaPanelSnapshot;
    private BeatmapMedia mediaPanelValues;
    private bool mediaBackgroundExists, mediaVideoExists;
    private Task<string[]>? mediaFilesTask;
    private string? mediaFilesDirectory, mediaFilesRequestedDirectory;
    private string[] mediaFiles = [], mediaAvailableFiles = [];
    private bool mediaChoicesDirty = true;
    private long mediaFilesRefreshAt;
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
    internal Rect MediaLibraryBounds { get; private set; }
    internal bool MediaFilesBusy => mediaFilesTask is not null;
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
            mediaChoicesDirty = true;
            mediaPanelSnapshot = convertedSnapshot; mediaPanelValues = BeatmapMedia.Read(Document);
            mediaBackgroundExists = File.Exists(MediaPath(mediaPanelValues.Background));
            mediaVideoExists = File.Exists(MediaPath(mediaPanelValues.Video));
            if (!ReferenceEquals(mediaOffsetOwner, history)) mediaOffsetEditing = false;
        }
        mediaPanelBounds = bounds;
        string? directory = StoryboardMediaDirectory(Document);
        if (mediaFilesTask is null && (directory != mediaFilesDirectory || Environment.TickCount64 >= mediaFilesRefreshAt)) RefreshMediaFiles();
        if (mediaChoicesDirty)
        {
            string? background = MediaPath(mediaPanelValues.Background), video = MediaPath(mediaPanelValues.Video);
            mediaAvailableFiles = mediaFilesDirectory == directory
                ? mediaFiles.Where(file => !MediaFileVersion.SamePath(file, background) && !MediaFileVersion.SamePath(file, video)).ToArray() : [];
            mediaChoicesDirty = false;
        }
        mediaPanelScroll = Math.Clamp(mediaPanelScroll, 0, Math.Max(0, mediaPanelContentHeight - bounds.Height));
        float half = (bounds.Width - 6) / 2;
        c.Clip(bounds);
        float tabFont = bounds.Width < 220 ? 10 : 12;
        Button(c, new(bounds.X, bounds.Y, half, 30), L.Get("media.files"), () => SwitchTab(0), mediaTab == 0, fontSize: tabFont, textPadding: 5, textRightPadding: 3);
        Button(c, new(bounds.X + half + 6, bounds.Y, half, 30), L.Get("media.palette"), () => SwitchTab(1), mediaTab == 1, fontSize: tabFont, textPadding: 5, textRightPadding: 3);
        c.Unclip();
        var content = new Rect(bounds.X, bounds.Y + 42, bounds.Width, Math.Max(0, bounds.Height - 42));
        float y = content.Y - mediaPanelScroll;
        int firstHit = hits.Count;
        MediaOffsetBounds = MediaGenerateBounds = MediaApplyBounds = MediaSyncBounds = MediaLibraryBounds = default;
        c.Clip(content);
        if (mediaTab == 0)
        {
            Slot(false, mediaPanelValues.Background, mediaBackgroundExists);
            Slot(true, mediaPanelValues.Video, mediaVideoExists);
            if (mediaPanelValues.Video is not null)
            {
                c.Text(L.Get("media.offset"), bounds.X, y, 12, Muted, bounds.Width); y += 21;
                var field = new Rect(bounds.X, y, bounds.Width, 30);
                MediaOffsetBounds = field;
                c.Fill(field, Surface, 4);
                DrawInputText(c, new(field.X + 8, field.Y + 6, field.Width - 16, 20),
                    MediaOffsetFocused ? mediaOffsetText : mediaPanelValues.OffsetMs.ToString(CultureInfo.InvariantCulture), 13, MediaOffsetFocused, "media:offset");
                hits.Add(new(field, () =>
                {
                    if (!CommitField()) return;
                    mediaOffsetOwner = history; mediaOffsetEditing = true;
                    mediaOffsetText = BeatmapMedia.Read(Document).OffsetMs.ToString(CultureInfo.InvariantCulture);
                    SelectInput("media:offset", mediaOffsetText);
                }, !MediaEditingBusy));
                y += 37; Hint("media.offsetHelp"); y += 10;
            }
            c.Line(bounds.X, y, bounds.Right, y, Grid); y += 14;
            MediaSyncBounds = ActionButton(L.Get("media.sync"), SynchronizeMedia,
                difficulties.Count > 1 && !SynchronizationBusy && !MediaEditingBusy, false);
            y += 4;
            var toggleLines = WrapSyncText(c, L.Get("media.includeSubfolders"), bounds.Width - 62);
            var toggle = new Rect(bounds.X, y, bounds.Width, Math.Max(32, toggleLines.Length * 17 + 12));
            ToggleSwitch(c, toggle, "", mediaIncludeSubfolders, () =>
            {
                mediaIncludeSubfolders = !mediaIncludeSubfolders;
                mediaFilesDirectory = null; mediaAvailableFiles = []; mediaChoicesDirty = true;
                mediaFilesRefreshAt = 0;
                RefreshMediaFiles();
            });
            for (int i = 0; i < toggleLines.Length; i++) c.Text(toggleLines[i].Text, bounds.X, y + 6 + i * 17, 12, Foreground, bounds.Width - 62);
            y += toggle.Height + 10;
            if (mediaAvailableFiles.Length > 0)
            {
                MediaLibraryBounds = ActionButton((mediaLibraryExpanded ? "▾ " : "▸ ") + L.Get("media.library", mediaAvailableFiles.Length),
                    () => mediaLibraryExpanded = !mediaLibraryExpanded, true, false);
                if (mediaLibraryExpanded)
                foreach (string file in mediaAvailableFiles)
                {
                    bool video = IsBeatmapVideo(file);
                    string label = L.Get(video ? "media.useVideo" : "media.useBackground");
                    float rowHeight = 56 + Math.Max(32, WrapSyncText(c, label, bounds.Width - 20).Length * 17 + 12);
                    if (y + rowHeight < content.Y || y > content.Bottom) { y += rowHeight; continue; }
                    var thumbnail = new Rect(bounds.X, y, 48, 40);
                    c.Fill(thumbnail, Surface, 4);
                    if (video)
                    {
                        c.Line(thumbnail.X + 19, y + 12, thumbnail.X + 30, y + 20, Muted, 2);
                        c.Line(thumbnail.X + 30, y + 20, thumbnail.X + 19, y + 28, Muted, 2);
                        c.Line(thumbnail.X + 19, y + 28, thumbnail.X + 19, y + 12, Muted, 2);
                    }
                    else c.Thumbnail(file, thumbnail);
                    c.Text(Path.GetRelativePath(directory!, file), bounds.X + 58, y + 12, 12, Foreground, bounds.Width - 58);
                    y += 46;
                    ActionButton(label,
                        () => ImportMediaFile(file, video), !MediaEditingBusy, false);
                }
            }
        }
        else
        {
            Heading("media.paletteTitle");
            Hint("media.paletteIntro"); y += 4;
            Preview(88);
            c.Text(mediaPanelValues.Background ?? L.Get("media.noBackground"), bounds.X, y, 11, Muted, bounds.Width); y += 28;
            if (!mediaBackgroundExists)
            {
                ActionButton(L.Get("media.choose"), () => ChooseMedia(false), RequestMediaFile is not null && !MediaEditingBusy, true);
            }
            bool narrow = bounds.Width < 220;
            c.Text(L.Get("media.colourCount", mediaColourCount), bounds.X, y + 8, 12, Foreground, narrow ? bounds.Width : bounds.Width - 78);
            if (narrow) y += 29;
            Button(c, new(bounds.Right - 70, y, 32, 30), "−", () => ChangeCount(-1), enabled: mediaColourCount > 3 && !MediaPaletteBusy, fontSize: 14);
            Button(c, new(bounds.Right - 32, y, 32, 30), "+", () => ChangeCount(1), enabled: mediaColourCount < 8 && !MediaPaletteBusy, fontSize: 14);
            y += 42;
            bool current = ReferenceEquals(mediaPaletteOwner, history) && mediaPalettePath == MediaPath(mediaPanelValues.Background) && mediaPalette.Length > 0;
            MediaGenerateBounds = ActionButton(L.Get(MediaPaletteBusy ? "media.generating" : "media.generate"), GenerateMediaPalette,
                mediaBackgroundExists && !MediaPaletteBusy && DecodePaletteImage is not null, !current);
            y += 6;
            if (current)
            {
                Heading("media.result");
                for (int i = 0; i < mediaPalette.Length; i++)
                {
                    uint colour = mediaPalette[i];
                    var swatch = new Rect(bounds.X + i % 2 * (half + 6), y + i / 2 * 50, half, 42);
                    c.Fill(swatch, colour, 5);
                    c.Text($"#{colour:X6}", swatch.X + 8, swatch.Y + 14, 11, BackgroundPalette.PerceivedBrightness(colour) > 145 ? 0x101010u : 0xFFFFFFu, swatch.Width - 16);
                }
                y += (mediaPalette.Length + 1) / 2 * 50 + 4;
                MediaApplyBounds = ActionButton(L.Get("media.apply"), ApplyMediaPalette, !MediaPaletteBusy, true);
            }
            else
            {
                for (int i = 0; i < mediaColourCount; i++)
                    c.Fill(new(bounds.X + i * (bounds.Width + 4) / mediaColourCount, y, (bounds.Width + 4) / mediaColourCount - 4, 28), Surface, 4);
                y += 38; Hint("media.previewHelp");
            }
        }
        if (MediaEditingBusy || mediaPanelError.Length > 0)
        {
            y += 10;
            foreach (var line in WrapSyncText(c, MediaEditingBusy ? L.Get("media.copying") : mediaPanelError, bounds.Width))
            { c.Text(line.Text, bounds.X, y, 12, MediaEditingBusy ? Muted : Error, bounds.Width); y += 18; }
        }
        mediaPanelContentHeight = y + 12 - (bounds.Y - mediaPanelScroll);
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--)
            if (hits[i].Bounds.Y < content.Y || hits[i].Bounds.Bottom > content.Bottom) hits.RemoveAt(i);
        MediaScrollTrack = MediaScrollThumb = default;
        if (mediaPanelContentHeight > bounds.Height)
        {
            float thumb = Math.Max(20, content.Height * content.Height / (mediaPanelContentHeight - 42));
            MediaScrollTrack = new(bounds.Right + 1, content.Y, 11, content.Height);
            MediaScrollThumb = new(bounds.Right + 1, content.Y + mediaPanelScroll / (mediaPanelContentHeight - bounds.Height) * (content.Height - thumb), 11, thumb);
            c.Fill(new(bounds.Right + 5, MediaScrollThumb.Y, 3, thumb), mediaScrollbarDragging || MediaScrollTrack.Contains(mouseX, mouseY) ? Accent : Muted, 1);
        }

        void SwitchTab(int tab) { if (CommitMediaOffset()) { mediaTab = tab; mediaPanelScroll = 0; } }
        void ChangeCount(int delta) { mediaColourCount += delta; mediaPalette = []; }
        void Heading(string key)
        {
            foreach (var line in WrapSyncText(c, L.Get(key), bounds.Width))
            { c.Text(line.Text, bounds.X, y, 13, Foreground, bounds.Width, true); y += 18; }
            y += 6;
        }
        void Hint(string key)
        {
            foreach (var line in WrapSyncText(c, L.Get(key), bounds.Width))
            { c.Text(line.Text, bounds.X, y, 11, Muted, bounds.Width); y += 17; }
        }
        void Preview(float height)
        {
            var rect = new Rect(bounds.X, y, bounds.Width, height);
            c.Fill(rect, Surface, 5);
            if (mediaBackgroundExists && MediaPath(mediaPanelValues.Background) is { } path) c.Thumbnail(path, rect);
            else
            {
                c.Stroke(rect, Grid, 1, 5);
                c.Text(L.Get("media.noBackground"), rect.X + 12, rect.Y + height / 2 - 7, 12, Muted, rect.Width - 24);
            }
            y += height + 8;
        }
        Rect ActionButton(string label, Action action, bool enabled, bool primary)
        {
            var lines = WrapSyncText(c, label, bounds.Width - 20);
            float height = Math.Max(32, lines.Length * 17 + 12);
            var rect = new Rect(bounds.X, y, bounds.Width, height);
            if (primary && enabled)
            {
                c.Fill(rect, Accent, 5);
                if (contextItems.Count == 0 && rect.Contains(mouseX, mouseY)) c.Stroke(rect, Foreground, 1, 5);
                hits.Add(new(rect, action, true));
            }
            else Button(c, rect, "", action, enabled: enabled);
            for (int i = 0; i < lines.Length; i++) c.Text(lines[i].Text, bounds.X + 10, y + 6 + i * 17, 12,
                !enabled ? Muted : primary ? (BackgroundPalette.PerceivedBrightness(Accent) > 145 ? 0x101010u : 0xFFFFFFu) : Foreground, bounds.Width - 20, primary);
            y += height + 10;
            return rect;
        }
        void Slot(bool video, string? name, bool exists)
        {
            Heading(video ? "media.video" : "media.background");
            if (!video && name is not null) Preview(100);
            if (name is null)
            {
                var card = new Rect(bounds.X, y, bounds.Width, 80);
                c.Fill(card, Surface, 5); c.Stroke(card, Grid, 1, 5);
                if (!MediaEditingBusy && RequestMediaFile is not null && contextItems.Count == 0 && card.Contains(mouseX, mouseY)) c.Stroke(card, Accent, 1, 5);
                c.Text(L.Get("media.unbound"), card.X + 12, y + 10, 11, Muted, card.Width - 24);
                c.Text(L.Get("media.choose"), card.X + 12, y + 30, 13, Accent, card.Width - 24, true);
                c.Text(L.Get("media.dropShort"), card.X + 12, y + 55, 11, Muted, card.Width - 24);
                hits.Add(new(card, () => ChooseMedia(video), !MediaEditingBusy && RequestMediaFile is not null));
                y += 92;
            }
            else
            {
                c.Text(name, bounds.X, y, 12, exists ? Foreground : Error, bounds.Width); y += 21;
                if (!exists) { c.Text(L.Get("media.missing"), bounds.X, y, 11, Error, bounds.Width); y += 20; }
                Button(c, new(bounds.X, y, half, 27), L.Get("media.choose"), () => ChooseMedia(video), enabled: !MediaEditingBusy && RequestMediaFile is not null, fontSize: 11);
                var clear = new Rect(bounds.X + half + 6, y, half, 27);
                if (!MediaEditingBusy && contextItems.Count == 0 && clear.Contains(mouseX, mouseY)) c.Fill(clear, HoverSurface, 4);
                c.Text(L.Get("media.clear"), clear.X + 8, y + 6, 11, Muted, clear.Width - 16);
                hits.Add(new(clear, () =>
                {
                    if (!CommitField()) return;
                    var value = BeatmapMedia.Read(Document);
                    SetMedia(video ? value with { Video = null, OffsetMs = 0 } : value with { Background = null });
                }, !MediaEditingBusy));
                y += 39;
            }
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

    private bool MediaScrollbarPointerDown(float x, float y, int button)
    {
        if (button != 0 || !DockContentInputAvailable || !PanelVisible(DockPanel.Media)
            || menu >= 0 || contextItems.Count != 0 || MediaScrollTrack.Height <= 0 || !MediaScrollTrack.Contains(x, y)) return false;
        if (!CommitField()) return true;
        mediaScrollbarGrab = MediaScrollThumb.Contains(x, y) ? y - MediaScrollThumb.Y : MediaScrollThumb.Height / 2;
        mediaScrollbarDragging = true;
        MoveMediaScrollbar(y);
        return true;
    }

    private void MoveMediaScrollbar(float y)
    {
        float travel = MediaScrollTrack.Height - MediaScrollThumb.Height;
        if (travel <= 0) return;
        mediaPanelScroll = Math.Clamp((y - MediaScrollTrack.Y - mediaScrollbarGrab) / travel, 0, 1)
            * Math.Max(0, mediaPanelContentHeight - mediaPanelBounds.Height);
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
            mediaFilesTask = null; mediaFilesDirectory = mediaFilesRequestedDirectory; mediaChoicesDirty = true;
            try { mediaFiles = files.GetAwaiter().GetResult(); }
            catch (Exception e) { mediaFiles = []; mediaPanelError = e.Message; }
            if (mediaFilesRequestedSubfolders != mediaIncludeSubfolders)
            { mediaFiles = []; mediaFilesDirectory = null; mediaFilesRefreshAt = 0; }
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
        mediaFilesRefreshAt = Environment.TickCount64 + 5000;
        string? directory = StoryboardMediaDirectory(Document);
        mediaFilesRequestedDirectory = directory;
        bool includeSubfolders = mediaFilesRequestedSubfolders = mediaIncludeSubfolders;
        mediaFilesTask = Task.Run(() => directory is null || !Directory.Exists(directory) ? []
            : Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = includeSubfolders, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                .Where(p => IsBackgroundImage(p) || IsBeatmapVideo(p)).Take(1000).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
