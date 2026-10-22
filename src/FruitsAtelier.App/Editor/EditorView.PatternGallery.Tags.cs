using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal bool GalleryTagsVisible { get; private set; }
    private Guid galleryTagsPattern;
    private string galleryTagSelected = "";
    private int galleryTagPage, galleryTagDrag;
    private double galleryTagHue, galleryTagSaturation, galleryTagValue;
    private Rect galleryTagPalette, galleryTagHueTrack;
    internal Rect GalleryTagPaletteBounds => galleryTagPalette;
    internal Rect GalleryTagHueBounds => galleryTagHueTrack;
    private uint GalleryTagColour => SongHsv(galleryTagHue, galleryTagSaturation, galleryTagValue);

    private void OpenGalleryTags(Guid pattern)
    {
        if (!CanEditGalleryTags) return;
        contextItems.Clear(); menu = -1; textEditor.Field = "";
        galleryTagsPattern = pattern; galleryTagPage = 0; galleryTagDrag = 0;
        GalleryTagsVisible = true;
        SelectGalleryTag(galleryTags.FirstOrDefault());
    }

    private void SelectGalleryTag(PatternTag? tag)
    {
        galleryTagSelected = tag?.Name ?? "";
        (galleryTagHue, galleryTagSaturation, galleryTagValue) = ColourToHsv(tag?.Colour ?? 0x58D8C1, 0);
    }

    private void CloseGalleryTags()
    {
        GalleryTagsVisible = false; galleryTagDrag = 0; textEditor.Field = "";
    }

    private void DrawGalleryTags(ICanvas c)
    {
        if (!GalleryTagsVisible) return;
        hits.Clear(); fields.Clear();
        c.Fill(new(0, 0, width, height), 0x10141B, opacity: .8f);
        float w = Math.Min(700, width - 32), h = Math.Min(460, height - 32);
        var box = new Rect((width - w) / 2, (height - h) / 2, w, h);
        c.Fill(box, Panel, 8); c.Stroke(box, Accent, radius: 8);
        var pattern = galleryPatterns.FirstOrDefault(p => p.Id == galleryTagsPattern);
        c.Text(L.Get(pattern is null ? "gallery.manageTags" : "gallery.editTags"), box.X + 20, box.Y + 18, 17, Foreground, w - 72, true);
        Button(c, new(box.Right - 44, box.Y + 10, 32, 28), "×", CloseGalleryTags);
        if (pattern is not null) c.Text(pattern.Name, box.X + 20, box.Y + 46, 12, Muted, w - 40);
        float left = box.X + 20, top = box.Y + 76, listWidth = (w - 60) * .42f;
        int rows = Math.Max(1, (int)((h - 160) / 34));
        galleryTagPage = Math.Clamp(galleryTagPage, 0, Math.Max(0, (galleryTags.Length - 1) / rows));
        foreach (var tag in galleryTags.Skip(galleryTagPage * rows).Take(rows))
        {
            var row = new Rect(left, top, listWidth, 30);
            bool assigned = pattern?.Tags.Any(t => string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)) == true;
            float inset = pattern is null ? 0 : 32;
            if (pattern is not null)
            {
                c.Stroke(new(left + 5, top + 5, 20, 20), assigned ? Accent : Muted, radius: 3);
                Button(c, new(left, top, 28, 30), assigned ? "✓" : "", () =>
                {
                    SaveGalleryTags(pattern, assigned ? pattern.Tags.Where(t => !string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)).ToArray()
                        : pattern.Tags.Append(tag).ToArray());
                }, active: assigned, enabled: CanEditGalleryTags);
            }
            Button(c, new(left + inset, top, listWidth - inset, 30), tag.Name, () => SelectGalleryTag(tag),
                active: galleryTagSelected == tag.Name, fontSize: 12);
            c.Fill(new(row.Right - 17, top + 11, 8, 8), tag.Colour, 2);
            top += 34;
        }
        if (galleryTags.Length == 0) c.Text(L.Get("gallery.noMatches"), left, top, 12, Muted, listWidth);
        float bottom = box.Bottom - 66;
        Button(c, new(left, bottom, listWidth / 2 - 2, 26), L.Get("gallery.previousTags"), () => galleryTagPage--, enabled: galleryTagPage > 0, fontSize: 11);
        Button(c, new(left + listWidth / 2 + 2, bottom, listWidth / 2 - 2, 26), L.Get("gallery.nextTags"), () => galleryTagPage++, enabled: (galleryTagPage + 1) * rows < galleryTags.Length, fontSize: 11);
        Button(c, new(left, box.Bottom - 34, listWidth, 26), L.Get("gallery.newTag"), () => RequestGalleryText(L.Get("gallery.newTag"), "", name =>
        {
            if (!ValidGalleryTagName(name) || galleryPatterns.FirstOrDefault(p => p.Id == galleryTagsPattern) is not { } current) return;
            var tag = galleryTags.FirstOrDefault(t => string.Equals(t.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase)) ?? new PatternTag(name!.Trim(), GalleryTagColour);
            if (!current.Tags.Any(t => string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase))) SaveGalleryTags(current, current.Tags.Append(tag).ToArray());
            SelectGalleryTag(tag);
        }), enabled: pattern is not null && CanEditGalleryTags, fontSize: 12);

        float x = left + listWidth + 20, rw = box.Right - 20 - x;
        var selected = galleryTags.FirstOrDefault(t => t.Name == galleryTagSelected);
        c.Text(selected?.Name ?? L.Get("gallery.tagColour"), x, box.Y + 76, 13, Foreground, rw, true);
        galleryTagPalette = new(x, box.Y + 104, rw, Math.Max(40, h - 298));
        DrawColourChart(c, galleryTagPalette, galleryTagHue, galleryTagSaturation, galleryTagValue);
        galleryTagHueTrack = new(x, galleryTagPalette.Bottom + 12, rw, 22);
        DrawHueTrack(c, galleryTagHueTrack, galleryTagHue);
        var swatch = new Rect(x, galleryTagHueTrack.Bottom + 12, rw, 28);
        c.Fill(swatch, GalleryTagColour, 4);
        uint ink = ((GalleryTagColour >> 16 & 255) * 299 + (GalleryTagColour >> 8 & 255) * 587 + (GalleryTagColour & 255) * 114) > 145000 ? 0x171A20u : 0xFFFFFFu;
        c.Text($"#{GalleryTagColour:X6}", x + 10, swatch.Y + 6, 12, ink, rw - 20);
        hits.Add(new(swatch, () => RequestGalleryText(L.Get("gallery.customColour"), $"#{GalleryTagColour:X6}", value =>
        {
            if (value is null) return;
            string hex = value.Trim().TrimStart('#');
            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
                (galleryTagHue, galleryTagSaturation, galleryTagValue) = ColourToHsv(colour, galleryTagHue);
            else StatusMessage = L.Get("gallery.invalidColour");
        }), CanEditGalleryTags));
        Button(c, new(x, swatch.Bottom + 10, rw, 28), L.Get("mac.save"), () =>
        { if (selected is not null) EditGalleryTag(selected, selected with { Colour = GalleryTagColour }); }, enabled: selected is not null && CanEditGalleryTags);
        Button(c, new(x, bottom, rw / 2 - 3, 28), L.Get("gallery.renameTag"), () =>
        {
            if (selected is null) return;
            RequestGalleryText(L.Get("gallery.renameTag"), selected.Name, name =>
            {
                if (!ValidGalleryTagName(name)) return;
                if (galleryTags.Any(t => t != selected && string.Equals(t.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase)))
                { StatusMessage = L.Get("gallery.tagExists"); return; }
                EditGalleryTag(selected, selected with { Name = name!.Trim() }); galleryTagSelected = name.Trim();
            });
        }, enabled: selected is not null && CanEditGalleryTags, fontSize: 11);
        Button(c, new(x + rw / 2 + 3, bottom, rw / 2 - 3, 28), L.Get("gallery.deleteTag"), () =>
        { if (selected is not null) { EditGalleryTag(selected, null); SelectGalleryTag(null); } }, enabled: selected is not null && CanEditGalleryTags, fontSize: 11);
    }

    private void GalleryTagsPointerDown(float x, float y, int button)
    {
        if (button != 0) return;
        if (galleryTagPalette.Contains(x, y) || galleryTagHueTrack.Contains(x, y))
        { galleryTagDrag = galleryTagPalette.Contains(x, y) ? 1 : 2; MoveGalleryTagColour(x, y); return; }
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return; }
    }

    private void MoveGalleryTagColour(float x, float y)
    {
        if (galleryTagDrag == 1)
        {
            galleryTagSaturation = Math.Clamp((x - galleryTagPalette.X) / galleryTagPalette.Width, 0, 1);
            galleryTagValue = 1 - Math.Clamp((y - galleryTagPalette.Y) / galleryTagPalette.Height, 0, 1);
        }
        else if (galleryTagDrag == 2) galleryTagHue = Math.Clamp((x - galleryTagHueTrack.X) / galleryTagHueTrack.Width, 0, .999999) * 360;
    }
}
