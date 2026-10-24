using System.Text.RegularExpressions;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapsetVerifierReport? verifierReport;
    private Task<MapsetVerifierReport>? verifierTask;
    private CancellationTokenSource? verifierCancellation;
    private string verifierError = "", verifierSnapshotTime = "";
    private bool verifierGeneral;
    private float verifierScroll, verifierContentHeight;
    private Rect verifierBounds, verifierBody;
    private readonly Dictionary<Guid, string> verifierInterpretations = [];
    private readonly HashSet<string> verifierCollapsed = [];
    private int verifierCollapseVersion;
    private readonly Dictionary<EditorHistory, long> verifierRevisions = [];
    private bool verifierTracking, verifierRefreshPending, verifierCompensation;
    private long verifierChangedAt;
    private readonly List<VerifierRun> verifierRuns = [];
    private (MapsetVerifierReport? Report, Guid Difficulty, bool General, float Width, string Language, int Collapsed)? verifierLayout;
    private sealed record VerifierRun(Rect Bounds, string Text, string Level = "", bool Bold = false,
        double? TimeMs = null, Guid? Difficulty = null, Action? Action = null, bool Icon = false, float Size = 13);
    internal Func<MapsetVerifierInput[], bool, CancellationToken, Task<MapsetVerifierReport>> VerifierAnalyze { get; set; } = MapsetVerifierClient.AnalyzeAsync;
    internal MapsetVerifierReport? VerifierReport => verifierReport;
    internal Rect VerifierPanelBounds => verifierBounds;
    internal IReadOnlyList<(Rect Bounds, double TimeMs, Guid? Difficulty)> VerifierTimestampBounds => verifierRuns
        .Where(r => r.TimeMs is not null).Select(r => (new Rect(verifierBody.X + r.Bounds.X, verifierBody.Y + r.Bounds.Y - verifierScroll, r.Bounds.Width, r.Bounds.Height), r.TimeMs!.Value, r.Difficulty)).ToArray();
    public bool MapsetVerifierVisible => PanelVisible(DockPanel.MapsetVerifier);
    public bool MapsetVerifierBusy => verifierTask is not null;
    private bool VerifierRefreshReady => verifierRefreshPending && MapsetVerifierVisible && HasEditorProject && !LibraryVisible
        && !IsTestplaying && ClipboardInteractionReady && !LegacyDropletRefreshBusy
        && difficulties.All(d => !d.History.HasActiveTransaction)
        && timeProvider.GetElapsedTime(verifierChangedAt) >= TimeSpan.FromSeconds(1);
    public bool MapsetVerifierNeedsRedraw => verifierTask is { IsCompleted: true } || VerifierRefreshReady;

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
        CancelVerifier(); verifierCancellation = null; verifierTask = null;
        verifierReport = null; verifierError = ""; verifierLayout = null; verifierRuns.Clear();
        verifierInterpretations.Clear(); verifierCollapsed.Clear(); verifierScroll = 0;
        verifierTracking = verifierRefreshPending = false; verifierRevisions.Clear();
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
        float y = bounds.Y;
        c.Fill(bounds, 0x202B3B, 7);
        Button(c, new(bounds.X + 8, y + 6, 90, 27), L.Get("mv.refresh"), RefreshMapsetVerifier, enabled: !MapsetVerifierBusy);
        c.Text(MapsetVerifierBusy ? L.Get("mv.checking") : verifierReport?.UpstreamTag ?? "", bounds.X + 108, y + 13, 11, Muted, Math.Max(0, bounds.Width - 120));
        y += 42;
        c.Text(SongSetup.Get(Document, "Metadata", "Artist") + " — " + SongSetup.Get(Document, "Metadata", "Title", Document.Name), bounds.X + 10, y, 15, Foreground, bounds.Width - 20, true);
        y += 26;
        Button(c, new(bounds.X + 8, y, 85, 28), L.Get("mv.general"), () => { verifierGeneral = true; verifierScroll = 0; }, active: verifierGeneral);
        float difficultyTabWidth = Math.Min(Math.Max(60, c.MeasureText(current.Name, 13) + 28), Math.Max(0, bounds.Width - 106));
        Button(c, new(bounds.X + 98, y, difficultyTabWidth, 28), current.Name, () => { verifierGeneral = false; verifierScroll = 0; }, active: !verifierGeneral);
        y += 39;
        c.Text(verifierGeneral ? L.Get("mv.general") : current.Name, bounds.X + 12, y, 16, Foreground, bounds.Width - 24, true);
        y += 26;
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
                LayoutVerifier(c, verifierGeneral ? verifierReport.General : difficulty?.Issues ?? [], current.Id, verifierBody.Width - 10);
                verifierLayout = key;
            }
            verifierScroll = Math.Clamp(verifierScroll, 0, Math.Max(0, verifierContentHeight - verifierBody.Height));
            c.Clip(verifierBody);
            foreach (var run in verifierRuns)
            {
                var rectangle = run.Bounds with { X = verifierBody.X + run.Bounds.X, Y = verifierBody.Y + run.Bounds.Y - verifierScroll };
                if (rectangle.Bottom <= verifierBody.Y || rectangle.Y >= verifierBody.Bottom) continue;
                uint colour = run.TimeMs is not null ? 0x38C8FF : run.Level.Length > 0 ? VerifierColour(run.Level) : Foreground;
                if (run.Icon)
                {
                    float cx = rectangle.X + 7, cy = rectangle.Y + 8;
                    c.Circle(cx, cy, 7, colour);
                    if (run.Level is "Problem" or "Error")
                    {
                        c.Line(cx - 2.5f, cy - 2.5f, cx + 2.5f, cy + 2.5f, 0x202B3B, 1.3f);
                        c.Line(cx - 2.5f, cy + 2.5f, cx + 2.5f, cy - 2.5f, 0x202B3B, 1.3f);
                    }
                    else
                    {
                        c.Line(cx, cy - 3.5f, cx, cy + .5f, 0x202B3B, 1.6f);
                        c.Circle(cx, cy + 3, .9f, 0x202B3B);
                    }
                }
                else
                {
                    if (run.TimeMs is not null) c.Fill(rectangle, Surface, 3);
                    c.Text(run.Text, rectangle.X + (run.TimeMs is not null ? 3 : 0), rectangle.Y + 2, run.Size, colour, rectangle.Width, run.Bold);
                }
                if (run.TimeMs is double time)
                    Hit(() => NavigateVerifierTimestamp(run.Difficulty, time));
                else if (run.Action is { } action) Hit(action);

                void Hit(Action action)
                {
                    var clipped = rectangle with { Y = Math.Max(rectangle.Y, verifierBody.Y), Height = Math.Max(0, Math.Min(rectangle.Bottom, verifierBody.Bottom) - Math.Max(rectangle.Y, verifierBody.Y)) };
                    hits.Add(new(clipped, action, true));
                }
            }
            c.Unclip();
            if (verifierContentHeight > verifierBody.Height && verifierBody.Height > 0)
            {
                float height = Math.Max(18, verifierBody.Height * verifierBody.Height / verifierContentHeight);
                c.Fill(new(verifierBody.Right - 5, verifierBody.Y, 5, verifierBody.Height), Surface, 2);
                c.Fill(new(verifierBody.Right - 5, verifierBody.Y + (verifierBody.Height - height) * verifierScroll / (verifierContentHeight - verifierBody.Height), 5, height), Muted, 2);
            }
        }
        if (footer > 12) c.Text(L.Get("mv.snapshot", verifierSnapshotTime), bounds.X + 10, bounds.Bottom - footer + 7, 10, Muted, bounds.Width - 20);
    }

    private static uint VerifierColour(string level) => level switch
    { "Problem" or "Error" => 0xFF6067, "Warning" => 0xFFB000, "Minor" => 0xF4D278, _ => 0x59C9E9 };

    private void LayoutVerifier(ICanvas c, MapsetVerifierIssue[] issues, Guid currentDifficulty, float width)
    {
        verifierRuns.Clear();
        float y = 0;
        if (issues.Length == 0) verifierRuns.Add(new(new(4, 4, width - 8, 24), L.Get("mv.noIssues")));
        foreach (string level in new[] { "Problem", "Warning", "Minor", "Error", "Check", "Info" })
        {
            var groupIssues = issues.Where(i => i.Level == level).ToArray();
            if (groupIssues.Length == 0) continue;
            verifierRuns.Add(new(new(4, y, width - 8, 24), L.Get("mv.count", groupIssues.Length, L.Get("mv.level" + level)), level, true, Size: 11));
            y += 31;
            foreach (var group in groupIssues.GroupBy(i => (i.Check, i.Title)).OrderBy(g => g.Key.Title, StringComparer.Ordinal))
            {
                string collapse = level + ":" + group.Key.Check;
                bool closed = verifierCollapsed.Contains(collapse);
                float top = y;
                float x = 27;
                AddText((closed ? "› " : "⌄ ") + group.Key.Title, bold: true, max: Math.Max(40, width - 105));
                float end = y + 25;
                verifierRuns.Add(new(new(4, top + 3, 14, 18), "", level, Icon: true));
                verifierRuns.Add(new(new(20, top, Math.Max(20, width - 100), end - top), "", Action: () =>
                {
                    if (!verifierCollapsed.Add(collapse)) verifierCollapsed.Remove(collapse);
                    verifierCollapseVersion++;
                }));
                verifierRuns.Add(new(new(Math.Max(0, width - 80), top, 80, 22), L.Get("mv.copyAll", group.Count()), "Info", Action: () => RequestCopyText?.Invoke(string.Join(Environment.NewLine, group.Select(i => i.Message))), Size: 10));
                y = end;
                if (!closed)
                    foreach (var issue in group.OrderBy(i => MapsetVerifierClient.Tokenize(i.Message).FirstOrDefault(t => t.TimeMs is not null)?.TimeMs ?? double.MinValue).ThenBy(i => i.Message, StringComparer.Ordinal))
                    {
                        x = 27; top = y;
                        verifierRuns.Add(new(new(7, top + 2, 14, 18), "", level, Icon: true));
                        foreach (var token in MapsetVerifierClient.Tokenize(issue.Message))
                            if (token.TimeMs is { } time)
                            {
                                float measure = c.MeasureText(token.Text, 12) + 6;
                                if (x + measure > width && x > 27) { y += 22; x = 27; }
                                verifierRuns.Add(new(new(x, y, Math.Min(measure, width - 27), 21), token.Text, TimeMs: time, Difficulty: issue.DifficultyId ?? currentDifficulty, Size: 12));
                                x += measure;
                            }
                            else AddText(token.Text);
                        y += 29;
                    }
                y += 12;

                void AddText(string text, bool bold = false, float? max = null)
                {
                    float right = max ?? width;
                    foreach (Match word in Regex.Matches(text, @"\S+\s*"))
                    {
                        string value = word.Value;
                        float measure = c.MeasureText(value, 13, bold);
                        if (x + measure > right && x > 27) { y += 22; x = 27; }
                        if (measure > right - 27)
                        {
                            foreach (char character in value)
                            {
                                float characterWidth = c.MeasureText(character.ToString(), 13, bold);
                                if (x + characterWidth > right && x > 27) { y += 22; x = 27; }
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
