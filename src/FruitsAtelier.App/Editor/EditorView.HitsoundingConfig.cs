using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool hitsoundingResourcesChanged;
    private Task<string?>? hitsoundingImportTask;
    private readonly Dictionary<string, string> hitsoundingFallbackFiles = new(StringComparer.OrdinalIgnoreCase);
    private Rect hitsoundingSampleBounds;
    private float hitsoundingSampleScroll;
    private readonly Dictionary<string, string> hitsoundingMapFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> hitsoundingMapPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? HitsoundingSlotFile(string name)
    {
        string stem = (hitsoundingBank == 2 ? "soft" : hitsoundingBank == 3 ? "drum" : "normal") + "-" + name
            + (hitsoundingIndex > 1 ? hitsoundingIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        return (hitsoundingIndex > 0 ? hitsoundingMapFiles.GetValueOrDefault(stem) : null)
            ?? hitsoundingFallbackFiles.GetValueOrDefault((hitsoundingBank == 2 ? "soft" : hitsoundingBank == 3 ? "drum" : "normal") + "-" + name)
            ?? HitsoundDefaults.Find(hitsoundingBank, name);
    }
    private string hitsoundingQuery = "";
    private string[] hitsoundingFilteredFiles = [];
    private string? hitsoundingLibraryDirectory;
    private readonly HashSet<string> hitsoundingLibraryDirectories = new(StringComparer.OrdinalIgnoreCase);
    private Rect hitsoundingSearchBounds;
    private bool HitsoundingSearchFocused => HitsoundingPageVisible && DockContentInputAvailable
        && PanelVisible(DockPanel.HitsoundingConfig) && textEditor.Field == "hitsounding:search";
    private void FilterHitsoundingLibrary()
    {
        hitsoundingFilteredFiles = hitsoundingLibraryDirectories.Order(StringComparer.OrdinalIgnoreCase).Concat(hitsoundingLibraryFiles)
            .Where(p => Path.GetFileName(p).Contains(hitsoundingQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
        hitsoundingLibraryScroll = 0;
    }
    private void HitsoundingSearchText(string text)
    {
        hitsoundingQuery = InsertInput("hitsounding:search", hitsoundingQuery, new string(text.Where(ch => !char.IsControl(ch)).ToArray()), 256);
        FilterHitsoundingLibrary();
    }
    private void DrawHitsoundingDock(ICanvas c, DockGroup group, Rect r)
    {
        float split = r.Y + r.Height * group.Split;
        hitsoundingDockBounds = r; hitsoundingSplitOwner = group;
        hitsoundingSplitBounds = new(r.X, split - 4, r.Width, 8);
        var upper = new Rect(r.X, r.Y, r.Width, Math.Max(0, split - r.Y - 5));
        var lower = new Rect(r.X, split + 5, r.Width, Math.Max(0, r.Bottom - split - 5));
        DrawSection(upper, "hitsounding.samplesets", DrawHitsoundingSamplesets);
        c.Line(r.X, split, r.Right, split, Grid, opacity: .7f);
        c.Fill(new(r.X + r.Width / 2 - 16, split - 1.5f, 32, 3),
            hitsoundingSplitDrag is not null || hitsoundingSplitBounds.Contains(mouseX, mouseY) ? Accent : Muted, 1.5f);
        DrawSection(lower, "hitsounding.library", DrawHitsoundingLibrary);
        void DrawSection(Rect bounds, string title, Action<ICanvas, Rect> draw)
        {
            c.Clip(bounds);
            c.Fill(bounds, Panel, 5); c.StrokeOpacity(bounds, Muted, 1, 5, .35f);
            c.Fill(new(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, 25), Surface, 4);
            c.Text(L.Get(title), bounds.X + 9, bounds.Y + 5, 12, Foreground, bounds.Width - 18, true);
            draw(c, new(bounds.X + 8, bounds.Y + 32, Math.Max(0, bounds.Width - 16), Math.Max(0, bounds.Height - 40)));
            c.Unclip();
        }
    }
    private void DrawHitsoundingSamplesets(ICanvas c, Rect r)
    {
        float toggleWidth = Math.Min(150, r.Width * .53f);
        var allSets = new Rect(r.X, r.Y, toggleWidth, 24);
        hits.Add(new(allSets, () => hitsoundingAllSets = !hitsoundingAllSets, true));
        c.Fill(new(r.X, r.Y + 5, 28, 14), hitsoundingAllSets ? Accent : Grid, 7);
        c.Circle(r.X + (hitsoundingAllSets ? 21 : 7), r.Y + 12, 5, Foreground);
        c.Text(L.Get("hitsounding.allSets"), r.X + 35, r.Y + 5, 10, Foreground, toggleWidth - 39);
        int[] sets = hitsoundingAllSets ? hitsoundingSets : [hitsoundingIndex];
        float chipsX = r.X + toggleWidth;
        float chipWidth = Math.Max(38, c.MeasureText(L.Get("hitsounding.default"), 10) + 20);
        int count = Math.Max(1, (int)((r.Width - toggleWidth - 32) / (chipWidth + 2)));
        hitsoundingSetScroll = Math.Clamp(hitsoundingSetScroll, 0, Math.Max(0, sets.Length - count));
        if (sets.Length > count) SetArrow(new(chipsX, r.Y, 16, 24), "‹", () => hitsoundingSetScroll = Math.Max(0, hitsoundingSetScroll - count));
        for (int i = hitsoundingSetScroll; i < Math.Min(sets.Length, hitsoundingSetScroll + count); i++)
        {
            int index = sets[i];
            Button(c, new(chipsX + 16 + (i - hitsoundingSetScroll) * (chipWidth + 2), r.Y, chipWidth, 24), index == 0 ? L.Get("hitsounding.default") : index.ToString(), () => hitsoundingIndex = index, hitsoundingIndex == index, fontSize: 10);
        }
        if (sets.Length > count) SetArrow(new(r.Right - 16, r.Y, 16, 24), "›", () => hitsoundingSetScroll = Math.Min(Math.Max(0, sets.Length - count), hitsoundingSetScroll + count));
        void SetArrow(Rect bounds, string label, Action action)
        {
            c.Text(label, bounds.X + 3, bounds.Y + 3, 14, Muted, 12);
            hits.Add(new(bounds, action, true));
        }
        for (int i = 0; i < 3; i++)
        {
            int bank = new[] { 2, 3, 1 }[i];
            Button(c, new(r.X + i * r.Width / 3, r.Y + 31, r.Width / 3 - 4, 26), BankCode(bank) + " · " + L.Get(bank == 2 ? "timing.soft" : bank == 3 ? "timing.drum" : "timing.normal"), () => hitsoundingBank = bank, bank == hitsoundingBank, fontSize: 11);
        }
        hitsoundingSampleBounds = new(r.X, r.Y + 65, r.Width, Math.Max(0, r.Height - 85));
        float rowHeight = Math.Clamp(hitsoundingSampleBounds.Height / 5, 40, 46);
        hitsoundingSampleScroll = Math.Clamp(hitsoundingSampleScroll, 0, Math.Max(0, rowHeight * 5 - hitsoundingSampleBounds.Height));
        c.Clip(hitsoundingSampleBounds);
        for (int i = 0; i < HitsoundingNames.Length; i++)
        {
            string name = HitsoundingNames[i]; string? path = HitsoundingSlotFile(name);
            var row = new Rect(r.X, hitsoundingSampleBounds.Y + i * rowHeight - hitsoundingSampleScroll, r.Width, rowHeight - 4);
            if (row.Bottom <= hitsoundingSampleBounds.Y || row.Y >= hitsoundingSampleBounds.Bottom) continue;
            var visibleRow = new Rect(row.X, Math.Max(row.Y, hitsoundingSampleBounds.Y), row.Width,
                Math.Min(row.Bottom, hitsoundingSampleBounds.Bottom) - Math.Max(row.Y, hitsoundingSampleBounds.Y));
            if (hitsoundingIndex > 0) hitsoundingSlots.Add((visibleRow, name));
            bool over = row.Contains(mouseX, mouseY);
            c.Fill(row, over ? SelectedSurface : Surface, 5);
            c.StrokeOpacity(row, over ? Accent : Muted, 1, 5, over ? .9f : .25f);
            var sound = new Hitsound(i == 4 ? CatchObjectKind.Droplet : CatchObjectKind.Fruit,
                path ?? HitsoundDefaults.Find(hitsoundingBank, name), 1, name, hitsoundingBank);
            DrawHitsoundingAudition(c, row, sound);
            float waveWidth = row.Width >= 240 ? 70 : 0;
            float textWidth = row.Width - 44 - waveWidth;
            c.Text(L.Get("hitsounding.lane" + (i + 1)), row.X + 36, row.Y + 4, 11, Foreground, textWidth);
            bool fallback = path is null || !hitsoundingMapPaths.Contains(path);
            c.Text(path is null ? L.Get("hitsounding.fallback") : Path.GetFileName(path), row.X + 36, row.Y + 20, 9, fallback ? Muted : Accent, textWidth);
            if (waveWidth > 0) DrawHitsoundingPreview(c, new(row.Right - waveWidth - 6, row.Y + 7, waveWidth, row.Height - 14), sound);
            if (path is not null) hitsoundingFileRows.Add((new(visibleRow.X + 32, visibleRow.Y, visibleRow.Width - 32, visibleRow.Height), path));
        }
        c.Unclip();
        c.Text(L.Get(hitsoundingIndex == 0 ? "hitsounding.defaultHint" : "hitsounding.slotHint"), r.X + 3, r.Bottom - 14, 9, Muted, r.Width - 6);
    }
    private void DrawHitsoundingAudition(ICanvas c, Rect row, Hitsound sound)
    {
        var hit = new Rect(row.X + 3, row.Y + 3, 28, row.Height - 6);
        c.Circle(row.X + 17, row.Y + row.Height / 2, 11, Muted, false, 1, .6f);
        c.Text("▶", row.X + 13, row.Y + row.Height / 2 - 8, 12, Foreground, 14);
        hits.Add(new(hit, () => RequestAuditionHitsound?.Invoke(sound), true));
    }
    private void DrawHitsoundingPreview(ICanvas c, Rect r, Hitsound sound)
    {
        var wave = hitsoundingAudio.Get(sound, RequestWaveform);
        if (wave is null) return;
        float center = r.Y + r.Height / 2;
        float maximum = Math.Max(.000001f, wave.Peak(0, wave.DurationMs));
        for (float x = 0; x < r.Width; x += 2)
        {
            float peak = Math.Max(.4f, wave.Peak(x / r.Width * wave.DurationMs, (x + 2) / r.Width * wave.DurationMs) / maximum * r.Height / 2);
            c.Line(r.X + x, center - peak, r.X + x, center + peak, Accent, 1, .85f);
        }
    }
    private void DrawHitsoundingLibrary(ICanvas c, Rect r)
    {
        hitsoundingLibraryBounds = r;
        bool atRoot = hitsoundingLibraryDirectory == hitsoundingLibraryRoot;
        Button(c, new(r.X, r.Y, 28, 24), "↑", () => NavigateHitsoundingDirectory(Path.GetDirectoryName(hitsoundingLibraryDirectory!)), enabled: !atRoot, fontSize: 12);
        Button(c, new(r.X + 32, r.Y, 28, 24), "⌂", () => NavigateHitsoundingDirectory(hitsoundingLibraryRoot), fontSize: 12);
        string relative = atRoot || hitsoundingLibraryRoot is null || hitsoundingLibraryDirectory is null
            ? L.Get("hitsounding.workspaceLibrary") : Path.GetRelativePath(hitsoundingLibraryRoot, hitsoundingLibraryDirectory);
        c.Text(relative, r.X + 67, r.Y + 5, 10, Muted, r.Width - 70);
        hitsoundingSearchBounds = new(r.X, r.Y + 31, r.Width, 26);
        c.Fill(hitsoundingSearchBounds, Surface, 4); c.StrokeOpacity(hitsoundingSearchBounds, HitsoundingSearchFocused ? Accent : Muted, 1, 4, .6f);
        DrawInputText(c, new(r.X + 8, r.Y + 36, r.Width - 16, 18), hitsoundingQuery, 11, HitsoundingSearchFocused, "hitsounding:search");
        if (hitsoundingQuery.Length == 0 && !HitsoundingSearchFocused) c.Text(L.Get("hitsounding.search"), r.X + 8, r.Y + 36, 11, Muted, r.Width - 16);
        hits.Add(new(hitsoundingSearchBounds, () => FocusInput("hitsounding:search", hitsoundingQuery, mouseX), true));
        int visible = Math.Max(0, (int)(r.Height - 119) / 34);
        hitsoundingLibraryScroll = Math.Clamp(hitsoundingLibraryScroll, 0, Math.Max(0, hitsoundingFilteredFiles.Length - visible));
        for (int i = hitsoundingLibraryScroll; i < Math.Min(hitsoundingFilteredFiles.Length, hitsoundingLibraryScroll + visible); i++)
        {
            string path = hitsoundingFilteredFiles[i];
            var row = new Rect(r.X, r.Y + 64 + (i - hitsoundingLibraryScroll) * 34, r.Width, 30);
            if (row.Contains(mouseX, mouseY)) c.Fill(row, Surface, 4);
            if (hitsoundingLibraryDirectories.Contains(path))
            {
                c.Fill(new(row.X + 8, row.Y + 8, 10, 4), Accent, 1);
                c.Stroke(new(row.X + 8, row.Y + 11, 18, 12), Accent, radius: 2);
                c.Text(Path.GetFileName(path), row.X + 36, row.Y + 7, 11, Foreground, row.Width - 44);
                hits.Add(new(row, () => NavigateHitsoundingDirectory(path), true));
                continue;
            }
            var sound = new Hitsound(CatchObjectKind.Fruit, path, 1, "custom");
            DrawHitsoundingAudition(c, row, sound);
            float waveWidth = row.Width >= 240 ? 65 : 0;
            c.Text(Path.GetFileName(path), row.X + 36, row.Y + 7, 11, Foreground, row.Width - 44 - waveWidth);
            if (waveWidth > 0) DrawHitsoundingPreview(c, new(row.Right - waveWidth - 4, row.Y + 6, waveWidth, 18), sound);
            hitsoundingFileRows.Add((new(row.X + 32, row.Y, row.Width - 32, row.Height), path));
        }
        var dropArea = new Rect(r.X, r.Bottom - 46, r.Width, 44);
        c.StrokeOpacity(dropArea, hitsoundingDragFile is not null ? Accent : Muted, 1, 5, .55f);
        c.Text("↓", dropArea.X + 10, dropArea.Y + 11, 16, Accent, 18);
        c.Text(L.Get(hitsoundingImportTask is not null ? "hitsounding.importing" : "hitsounding.libraryHint"), dropArea.X + 34, dropArea.Y + 14, 10, Muted, dropArea.Width - 42);
    }
    private void RefreshHitsoundingLibrary()
    {
        string? root = string.IsNullOrWhiteSpace(LibrarySettings.Workspace) ? null : Path.GetFullPath(Path.Combine(LibrarySettings.Workspace, "HitsoundLibrary"));
        if (root != hitsoundingLibraryRoot) hitsoundingLibraryDirectory = root;
        hitsoundingLibraryRoot = root;
        hitsoundingLibraryFiles = []; hitsoundingLibraryDirectories.Clear();
        if (!HitsoundingDirectoryInsideRoot(hitsoundingLibraryDirectory)) hitsoundingLibraryDirectory = root;
        try
        {
            if (hitsoundingLibraryDirectory is { } current && HitsoundingDirectoryInsideRoot(current) && Directory.Exists(current))
            {
                var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
                hitsoundingLibraryDirectories.UnionWith(Directory.EnumerateDirectories(current, "*", options).Take(20000));
                hitsoundingLibraryFiles = Directory.EnumerateFiles(current, "*", options).Where(IsHitsoundingAudio)
                    .Order(StringComparer.OrdinalIgnoreCase).Take(20000).ToArray();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowError(error.Message); }
        FilterHitsoundingLibrary();
    }
    private bool HitsoundingDirectoryInsideRoot(string? path)
    {
        if (path is null || hitsoundingLibraryRoot is null) return false;
        string full = Path.GetFullPath(path), root = Path.TrimEndingDirectorySeparator(hitsoundingLibraryRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.Equals(root, comparison) && !full.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return false;
        for (string? check = full; check is not null; check = Path.GetDirectoryName(check))
        {
            if (Directory.Exists(check) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) return false;
            if (check.Equals(root, comparison)) return true;
        }
        return false;
    }
    internal void NavigateHitsoundingDirectory(string? path)
    {
        try
        {
            if (!HitsoundingDirectoryInsideRoot(path) || !Directory.Exists(path)) return;
            hitsoundingLibraryDirectory = Path.GetFullPath(path!); hitsoundingQuery = "";
            textEditor.Field = ""; RefreshHitsoundingLibrary();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { ShowError(error.Message); }
    }
    private static bool IsHitsoundingAudio(string path) => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3";
    private void ImportHitsoundingLibrary(IEnumerable<string> paths)
    {
        if (hitsoundingImportTask is not null) return;
        RefreshHitsoundingLibrary();
        if (hitsoundingLibraryDirectory is not { } root || !HitsoundingDirectoryInsideRoot(root)) { ShowError(L.Get("hitsounding.workspaceRequired")); return; }
        var files = paths.Where(IsHitsoundingAudio).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length > 128) { ShowError(L.Get("resource.limit")); return; }
        hitsoundingImportTask = Task.Run<string?>(() =>
        {
            try { foreach (string path in files) HitsoundResourcePlan.ImportLibrarySample(root, path); return null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return error.Message; }
        });
    }
    private void PumpHitsoundingResources()
    {
        if (hitsoundingResourcesChanged)
        {
            hitsoundingResourcesChanged = false;
            ResetHitsounds(); PreloadProjectHitsounds();
        }
        if (hitsoundingImportTask is { IsCompleted: true } task)
        {
            hitsoundingImportTask = null;
            RefreshHitsoundingLibrary();
            if (task.IsCompletedSuccessfully) { if (task.Result is { } error) ShowError(error); }
            else if (task.Exception is { } error) ShowError(error.GetBaseException().Message);
        }
    }
    private void AssignHitsoundingSlot(string name, string source)
    {
        if (hitsoundingIndex == 0) return;
        if (!PrepareFileOperation()) return;
        try
        {
            string? root = Path.GetDirectoryName(Document.SourcePath ?? Document.AudioPath);
            if (root is null) { ShowError(L.Get("copier.saveFirst")); return; }
            var plan = HitsoundResourcePlan.ReplaceSample(root, hitsoundingBank, hitsoundingIndex, name, source);
            if (plan.DisplayNames.Count == 0) return;
            history.EditResources(L.Get("hitsounding.replace"), forward =>
            {
                if (forward) plan.Apply(); else plan.Restore();
                InvalidateHitsoundingResources();
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { ShowError(error.Message); }
    }
    private void InvalidateHitsoundingResources()
    {
        hitsoundingAudio.Clear();
        hitsoundingConversion = null; hitsoundConversion = null;
        hitsoundingResourcesChanged = true;
    }
    private bool HitsoundingPointerDown(float x, float y, int button)
    {
        if (!HitsoundingPageVisible || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0 || y < 122 || y >= TransportTop) return false;
        if (leftDockResizeBounds.Contains(x, y) || rightDockResizeBounds.Contains(x, y) || dockTabs.Any(t => t.Bounds.Contains(x, y))) return false;
        if (HitsoundingSearchFocused && !hitsoundingSearchBounds.Contains(x, y)) textEditor.Field = "";
        if (!hitsoundingBounds.Contains(x, y) && !hitsoundingDockBounds.Contains(x, y)) return false;
        if (BeginHitsoundingCanvasPointer(x, y, button)) return true;
        if (button == 0)
        {
            if (hitsoundingSplitBounds.Contains(x, y))
            { hitsoundingSplitDrag = hitsoundingSplitOwner; return true; }
            var row = hitsoundingFileRows.LastOrDefault(r => r.Bounds.Contains(x, y));
            if (row.Path is not null) { hitsoundingDragFile = row.Path; return true; }
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return true; }
            if (hitsoundingBounds.Contains(x, y))
            {
                hitsoundingSelectedTiming = null; hitsoundingSelected = null; hitsoundingMultiSelection.Clear();
                SelectObjects([]);
                double start = HitsoundingStart;
                SeekTo(Math.Max(0, start + (x - hitsoundingGridBounds.X) / hitsoundingGridBounds.Width * hitsoundingSpan));
            }
        }
        return true;
    }
    private bool HitsoundingPointerMove(float x, float y)
    {
        if (MoveHitsoundingCanvasPointer(x, y)) { mouseX = x; mouseY = y; return true; }
        if (hitsoundingSplitDrag is null && hitsoundingDragFile is null) return false;
        mouseX = x; mouseY = y;
        if (hitsoundingSplitDrag is { } group) group.Split = Math.Clamp((y - hitsoundingDockBounds.Y) / Math.Max(1, hitsoundingDockBounds.Height), .25f, .75f);
        return true;
    }
    private bool HitsoundingPointerUp(float x, float y)
    {
        if (EndHitsoundingCanvasPointer()) return true;
        bool active = hitsoundingSplitDrag is not null || hitsoundingDragFile is not null;
        hitsoundingSplitDrag = null;
        string? path = hitsoundingDragFile; hitsoundingDragFile = null;
        if (path is null) return active;
        var slot = hitsoundingSlots.LastOrDefault(s => s.Bounds.Contains(x, y));
        if (slot.Name is not null) AssignHitsoundingSlot(slot.Name, path);
        else if (hitsoundingLibraryBounds.Contains(x, y)) ImportHitsoundingLibrary([path]);
        return true;
    }
    private bool HitsoundingWheel(float x, float y, float delta, bool alt)
    {
        if (!HitsoundingPageVisible || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0) return false;
        if (hitsoundingInspectorBounds.Contains(x, y))
        { if (CommitField()) hitsoundingInspectorScroll = Math.Clamp(hitsoundingInspectorScroll - delta / 120 * 32, 0, hitsoundingInspectorScrollMaximum); return true; }
        if (hitsoundingSampleBounds.Contains(x, y)) { hitsoundingSampleScroll = Math.Max(0, hitsoundingSampleScroll - delta / 120 * 30); return true; }
        if (hitsoundingLibraryBounds.Contains(x, y)) { hitsoundingLibraryScroll = Math.Max(0, hitsoundingLibraryScroll - (int)(delta / 120)); return true; }
        if (!hitsoundingBounds.Contains(x, y)) return false;
        if (alt) ZoomHitsounding(Math.Pow(1.25, -delta / 120), x);
        else SeekTo(Math.Max(0, playhead - delta / 120 * renderedTiming!.At(playhead).BeatLengthMs / divisor));
        return true;
    }
}
