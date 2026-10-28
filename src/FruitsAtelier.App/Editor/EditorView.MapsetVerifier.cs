using System.Text.RegularExpressions;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapsetVerifierReport? verifierReport;
    private Task<MapsetVerifierReport>? verifierTask;
    private Task<MapDocument?>? verifierCleanupTask;
    private EditorHistory? verifierCleanupHistory;
    private long verifierCleanupRevision;
    private Guid verifierCleanupDifficulty;
    private bool verifierCleanupCompensation;
    private CancellationTokenSource? verifierCancellation;
    private string verifierError = "", verifierSnapshotTime = "";
    private bool verifierGeneral;
    private float verifierScroll, verifierContentHeight;
    private Rect verifierBounds, verifierBody, verifierScrollbarTrack, verifierScrollbarThumb;
    private Rect verifierGeneralTab, verifierDifficultyTab, verifierRefreshButton;
    private bool verifierScrollbarDragging;
    private float verifierScrollbarOffset;
    private long? verifierCopiedAt;
    private string? verifierCopiedKey;
    private float verifierCopiedPainted;
    internal float VerifierCopyOpacity => verifierCopiedAt is { } started
        ? (float)Math.Clamp((2 - timeProvider.GetElapsedTime(started).TotalSeconds) / .4, 0, 1) : 0;
    private readonly List<Rect> verifierHandHits = [];
    private readonly Dictionary<string, Rect> verifierFilterBounds = [];
    private string? verifierSelectedLevel;
    private static readonly string[] verifierLevels = ["Error", "Problem", "Warning", "Minor", "Info"];
    internal Rect VerifierGeneralTabBounds => verifierGeneralTab;
    internal Rect VerifierDifficultyTabBounds => verifierDifficultyTab;
    internal Rect VerifierRefreshBounds => verifierRefreshButton;
    internal Rect VerifierScrollbarTrack => verifierScrollbarTrack;
    internal Rect VerifierScrollbarThumb => verifierScrollbarThumb;
    internal Rect VerifierFilterBounds(string level) => verifierFilterBounds.GetValueOrDefault(level);
    internal float VerifierScrollOffset => verifierScroll;
    internal IReadOnlyList<Rect> VerifierCopyBounds => verifierRuns.Where(r => r.CopyKey is not null)
        .Select(r => new Rect(verifierBody.X + r.Bounds.X, verifierBody.Y + r.Bounds.Y - verifierScroll, r.Bounds.Width, r.Bounds.Height)).ToArray();
    internal IReadOnlyList<Rect> VerifierFixBounds => verifierRuns.Where(r => r.Fix)
        .Select(r => new Rect(verifierBody.X + r.Bounds.X, verifierBody.Y + r.Bounds.Y - verifierScroll, r.Bounds.Width, r.Bounds.Height)).ToArray();
    public bool MapsetVerifierHandCursor => MapsetVerifierVisible && DockContentInputAvailable && menu < 0
        && contextItems.Count == 0 && !dockDragging && !verifierScrollbarDragging
        && verifierHandHits.Any(r => r.Contains(mouseX, mouseY));
    private readonly Dictionary<Guid, string> verifierInterpretations = [];
    private readonly HashSet<string> verifierCollapsed = [];
    private int verifierCollapseVersion;
    private readonly Dictionary<EditorHistory, long> verifierRevisions = [];
    private bool verifierTracking, verifierRefreshPending, verifierCompensation;
    private long verifierChangedAt;
    private readonly List<VerifierRun> verifierRuns = [];
    private (MapsetVerifierReport? Report, Guid Difficulty, bool General, float Width, string Language, int Collapsed)? verifierLayout;
    private sealed record VerifierRun(Rect Bounds, string Text, string Level = "", bool Bold = false,
        double? TimeMs = null, Guid? Difficulty = null, Action? Action = null, bool Icon = false, float Size = 13, bool Chevron = false, bool Collapsed = false, string? CopyKey = null, bool Fix = false);
    internal Func<MapsetVerifierInput[], bool, CancellationToken, Task<MapsetVerifierReport>> VerifierAnalyze { get; set; } = MapsetVerifierClient.AnalyzeAsync;
    internal MapsetVerifierReport? VerifierReport => verifierReport;
    internal Rect VerifierPanelBounds => verifierBounds;
    internal IReadOnlyList<(Rect Bounds, double TimeMs, Guid? Difficulty)> VerifierTimestampBounds => verifierRuns
        .Where(r => r.TimeMs is not null).Select(r => (new Rect(verifierBody.X + r.Bounds.X, verifierBody.Y + r.Bounds.Y - verifierScroll, r.Bounds.Width, r.Bounds.Height), r.TimeMs!.Value, r.Difficulty)).ToArray();
    public bool MapsetVerifierVisible => PanelVisible(DockPanel.MapsetVerifier);
    public bool MapsetVerifierBusy => verifierTask is not null || verifierCleanupTask is not null;
    private bool VerifierRefreshReady => verifierRefreshPending && MapsetVerifierVisible && HasEditorProject && !LibraryVisible
        && !IsTestplaying && ClipboardInteractionReady && !LegacyDropletRefreshBusy
        && difficulties.All(d => !d.History.HasActiveTransaction)
        && timeProvider.GetElapsedTime(verifierChangedAt) >= TimeSpan.FromSeconds(1);
    public bool MapsetVerifierNeedsRedraw => verifierCleanupTask is { IsCompleted: true } || verifierTask is { IsCompleted: true } || VerifierRefreshReady
        || MapsetVerifierVisible && verifierCopiedPainted > 0;

    public void ShowMapsetVerifier()
    {
        if (!HasEditorProject || LibraryVisible || IsTestplaying || !ClipboardInteractionReady) return;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        if (TimingPageVisible) ShowTimingPage(false);
        AddDockPanel(DockPanel.MapsetVerifier, false);
        var group = PanelGroup(DockPanel.MapsetVerifier); group.Active = DockPanel.MapsetVerifier; group.Collapsed = false;
        EnsureMapsetVerifierReport();
    }

    private void EnsureMapsetVerifierReport()
    {
        if (HasEditorProject && MapsetVerifierVisible && verifierReport is null && verifierTask is null && verifierError.Length == 0)
            RefreshMapsetVerifier();
    }

    public void RefreshMapsetVerifier()
    {
        if (!HasEditorProject || !ClipboardInteractionReady || IsTestplaying) return;
        CancelVerifier();
        var cancellation = new CancellationTokenSource(); verifierCancellation = cancellation;
        // Capture once at the explicit refresh boundary; conversion and resource I/O belong to the worker.
        var inputs = difficulties.Select(d => new MapsetVerifierInput(d.Id, d.Name, d.History.Document.DeepClone(), verifierInterpretations.GetValueOrDefault(d.Id))).ToArray();
        bool compensation = compensateTinyDroplets;
        TrackVerifierRevisions(); verifierRefreshPending = false;
        verifierError = "";
        verifierTask = Task.Run(() => VerifierAnalyze(inputs, compensation, cancellation.Token), cancellation.Token);
        _ = verifierTask.ContinueWith(t => { _ = t.Exception;  }, TaskScheduler.Default);
    }

    private void CancelVerifier()
    {
        verifierCancellation?.Cancel();
        if (verifierCancellation is { } retired)
        {
            if (verifierTask is { } task) _ = task.ContinueWith(_ => retired.Dispose(), TaskScheduler.Default);
            else retired.Dispose();
        }
        verifierCancellation = null;
    }

    private void PumpMapsetVerifier()
    {
        PumpVerifierFix();
        if (!MapsetVerifierVisible || !DockContentInputAvailable) verifierScrollbarDragging = false;
        if (verifierTracking && (verifierCompensation != compensateTinyDroplets || verifierRevisions.Count != difficulties.Count
            || difficulties.Any(d => !verifierRevisions.TryGetValue(d.History, out var revision) || revision != d.History.ContentRevision)))
        {
            TrackVerifierRevisions();
            verifierRefreshPending = true; verifierChangedAt = timeProvider.GetTimestamp();
            CancelVerifier(); verifierTask = null;
        }
        if (VerifierRefreshReady) RefreshMapsetVerifier();
        if (verifierTask is not { IsCompleted: true } task) return;
        verifierTask = null; verifierCancellation?.Dispose(); verifierCancellation = null;
        if (task.IsCompletedSuccessfully)
        {
            verifierReport = task.Result;
            verifierSnapshotTime = timeProvider.GetLocalNow().ToString("HH:mm:ss");
            verifierLayout = null; verifierScroll = 0;
        }
        else if (task.IsFaulted)
            verifierError = L.Get("mv.failed", task.Exception!.GetBaseException().Message);
    }

    private void TrackVerifierRevisions()
    {
        verifierRevisions.Clear();
        foreach (var difficulty in difficulties) verifierRevisions[difficulty.History] = difficulty.History.ContentRevision;
        verifierCompensation = compensateTinyDroplets; verifierTracking = true;
    }

    public void StopMapsetVerifier() => CloseMapsetVerifier();

    private void CloseMapsetVerifier()
    {
        verifierFixPrompt = null;
        verifierCleanupTask = null; verifierCleanupHistory = null;
        CancelVerifier(); verifierCancellation = null; verifierTask = null;
        verifierReport = null; verifierError = ""; verifierLayout = null; verifierRuns.Clear();
        verifierInterpretations.Clear(); verifierCollapsed.Clear(); verifierScroll = 0;
        verifierTracking = verifierRefreshPending = false; verifierRevisions.Clear();
        verifierScrollbarDragging = false; verifierScrollbarTrack = verifierScrollbarThumb = default;
        verifierHandHits.Clear(); verifierFilterBounds.Clear(); verifierSelectedLevel = null;
        verifierCopiedAt = null; verifierCopiedKey = null; verifierCopiedPainted = 0;
    }

    internal void NavigateVerifierTimestamp(Guid? difficulty, double time)
    {
        if (!double.IsFinite(time)) return;
        if (difficulty is { } id)
        {
            int index = difficulties.FindIndex(d => d.Id == id);
            if (index < 0 || !SwitchDifficulty(index)) return;
        }
        SeekTo(time);
    }

    private void DrawMapsetVerifier(ICanvas c, Rect bounds)
    {
        verifierBounds = bounds;
        var current = difficulties[activeDifficulty];
        var difficulty = verifierReport?.Difficulties.FirstOrDefault(d => d.Id == current.Id);
        verifierHandHits.Clear(); verifierFilterBounds.Clear();
        verifierCopiedPainted = 0;
        Rect? copiedButton = null;
        verifierScrollbarTrack = verifierScrollbarThumb = default;
        float y = bounds.Y + 6;
        c.Fill(bounds, Chrome(0x202B3B, Panel), 7);
        string version = verifierReport?.UpstreamTag ?? "";
        float versionWidth = Math.Min(c.MeasureText(version, 10) + 2, bounds.Width * .18f);
        verifierRefreshButton = new(bounds.Right - 8 - versionWidth - 32, y, 26, 28);
        Button(c, verifierRefreshButton, "", RefreshMapsetVerifier, enabled: !MapsetVerifierBusy);
        DrawBrowserIcon(c, verifierRefreshButton, BrowserCommand.Refresh, MapsetVerifierBusy ? Muted : Foreground);
        c.Text(version, verifierRefreshButton.Right + 6, y + 8, 10, Muted, versionWidth);
        float tabSpace = Math.Max(0, verifierRefreshButton.X - bounds.X - 20);
        float generalWidth = Math.Min(c.MeasureText(L.Get("mv.general"), 12, verifierGeneral) + 40, tabSpace * .5f);
        verifierGeneralTab = new(bounds.X + 8, y, generalWidth, 28);
        verifierDifficultyTab = new(verifierGeneralTab.Right + 5, y,
            Math.Min(c.MeasureText(current.Name, 12, !verifierGeneral) + 40, Math.Max(0, tabSpace - generalWidth - 5)), 28);
        StatusTab(verifierGeneralTab, L.Get("mv.general"), verifierReport?.General, true);
        StatusTab(verifierDifficultyTab, current.Name, difficulty?.Issues, false);
        y += 42;
        var issues = verifierGeneral ? verifierReport?.General : difficulty?.Issues;
        DrawVerifierIcon(c, new(bounds.X + 12, y, 24, 24), VerifierHighestLevel(issues));
        c.Text(verifierGeneral ? L.Get("mv.general") : current.Name, bounds.X + 46, y + 2, 16, Foreground, bounds.Width - 58, true);
        y += 34;

        void StatusTab(Rect rectangle, string title, MapsetVerifierIssue[]? rows, bool general)
        {
            Button(c, rectangle, title, () => { verifierGeneral = general; verifierScroll = 0; },
                active: verifierGeneral == general, textPadding: 32);
            DrawVerifierIcon(c, new(rectangle.X + 7, rectangle.Y + 6, 16, 16), VerifierHighestLevel(rows));
        }
        if (!verifierGeneral)
        {
            c.Text(L.Get("mv.interpretedAs", difficulty?.Stars ?? 0), bounds.X + 12, y, 11, Muted, bounds.Width - 24);
            y += 21;
            string[] levels = ["Easy", "Normal", "Hard", "Insane", "Expert"];
            string[] titles = ["Cup", "Salad", "Platter", "Rain", "Overdose"];
            int columns = bounds.Width < 400 ? 3 : 6;
            float size = Math.Max(1, (bounds.Width - 20) / columns);
            for (int i = 0; i < 6; i++)
            {
                int choice = i;
                bool active = i == 0 ? !verifierInterpretations.ContainsKey(current.Id)
                    : (difficulty?.Interpretation == levels[i - 1] || i == 5 && difficulty?.Interpretation == "Ultra");
                Button(c, new(bounds.X + 10 + i % columns * size, y + i / columns * 27, size - 2, 24),
                    i == 0 ? L.Get("mv.auto") : titles[i - 1], () =>
                    {
                        if (choice == 0) verifierInterpretations.Remove(current.Id); else verifierInterpretations[current.Id] = levels[choice - 1];
                        RefreshMapsetVerifier();
                    }, enabled: !MapsetVerifierBusy, active: active, fontSize: 11, textPadding: 3, textRightPadding: 3);
            }
            y += (6 / columns) * 27 + 8;
        }
        float filterX = bounds.X + 10, filterStart = filterX;
        foreach (string level in verifierLevels)
        {
            int count = issues?.Count(i => VerifierDisplayLevel(i.Level) == level) ?? 0;
            if (count == 0) continue;
            string label = L.Get("mv.count", count, L.Get("mv.level" + level));
            float badgeWidth = Math.Min(bounds.Width - 20, c.MeasureText(label, 10, true) + 18);
            if (filterX > filterStart && filterX + badgeWidth > bounds.Right - 10) { filterX = filterStart; y += 28; }
            var badge = new Rect(filterX, y, badgeWidth, 22);
            bool selected = verifierSelectedLevel == level;
            bool enabled = verifierSelectedLevel is null || selected;
            uint color = level == "Minor" ? 0xB4DE60u : VerifierColour(level);
            c.Fill(badge, color, 11, selected ? .32f : enabled ? .18f : .05f);
            if (selected) c.Stroke(badge, color, radius: 11);
            if (!enabled) c.Stroke(badge, Grid, radius: 11);
            c.Text(label, badge.X + 9, badge.Y + 4, 10, enabled ? color : Muted, badge.Width - 18, true);
            verifierFilterBounds[level] = badge;
            hits.Add(new(badge, () =>
            {
                verifierSelectedLevel = verifierSelectedLevel == level ? null : level;
                verifierCollapseVersion++; verifierScroll = 0;
            }, true));
            filterX += badgeWidth + 6;
        }
        if (filterX > filterStart) y += 32;
        float footer = Math.Min(36, Math.Max(0, bounds.Bottom - y));
        verifierBody = new(bounds.X + 8, y, Math.Max(1, bounds.Width - 16), Math.Max(0, bounds.Bottom - y - footer));
        if (verifierError.Length > 0)
        {
            c.Clip(verifierBody); c.Text(verifierError, verifierBody.X + 4, verifierBody.Y + 4, 12, Error, verifierBody.Width - 8); c.Unclip();
        }
        else if (verifierReport is null)
            c.Text(L.Get(MapsetVerifierBusy ? "mv.checking" : "mv.refreshHint"), verifierBody.X + 4, verifierBody.Y + 4, 12, Muted, verifierBody.Width - 8);
        else
        {
            var key = (verifierReport, current.Id, verifierGeneral, verifierBody.Width, L.Language, verifierCollapseVersion);
            if (verifierLayout != key)
            {
                LayoutVerifier(c, issues ?? [], current.Id, verifierBody.Width - 16);
                verifierLayout = key;
            }
            verifierScroll = Math.Clamp(verifierScroll, 0, Math.Max(0, verifierContentHeight - verifierBody.Height));
            c.Clip(verifierBody);
            foreach (var run in verifierRuns)
            {
                var rectangle = run.Bounds with { X = verifierBody.X + run.Bounds.X, Y = verifierBody.Y + run.Bounds.Y - verifierScroll };
                if (rectangle.Bottom <= verifierBody.Y || rectangle.Y >= verifierBody.Bottom) continue;
                uint colour = run.TimeMs is not null ? 0x38C8FF : run.Level.Length > 0 ? VerifierColour(run.Level) : Foreground;
                if (run.Icon) DrawVerifierIcon(c, rectangle, run.Level);
                else if (run.Chevron)
                {
                    float cx = rectangle.X + rectangle.Width / 2, cy = rectangle.Y + rectangle.Height / 2;
                    if (run.Collapsed)
                    {
                        c.Line(cx - 3, cy - 5, cx + 2, cy, Foreground, 1.8f);
                        c.Line(cx + 2, cy, cx - 3, cy + 5, Foreground, 1.8f);
                    }
                    else
                    {
                        c.Line(cx - 5, cy - 2, cx, cy + 3, Foreground, 1.8f);
                        c.Line(cx, cy + 3, cx + 5, cy - 2, Foreground, 1.8f);
                    }
                }
                else
                {
                    if (run.TimeMs is not null) c.Fill(rectangle, Surface, 3);
                    if (run.Fix || run.CopyKey is not null)
                    {
                        bool hovered = DockContentInputAvailable && menu < 0 && contextItems.Count == 0
                            && rectangle.Contains(mouseX, mouseY);
                        c.Fill(rectangle, hovered ? Grid : Surface, 3);
                        c.Stroke(rectangle, hovered ? Accent : Grid, radius: 3);
                        colour = Accent;
                        if (run.CopyKey is not null && run.CopyKey == verifierCopiedKey) copiedButton = rectangle;
                    }
                    c.Text(run.Text, rectangle.X + (run.TimeMs is not null ? 3 : 0), rectangle.Y + 2, run.Size, colour, rectangle.Width, run.Bold);
                }
                if (run.TimeMs is double time)
                {
                    Hit(() => NavigateVerifierTimestamp(run.Difficulty, time), hand: true);
                    if (MapsetVerifierHandCursor && rectangle.Contains(mouseX, mouseY))
                        c.Line(rectangle.X + 3, rectangle.Bottom - 2, rectangle.Right - 3, rectangle.Bottom - 2, colour);
                }
                else if (run.Action is { } action) Hit(action, hand: run.CopyKey is not null || run.Fix);

                void Hit(Action action, bool hand = false)
                {
                    var clipped = rectangle with { Y = Math.Max(rectangle.Y, verifierBody.Y), Height = Math.Max(0, Math.Min(rectangle.Bottom, verifierBody.Bottom) - Math.Max(rectangle.Y, verifierBody.Y)) };
                    hits.Add(new(clipped, action, true));
                    if (hand && clipped.Height > 0) verifierHandHits.Add(clipped);
                }
            }
            c.Unclip();
            if (verifierContentHeight > verifierBody.Height && verifierBody.Height > 0)
            {
                float height = Math.Min(verifierBody.Height, Math.Max(24, verifierBody.Height * verifierBody.Height / verifierContentHeight));
                verifierScrollbarTrack = new(verifierBody.Right - 10, verifierBody.Y, 10, verifierBody.Height);
                verifierScrollbarThumb = new(verifierScrollbarTrack.X + 1, verifierBody.Y + (verifierBody.Height - height) * verifierScroll / (verifierContentHeight - verifierBody.Height), 8, height);
                c.Fill(verifierScrollbarTrack, Surface, 3);
                c.Fill(verifierScrollbarThumb, verifierScrollbarDragging || verifierScrollbarTrack.Contains(mouseX, mouseY) ? Foreground : Muted, 3);
            }
        }
        if (copiedButton is { } copiedAnchor && VerifierCopyOpacity > 0)
        {
            verifierCopiedPainted = VerifierCopyOpacity;
            string copied = L.Get("discussion.linkCopied");
            float popupRight = Math.Max(verifierBody.X, verifierBody.Right - 12);
            float popupWidth = Math.Min(popupRight - verifierBody.X, c.MeasureText(copied, 11) + 16);
            float left = Math.Clamp(copiedAnchor.X, verifierBody.X, popupRight - popupWidth);
            float top = Math.Clamp(copiedAnchor.Bottom + 4, verifierBody.Y, Math.Max(verifierBody.Y, verifierBody.Bottom - 22));
            var popup = new Rect(left, top, popupWidth, 22);
            c.Clip(verifierBody);
            c.Fill(popup, Surface, 4, verifierCopiedPainted);
            c.StrokeOpacity(popup, Accent, 1, 4, verifierCopiedPainted);
            c.TextOpacity(copied, left + 8, top + 4, 11, Accent, popupWidth - 16, false, verifierCopiedPainted);
            c.Unclip();
        }
        if (footer > 12) c.Text(L.Get("mv.snapshot", verifierSnapshotTime), bounds.X + 10, bounds.Bottom - footer + 7, 10, Muted, bounds.Width - 20);
    }

    private static string VerifierDisplayLevel(string level) => level == "Check" ? "Info" : level;
    internal static string VerifierHighestLevel(MapsetVerifierIssue[]? issues)
        => issues is null ? "Pending" : verifierLevels.FirstOrDefault(level => issues.Any(i => VerifierDisplayLevel(i.Level) == level)) ?? "Check";
    private static uint VerifierColour(string level) => level switch
    { "Problem" => 0xFF6067, "Warning" => 0xFFB000, "Minor" or "Check" => 0x22D66C, "Error" or "Pending" => 0x9AA8BC, _ => 0x38C7B0 };

    private static void DrawVerifierIcon(ICanvas c, Rect bounds, string level)
    {
        float radius = Math.Min(bounds.Width, bounds.Height) / 2;
        float cx = bounds.X + bounds.Width / 2, cy = bounds.Y + bounds.Height / 2;
        const uint ink = 0x202B3B;
        c.Circle(cx, cy, radius, VerifierColour(level), filled: level != "Pending");
        if (level == "Pending") return;
        float scale = radius / 7;
        void Line(float x1, float y1, float x2, float y2) => c.Line(cx + x1 * scale, cy + y1 * scale, cx + x2 * scale, cy + y2 * scale, ink, 1.5f * scale);
        if (level is "Problem" or "Error") { Line(-2.5f, -2.5f, 2.5f, 2.5f); Line(-2.5f, 2.5f, 2.5f, -2.5f); }
        else if (level is "Check" or "Minor")
        {
            Line(-3, 0, -1, 2); Line(-1, 2, 3, -2);
            if (level == "Minor")
            {
                float x = cx + radius * .6f, y = cy + radius * .6f;
                c.Circle(x, y, radius * .48f, 0xFFB000);
                c.Line(x, y - radius * .25f, x, y + radius * .02f, ink, Math.Max(.8f, scale));
                c.Circle(x, y + radius * .23f, radius * .06f, ink);
            }
        }
        else if (level == "Info") { Line(0, -1, 0, 3); c.Circle(cx, cy - 3 * scale, .9f * scale, ink); }
        else { Line(0, -3.5f, 0, .5f); c.Circle(cx, cy + 3 * scale, .9f * scale, ink); }
    }

    private bool VerifierPointerDown(float x, float y, int button)
    {
        if (button != 0 || !MapsetVerifierVisible || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0
            || verifierScrollbarThumb.Height <= 0 || !verifierScrollbarTrack.Contains(x, y)) return false;
        mouseX = x; mouseY = y;
        verifierScrollbarOffset = verifierScrollbarThumb.Contains(x, y) ? y - verifierScrollbarThumb.Y : verifierScrollbarThumb.Height / 2;
        verifierScrollbarDragging = true; MoveVerifierScrollbar(y); return true;
    }
    private void MoveVerifierScrollbar(float y)
    {
        float travel = verifierScrollbarTrack.Height - verifierScrollbarThumb.Height;
        if (travel > 0) verifierScroll = Math.Clamp((y - verifierScrollbarTrack.Y - verifierScrollbarOffset) / travel, 0, 1)
            * Math.Max(0, verifierContentHeight - verifierBody.Height);
    }

    private void LayoutVerifier(ICanvas c, MapsetVerifierIssue[] issues, Guid currentDifficulty, float width)
    {
        verifierRuns.Clear();
        float y = 0;
        int shown = issues.Count(i => verifierSelectedLevel is null || verifierSelectedLevel == VerifierDisplayLevel(i.Level));
        if (shown == 0) verifierRuns.Add(new(new(4, 4, width - 8, 24), L.Get(issues.Length == 0 ? "mv.noIssues" : "mv.filteredEmpty")));
        foreach (string level in verifierLevels)
        {
            if (verifierSelectedLevel is not null && verifierSelectedLevel != level) continue;
            var groupIssues = issues.Where(i => VerifierDisplayLevel(i.Level) == level).ToArray();
            foreach (var group in groupIssues.GroupBy(i => (i.Check, i.Title)).OrderBy(g => g.Key.Title, StringComparer.Ordinal))
            {
                string collapse = level + ":" + group.Key.Check;
                bool closed = verifierCollapsed.Contains(collapse);
                float top = y;
                bool canFix = !verifierGeneral && level == "Minor"
                    && (group.Key.Check == "MapsetVerifier.Checks.AllModes.Timing.CheckUnsnaps"
                        || group.Key.Check == MapsetVerifierClient.TimingOffsetsCheck
                        || group.Key.Check == "MapsetVerifier.Checks.AllModes.Timing.CheckKiaiUnsnap");
                string fixLabel = L.Get("mv.fixForMe");
                float fixWidth = c.MeasureText(fixLabel, 11) + 14;
                float titleRight = width - 85 - (canFix ? fixWidth + 8 : 0);
                float indent = 54, x = indent;
                AddText(group.Key.Title, bold: true, max: Math.Max(indent + 1, titleRight));
                float end = y + 26;
                verifierRuns.Add(new(new(5, top + 3, 18, 18), "", Chevron: true, Collapsed: closed));
                verifierRuns.Add(new(new(30, top + 4, 16, 16), "", level, Icon: true));
                verifierRuns.Add(new(new(0, top, Math.Max(20, canFix ? titleRight : width - 82), end - top), "", Action: () =>
                {
                    if (!verifierCollapsed.Add(collapse)) verifierCollapsed.Remove(collapse);
                    verifierCollapseVersion++;
                }));
                string copyKey = (verifierGeneral ? "general" : currentDifficulty.ToString()) + ":" + collapse;
                verifierRuns.Add(new(new(Math.Max(0, width - 78), top + 2, 78, 22), L.Get("mv.copyAll", group.Count()), "Info", Action: () =>
                {
                    if (RequestCopyText is null) return;
                    RequestCopyText(string.Join(Environment.NewLine, group.Select(i => i.Message)));
                    verifierCopiedAt = timeProvider.GetTimestamp(); verifierCopiedKey = copyKey;
                }, Size: 10, CopyKey: copyKey));
                if (canFix)
                {
                    bool timingPoints = group.Key.Check == MapsetVerifierClient.TimingOffsetsCheck;
                    verifierRuns.Add(new(new(width - 86 - fixWidth, top + 2, fixWidth, 22),
                        fixLabel, Action: () =>
                        {
                            if (group.Key.Check == "MapsetVerifier.Checks.AllModes.Timing.CheckKiaiUnsnap")
                                OfferVerifierFix(currentDifficulty, VerifierFixCategory.Kiai);
                            else OfferVerifierFix(currentDifficulty, timingPoints ? VerifierFixCategory.Timing : VerifierFixCategory.Objects);
                        }, Size: 11, Fix: true));
                }
                y = end;
                if (!closed)
                    foreach (var issue in group.OrderBy(i => MapsetVerifierClient.Tokenize(i.Message).FirstOrDefault(t => t.TimeMs is not null)?.TimeMs ?? double.MinValue).ThenBy(i => i.Message, StringComparer.Ordinal))
                    {
                        indent = width < 260 ? 66 : 86; x = indent; top = y;
                        verifierRuns.Add(new(new(indent - 25, top + 4, 16, 16), "", level, Icon: true));
                        foreach (var token in MapsetVerifierClient.Tokenize(issue.Message))
                            if (token.TimeMs is { } time)
                            {
                                float measure = c.MeasureText(token.Text, 12) + 6;
                                if (x + measure > width && x > indent) { y += 22; x = indent; }
                                verifierRuns.Add(new(new(x, y, Math.Min(measure, Math.Max(1, width - indent)), 21), token.Text, TimeMs: time, Difficulty: issue.DifficultyId ?? currentDifficulty, Size: 12));
                                x += measure;
                            }
                            else AddText(token.Text);
                        y += 29;
                    }
                y += 15;

                void AddText(string text, bool bold = false, float? max = null)
                {
                    float right = max ?? width;
                    foreach (Match word in Regex.Matches(text, @"\S+\s*"))
                    {
                        string value = word.Value;
                        float measure = c.MeasureText(value, 13, bold);
                        if (x + measure > right && x > indent) { y += 22; x = indent; }
                        if (measure > right - indent)
                        {
                            foreach (char character in value)
                            {
                                float characterWidth = c.MeasureText(character.ToString(), 13, bold);
                                if (x + characterWidth > right && x > indent) { y += 22; x = indent; }
                                verifierRuns.Add(new(new(x, y, characterWidth, 22), character.ToString(), Bold: bold)); x += characterWidth;
                            }
                        }
                        else { verifierRuns.Add(new(new(x, y, measure, 22), value, Bold: bold)); x += measure; }
                    }
                }
            }
        }
        verifierContentHeight = Math.Max(30, y);
    }
}
