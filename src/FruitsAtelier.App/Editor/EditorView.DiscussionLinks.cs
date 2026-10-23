using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool DiscussionLinkHovered(Rect bounds) => discussionTextPressed is null && DockContentInputAvailable && menu < 0 && contextItems.Count == 0
        && bounds.Width > 0 && bounds.Height > 0 && bounds.Contains(mouseX, mouseY);

    private void CopyDiscussionPermalink(long post, long thread, bool canvasQuote)
    {
        if (RequestCopyText is null) return;
        RequestCopyText($"https://osu.ppy.sh/beatmapsets/{discussionSetId}/discussion#/{thread}/{post}");
        discussionCopiedLink = (post, canvasQuote); discussionCopiedUntil = timeProvider.GetUtcNow().AddSeconds(2);
    }

    private void DrawDiscussionLinkTooltip(ICanvas c)
    {
        if (!DockContentInputAvailable) return;
        bool copied = discussionCopiedLink is not null && discussionCopyFeedbackBounds.Height > 0;
        var anchor = copied ? discussionCopyFeedbackBounds : discussionCopyHoverBounds;
        if (anchor.Height <= 0) return;
        string label = L.Get(copied ? "discussion.linkCopied" : "discussion.copyLinkHint");
        float w = Math.Min(width - 16, c.MeasureText(label, 12) + 24);
        float x = Math.Clamp(anchor.X + anchor.Width / 2 - w / 2, 8, Math.Max(8, width - w - 8));
        float y = anchor.Bottom + 10;
        if (y + 32 > height - 8) y = anchor.Y - 42;
        c.Fill(new(x, y, w, 32), 0x14191D, 5);
        c.Text(label, x + 12, y + 8, 12, Foreground, w - 24);
    }
}
