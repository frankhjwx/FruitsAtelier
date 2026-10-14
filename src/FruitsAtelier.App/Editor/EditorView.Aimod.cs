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
        AimodVisible = true;
        RefreshAimod();
        hits.Clear(); fields.Clear();
    }

    private void RefreshAimod()
    {
        EnsureConversion();
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
        aimodFirst = 0;
    }

    private void CloseAimod()
    {
        aimodResourceTask = null; AimodVisible = false; AimodErrors = []; AimodIssues = []; aimodRows = []; aimodFirst = 0;
        hits.Clear();
    }

    private void LocateAimodIssue(AimodIssue issue)
    {
        if (issue.TimeMs is not double time) return;
        CloseAimod();
        tool = Tool.Select;
        if (issue.ObjectIds is { Length: > 0 } ids) SelectObjects(ids, ids[0]);
        SeekTo(time);
    }

    private void DrawAimod(ICanvas c)
    {
        if (!AimodVisible) return;
        if (aimodResourceTask is { IsCompleted: true } completed)
        {
            aimodResourceTask = null;
            var extra = completed.IsCompletedSuccessfully ? completed.Result
                : new[] { new AimodIssue(AimodCategory.Design, AimodSeverity.Error, "checkFailed", Arguments: [completed.Exception!.GetBaseException().Message]) };
            AimodIssues = AimodIssues.Concat(extra).OrderBy(i => i.Category).ThenBy(i => i.TimeMs ?? (i.Code == "previewUnset" ? double.MaxValue : double.MinValue)).ToArray();
            FilterAimod();
        }
        hits.Clear(); fields.Clear();
        float w = Math.Min(860, width - 32), h = Math.Min(540, height - 48);
        var r = new Rect((width - w) / 2, (height - h) / 2, w, h);
        c.Fill(new(0, 34, width, height - 34), 0x000000, opacity: .35f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("aimod.title"), r.X + 20, r.Y + 17, 18, Foreground, w - 200, true);
        Button(c, new(r.Right - 184, r.Y + 10, 80, 32), L.Get("aimod.refresh"), RefreshAimod);
        Button(c, new(r.Right - 96, r.Y + 10, 80, 32), L.Get("ui.close"), CloseAimod);
        c.Text(L.Get("aimod.reportSummary", CurrentDifficultyName, AimodIssues.Count(i => i.Severity == AimodSeverity.Warning), AimodIssues.Count(i => i.Severity == AimodSeverity.Error)), r.X + 20, r.Y + 57, 14,
            AimodErrors.Count > 0 ? Error : Foreground, w - 40);
        Button(c, new(r.X + 16, r.Y + 82, 260, 28), L.Get(aimodDistanceSnap ? "aimod.distanceOn" : "aimod.distanceOff"),
            () => { aimodDistanceSnap = !aimodDistanceSnap; RefreshAimod(); });
        if (aimodResourceTask is not null)
            c.Text(L.Get("aimod.checkingResources"), r.X + 290, r.Y + 88, 12, Muted, w - 310);
        for (int tab = 0; tab < 6; tab++)
        {
            AimodCategory? category = tab == 0 ? null : (AimodCategory)(tab - 1);
            int count = category is null ? AimodIssues.Count : AimodIssues.Count(i => i.Category == category);
            Button(c, new(r.X + 16 + tab * (w - 32) / 6, r.Y + 116, (w - 32) / 6 - 3, 28),
                L.Get("aimod.tab" + (category?.ToString() ?? "All"), count), () => { aimodCategory = category; FilterAimod(); }, active: category == aimodCategory);
        }
        aimodListBounds = new(r.X + 16, r.Y + 154, w - 32, h - 212);
        aimodVisibleRows = Math.Max(1, (int)(aimodListBounds.Height / 44));
        aimodFirst = Math.Clamp(aimodFirst, 0, Math.Max(0, aimodRows.Count - aimodVisibleRows));
        if (aimodRows.Count == 0)
            c.Text(L.Get("aimod.empty"), aimodListBounds.X + 12, aimodListBounds.Y + 16, 14, Foreground, aimodListBounds.Width - 24);
        for (int row = 0; row < aimodVisibleRows && aimodFirst + row < aimodRows.Count; row++)
        {
            var issue = aimodRows[aimodFirst + row];
            var bounds = new Rect(aimodListBounds.X, aimodListBounds.Y + row * 44, aimodListBounds.Width, 42);
            c.Fill(bounds, bounds.Contains(mouseX, mouseY) ? Surface : Panel, 3);
            uint colour = issue.Severity == AimodSeverity.Error ? Error : issue.Severity == AimodSeverity.Warning ? 0xE3AD42u : Foreground;
            c.Text(issue.Message, bounds.X + 10, bounds.Y + 4, 13, colour, bounds.Width - 20);
            c.Text(L.Get("aimod.severity" + issue.Severity), bounds.X + 10, bounds.Y + 23, 11, colour, 110);
            if (issue.TimeMs is double time)
                c.Text(Time(Math.Truncate(time)), bounds.X + 130, bounds.Y + 23, 12, Accent, bounds.Width - 140);
            hits.Add(new(bounds, () => LocateAimodIssue(issue), true));
        }
        Button(c, new(r.X + 16, r.Bottom - 44, 44, 30), "‹", () => aimodFirst = Math.Max(0, aimodFirst - aimodVisibleRows), enabled: aimodFirst > 0);
        Button(c, new(r.X + 66, r.Bottom - 44, 44, 30), "›", () => aimodFirst += aimodVisibleRows,
            enabled: aimodFirst + aimodVisibleRows < aimodRows.Count);
        c.Text(L.Get("aimod.range", aimodRows.Count == 0 ? 0 : aimodFirst + 1,
            Math.Min(aimodRows.Count, aimodFirst + aimodVisibleRows), aimodRows.Count), r.X + 124, r.Bottom - 36, 12, Muted, w - 144);
    }
}
