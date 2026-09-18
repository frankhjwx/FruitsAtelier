using System.Text.Json;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed class LibraryPosition
    {
        public string Query { get; set; } = "";
        public string? Selected { get; set; }
        public float Scroll { get; set; }
        public int DifficultyScroll { get; set; }
    }
    private sealed class LibraryMemory
    {
        public bool Projects { get; set; }
        public LibraryPosition Songs { get; set; } = new();
        public LibraryPosition MyProjects { get; set; } = new();
    }
    private LibraryMemory libraryMemory = new();
    private bool libraryMemoryLoaded, libraryMemoryDirty, revealLibrarySelection;
    private DateTime libraryMemorySaveAfter;
    private Rect libraryListBounds, libraryScrollTrack, libraryScrollThumb, libraryDiffTrack, libraryDiffThumb;
    private bool libraryPointerActive, libraryPointerMoved, libraryDraggingThumb, libraryDraggingDifficulties;
    private float libraryPointerY, libraryPointerScroll, libraryThumbOffset;
    private LibraryMap? libraryPressedMap;
    private float LibraryVisibleRows => Math.Max(1, libraryListBounds.Height / 86);
    private float LibraryMaxScroll => Math.Max(0, LibrarySetCount - LibraryVisibleRows);

    private void LoadLibraryMemory()
    {
        libraryMemoryLoaded = true;
        try
        {
            string path = Path.Combine(LibrarySettings.Workspace, "library-view.json");
            libraryMemory = File.Exists(path) ? JsonSerializer.Deserialize<LibraryMemory>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { libraryMemory = new(); }
        libraryProjectsOnly = libraryMemory.Projects;
        RestoreLibraryPosition();
    }
    private void RememberLibraryPosition()
    {
        var position = new LibraryPosition { Query = libraryQuery, Selected = selectedLibraryGroup, Scroll = libraryScroll, DifficultyScroll = libraryDiffScroll };
        if (libraryProjectsOnly) libraryMemory.MyProjects = position; else libraryMemory.Songs = position;
        libraryMemory.Projects = libraryProjectsOnly;
        libraryMemoryDirty = true; libraryMemorySaveAfter = DateTime.UtcNow.AddMilliseconds(350);
    }
    public void SaveLibraryMemory()
    {
        if (!libraryMemoryLoaded || !libraryMemoryDirty) return;
        try
        {
            Directory.CreateDirectory(LibrarySettings.Workspace);
            File.WriteAllText(Path.Combine(LibrarySettings.Workspace, "library-view.json"), JsonSerializer.Serialize(libraryMemory));
            libraryMemoryDirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { libraryMemoryDirty = false; libraryError = e.Message; }
    }
    private void RestoreLibraryPosition()
    {
        var position = (libraryProjectsOnly ? libraryMemory.MyProjects : libraryMemory.Songs) ?? new();
        libraryQuery = position.Query ?? ""; selectedLibraryGroup = position.Selected;
        libraryScroll = float.IsFinite(position.Scroll) ? Math.Max(0, position.Scroll) : 0;
        libraryDiffScroll = Math.Max(0, position.DifficultyScroll);
    }
    private void SwitchLibraryCategory(bool projects)
    {
        if (libraryProjectsOnly == projects) return;
        RememberLibraryPosition(); libraryProjectsOnly = projects; RestoreLibraryPosition();
        libraryBrowser?.Retire(); libraryBrowser = null; libraryCards.Clear(); libraryResultsReady = false;
        libraryProjectsNeedReindex |= projects; libraryField = -1; contextItems.Clear();
        QueueLibrarySearch(); RememberLibraryPosition();
    }
    private void OpenSelectedLibraryMap(LibraryMap map)
    {
        libraryBrowser?.SelectMap(map);
        selectedLibraryGroup = libraryProjectsOnly ? map.ProjectPath ?? map.Directory : map.Directory;
        libraryField = -1; RememberLibraryPosition(); SaveLibraryMemory();
        RequestLibraryOpen?.Invoke(map);
    }
    private void DrawLibraryScrollbar(ICanvas c, Rect track, float offset, float total, float visible, bool details)
    {
        float maximum = Math.Max(0, total - visible);
        offset = Math.Clamp(offset, 0, maximum);
        float size = maximum == 0 ? track.Height : Math.Max(28, track.Height * visible / total);
        var thumb = new Rect(track.X, track.Y + (maximum == 0 ? 0 : offset / maximum * (track.Height - size)), track.Width, size);
        if (details) { libraryDiffTrack = track; libraryDiffThumb = thumb; }
        else { libraryScrollTrack = track; libraryScrollThumb = thumb; }
        c.Fill(track, Panel, 5);
        c.Fill(thumb, track.Contains(mouseX, mouseY) || libraryPointerActive && libraryDraggingDifficulties == details ? Accent : Muted, 5);
    }
    private bool LibraryPointerDown(float x, float y, int button)
    {
        if (contextItems.Count > 0) { if (button == 0) ActivateContextMenu(x, y); else contextItems.Clear(); return true; }
        if (librarySettingsOpen || resourcePage) return false;
        if (button == 2 && libraryListBounds.Contains(x, y))
        {
            libraryField = -1;
            contextItems.Add(new(L.Get("library.new"), () => RequestNewProject?.Invoke()));
            contextItems.Add(new(L.Get("library.importFolder"), () => RequestLibraryImport?.Invoke(true)));
            contextItems.Add(new(L.Get("library.importFile"), () => RequestLibraryImport?.Invoke(false)));
            contextBounds = new(Math.Clamp(x, 0, width - 270), Math.Clamp(y, 64, height - 108), 270, 108);
            return true;
        }
        if (button != 0) return true;
        bool thumb = libraryScrollTrack.Contains(x, y), details = libraryDiffTrack.Contains(x, y);
        if (!thumb && !details && !libraryListBounds.Contains(x, y)) return false;
        libraryPointerActive = true; libraryPointerMoved = false; libraryDraggingThumb = thumb || details;
        libraryDraggingDifficulties = details;
        libraryPointerY = y; libraryPointerScroll = details ? libraryDiffScroll : libraryScroll;
        libraryPressedMap = libraryCards.FirstOrDefault(card => card.Bounds.Contains(x, y)).Map;
        var handle = details ? libraryDiffThumb : libraryScrollThumb;
        libraryThumbOffset = handle.Contains(x, y) ? y - handle.Y : handle.Height / 2;
        if (libraryDraggingThumb) MoveLibraryPointer(y);
        return true;
    }
    private void MoveLibraryPointer(float y)
    {
        if (!libraryPointerActive) return;
        if (Math.Abs(y - libraryPointerY) > 3) libraryPointerMoved = true;
        if (libraryDraggingThumb)
        {
            var track = libraryDraggingDifficulties ? libraryDiffTrack : libraryScrollTrack;
            var thumb = libraryDraggingDifficulties ? libraryDiffThumb : libraryScrollThumb;
            float fraction = Math.Clamp((y - track.Y - libraryThumbOffset) / Math.Max(1, track.Height - thumb.Height), 0, 1);
            if (libraryDraggingDifficulties)
            {
                int count = libraryBrowser?.Selected?.Count ?? 0;
                libraryDiffScroll = (int)Math.Round(fraction * Math.Max(0, count - LibraryVisibleDifficulties));
            }
            else libraryScroll = fraction * LibraryMaxScroll;
        }
        else if (libraryPointerMoved) libraryScroll = Math.Clamp(libraryPointerScroll + (libraryPointerY - y) / 86, 0, LibraryMaxScroll);
        RememberLibraryPosition();
    }
    private void EndLibraryPointer(float x, float y)
    {
        if (!libraryPointerActive) return;
        MoveLibraryPointer(y);
        if (!libraryDraggingThumb && !libraryPointerMoved && libraryPressedMap is { } map && libraryListBounds.Contains(x, y))
        {
            libraryBrowser?.SelectMap(map);
            selectedLibraryGroup = libraryProjectsOnly ? map.ProjectPath ?? map.Directory : map.Directory;
            libraryDiffScroll = 0; libraryField = -1;
        }
        libraryPointerActive = false; libraryPressedMap = null; RememberLibraryPosition(); SaveLibraryMemory();
    }
    private int LibraryVisibleDifficulties => Math.Max(1, (int)(height - 390) / 40);
}
