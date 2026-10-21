using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<string>? RequestVisitOsuWeb { get; set; }
    private readonly List<(Rect Bounds, string Url)> libraryWebButtons = [];

    internal static string? LibraryWebUrl(LibraryMap map)
        => map.BeatmapId <= 0 ? null : map.BeatmapSetId > 0
            ? $"https://osu.ppy.sh/beatmapsets/{map.BeatmapSetId}#{(map.Mode == 2 ? "fruits" : "osu")}/{map.BeatmapId}"
            : $"https://osu.ppy.sh/b/{map.BeatmapId}";

    private float DrawLibraryWebButton(ICanvas c, Rect card, LibraryMap map)
    {
        if (LibraryWebUrl(map) is not { } url) return 0;
        string label = L.Get("library.visitWeb");
        float buttonWidth = c.MeasureText(label, 11) + 20;
        var bounds = new Rect(card.Right - buttonWidth - 12, card.Y + 7, buttonWidth, 26);
        c.Fill(bounds, Surface, 4); c.Stroke(bounds, Grid, radius: 4);
        c.Text(label, bounds.X + 10, bounds.Y + 6, 11, bounds.Contains(mouseX, mouseY) ? Accent : Foreground, buttonWidth - 20);
        var clipped = new Rect(bounds.X, Math.Max(bounds.Y, libraryListBounds.Y), bounds.Width,
            Math.Max(0, Math.Min(bounds.Bottom, libraryListBounds.Bottom) - Math.Max(bounds.Y, libraryListBounds.Y)));
        libraryWebButtons.Add((clipped, url));
        return buttonWidth + 12;
    }
}
