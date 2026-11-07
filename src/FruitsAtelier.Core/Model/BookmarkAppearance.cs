namespace FruitsAtelier.Core;

public sealed record BookmarkAppearance(uint Colour = 0x4B9EF5, bool ShowInterval = false);

public sealed partial class MapDocument
{
    public Dictionary<int, BookmarkAppearance> BookmarkAppearances { get; } = new();

    public BookmarkAppearance BookmarkAppearanceAt(int time)
        => BookmarkAppearances.GetValueOrDefault(time) ?? DefaultBookmarkAppearance;

    public static BookmarkAppearance DefaultBookmarkAppearance { get; } = new();
}
