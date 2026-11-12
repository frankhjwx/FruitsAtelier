using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Rect hitsoundingLibraryScrollTrack, hitsoundingLibraryScrollThumb;
    private int hitsoundingLibraryVisibleRows;
    private bool hitsoundingLibraryScrollbarDragging;
    private float hitsoundingLibraryScrollGrab;
    internal Rect HitsoundingLibraryScrollbarBounds => hitsoundingLibraryScrollTrack;
    private void DrawHitsoundingLibraryScrollbar(ICanvas c)
    {
        hitsoundingLibraryScrollTrack = hitsoundingLibraryScrollThumb = default;
        if (hitsoundingFilteredFiles.Length <= hitsoundingLibraryVisibleRows || hitsoundingLibraryList.Height <= 0) return;
        var track = hitsoundingLibraryScrollTrack = new(hitsoundingLibraryList.Right - 9, hitsoundingLibraryList.Y, 9, hitsoundingLibraryList.Height);
        float thumb = Math.Min(track.Height, Math.Max(24, track.Height * hitsoundingLibraryVisibleRows / hitsoundingFilteredFiles.Length));
        hitsoundingLibraryScrollThumb = new(track.X + 1, track.Y + (track.Height - thumb) * hitsoundingLibraryScroll / Math.Max(1, hitsoundingFilteredFiles.Length - hitsoundingLibraryVisibleRows), 7, thumb);
        c.Fill(track, Surface, 4); c.Fill(hitsoundingLibraryScrollThumb, hitsoundingLibraryScrollbarDragging ? Accent : Muted, 3);
    }
    private void MoveHitsoundingLibraryScrollbar(float y)
    {
        float travel = hitsoundingLibraryScrollTrack.Height - hitsoundingLibraryScrollThumb.Height;
        int maximum = Math.Max(0, hitsoundingFilteredFiles.Length - hitsoundingLibraryVisibleRows);
        hitsoundingLibraryScroll = travel <= 0 ? 0 : (int)Math.Round(Math.Clamp((y - hitsoundingLibraryScrollTrack.Y - hitsoundingLibraryScrollGrab) / travel, 0, 1) * maximum);
    }
    public Action<IReadOnlyList<string>>? RequestRecycleHitsoundingFiles { get; set; }
    private readonly HashSet<string> hitsoundingLibrarySelection = new(WorkspaceSynchronization.Paths);
    private readonly List<(Rect Bounds, string Path)> hitsoundingLibraryRows = [];
    private Rect hitsoundingLibrarySection, hitsoundingLibraryList;
    private bool hitsoundingLibraryFocused, hitsoundingLibraryPressed, hitsoundingLibraryDragging, hitsoundingLibraryBox;
    private bool hitsoundingLibraryAdd, hitsoundingLibrarySubtract, hitsoundingLibraryCopyDrag;
    private (float X, float Y) hitsoundingLibraryStart;
    private float hitsoundingLibraryStartContentY;
    private string? hitsoundingLibraryPressedPath, hitsoundingLibraryAnchor;
    private string[] hitsoundingLibraryBefore = [], hitsoundingFileClipboard = [], hitsoundingLibraryDragPaths = [];
    private string? hitsoundingHoverButton;
    private double hitsoundingHoverStarted;
    private bool hitsoundingHoverNavigated;
    private bool HitsoundingLibraryHoverNeedsRedraw => hitsoundingLibraryDragging && hitsoundingHoverButton is not null && !hitsoundingHoverNavigated;
    private void UpdateHitsoundingLibraryHover()
    {
        string? button = hitsoundingLibraryDragging && !hitsoundingLibraryBox && hitsoundingLibraryDragPaths.Length > 0
            ? new Rect(hitsoundingLibraryBounds.X, hitsoundingLibraryBounds.Y, 28, 24).Contains(mouseX, mouseY) ? "parent"
                : new Rect(hitsoundingLibraryBounds.X + 32, hitsoundingLibraryBounds.Y, 28, 24).Contains(mouseX, mouseY) ? "home" : null
            : null;
        if (button != hitsoundingHoverButton)
        { hitsoundingHoverButton = button; hitsoundingHoverStarted = CanvasDimNowMs; hitsoundingHoverNavigated = false; }
        if (button is null || hitsoundingHoverNavigated || CanvasDimNowMs - hitsoundingHoverStarted < 1000) return;
        hitsoundingHoverNavigated = true;
        string? target = button == "home" ? hitsoundingLibraryRoot
            : hitsoundingLibraryDirectory == hitsoundingLibraryRoot ? null : Path.GetDirectoryName(hitsoundingLibraryDirectory);
        if (target is not null) NavigateHitsoundingDirectory(target);
    }
    private string? hitsoundingClipboardRoot;
    private bool hitsoundingFileCut;
    private Action? hitsoundingFileCompleted;
    private bool hitsoundingNaming;
    private string hitsoundingName = "";
    private string? hitsoundingRenamePath;
    private bool HitsoundingNameFocused => hitsoundingNaming && HitsoundingPageVisible && DockContentInputAvailable;
    private bool HitsoundingLibraryAvailable => HitsoundingPageVisible && DockContentInputAvailable && PanelVisible(DockPanel.HitsoundingConfig);

    private void BeginHitsoundingName(string? rename = null)
    {
        if (hitsoundingImportTask is not null || hitsoundingLibraryDirectory is null) return;
        hitsoundingRenamePath = rename;
        hitsoundingName = rename is null ? L.Get("hitsounding.newFolder") : Path.GetFileName(rename);
        hitsoundingNaming = true; hitsoundingLibraryFocused = true;
        SelectInput("hitsounding:name", hitsoundingName);
    }

    private void HitsoundingNameText(string text) => hitsoundingName = InsertInput("hitsounding:name", hitsoundingName,
        new string(text.Where(ch => !char.IsControl(ch)).ToArray()), 200);

    private void CommitHitsoundingName()
    {
        if (hitsoundingLibraryRoot is not { } root || hitsoundingLibraryDirectory is not { } directory) return;
        string name = hitsoundingName, source = hitsoundingRenamePath ?? "";
        RunHitsoundingFileOperation(() =>
        {
            if (source.Length > 0 && name == Path.GetFileName(source)) return;
            string target = HitsoundLibraryFiles.NamedPath(root, directory, name);
            if (source.Length == 0) Directory.CreateDirectory(target);
            else
            {
                HitsoundLibraryFiles.Validate(root, source);
                if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
            }
        });
        hitsoundingNaming = false; textEditor.Field = "";
    }

    private void RunHitsoundingFileOperation(Action operation, Action? completed = null)
    {
        if (hitsoundingImportTask is not null) return;
        StopHitsoundingLibraryAudition();
        hitsoundingFileCompleted = completed;
        hitsoundingImportTask = Task.Run<string?>(() =>
        {
            try { operation(); return null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { return error.Message; }
        });
    }

    private void CopyHitsoundingFiles(bool cut)
    {
        hitsoundingFileClipboard = hitsoundingFilteredFiles.Where(hitsoundingLibrarySelection.Contains).ToArray();
        hitsoundingClipboardRoot = hitsoundingLibraryRoot; hitsoundingFileCut = cut;
    }

    private void TransferHitsoundingFiles(string[] paths, string destination, bool move, bool clipboard = false)
    {
        if (hitsoundingLibraryRoot is not { } root || paths.Length == 0) return;
        string[] targets = [];
        RunHitsoundingFileOperation(() => targets = HitsoundLibraryFiles.Transfer(root, paths, destination, move), () =>
        {
            if (clipboard && move) { hitsoundingFileClipboard = []; hitsoundingFileCut = false; }
            if (WorkspaceSynchronization.Paths.Equals(hitsoundingLibraryDirectory, destination))
            { hitsoundingLibrarySelection.Clear(); hitsoundingLibrarySelection.UnionWith(targets); }
        });
    }

    private void PasteHitsoundingFiles()
    {
        if (hitsoundingLibraryDirectory is { } directory && WorkspaceSynchronization.Paths.Equals(hitsoundingClipboardRoot, hitsoundingLibraryRoot))
            TransferHitsoundingFiles(hitsoundingFileClipboard, directory, hitsoundingFileCut, clipboard: true);
    }

    private void DeleteHitsoundingFiles()
    {
        if (hitsoundingLibraryRoot is not { } root || RequestRecycleHitsoundingFiles is not { } recycle) return;
        string[] selected = hitsoundingFilteredFiles.Where(hitsoundingLibrarySelection.Contains).ToArray();
        if (selected.Length == 0) return;
        RunHitsoundingFileOperation(() =>
        {
            foreach (string path in selected) HitsoundLibraryFiles.Validate(root, path);
            recycle(selected);
        });
    }

    private void OpenHitsoundingLibraryMenu(float x, float y)
    {
        int scroll = hitsoundingLibraryScroll; RefreshHitsoundingLibrary(); hitsoundingLibraryScroll = scroll;
        hitsoundingLibraryFocused = true; hitsoundingNaming = false; textEditor.Field = "";
        string? path = hitsoundingLibraryRows.FirstOrDefault(row => row.Bounds.Contains(x, y)).Path;
        if (path is not null && !hitsoundingLibrarySelection.Contains(path))
        { hitsoundingLibrarySelection.Clear(); hitsoundingLibrarySelection.Add(path); }
        bool selected = hitsoundingLibrarySelection.Count > 0, available = hitsoundingImportTask is null && hitsoundingLibraryDirectory is not null;
        contextItems.Clear();
        contextItems.Add(new(L.Get("hitsounding.openFolder"), () =>
        {
            if (hitsoundingLibraryRoot is not { } root || hitsoundingLibraryDirectory is not { } folder) return;
            try { HitsoundLibraryFiles.Validate(root, folder, allowRoot: true); Directory.CreateDirectory(folder); RequestOpenExternalPath?.Invoke(folder); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowError(error.Message); }
        }, hitsoundingLibraryDirectory is not null));
        contextItems.Add(new(L.Get("hitsounding.newFolder"), () => BeginHitsoundingName(), available, ShortcutKeys("sampleNewFolder", ShortcutScope.Samples)));
        contextItems.Add(new(L.Get("hitsounding.rename"), () => BeginHitsoundingName(hitsoundingLibrarySelection.Single()), available && hitsoundingLibrarySelection.Count == 1, ShortcutKeys("sampleRename", ShortcutScope.Samples)));
        AddContextSeparator();
        contextItems.Add(new(L.Get("hitsounding.copyFiles"), () => CopyHitsoundingFiles(false), selected && available, ShortcutKeys("copy", ShortcutScope.Samples)));
        contextItems.Add(new(L.Get("hitsounding.cutFiles"), () => CopyHitsoundingFiles(true), selected && available, ShortcutKeys("cut", ShortcutScope.Samples)));
        contextItems.Add(new(L.Get("hitsounding.pasteFiles"), PasteHitsoundingFiles, available && hitsoundingFileClipboard.Length > 0 && WorkspaceSynchronization.Paths.Equals(hitsoundingClipboardRoot, hitsoundingLibraryRoot), ShortcutKeys("paste", ShortcutScope.Samples)));
        contextItems.Add(new(L.Get("hitsounding.deleteFiles"), DeleteHitsoundingFiles, selected && available && RequestRecycleHitsoundingFiles is not null, ShortcutKeys("sampleDelete", ShortcutScope.Samples)));
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 280)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 280, ContextMenuHeight);
    }

    private bool BeginHitsoundingLibraryPointer(float x, float y, int button, bool ctrl, bool shift)
    {
        if (!hitsoundingLibrarySection.Contains(x, y)) { hitsoundingLibraryFocused = false; return false; }
        hitsoundingLibraryFocused = true;
        if (button == 2) { OpenHitsoundingLibraryMenu(x, y); return true; }
        if (button == 0 && hitsoundingLibraryScrollTrack.Contains(x, y))
        {
            hitsoundingLibraryScrollbarDragging = true;
            hitsoundingLibraryScrollGrab = hitsoundingLibraryScrollThumb.Contains(x, y) ? y - hitsoundingLibraryScrollThumb.Y : hitsoundingLibraryScrollThumb.Height / 2;
            MoveHitsoundingLibraryScrollbar(y); return true;
        }
        if (button != 0 || !hitsoundingLibraryList.Contains(x, y)) return false;
        if (hitsoundingNaming) { hitsoundingNaming = false; textEditor.Field = ""; }
        var row = hitsoundingLibraryRows.FirstOrDefault(row => row.Bounds.Contains(x, y));
        if (row.Path is not null && !hitsoundingLibraryDirectories.Contains(row.Path) && x < row.Bounds.X + 32) return false;
        hitsoundingLibraryPressed = true; hitsoundingLibraryDragging = false; hitsoundingLibraryDragPaths = []; hitsoundingHoverButton = null;
        hitsoundingLibraryPressedPath = row.Path; hitsoundingLibraryBox = row.Path is null;
        hitsoundingLibraryStart = (x, y); hitsoundingLibraryStartContentY = y - hitsoundingLibraryList.Y + hitsoundingLibraryScroll * 34;
        hitsoundingLibraryAdd = ctrl || shift; hitsoundingLibrarySubtract = altHeld; hitsoundingLibraryCopyDrag = ctrl;
        hitsoundingLibraryBefore = hitsoundingLibrarySelection.ToArray();
        if (row.Path is { } path)
        {
            if (altHeld) hitsoundingLibrarySelection.Remove(path);
            else if (shift && hitsoundingLibraryAnchor is { } anchor)
            {
                int a = Array.IndexOf(hitsoundingFilteredFiles, anchor), b = Array.IndexOf(hitsoundingFilteredFiles, path);
                if (!ctrl) hitsoundingLibrarySelection.Clear();
                if (a < 0) a = b;
                foreach (string item in hitsoundingFilteredFiles.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)) hitsoundingLibrarySelection.Add(item);
            }
            else if (ctrl) { if (!hitsoundingLibrarySelection.Add(path)) hitsoundingLibrarySelection.Remove(path); }
            else if (!hitsoundingLibrarySelection.Contains(path)) { hitsoundingLibrarySelection.Clear(); hitsoundingLibrarySelection.Add(path); }
            hitsoundingLibraryAnchor = path;
        }
        else if (!hitsoundingLibraryAdd && !altHeld) hitsoundingLibrarySelection.Clear();
        return true;
    }

    private bool MoveHitsoundingLibraryPointer(float x, float y)
    {
        if (hitsoundingLibraryScrollbarDragging) { MoveHitsoundingLibraryScrollbar(y); return true; }
        if (!hitsoundingLibraryPressed) return false;
        mouseX = x; mouseY = y;
        if (!hitsoundingLibraryDragging && Math.Abs(x - hitsoundingLibraryStart.X) + Math.Abs(y - hitsoundingLibraryStart.Y) >= ContentDragThreshold)
        {
            hitsoundingLibraryDragging = true;
            if (!hitsoundingLibraryBox && hitsoundingLibraryPressedPath is { } path && hitsoundingLibrarySelection.Contains(path))
                hitsoundingLibraryDragPaths = hitsoundingFilteredFiles.Where(hitsoundingLibrarySelection.Contains).ToArray();
        }
        UpdateHitsoundingLibraryHover();
        if (hitsoundingLibraryBox && hitsoundingLibraryDragging)
        {
            hitsoundingLibrarySelection.Clear();
            if (hitsoundingLibraryAdd || hitsoundingLibrarySubtract) hitsoundingLibrarySelection.UnionWith(hitsoundingLibraryBefore);
            float end = y - hitsoundingLibraryList.Y + hitsoundingLibraryScroll * 34;
            int first = Math.Max(0, (int)(Math.Min(hitsoundingLibraryStartContentY, end) / 34));
            int last = Math.Min(hitsoundingFilteredFiles.Length - 1, (int)(Math.Max(hitsoundingLibraryStartContentY, end) / 34));
            for (int i = first; i <= last; i++)
                if (hitsoundingLibrarySubtract) hitsoundingLibrarySelection.Remove(hitsoundingFilteredFiles[i]); else hitsoundingLibrarySelection.Add(hitsoundingFilteredFiles[i]);
        }
        return true;
    }

    private bool EndHitsoundingLibraryPointer(float x, float y)
    {
        if (hitsoundingLibraryScrollbarDragging) { MoveHitsoundingLibraryScrollbar(y); hitsoundingLibraryScrollbarDragging = false; return true; }
        if (!hitsoundingLibraryPressed) return false;
        if (hitsoundingLibraryDragging && !hitsoundingLibraryBox && hitsoundingLibraryDragPaths.Length > 0)
        {
            string[] paths = hitsoundingLibraryDragPaths;
            string? folder = HitsoundingDropFolder(x, y) ?? (hitsoundingLibraryList.Contains(x, y) ? hitsoundingLibraryDirectory : null);
            if (folder is not null) TransferHitsoundingFiles(paths, folder, !hitsoundingLibraryCopyDrag);
            else if (paths.Length == 1 && File.Exists(paths[0]))
            {
                var slot = hitsoundingSlots.LastOrDefault(slot => slot.Bounds.Contains(x, y));
                if (slot.Name is not null) AssignHitsoundingSlot(slot.Name, paths[0]);
            }
        }
        else if (!hitsoundingLibraryDragging && !hitsoundingLibraryAdd && !hitsoundingLibrarySubtract && hitsoundingLibraryPressedPath is { } path)
        { hitsoundingLibrarySelection.Clear(); hitsoundingLibrarySelection.Add(path); }
        hitsoundingLibraryPressed = hitsoundingLibraryDragging = hitsoundingLibraryBox = false;
        return true;
    }

    private string? HitsoundingDropFolder(float x, float y)
    {
        string? folder = hitsoundingLibraryRows.FirstOrDefault(row => row.Bounds.Contains(x, y) && hitsoundingLibraryDirectories.Contains(row.Path)).Path;
        if (folder is not null) return folder;
        if (new Rect(hitsoundingLibraryBounds.X, hitsoundingLibraryBounds.Y, 28, 24).Contains(x, y))
            return hitsoundingLibraryDirectory == hitsoundingLibraryRoot ? null : Path.GetDirectoryName(hitsoundingLibraryDirectory);
        if (new Rect(hitsoundingLibraryBounds.X + 32, hitsoundingLibraryBounds.Y, 28, 24).Contains(x, y)) return hitsoundingLibraryRoot;
        return null;
    }

    private bool HitsoundingLibraryDoubleClick(float x, float y)
    {
        if (!HitsoundingLibraryAvailable || contextItems.Count > 0 || !hitsoundingLibrarySection.Contains(x, y)) return false;
        hitsoundingLibraryPressed = hitsoundingLibraryDragging = hitsoundingLibraryBox = false;
        var row = hitsoundingLibraryRows.FirstOrDefault(row => row.Bounds.Contains(x, y));
        if (row.Path is not null && hitsoundingLibraryDirectories.Contains(row.Path)) NavigateHitsoundingDirectory(row.Path);
        return true;
    }

    private bool HitsoundingLibraryKey(int key, bool ctrl, bool shift)
    {
        if (!HitsoundingLibraryAvailable || contextItems.Count > 0) return false;
        if (HitsoundingNameFocused)
        {
            if (key == 27) { hitsoundingNaming = false; textEditor.Field = ""; }
            else if (key == 13) CommitHitsoundingName();
            else InputKey("hitsounding:name", ref hitsoundingName, key, ctrl, shift, 200, RequestPasteField);
            return true;
        }
        if (!hitsoundingLibraryFocused || HitsoundingSearchFocused || IsEditingText) return false;
        if (key == 27) { hitsoundingLibraryPressed = hitsoundingLibraryDragging = hitsoundingLibraryBox = false; hitsoundingLibrarySelection.Clear(); return true; }
        if (ctrl && key == 65) { hitsoundingLibrarySelection.UnionWith(hitsoundingFilteredFiles); return true; }
        if (hitsoundingImportTask is not null) return key is 46 or 113 || ctrl && key is 67 or 88 or 86 or 78;
        if (ctrl && key == 67) { CopyHitsoundingFiles(false); return true; }
        if (ctrl && key == 88) { CopyHitsoundingFiles(true); return true; }
        if (ctrl && key == 86) { PasteHitsoundingFiles(); return true; }
        if (ctrl && shift && key == 78) { BeginHitsoundingName(); return true; }
        if (key == 46) { DeleteHitsoundingFiles(); return true; }
        if (key == 113) { if (hitsoundingLibrarySelection.Count == 1) BeginHitsoundingName(hitsoundingLibrarySelection.Single()); return true; }
        if (key == 13) { if (hitsoundingLibrarySelection.Count == 1 && Directory.Exists(hitsoundingLibrarySelection.Single())) NavigateHitsoundingDirectory(hitsoundingLibrarySelection.Single()); return true; }
        if (key == 8) { if (hitsoundingLibraryDirectory != hitsoundingLibraryRoot) NavigateHitsoundingDirectory(Path.GetDirectoryName(hitsoundingLibraryDirectory)); return true; }
        return false;
    }
}
