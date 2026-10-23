using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const uint CollaborationLockColour = 0xFF9BAA;
    private CatchConversionResult? collaborationSelectionConversion;
    private List<CollaborationEditRange>? collaborationSelectionRanges;
    private readonly HashSet<Guid> collaborationSelectableParents = [];

    private bool CanSelectCollaborationObject(Guid id)
    {
        if (Document.CollaborationEditableRanges is not { } ranges)
        {
            collaborationSelectionConversion = null; collaborationSelectionRanges = null;
            collaborationSelectableParents.Clear();
            return true;
        }
        if (id == Guid.Empty) return true;
        if (conversion is null) return false;
        if (!ReferenceEquals(collaborationSelectionConversion, conversion) || !ReferenceEquals(collaborationSelectionRanges, ranges))
        {
            RefreshTimelineSources();
            collaborationSelectableParents.Clear();
            foreach (var source in timelineSources)
                if (CollaborationSectionEditing.Contains(ranges, source.Start, source.End)) collaborationSelectableParents.Add(source.Id);
            collaborationSelectionConversion = conversion;
            collaborationSelectionRanges = ranges;
        }
        return collaborationSelectableParents.Contains(id);
    }
    private void DrawCollaborationLocks(ICanvas c, Rect bounds, double start, double end, bool vertical = false)
    {
        if (Document.CollaborationEditableRanges is not { } ranges || end <= start) return;
        double cursor = start;
        foreach (var range in ranges)
        {
            Band(cursor, Math.Min(end, range.StartMs));
            cursor = Math.Max(cursor, range.EndMs);
            if (cursor >= end) break;
        }
        Band(cursor, end);
        void Band(double from, double to)
        {
            from = Math.Max(start, from); to = Math.Min(end, to);
            if (to <= from) return;
            if (vertical)
            {
                float a = Screen(new(from, 0)).Y, b = Screen(new(to, 0)).Y;
                c.Fill(new(bounds.X, Math.Min(a, b), bounds.Width, Math.Abs(b - a)), CollaborationLockColour, opacity: .30f);
            }
            else
            {
                float a = bounds.X + (float)((from - start) / (end - start)) * bounds.Width;
                float b = bounds.X + (float)((to - start) / (end - start)) * bounds.Width;
                c.Fill(new(a, bounds.Y, b - a, bounds.Height), CollaborationLockColour, opacity: .30f);
            }
        }
    }

    private void DrawCollaborationCanvasLock(ICanvas c)
    {
        if (Document.CollaborationEditableRanges is not { } ranges) return;
        c.Clip(CanvasRenderBounds);
        if (IsOsuCanvas)
        {
            if (!CollaborationSectionEditing.Contains(ranges, PlayheadMs, PlayheadMs))
                c.Fill(OsuCanvasBounds, CollaborationLockColour, opacity: .30f);
        }
        else DrawCollaborationLocks(c, Playfield, PresentationViewStart,
            PresentationViewStart + plot.Height / pixelsPerMs, vertical: true);
        c.Unclip();
    }
}
