using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static partial class OsuTimeline
{
    public static void SetBookmarkAppearance(MapDocument document, int time, BookmarkAppearance appearance)
    {
        if (!Bookmarks(document).Contains(time) || appearance.Colour > 0xFFFFFF)
            throw new ArgumentException(L.Get("bookmark.invalid"));
        if (appearance == MapDocument.DefaultBookmarkAppearance) document.BookmarkAppearances.Remove(time);
        else document.BookmarkAppearances[time] = appearance;
    }

    public static void MoveBookmark(MapDocument document, int time, int replacement)
    {
        var values = Bookmarks(document);
        if (replacement < 0 || !values.Contains(time) || replacement != time && values.Contains(replacement))
            throw new ArgumentException(L.Get("bookmark.invalid"));
        if (time == replacement) return;
        var appearance = document.BookmarkAppearanceAt(time);
        ToggleBookmark(document, time);
        AddBookmark(document, replacement);
        SetBookmarkAppearance(document, replacement, appearance);
    }

    internal static void TransformBookmarks(MapDocument document, MapDocument before, Func<int, int> transform)
    {
        var values = Bookmarks(before);
        var moved = values.Select(transform).ToArray();
        if (values.SequenceEqual(moved)) return;
        document.BookmarkAppearances.Clear();
        for (int i = 0; i < values.Count; i++)
            if (!document.BookmarkAppearances.ContainsKey(moved[i])
                && before.BookmarkAppearances.TryGetValue(values[i], out var appearance))
                document.BookmarkAppearances[moved[i]] = appearance;
        SongSetup.Set(document, "Editor", "Bookmarks", moved.Length == 0 ? null
            : string.Join(',', moved.Distinct().Order().Select(time => time.ToString(CultureInfo.InvariantCulture))));
    }
}
