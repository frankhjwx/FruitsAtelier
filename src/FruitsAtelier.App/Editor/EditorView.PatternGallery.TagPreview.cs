using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core.Patterns;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record GalleryTagChip(string[] Lines, uint Colour, Rect Bounds);
    private sealed record GalleryTagLayout(GalleryTagChip[] Chips, float Height);
    private readonly Dictionary<(PatternSnapshot Pattern, float Width, bool Expanded, Type Canvas), GalleryTagLayout> galleryTagLayouts = [];
    private Rect galleryPreviewTagsBounds;
    private Guid galleryPreviewTagsId;
    private float galleryPreviewTagsScroll, galleryPreviewTagsMaximum;
    internal Rect PatternGalleryPreviewTagsBounds => galleryPreviewTagsBounds;
    internal float PatternGalleryPreviewTagsMaximum => galleryPreviewTagsMaximum;

    private GalleryTagLayout GalleryTagsLayout(ICanvas c, PatternSnapshot pattern, float width, bool expanded)
    {
        var key = (pattern, width, expanded, c.GetType());
        if (galleryTagLayouts.TryGetValue(key, out var cached)) return cached;
        if (galleryTagLayouts.Count >= 128) galleryTagLayouts.Clear();
        var chips = new List<GalleryTagChip>();
        var ranks = galleryTags.Select((t, i) => (t.Name, i)).ToDictionary(t => t.Name, t => t.i, StringComparer.OrdinalIgnoreCase);
        var tags = pattern.Tags.OrderBy(t => ranks.GetValueOrDefault(t.Name, int.MaxValue)).ToArray();
        var labels = tags.Select(t => t.Name).ToArray();
        var sizes = labels.Select(t => MathF.Ceiling(c.MeasureText(t, 10)) + 14).ToArray();
        if (!expanded && sizes.Sum() + Math.Max(0, sizes.Length - 1) * 4 > width)
        {
            labels = labels.Select(name =>
            {
                var characters = StringInfo.ParseCombiningCharacters(name);
                return characters.Length > 5 ? name[..characters[5]] + "…" : name;
            }).ToArray();
            sizes = labels.Select(t => MathF.Ceiling(c.MeasureText(t, 10)) + 14).ToArray();
        }
        if (!expanded)
        {
            int visible = pattern.Tags.Count;
            if (sizes.Sum() + Math.Max(0, sizes.Length - 1) * 4 > width)
            {
                visible = 0;
                float used = 0;
                for (int i = 0; i < sizes.Length; i++)
                {
                    used += sizes[i] + (i == 0 ? 0 : 4);
                    if (used > width) break;
                    if (used + 4 + c.MeasureText($"+{sizes.Length - i - 1}", 10) + 14 <= width) visible = i + 1;
                }
            }
            float x = 0;
            for (int i = 0; i < visible; i++)
            {
                chips.Add(new([labels[i]], tags[i].Colour, new(x, 0, sizes[i], 18)));
                x += sizes[i] + 4;
            }
            if (visible < pattern.Tags.Count)
            {
                string more = $"+{pattern.Tags.Count - visible}";
                chips.Add(new([more], Muted, new(x, 0, c.MeasureText(more, 10) + 14, 18)));
            }
        }
        else
        {
            float x = 0, y = 0, rowHeight = 0;
            for (int i = 0; i < pattern.Tags.Count; i++)
            {
                float chipWidth = Math.Min(width, sizes[i]);
                var lines = GalleryTagLines(c, labels[i], chipWidth - 14);
                float chipHeight = lines.Length * 12 + 6;
                if (x > 0 && x + chipWidth > width) { x = 0; y += rowHeight + 4; rowHeight = 0; }
                chips.Add(new(lines, tags[i].Colour, new(x, y, chipWidth, chipHeight)));
                x += chipWidth + 4; rowHeight = Math.Max(rowHeight, chipHeight);
            }
        }
        var layout = new GalleryTagLayout(chips.ToArray(), chips.Count == 0 ? 0 : chips.Max(t => t.Bounds.Bottom));
        galleryTagLayouts[key] = layout;
        return layout;
    }

    private static string[] GalleryTagLines(ICanvas c, string name, float width)
    {
        if (c.MeasureText(name, 10) <= width + .01f) return [name];
        var starts = StringInfo.ParseCombiningCharacters(name).Append(name.Length).ToArray();
        var lines = new List<string>();
        for (int first = 0; first < starts.Length - 1;)
        {
            int end = first + 1;
            while (end + 1 < starts.Length && c.MeasureText(name[starts[first]..starts[end + 1]], 10) <= width + .01f) end++;
            lines.Add(name[starts[first]..starts[end]]); first = end;
        }
        return lines.ToArray();
    }

    private void DrawGalleryTagChips(ICanvas c, GalleryTagLayout layout, Rect bounds, float scroll = 0)
    {
        c.Clip(bounds);
        foreach (var chip in layout.Chips)
        {
            var rect = chip.Bounds with { X = bounds.X + chip.Bounds.X, Y = bounds.Y + chip.Bounds.Y - scroll };
            if (rect.Bottom <= bounds.Y || rect.Y >= bounds.Bottom) continue;
            c.Fill(rect, chip.Colour, 3, .18f); c.StrokeOpacity(rect, chip.Colour, 1, 3, .4f);
            for (int line = 0; line < chip.Lines.Length; line++)
                c.Text(chip.Lines[line], rect.X + 7, rect.Y + 3 + line * 12, 10, Foreground, rect.Width - 14);
        }
        c.Unclip();
    }

    private float DrawGalleryPopupTags(ICanvas c, PatternSnapshot pattern, float left, float top, float width, float popupHeight)
    {
        if (galleryPreviewTagsId != pattern.Id) { galleryPreviewTagsId = pattern.Id; galleryPreviewTagsScroll = 0; }
        if (pattern.Tags.Count == 0) { galleryPreviewTagsBounds = default; galleryPreviewTagsMaximum = 0; return 0; }
        var layout = GalleryTagsLayout(c, pattern, width - 8, true);
        float height = Math.Min(layout.Height, Math.Clamp(popupHeight - 250, 18, 104));
        galleryPreviewTagsBounds = new(left, top, width, height);
        galleryPreviewTagsMaximum = Math.Max(0, layout.Height - height);
        galleryPreviewTagsScroll = Math.Clamp(galleryPreviewTagsScroll, 0, galleryPreviewTagsMaximum);
        DrawGalleryTagChips(c, layout, galleryPreviewTagsBounds, galleryPreviewTagsScroll);
        DrawGalleryScrollbar(c, galleryPreviewTagsBounds, galleryPreviewTagsScroll, galleryPreviewTagsMaximum);
        return height + 8;
    }
}
