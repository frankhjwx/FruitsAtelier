using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private IReadOnlyList<CollaborationMapping.ExcludedObject> collaborationExcluded = [];
    internal bool CollaborationReportVisible => collaborationExcluded.Count > 0;
    private (float X, float Y)? collaborationReportPosition;
    private (float X, float Y) collaborationReportDragOffset;
    private bool collaborationReportDragging;
    private float collaborationReportScroll;
    internal Rect CollaborationReportBounds
    {
        get
        {
            float w = Math.Min(560, width - 32), h = Math.Min(Math.Min(350, 102 + collaborationExcluded.Count * 58), height - 48);
            var position = collaborationReportPosition ?? ((width - w) / 2, (height - h) / 2);
            return new(Math.Clamp(position.Item1, 0, Math.Max(0, width - w)),
                Math.Clamp(position.Item2, 0, Math.Max(0, height - h)), w, h);
        }
    }
    private Rect CollaborationReportList => new(CollaborationReportBounds.X + 12, CollaborationReportBounds.Y + 90,
        CollaborationReportBounds.Width - 24, CollaborationReportBounds.Height - 102);

    private void CloseCollaborationReport()
    {
        collaborationExcluded = []; collaborationReportDragging = false; collaborationReportScroll = 0;
    }

    private void DrawCollaborationReport(ICanvas c)
    {
        if (!CollaborationReportVisible) return;
        var r = CollaborationReportBounds; var list = CollaborationReportList;
        collaborationReportScroll = Math.Clamp(collaborationReportScroll, 0, Math.Max(0, collaborationExcluded.Count * 58 - list.Height));
        c.Fill(r, Panel, 6); c.Stroke(r, Grid, radius: 6);
        c.Text(L.Get("collab.excludedTitle", collaborationExcluded.Count), r.X + 12, r.Y + 12, 15, Foreground, r.Width - 56, true);
        c.Text("×", r.Right - 29, r.Y + 10, 18, Foreground, 24);
        c.Text(L.Get("collab.excludedHelp"), r.X + 12, r.Y + 44, 12, Muted, r.Width - 24);
        c.Clip(list);
        int first = Math.Max(0, (int)(collaborationReportScroll / 58));
        for (int i = first; i < collaborationExcluded.Count; i++)
        {
            var item = collaborationExcluded[i]; float y = list.Y + i * 58 - collaborationReportScroll;
            if (y >= list.Bottom) break;
            string source = difficulties.FirstOrDefault(d => d.Id == item.DifficultyId)?.Name ?? L.Get("collab.missing");
            var row = new Rect(list.X, y, list.Width - 6, 54);
            c.Fill(row, row.Contains(mouseX, mouseY) ? Grid : Surface, 4);
            c.Text(L.Get("collab.excludedItem", source, L.Get(item.Spinner ? "collab.spinner" : "collab.slider"),
                Time(item.StartMs), Time(item.EndMs)), row.X + 8, y + 7, 12, Foreground, row.Width - 16);
            c.Text(L.Get("collab.excludedSection", Time(item.SectionStartMs), Time(item.SectionEndMs)),
                row.X + 8, y + 30, 11, Muted, row.Width - 16);
        }
        c.Unclip();
        if (collaborationExcluded.Count * 58 > list.Height)
        {
            float thumb = Math.Max(16, list.Height * list.Height / (collaborationExcluded.Count * 58));
            c.Fill(new(list.Right - 3, list.Y + collaborationReportScroll / (collaborationExcluded.Count * 58 - list.Height)
                * (list.Height - thumb), 3, thumb), Muted, 1);
        }
    }

    private void CollaborationReportPointerDown(float x, float y, int button)
    {
        if (button != 0) return;
        var r = CollaborationReportBounds;
        if (new Rect(r.Right - 40, r.Y, 40, 36).Contains(x, y)) { CloseCollaborationReport(); return; }
        if (new Rect(r.X, r.Y, r.Width, 36).Contains(x, y))
        {
            collaborationReportDragging = true; collaborationReportDragOffset = (x - r.X, y - r.Y); return;
        }
        var list = CollaborationReportList;
        if (!list.Contains(x, y)) return;
        int index = (int)((y - list.Y + collaborationReportScroll) / 58);
        if (index >= collaborationExcluded.Count) return;
        var item = collaborationExcluded[index];
        int difficulty = difficulties.FindIndex(d => d.Id == item.DifficultyId);
        if (difficulty < 0 || !SwitchDifficulty(difficulty)) return;
        tool = Tool.Select; SelectObjects([item.ObjectId], item.ObjectId); SeekTo(item.StartMs);
    }

    private void MoveCollaborationReport(float x, float y)
    {
        collaborationReportPosition = (Math.Clamp(x - collaborationReportDragOffset.X, 0, Math.Max(0, width - CollaborationReportBounds.Width)),
            Math.Clamp(y - collaborationReportDragOffset.Y, 0, Math.Max(0, height - CollaborationReportBounds.Height)));
    }
}
