using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal bool GalleryTagsVisible { get; private set; }
    private Guid galleryTagsPattern;
    private string galleryTagSelected = "", galleryTagName = "", galleryTagHex = "", galleryTagError = "";
    private bool galleryTagCreating;
    private Rect galleryTagNameBounds, galleryTagHexBounds;
    internal Rect GalleryTagNameBounds => galleryTagNameBounds;
    internal Rect GalleryNewTagBounds { get; private set; }
    private bool GalleryTagTextFocused => GalleryTagsVisible && textEditor.Field is "gallery:tag-name" or "gallery:tag-hex";
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
        galleryTagSelected = tag?.Name ?? ""; galleryTagName = galleryTagSelected;
        galleryTagCreating = tag is null; galleryTagError = ""; textEditor.Field = "";
        (galleryTagHue, galleryTagSaturation, galleryTagValue) = ColourToHsv(tag?.Colour ?? 0x58D8C1, 0);
        galleryTagHex = $"#{GalleryTagColour:X6}";
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
        float w = Math.Min(700, width - 32), h = Math.Min(500, height - 32);
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
        GalleryNewTagBounds = new(left, box.Bottom - 34, listWidth, 26);
        Button(c, GalleryNewTagBounds, L.Get("gallery.newTag"), () =>
        { SelectGalleryTag(null); SelectInput("gallery:tag-name", galleryTagName); }, enabled: CanEditGalleryTags, fontSize: 12);

        float x = left + listWidth + 20, rw = box.Right - 20 - x;
        var selected = galleryTags.FirstOrDefault(t => t.Name == galleryTagSelected);
        c.Text(L.Get("gallery.tagName"), x, box.Y + 76, 12, Muted, rw);
        galleryTagNameBounds = new(x, box.Y + 98, rw, 30);
        DrawGalleryTagField(c, galleryTagNameBounds, "gallery:tag-name", galleryTagName);
        galleryTagPalette = new(x, box.Y + 140, rw, Math.Max(40, h - 334));
        DrawColourChart(c, galleryTagPalette, galleryTagHue, galleryTagSaturation, galleryTagValue);
        galleryTagHueTrack = new(x, galleryTagPalette.Bottom + 12, rw, 22);
        DrawHueTrack(c, galleryTagHueTrack, galleryTagHue);
        var swatch = galleryTagHexBounds = new Rect(x, galleryTagHueTrack.Bottom + 12, rw, 28);
        c.Fill(swatch, GalleryTagColour, 4);
        uint ink = ((GalleryTagColour >> 16 & 255) * 299 + (GalleryTagColour >> 8 & 255) * 587 + (GalleryTagColour & 255) * 114) > 145000 ? 0x171A20u : 0xFFFFFFu;
        if (textEditor.Field == "gallery:tag-hex")
        {
            c.Fill(swatch, Surface, 4);
            DrawInputText(c, new(x + 10, swatch.Y + 6, rw - 20, 20), galleryTagHex, 12, true, "gallery:tag-hex");
        }
        else c.Text($"#{GalleryTagColour:X6}", x + 10, swatch.Y + 6, 12, ink, rw - 20);
        hits.Add(new(swatch, () => SelectInput("gallery:tag-hex", galleryTagHex), CanEditGalleryTags));
        Button(c, new(x, swatch.Bottom + 10, rw, 28), L.Get("mac.save"), SaveGalleryTagDraft,
            enabled: CanEditGalleryTags && !string.IsNullOrWhiteSpace(galleryTagName));
        Button(c, new(x, bottom, rw, 28), L.Get("gallery.deleteTag"), () =>
        { if (selected is not null) { EditGalleryTag(selected, null); SelectGalleryTag(null); } }, enabled: selected is not null && CanEditGalleryTags, fontSize: 11);
        if (galleryTagError.Length > 0) c.Text(galleryTagError, x, box.Bottom - 30, 11, Foreground, rw);
    }

    private void GalleryTagsPointerDown(float x, float y, int button)
    {
        if (button != 0) return;
        if (galleryTagNameBounds.Contains(x, y)) { FocusInput("gallery:tag-name", galleryTagName, x); return; }
        if (galleryTagHexBounds.Contains(x, y)) { SelectInput("gallery:tag-hex", galleryTagHex); return; }
        if (galleryTagPalette.Contains(x, y) || galleryTagHueTrack.Contains(x, y))
        { textEditor.Field = ""; galleryTagDrag = galleryTagPalette.Contains(x, y) ? 1 : 2; MoveGalleryTagColour(x, y); return; }
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
        if (galleryTagDrag != 0) galleryTagHex = $"#{GalleryTagColour:X6}";
    }

    private void DrawGalleryTagField(ICanvas c, Rect bounds, string key, string value)
    {
        c.Fill(bounds, Surface, 4); c.Stroke(bounds, textEditor.Field == key ? Accent : Grid, radius: 4);
        DrawInputText(c, new(bounds.X + 9, bounds.Y + 6, bounds.Width - 18, 20), value, 12, textEditor.Field == key, key);
    }

    private void SaveGalleryTagDraft()
    {
        if (!CanEditGalleryTags || galleryStore is not { } store) return;
        string name = galleryTagName.Trim(), hex = galleryTagHex.Trim().TrimStart('#');
        if (!ValidGalleryTagName(name)) { galleryTagError = L.Get("core.pattern.invalid"); return; }
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        { galleryTagError = L.Get("gallery.invalidColour"); return; }
        if (galleryTags.Any(t => (galleryTagCreating || t.Name != galleryTagSelected) && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        { galleryTagError = L.Get("gallery.tagExists"); return; }
        var tag = new PatternTag(name, colour);
        if (galleryTagCreating)
        {
            Guid patternId = galleryTagsPattern;
            StartGalleryIo(() => { store.AddTag(tag, patternId); return ReloadGallery(store, "", true); });
        }
        else if (galleryTags.FirstOrDefault(t => t.Name == galleryTagSelected) is { } selected) EditGalleryTag(selected, tag);
        SelectGalleryTag(tag);
    }

    private void GalleryTagsKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { CloseGalleryTags(); return; }
        if (key == 13) { SaveGalleryTagDraft(); return; }
        if (key == 9) { SelectInput(textEditor.Field == "gallery:tag-name" ? "gallery:tag-hex" : "gallery:tag-name", textEditor.Field == "gallery:tag-name" ? galleryTagHex : galleryTagName); return; }
        if (!GalleryTagTextFocused) return;
        if (textEditor.Field == "gallery:tag-name") InputKey("gallery:tag-name", ref galleryTagName, key, ctrl, shift, 64, RequestPasteField);
        else { InputKey("gallery:tag-hex", ref galleryTagHex, key, ctrl, shift, 7, RequestPasteField); UpdateGalleryTagHex(); }
    }

    private void GalleryTagsText(string text)
    {
        if (!GalleryTagTextFocused) return;
        string value = new(text.Where(c => !char.IsControl(c)).ToArray());
        if (value.Length == 0) return;
        if (textEditor.Field == "gallery:tag-name") galleryTagName = InsertInput("gallery:tag-name", galleryTagName, value, 64);
        else { galleryTagHex = InsertInput("gallery:tag-hex", galleryTagHex, value, 7); UpdateGalleryTagHex(); }
    }

    private void UpdateGalleryTagHex()
    {
        string hex = galleryTagHex.Trim().TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
            (galleryTagHue, galleryTagSaturation, galleryTagValue) = ColourToHsv(colour, galleryTagHue);
    }
}
