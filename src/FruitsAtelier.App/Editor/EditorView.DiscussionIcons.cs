using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static readonly string discussionIconDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "discussion");
    private static readonly Dictionary<string, string> discussionIconPaths = new(StringComparer.Ordinal);

    private static void DiscussionIcon(ICanvas c, string name, Rect bounds, uint colour)
    {
        if (!discussionIconPaths.TryGetValue(name, out string? path))
            discussionIconPaths[name] = path = Path.Combine(discussionIconDirectory, name + ".png");
        c.Image(path, bounds, colour);
    }

    internal static (string Icon, uint Colour) DiscussionTypeIcon(string type) => type switch
    {
        "hype" => ("bullhorn", 0x329FC7),
        "praise" => ("heart", 0x329FC7),
        "problem" or "issue" => ("exclamation-circle", 0xDD3535),
        "review" => ("tasks", 0x70D9CF),
        "mapper_note" => ("sticky-note", 0xC29DDD),
        "suggestion" => ("circle", 0xDBAD26),
        _ => ("sticky-note", 0xA6BBC4)
    };

    private static bool DiscussionHasState(BeatmapDiscussion thread)
        => thread.CanResolve || thread.Resolved || thread.MessageType is "problem" or "suggestion" or "issue";

    private static void DrawDiscussionTypeIcons(ICanvas c, BeatmapDiscussion thread, float x, float y)
    {
        var type = DiscussionTypeIcon(thread.MessageType);
        DiscussionIcon(c, type.Icon, new(x, y, 12, 12), type.Colour);
        if (DiscussionHasState(thread))
            DiscussionIcon(c, thread.Resolved ? "check-circle" : "clock", new(x + 16, y, 12, 12),
                thread.Resolved ? 0x88CC22u : DiscussionPendingColour);
    }
}
