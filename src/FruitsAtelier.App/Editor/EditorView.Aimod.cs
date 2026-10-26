using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool AimodVisible => PanelVisible(DockPanel.Aimod);
    public IReadOnlyList<NoteOverlap> AimodErrors { get; private set; } = [];
    public IReadOnlyList<AimodIssue> AimodIssues { get; private set; } = [];
    private IReadOnlyList<AimodIssue> aimodRows = [];
    private AimodCategory? aimodCategory;
    private bool aimodDistanceSnap;
    private Task<IReadOnlyList<AimodIssue>>? aimodResourceTask;
    private CancellationTokenSource? aimodResourceCancellation;
    private double? aimodAudioDurationMs;
    private int aimodFirst, aimodVisibleRows = 1;
    private Rect aimodListBounds;

    public void ShowAimod()
    {
        if (!HasEditorProject || LibraryVisible || IsTestplaying || SynchronizationVisible || !ClipboardInteractionReady) return;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        if (TimingPageVisible) ShowTimingPage(false);
        bool existed = DockPanelExists(DockPanel.Aimod);
        AddDockPanel(DockPanel.Aimod, false);
        PanelGroup(DockPanel.Aimod).Active = DockPanel.Aimod;
        PanelGroup(DockPanel.Aimod).Collapsed = false;
        aimodFocused = true;
        if (existed) RefreshAimod();
        hits.Clear(); fields.Clear();
    }

    private void RefreshAimod()
    {
        CancelAimodResources();
        EnsureConversion();
        aimodSnapshot = convertedSnapshot;
        aimodAudioDurationMs = ReadyAimodAudioDurationMs;
        AimodErrors = Aimod.FindOverlaps(Document);
        try
        {
            AimodIssues = Aimod.Analyze(Document, playableExport, aimodDistanceSnap,
                difficulties.Where(d => d.History.Document != Document).Select(d => d.History.Document), aimodAudioDurationMs);
            var snapshot = Document.DeepClone();
            var cancellation = new CancellationTokenSource();
            aimodResourceCancellation = cancellation;
            aimodResourceTask = Task.Run(() => Aimod.AnalyzeResources(snapshot, path => Media.VideoMetadata.Read(path, cancellation.Token), cancellation.Token), cancellation.Token);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            AimodIssues = [new(AimodCategory.Compose, AimodSeverity.Error, "checkFailed", Arguments: [error.Message])];
            aimodResourceTask = null;
        }
        FilterAimod();
        aimodFirst = 0; aimodScrollY = 0;
    }

    private void RefreshLoadedAimod()
    {
        if (HasEditorProject && DockPanelExists(DockPanel.Aimod) && aimodSnapshot is null) RefreshAimod();
    }

    private double? ReadyAimodAudioDurationMs => AudioReady && !AudioLoading && AudioDurationMs > 0 ? AudioDurationMs : null;

    private void RefreshAimodAudio()
    {
        // Audio finishes loading after restored dock reports have already been initialized.
        if (aimodSnapshot is not null && aimodAudioDurationMs != ReadyAimodAudioDurationMs) RefreshAimod();
    }

    private void FilterAimod()
    {
        aimodRows = AimodIssues.Where(i => aimodCategory is null || i.Category == aimodCategory).ToArray();
        aimodFirst = 0; aimodScrollY = 0; aimodSelected.Clear(); aimodSelectionAnchor = -1;
    }

    private void CloseAimod()
    {
        CancelAimodResources();
        aimodResourceTask = null; AimodErrors = []; AimodIssues = []; aimodRows = []; aimodFirst = 0; aimodScrollY = 0;
        aimodSnapshot = null; aimodFocused = false; aimodGesture = AimodGesture.None; aimodSelected.Clear();
        aimodAudioDurationMs = null;
    }

    private void CancelAimodResources()
    {
        if (aimodResourceCancellation is not { } retired) return;
        retired.Cancel(); aimodResourceCancellation = null;
        if (aimodResourceTask is { } task)
            _ = task.ContinueWith(t => { _ = t.Exception; retired.Dispose(); }, TaskScheduler.Default);
        else retired.Dispose();
    }

    private void LocateAimodIssue(AimodIssue issue)
    {
        if (issue.TimeMs is not double time) return;
        aimodFocused = false;
        tool = Tool.Select;
        if (issue.ObjectIds is { Length: > 0 } ids) SelectObjects(ids, ids[0]);
        SeekTo(time);
    }

    private void PumpAimodResources()
    {
        if (aimodResourceTask is not { IsCompleted: true } completed) return;
        aimodResourceTask = null;
        aimodResourceCancellation?.Dispose(); aimodResourceCancellation = null;
        var extra = completed.IsCompletedSuccessfully ? completed.Result
            : new[] { new AimodIssue(AimodCategory.Design, AimodSeverity.Error, "checkFailed", Arguments: [completed.Exception!.GetBaseException().Message]) };
        var selected = aimodSelected.Where(i => i < aimodRows.Count).Select(i => aimodRows[i]).ToHashSet();
        AimodIssues = AimodIssues.Concat(extra).OrderBy(i => i.Category).ThenBy(i => i.TimeMs ?? (i.Code == "previewUnset" ? double.MaxValue : double.MinValue)).ToArray();
        aimodRows = AimodIssues.Where(i => aimodCategory is null || i.Category == aimodCategory).ToArray();
        aimodSelected.Clear();
        for (int i = 0; i < aimodRows.Count; i++) if (selected.Contains(aimodRows[i])) aimodSelected.Add(i);
    }

    private Rect aimodPanelBounds;
    private MapDocument? aimodSnapshot;
    private bool aimodFocused;
    internal bool AimodDocked => AimodVisible;
    internal Rect AimodPanelBounds => aimodPanelBounds;

    private void DrawAimodPanel(ICanvas c, Rect r)
    {
        PumpAimodResources();
        aimodPanelBounds = r;
        float buttonWidth = Math.Min(100, (r.Width - 8) / 3);
        Button(c, new(r.X, r.Y, buttonWidth, 28), L.Get("aimod.refresh"), RefreshAimod);
        Button(c, new(r.X + buttonWidth + 4, r.Y, buttonWidth, 28), L.Get("aimod.copy"), CopyAimodSelection, enabled: aimodSelected.Count > 0);
        Button(c, new(r.X + (buttonWidth + 4) * 2, r.Y, buttonWidth, 28), L.Get("ui.close"), () => RemoveDockPanel(DockPanel.Aimod));
        c.Text(L.Get("aimod.counts", AimodIssues.Count(i => i.Severity == AimodSeverity.Warning), AimodIssues.Count(i => i.Severity == AimodSeverity.Error)),
            r.X + 4, r.Y + 34, 12, Foreground, r.Width - 8);
        if (r.Width > 560)
            ToggleSwitch(c, new(r.Right - 240, r.Y + 28, 240, 26), L.Get("aimod.distanceSnap"), aimodDistanceSnap,
                () => { aimodDistanceSnap = !aimodDistanceSnap; RefreshAimod(); });
        int columns = r.Width < 240 ? 2 : r.Width < 600 ? 3 : 6;
        int tabRows = 6 / columns;
        for (int tab = 0; tab < 6; tab++)
        {
            AimodCategory? category = tab == 0 ? null : (AimodCategory)(tab - 1);
            int count = category is null ? AimodIssues.Count : AimodIssues.Count(i => i.Category == category);
            Button(c, new(r.X + tab % columns * r.Width / columns, r.Y + 58 + tab / columns * 28, r.Width / columns - 2, 26),
                L.Get("aimod.tab" + (category?.ToString() ?? "All"), count), () => { aimodCategory = category; FilterAimod(); }, active: category == aimodCategory,
                fontSize: r.Width / columns < 100 ? 10 : 12, textPadding: 5, textRightPadding: 3);
        }
        float tableY = r.Y + 64 + tabRows * 28;
        DrawAimodTable(c, new(r.X, tableY, r.Width, Math.Max(40, r.Bottom - tableY - 38)));
        string status = aimodResourceTask is not null ? "aimod.checkingResources" : aimodSnapshot is null || !ReferenceEquals(aimodSnapshot, convertedSnapshot)
            ? "aimod.refreshHint" : "aimod.selectHint";
        c.Text(L.Get(status), r.X + 4, r.Bottom - 22, 11, Muted, r.Width - 8);
    }
}
