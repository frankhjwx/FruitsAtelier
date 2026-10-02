using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool AimodVisible { get; private set; }
    public IReadOnlyList<NoteOverlap> AimodErrors { get; private set; } = [];
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
        AimodErrors = Aimod.FindOverlaps(Document);
        aimodFirst = 0;
    }

    private void CloseAimod()
    {
        AimodVisible = false; AimodErrors = []; aimodFirst = 0;
        hits.Clear();
    }

    private void LocateAimodError(NoteOverlap error)
    {
        CloseAimod();
        tool = Tool.Select;
        SelectObjects([error.FirstId, error.SecondId], error.FirstId);
        SeekTo(error.FirstTimeMs);
    }

    private void DrawAimod(ICanvas c)
    {
        if (!AimodVisible) return;
        hits.Clear(); fields.Clear();
        float w = Math.Min(860, width - 32), h = Math.Min(540, height - 48);
        var r = new Rect((width - w) / 2, (height - h) / 2, w, h);
        c.Fill(new(0, 34, width, height - 34), 0x000000, opacity: .35f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("aimod.title"), r.X + 20, r.Y + 17, 18, Foreground, w - 200, true);
        Button(c, new(r.Right - 184, r.Y + 10, 80, 32), L.Get("aimod.refresh"), RefreshAimod);
        Button(c, new(r.Right - 96, r.Y + 10, 80, 32), L.Get("ui.close"), CloseAimod);
        c.Text(L.Get("aimod.summary", CurrentDifficultyName, AimodErrors.Count), r.X + 20, r.Y + 57, 14,
            AimodErrors.Count > 0 ? Error : Foreground, w - 40);
        c.Text(L.Get("aimod.scope"), r.X + 20, r.Y + 85, 12, Muted, w - 40);
        aimodListBounds = new(r.X + 16, r.Y + 116, w - 32, h - 174);
        aimodVisibleRows = Math.Max(1, (int)(aimodListBounds.Height / 44));
        aimodFirst = Math.Clamp(aimodFirst, 0, Math.Max(0, AimodErrors.Count - aimodVisibleRows));
        if (AimodErrors.Count == 0)
            c.Text(L.Get("aimod.empty"), aimodListBounds.X + 12, aimodListBounds.Y + 16, 14, Foreground, aimodListBounds.Width - 24);
        for (int row = 0; row < aimodVisibleRows && aimodFirst + row < AimodErrors.Count; row++)
        {
            var error = AimodErrors[aimodFirst + row];
            var bounds = new Rect(aimodListBounds.X, aimodListBounds.Y + row * 44, aimodListBounds.Width, 42);
            c.Fill(bounds, bounds.Contains(mouseX, mouseY) ? Surface : Panel, 3);
            c.Text(L.Get("aimod.overlap"), bounds.X + 10, bounds.Y + 4, 13, Error, bounds.Width - 20);
            c.Text(L.Get("aimod.times", Time(error.FirstTimeMs), Time(error.SecondTimeMs), Number(error.SecondTimeMs - error.FirstTimeMs)),
                bounds.X + 10, bounds.Y + 23, 12, Accent, bounds.Width - 20);
            hits.Add(new(bounds, () => LocateAimodError(error), true));
        }
        Button(c, new(r.X + 16, r.Bottom - 44, 44, 30), "‹", () => aimodFirst = Math.Max(0, aimodFirst - aimodVisibleRows), aimodFirst > 0);
        Button(c, new(r.X + 66, r.Bottom - 44, 44, 30), "›", () => aimodFirst += aimodVisibleRows,
            aimodFirst + aimodVisibleRows < AimodErrors.Count);
        c.Text(L.Get("aimod.range", AimodErrors.Count == 0 ? 0 : aimodFirst + 1,
            Math.Min(AimodErrors.Count, aimodFirst + aimodVisibleRows), AimodErrors.Count), r.X + 124, r.Bottom - 36, 12, Muted, w - 144);
    }
}
