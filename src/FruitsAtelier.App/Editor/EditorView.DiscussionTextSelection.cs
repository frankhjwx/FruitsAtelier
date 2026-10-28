using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record DiscussionTextHit(Rect Bounds, Rect FullBounds, DiscussionLayout Layout, DiscussionRun Run,
        ICanvas Canvas, bool Quote, Action? Click);
    private readonly List<DiscussionTextHit> discussionTextHits = [];
    private DiscussionLayout? discussionSelectedLayout;
    private DiscussionTextHit? discussionTextPressed;
    private int discussionTextAnchor, discussionTextEnd;
    private float discussionTextPressX, discussionTextPressY;
    private bool discussionTextDragging, discussionSelectedQuote;
    internal string SelectedDiscussionText => discussionSelectedLayout is { } layout
        ? layout.SelectionText[Math.Min(discussionTextAnchor, discussionTextEnd)..Math.Max(discussionTextAnchor, discussionTextEnd)] : "";
    internal Rect DiscussionRenderedTextBounds(string text, bool quote = false)
        => discussionTextHits.FirstOrDefault(hit => hit.Run.Text == text && hit.Quote == quote)?.Bounds ?? default;

    private void ClearDiscussionTextSelection()
    {
        discussionSelectedLayout = null; discussionTextPressed = null; discussionTextDragging = false;
        discussionTextAnchor = discussionTextEnd = 0;
    }

    private bool BeginDiscussionTextSelection(float x, float y)
    {
        ClearDiscussionTextSelection();
        var hit = discussionTextHits.LastOrDefault(hit => hit.Bounds.Contains(x, y));
        if (hit is null) return false;
        discussionSelectedLayout = hit.Layout; discussionSelectedQuote = hit.Quote; discussionTextPressed = hit;
        discussionTextAnchor = discussionTextEnd = DiscussionCharacterAt(hit, x);
        discussionTextPressX = x; discussionTextPressY = y;
        return true;
    }

    private static int DiscussionCharacterAt(DiscussionTextHit hit, float x)
    {
        string text = hit.Run.Text;
        int[] offsets = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        int lo = 0, hi = offsets.Length - 1;
        float target = x - hit.FullBounds.X;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            float a = Measure(offsets[mid]), b = Measure(offsets[mid + 1]);
            if (target < (a + b) / 2) hi = mid; else lo = mid + 1;
        }
        return hit.Run.TextStart + offsets[lo];
        float Measure(int count) => hit.Canvas.MeasureStyledText(text[..count], 12,
            hit.Run.Style.HasFlag(DiscussionTextStyle.Bold), hit.Run.Style.HasFlag(DiscussionTextStyle.Italic));
    }

    private void MoveDiscussionTextSelection(float x, float y)
    {
        if (discussionTextPressed is null) return;
        if (!discussionTextDragging && Math.Abs(x - discussionTextPressX) + Math.Abs(y - discussionTextPressY) < ContentDragThreshold) return;
        discussionTextDragging = true;
        var hit = discussionTextHits.Where(hit => ReferenceEquals(hit.Layout, discussionSelectedLayout) && hit.Quote == discussionSelectedQuote)
            .MinBy(hit => Math.Max(0, Math.Max(hit.Bounds.Y - y, y - hit.Bounds.Bottom)) * 10000
                + Math.Max(0, Math.Max(hit.Bounds.X - x, x - hit.Bounds.Right)));
        if (hit is not null) discussionTextEnd = DiscussionCharacterAt(hit, x);
    }

    private void EndDiscussionTextSelection(float x, float y)
    {
        MoveDiscussionTextSelection(x, y);
        var pressed = discussionTextPressed; discussionTextPressed = null;
        if (!discussionTextDragging)
        { ClearDiscussionTextSelection(); pressed?.Click?.Invoke(); }
        discussionTextDragging = false;
    }

    private bool DiscussionTextKey(int key, bool ctrl)
    {
        if (discussionSelectedLayout is null) return false;
        if (ctrl && key == 67) RequestCopyText?.Invoke(SelectedDiscussionText);
        else if (ctrl && key == 65) { discussionTextAnchor = 0; discussionTextEnd = discussionSelectedLayout.SelectionText.Length; }
        else if (key == 27) ClearDiscussionTextSelection();
        return true;
    }

    private void DrawDiscussionTextSelection(ICanvas c, DiscussionLayout layout, DiscussionRun run, Rect bounds)
    {
        if (!ReferenceEquals(discussionSelectedLayout, layout)) return;
        int start = Math.Max(run.TextStart, Math.Min(discussionTextAnchor, discussionTextEnd));
        int end = Math.Min(run.TextStart + run.Text.Length, Math.Max(discussionTextAnchor, discussionTextEnd));
        if (end <= start) return;
        float x = Measure(start - run.TextStart), right = Measure(end - run.TextStart);
        c.Fill(new(bounds.X + x, bounds.Y, Math.Max(0, right - x), bounds.Height), Chrome(0x3B708B, SelectedSurface), 2, .8f);
        float Measure(int length) => c.MeasureStyledText(run.Text[..length], 12,
            run.Style.HasFlag(DiscussionTextStyle.Bold), run.Style.HasFlag(DiscussionTextStyle.Italic));
    }
}
