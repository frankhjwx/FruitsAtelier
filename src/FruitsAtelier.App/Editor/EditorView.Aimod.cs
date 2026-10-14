using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool AimodVisible { get; private set; }
    public IReadOnlyList<NoteOverlap> AimodErrors { get; private set; } = [];
    public IReadOnlyList<AimodIssue> AimodIssues { get; private set; } = [];
    private IReadOnlyList<AimodIssue> aimodRows = [];
    private AimodCategory? aimodCategory;
    private bool aimodDistanceSnap;
    private Task<IReadOnlyList<AimodIssue>>? aimodResourceTask;
    private int aimodFirst, aimodVisibleRows = 1;
    private Rect aimodListBounds;

    public void ShowAimod()
    {
        if (!HasEditorProject || LibraryVisible || IsTestplaying || SynchronizationVisible || !ClipboardInteractionReady) return;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        if (DockPanelExists(DockPanel.Aimod) && !TimingPageVisible)
        {
            PanelGroup(DockPanel.Aimod).Active = DockPanel.Aimod;
            PanelGroup(DockPanel.Aimod).Collapsed = false;
        }
        else AimodVisible = true;
        aimodFocused = true;
        RefreshAimod();
        hits.Clear(); fields.Clear();
    }

    private void RefreshAimod()
    {
        EnsureConversion();
        aimodSnapshot = convertedSnapshot;
        AimodErrors = Aimod.FindOverlaps(Document);
        try
        {
            AimodIssues = Aimod.Analyze(Document, playableExport, aimodDistanceSnap,
                difficulties.Where(d => d.History.Document != Document).Select(d => d.History.Document));
            var snapshot = Document.DeepClone();
            aimodResourceTask = Task.Run(() => Aimod.AnalyzeResources(snapshot));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        {
            AimodIssues = [new(AimodCategory.Compose, AimodSeverity.Error, "checkFailed", Arguments: [error.Message])];
            aimodResourceTask = null;
        }
        FilterAimod();
        aimodFirst = 0;
    }

    private void FilterAimod()
    {
        aimodRows = AimodIssues.Where(i => aimodCategory is null || i.Category == aimodCategory).ToArray();
        aimodFirst = 0; aimodSelected.Clear(); aimodSelectionAnchor = -1;
    }

    private void CloseAimod()
    {
        aimodResourceTask = null; AimodVisible = false; AimodErrors = []; AimodIssues = []; aimodRows = []; aimodFirst = 0;
        aimodSnapshot = null; aimodFocused = false; aimodGesture = AimodGesture.None; aimodSelected.Clear();
        hits.Clear();
    }

    private void LocateAimodIssue(AimodIssue issue)
    {
        if (issue.TimeMs is not double time) return;
        if (AimodVisible) CloseAimod();
        else aimodFocused = false;
        tool = Tool.Select;
        if (issue.ObjectIds is { Length: > 0 } ids) SelectObjects(ids, ids[0]);
        SeekTo(time);
    }

    private void PumpAimodResources()
    {
        if (aimodResourceTask is not { IsCompleted: true } completed) return;
        aimodResourceTask = null;
        var extra = completed.IsCompletedSuccessfully ? completed.Result
            : new[] { new AimodIssue(AimodCategory.Design, AimodSeverity.Error, "checkFailed", Arguments: [completed.Exception!.GetBaseException().Message]) };
        var selected = aimodSelected.Where(i => i < aimodRows.Count).Select(i => aimodRows[i]).ToHashSet();
        AimodIssues = AimodIssues.Concat(extra).OrderBy(i => i.Category).ThenBy(i => i.TimeMs ?? (i.Code == "previewUnset" ? double.MaxValue : double.MinValue)).ToArray();
        aimodRows = AimodIssues.Where(i => aimodCategory is null || i.Category == aimodCategory).ToArray();
        aimodSelected.Clear();
        for (int i = 0; i < aimodRows.Count; i++) if (selected.Contains(aimodRows[i])) aimodSelected.Add(i);
    }

    private Rect aimodFloatingBounds;
    private Rect aimodPanelBounds, aimodTitleBounds, aimodResizeBounds;
    private MapDocument? aimodSnapshot;
    private bool aimodFocused;
    internal bool AimodDocked => PanelVisible(DockPanel.Aimod);
    internal Rect AimodPanelBounds => aimodPanelBounds;

    private void AttachAimod(bool left)
    {
        AimodVisible = false;
        AddDockPanel(DockPanel.Aimod, left);
        var group = PanelGroup(DockPanel.Aimod);
        group.Active = DockPanel.Aimod; group.Collapsed = false;
        aimodFocused = true; hits.Clear(); fields.Clear();
    }

    private void DetachAimod()
    {
        RemoveDockPanel(DockPanel.Aimod);
        AimodVisible = true; aimodFocused = true;
        hits.Clear(); fields.Clear();
    }

    private void DrawAimod(ICanvas c)
    {
        if (!AimodVisible) return;
        hits.Clear(); fields.Clear();
        float w = Math.Min(aimodFloatingBounds.Width > 0 ? aimodFloatingBounds.Width : 900, width - 32);
        float h = Math.Min(aimodFloatingBounds.Height > 0 ? aimodFloatingBounds.Height : 560, height - 48);
        float x = aimodFloatingBounds.Width > 0 ? Math.Clamp(aimodFloatingBounds.X, 0, width - w) : (width - w) / 2;
        float y = aimodFloatingBounds.Height > 0 ? Math.Clamp(aimodFloatingBounds.Y, 34, height - h) : (height - h) / 2;
        aimodFloatingBounds = new(x, y, w, h);
        c.Fill(new(0, 34, width, height - 34), 0x000000, opacity: .25f);
        c.Fill(aimodFloatingBounds, Panel, 6); c.Stroke(aimodFloatingBounds, Grid, radius: 6);
        aimodTitleBounds = new(x + 8, y + 4, w - 92, 32);
        c.Text(L.Get("aimod.title") + " · " + CurrentDifficultyName, x + 16, y + 10, 16, Foreground, w - 120, true);
        Button(c, new(x + w - 80, y + 4, 72, 30), L.Get("ui.close"), CloseAimod);
        DrawAimodPanel(c, new(x + 12, y + 40, w - 24, h - 52), false);
        aimodResizeBounds = new(x + w - 14, y + h - 14, 14, 14);
        c.Line(x + w - 12, y + h - 3, x + w - 3, y + h - 12, Muted);
    }

    private void DrawAimodPanel(ICanvas c, Rect r, bool docked)
    {
        PumpAimodResources();
        aimodPanelBounds = r;
        float buttonWidth = Math.Min(100, (r.Width - 8) / 3);
        Button(c, new(r.X, r.Y, buttonWidth, 28), L.Get("aimod.refresh"), RefreshAimod);
        Button(c, new(r.X + buttonWidth + 4, r.Y, buttonWidth, 28), L.Get("aimod.copy"), CopyAimodSelection, enabled: aimodSelected.Count > 0);
        Button(c, new(r.X + (buttonWidth + 4) * 2, r.Y, buttonWidth, 28), L.Get(docked ? "aimod.detach" : "aimod.attach"),
            () => { if (docked) DetachAimod(); else AttachAimod(false); }, enabled: !TimingPageVisible);
        c.Text(L.Get("aimod.counts", AimodIssues.Count(i => i.Severity == AimodSeverity.Warning), AimodIssues.Count(i => i.Severity == AimodSeverity.Error)),
            r.X + 4, r.Y + 34, 12, Foreground, r.Width - 8);
        if (r.Width > 560)
            Button(c, new(r.Right - 240, r.Y + 28, 240, 26), L.Get(aimodDistanceSnap ? "aimod.distanceOn" : "aimod.distanceOff"),
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
        DrawAimodTable(c, new(r.X, tableY, r.Width, Math.Max(40, r.Bottom - tableY - 62)));
        Button(c, new(r.X, r.Bottom - 56, 28, 26), "‹", () => aimodFirst = Math.Max(0, aimodFirst - aimodVisibleRows), enabled: aimodFirst > 0);
        Button(c, new(r.X + 30, r.Bottom - 56, 28, 26), "›", () => aimodFirst += aimodVisibleRows, enabled: aimodFirst + aimodVisibleRows < aimodRows.Count);
        c.Text(L.Get("aimod.range", aimodRows.Count == 0 ? 0 : aimodFirst + 1, Math.Min(aimodRows.Count, aimodFirst + aimodVisibleRows), aimodRows.Count),
            r.X + 64, r.Bottom - 50, 12, Muted, r.Width - 68);
        string status = aimodResourceTask is not null ? "aimod.checkingResources" : aimodSnapshot is null || !ReferenceEquals(aimodSnapshot, convertedSnapshot)
            ? "aimod.refreshHint" : "aimod.selectHint";
        c.Text(L.Get(status), r.X + 4, r.Bottom - 22, 11, Muted, r.Width - 8);
    }
}
