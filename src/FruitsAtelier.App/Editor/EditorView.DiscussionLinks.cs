using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly List<Rect> discussionHandHits = [];
    private readonly List<Rect> discussionBodyHits = [];
    private bool DiscussionPointerAvailable => DockContentInputAvailable && menu < 0 && contextItems.Count == 0;
    public bool DiscussionHandCursor => discussionTextPressed is null && DiscussionPointerAvailable
        && discussionHandHits.Any(bounds => bounds.Contains(mouseX, mouseY));
    public bool DiscussionTextCursor => DiscussionPointerAvailable && !DiscussionHandCursor
        && (discussionTextPressed is not null || discussionBodyHits.Any(bounds => bounds.Contains(mouseX, mouseY)));
    internal Rect DiscussionRenderedLinkBounds(string url, bool quote = false)
        => discussionTextHits.FirstOrDefault(hit => hit.Run.Url == url && hit.Quote == quote)?.Bounds ?? default;

    private static string DiscussionProfileLink(long id, string name)
    {
        var label = new System.Text.StringBuilder();
        foreach (char character in name)
        {
            if (character is '\\' or '[' or ']' or '*' or '_' or '`' or '~') label.Append('\\');
            label.Append(character);
        }
        return $"[{label}](https://osu.ppy.sh/users/{id})";
    }
    private bool DiscussionLinkHovered(Rect bounds) => discussionTextPressed is null && DockContentInputAvailable && menu < 0 && contextItems.Count == 0
        && bounds.Width > 0 && bounds.Height > 0 && bounds.Contains(mouseX, mouseY);

    private int? DiscussionHoveredLink(DiscussionLayout layout, Rect viewport, float contentX, float scroll)
    {
        if (!DiscussionPointerAvailable || discussionTextPressed is not null || !viewport.Contains(mouseX, mouseY)) return null;
        for (int i = DiscussionFirstVisible(layout.LineEnds, scroll); i < layout.Lines.Count; i++)
        {
            var line = layout.Lines[i];
            if (line.Top >= scroll + viewport.Height) break;
            float y = viewport.Y + line.Top - scroll;
            if (mouseY < y || mouseY >= y + line.Height) continue;
            foreach (var run in line.Runs)
                if (run.Url is not null && new Rect(contentX + line.Indent + run.X, y, run.Width, line.Height).Contains(mouseX, mouseY)) return run.LinkId;
        }
        return null;
    }

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
        c.Fill(new(x, y, w, 32), Chrome(0x14191D, Background), 5);
        c.Text(label, x + 12, y + 8, 12, Foreground, w - 24);
    }
}
