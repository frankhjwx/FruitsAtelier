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
    internal Rect GalleryTagListBounds { get; private set; }
    private IReadOnlyList<PatternTag> GalleryDialogTags => galleryTags;
    private PatternTag? GalleryExistingTagDraft => galleryTagCreating && galleryTagsPattern != Guid.Empty
        ? galleryTags.FirstOrDefault(t => string.Equals(t.Name, galleryTagName.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
    internal Rect GalleryTagSaveBounds { get; private set; }
    internal Rect GalleryTagDeleteBounds { get; private set; }
    internal Rect GalleryTagPreviousBounds { get; private set; }
    internal Rect GalleryTagNextBounds { get; private set; }
    private bool GalleryTagDraftValid => !string.IsNullOrWhiteSpace(galleryTagName) && galleryTagHex.Trim().TrimStart('#') is { Length: 6 } hex
        && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
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
        CancelGalleryTagOrder();
        galleryTagsPattern = pattern; galleryTagPage = 0; galleryTagDrag = 0;
        GalleryTagsVisible = true;
        SelectGalleryTag(GalleryDialogTags.FirstOrDefault());
    }

    private void SelectGalleryTag(PatternTag? tag)
    {
        FlushGalleryTagAutosave();
        if (galleryCurrentTagEdit is { Pending: false } previous && galleryTagSaving?.Edit != previous) galleryTagEdits.Remove(previous);
        galleryCurrentTagEdit = tag is null ? null : galleryTagEdits.FirstOrDefault(e => e.Saved.Name == tag.Name);
        if (tag is not null && galleryCurrentTagEdit is null) { galleryCurrentTagEdit = new(tag); galleryTagEdits.Add(galleryCurrentTagEdit); }
        galleryTagSelected = galleryCurrentTagEdit?.Saved.Name ?? "";
        galleryTagName = galleryCurrentTagEdit?.Desired.Name ?? "";
        galleryTagCreating = tag is null; galleryTagError = ""; textEditor.Field = "";
        (galleryTagHue, galleryTagSaturation, galleryTagValue) = ColourToHsv(galleryCurrentTagEdit?.Desired.Colour ?? 0x58D8C1, 0);
        galleryTagHex = $"#{GalleryTagColour:X6}";
    }

    private void CloseGalleryTags()
    {
        FlushGalleryTagAutosave();
        CancelGalleryTagOrder();
        GalleryTagsVisible = false; galleryTagDrag = 0; textEditor.Field = "";
    }

    private void DrawGalleryTags(ICanvas c)
    {
        if (!GalleryTagsVisible) return;
        AdvanceGalleryTagOrderPage();
        galleryTagOrderRows.Clear();
        hits.Clear(); fields.Clear();
        GalleryTagSaveBounds = GalleryTagDeleteBounds = GalleryTagPreviousBounds = GalleryTagNextBounds = default;
        c.Fill(new(0, 0, width, height), 0x10141B, opacity: .8f);
        float w = Math.Min(760, width - 32), h = Math.Min(532, height - 32);
        var box = new Rect((width - w) / 2, (height - h) / 2, w, h);
        c.Fill(box, Panel, 8); c.Stroke(box, Grid, radius: 8);
        var pattern = galleryPatterns.FirstOrDefault(p => p.Id == galleryTagsPattern);
        c.Text(L.Get(pattern is null ? "gallery.manageTags" : "gallery.editTags"), box.X + 20, box.Y + 20, 18, Foreground, w - 240, true);
        Button(c, new(box.Right - 48, box.Y + 15, 32, 32), "×", CloseGalleryTags);
        if (pattern is not null) c.Text(pattern.Name, box.X + 20, box.Y + 45, 10, Muted, w - 240);
        c.Line(box.X + 20, box.Y + 62, box.Right - 20, box.Y + 62, Grid);

        float left = box.X + 20, listWidth = (w - 64) * .38f, footer = box.Bottom - 68;
        float x = left + listWidth + 24, rw = box.Right - 20 - x;
        var listTags = GalleryDialogTags;
        c.Text(L.Get("gallery.filterTags", listTags.Count), left, box.Y + 78, 12, Muted, listWidth, true);
        var list = GalleryTagListBounds = new Rect(left, box.Y + 104, listWidth, footer - box.Y - 118);
        c.Fill(list, Surface, 5); c.Stroke(list, Grid, radius: 5);
        int rows = Math.Max(1, (int)((list.Height - 80) / 36));
        int pages = Math.Max(1, (listTags.Count + rows - 1) / rows);
        galleryTagPage = Math.Clamp(galleryTagPage, 0, pages - 1);
        float top = list.Y + 6;
        foreach (var tag in listTags.Skip(galleryTagPage * rows).Take(rows))
        {
            bool assigned = pattern?.Tags.Any(t => string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)) == true;
            var display = GalleryTagDisplay(tag);
            var row = new Rect(left + 5, top, listWidth - 10, 32);
            galleryTagOrderRows.Add((row, tag.Name));
            Button(c, row, display.Name, () =>
            {
                if (pattern is not null) SaveGalleryTags(pattern, assigned
                    ? pattern.Tags.Where(t => !string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)).ToArray()
                    : pattern.Tags.Append(tag).ToArray());
                SelectGalleryTag(tag);
            }, active: pattern is null ? galleryTagOrderPressed == tag.Name || !galleryTagCreating && galleryTagSelected == tag.Name : assigned,
                enabled: pattern is null || CanEditGalleryTags, fontSize: 12, textPadding: pattern is null ? 27 : 37, textRightPadding: 28);
            if (pattern is null)
                for (int grip = 0; grip < 3; grip++)
                    c.Line(row.X + 9, top + 11 + grip * 4, row.X + 17, top + 11 + grip * 4, Muted);
            if (pattern is not null)
            {
                c.Stroke(new(row.X + 8, top + 7, 18, 18), assigned ? Accent : Muted, radius: 3);
                if (assigned) c.Text("✓", row.X + 10, top + 7, 12, Accent, 16);
            }
            c.Fill(new(row.Right - 19, top + 12, 8, 8), display.Colour, 2);
            top += 36;
        }
        DrawGalleryTagOrderMarker(c);
        GalleryNewTagBounds = new(left + 5, top, listWidth - 10, 32);
        GalleryTagActionButton(c, GalleryNewTagBounds, L.Get("gallery.newTag"), () =>
        { SelectGalleryTag(null); SelectInput("gallery:tag-name", galleryTagName); }, enabled: CanEditGalleryTags);
        if (listTags.Count == 0)
        {
            c.Text(L.Get("gallery.emptyTags"), left + 16, top + 60, 14, Foreground, listWidth - 32, true);
            c.Text(L.Get("gallery.emptyTagsHint"), left + 16, top + 90, 12, Muted, listWidth - 32);
        }
        if (pages > 1)
        {
            float y = list.Bottom - 36;
            GalleryTagPreviousBounds = new(left + 8, y, 34, 28);
            GalleryTagNextBounds = new(list.Right - 42, y, 34, 28);
            GalleryTagActionButton(c, GalleryTagPreviousBounds, "←", () => galleryTagPage--, enabled: galleryTagPage > 0);
            GalleryTagActionButton(c, GalleryTagNextBounds, "→", () => galleryTagPage++, enabled: galleryTagPage + 1 < pages);
            string page = L.Get("sync.page", galleryTagPage + 1, pages);
            float pageWidth = c.MeasureText(page, 11);
            c.Text(page, left + (listWidth - pageWidth) / 2, y + 6, 11, Muted, listWidth - 90);
        }
        var selected = galleryTags.FirstOrDefault(t => t.Name == galleryTagSelected);
        c.Text(L.Get(galleryTagCreating ? "gallery.newTag" : "gallery.editTag"), x, box.Y + 78, 14, Foreground, rw, true);
        string state = galleryTagError.Length > 0 ? galleryTagError
            : GalleryExistingTagDraft is not null ? L.Get("gallery.existingTagHint")
            : galleryTagCreating ? L.Get("gallery.tagDraftHint") : L.Get(galleryCurrentTagEdit?.Pending == true || galleryTagSaving?.Edit == galleryCurrentTagEdit ? "gallery.tagSaving" : "gallery.tagSaved");
        c.Text(state, x, box.Y + 100, 11, galleryTagError.Length > 0 ? 0xFF9A9Au : Muted, rw);
        c.Text(L.Get("gallery.tagName"), x, box.Y + 124, 12, Muted, rw);
        galleryTagNameBounds = new(x, box.Y + 144, rw, 32);
        DrawGalleryTagField(c, galleryTagNameBounds, "gallery:tag-name", galleryTagName);
        c.Text(L.Get("gallery.tagColour"), x, box.Y + 190, 12, Muted, rw);
        galleryTagPalette = new(x, box.Y + 212, rw, Math.Max(40, h - 378));
        DrawColourChart(c, galleryTagPalette, galleryTagHue, galleryTagSaturation, galleryTagValue, GalleryExistingTagDraft is null ? 1 : .45f);
        galleryTagHueTrack = new(x, galleryTagPalette.Bottom + 12, rw, 20);
        DrawHueTrack(c, galleryTagHueTrack, galleryTagHue);
        if (GalleryExistingTagDraft is not null) c.Fill(galleryTagHueTrack, Panel, opacity: .55f);
        var swatch = galleryTagHexBounds = new Rect(x, galleryTagHueTrack.Bottom + 12, rw, 30);
        c.Fill(swatch, GalleryTagColour, 4);
        uint ink = ((GalleryTagColour >> 16 & 255) * 299 + (GalleryTagColour >> 8 & 255) * 587 + (GalleryTagColour & 255) * 114) > 145000 ? 0x171A20u : 0xFFFFFFu;
        if (textEditor.Field == "gallery:tag-hex")
        {
            c.Fill(swatch, Surface, 4); c.Stroke(swatch, Accent, radius: 4);
            DrawInputText(c, new(x + 10, swatch.Y + 7, rw - 20, 20), galleryTagHex, 12, true, "gallery:tag-hex");
        }
        else c.Text($"#{GalleryTagColour:X6}", x + 10, swatch.Y + 7, 12, ink, rw - 20);
        c.Line(box.X + 20, footer, box.Right - 20, footer, Grid);
        if (selected is not null && !galleryTagCreating && pattern is null)
        {
            GalleryTagDeleteBounds = new(left, footer + 17, 140, 34);
            GalleryTagActionButton(c, GalleryTagDeleteBounds, L.Get("gallery.deleteTag"), () =>
            {
                ForgetGalleryTagEdit(selected);
                EditGalleryTag(selected, null);
                SelectGalleryTag(null);
            }, danger: true, enabled: CanEditGalleryTags);
        }
        GalleryTagActionButton(c, new(box.Right - (galleryTagCreating ? 272 : 116), footer + 17, 96, 34), L.Get(galleryTagCreating ? "mac.cancel" : "gallery.close"), CloseGalleryTags);
        if (galleryTagCreating)
        {
            GalleryTagSaveBounds = new(box.Right - 164, footer + 17, 144, 34);
            GalleryTagActionButton(c, GalleryTagSaveBounds, L.Get(GalleryExistingTagDraft is not null ? "gallery.addTag" : "gallery.createTag"), SaveGalleryTagDraft,
                primary: true, enabled: CanEditGalleryTags && GalleryTagDraftValid);
        }
    }

    private void GalleryTagActionButton(ICanvas c, Rect bounds, string label, Action action, bool primary = false, bool danger = false, bool enabled = true)
    {
        bool hover = enabled && bounds.Contains(mouseX, mouseY);
        uint fill = !enabled ? Surface : primary ? Accent : danger ? 0x3B292Fu : hover ? 0x354353u : Surface;
        uint border = !enabled ? Grid : primary ? Accent : danger ? 0x87505Cu : hover ? Muted : Grid;
        uint ink = !enabled ? Muted : primary ? 0x13282Au : danger ? 0xFFABABu : Foreground;
        c.Fill(bounds, fill, 5); c.Stroke(bounds, border, radius: 5);
        float labelWidth = c.MeasureText(label, 12, primary);
        c.Text(label, bounds.X + Math.Max(9, (bounds.Width - labelWidth) / 2), bounds.Y + 9, 12, ink, bounds.Width - 18, primary);
        hits.Add(new(bounds, action, enabled));
    }

    private void GalleryTagsPointerDown(float x, float y, int button)
    {
        if (button != 0) return;
        if (BeginGalleryTagOrder(x, y)) return;
        if (galleryTagNameBounds.Contains(x, y)) { FocusInput("gallery:tag-name", galleryTagName, x); return; }
        if (GalleryExistingTagDraft is not null && (galleryTagHexBounds.Contains(x, y) || galleryTagPalette.Contains(x, y) || galleryTagHueTrack.Contains(x, y))) return;
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
        if (galleryTagDrag != 0) { galleryTagHex = $"#{GalleryTagColour:X6}"; QueueGalleryTagAutosave(); }
    }

    private void DrawGalleryTagField(ICanvas c, Rect bounds, string key, string value)
    {
        c.Fill(bounds, Surface, 4); c.Stroke(bounds, textEditor.Field == key ? Accent : Grid, radius: 4);
        DrawInputText(c, new(bounds.X + 9, bounds.Y + 6, bounds.Width - 18, 20), value, 12, textEditor.Field == key, key);
    }

    private void SaveGalleryTagDraft()
    {
        if (!galleryTagCreating) { QueueGalleryTagAutosave(); FlushGalleryTagAutosave(); return; }
        if (!CanEditGalleryTags || galleryStore is not { } store) return;
        string name = galleryTagName.Trim(), hex = galleryTagHex.Trim().TrimStart('#');
        if (!ValidGalleryTagName(name)) { galleryTagError = L.Get("core.pattern.invalid"); return; }
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        { galleryTagError = L.Get("gallery.invalidColour"); return; }
        if (GalleryExistingTagDraft is { } existing && galleryPatterns.FirstOrDefault(p => p.Id == galleryTagsPattern) is { } pattern)
        {
            if (!pattern.Tags.Any(t => t.Name == existing.Name)) SaveGalleryTags(pattern, pattern.Tags.Append(existing).ToArray());
            SelectGalleryTag(existing); return;
        }
        if (galleryTags.Any(t => (galleryTagCreating || t.Name != galleryTagSelected) && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        { galleryTagError = L.Get("gallery.tagExists"); return; }
        var tag = new PatternTag(name, colour);
        Guid patternId = galleryTagsPattern;
        StartGalleryIo(() => { store.AddTag(tag, patternId); return ReloadGallery(store, "", true); });
        SelectGalleryTag(tag);
    }

    private void GalleryTagsKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { CloseGalleryTags(); return; }
        if (key == 13) { SaveGalleryTagDraft(); return; }
        if (key == 9 && GalleryExistingTagDraft is not null) { SelectInput("gallery:tag-name", galleryTagName); return; }
        if (key == 9) { SelectInput(textEditor.Field == "gallery:tag-name" ? "gallery:tag-hex" : "gallery:tag-name", textEditor.Field == "gallery:tag-name" ? galleryTagHex : galleryTagName); return; }
        if (!GalleryTagTextFocused) return;
        if (textEditor.Field == "gallery:tag-name") { InputKey("gallery:tag-name", ref galleryTagName, key, ctrl, shift, 64, RequestPasteField); MatchExistingGalleryTag(); }
        else { InputKey("gallery:tag-hex", ref galleryTagHex, key, ctrl, shift, 7, RequestPasteField); UpdateGalleryTagHex(); }
        QueueGalleryTagAutosave();
    }

    private void GalleryTagsText(string text)
    {
        if (!GalleryTagTextFocused) return;
        galleryTagError = "";
        string value = new(text.Where(c => !char.IsControl(c)).ToArray());
        if (value.Length == 0) return;
        if (textEditor.Field == "gallery:tag-name") { galleryTagName = InsertInput("gallery:tag-name", galleryTagName, value, 64); MatchExistingGalleryTag(); }
        else { galleryTagHex = InsertInput("gallery:tag-hex", galleryTagHex, value, 7); UpdateGalleryTagHex(); }
        QueueGalleryTagAutosave();
    }

    private void MatchExistingGalleryTag()
    {
        if (GalleryExistingTagDraft is not { } tag) return;
        (galleryTagHue, galleryTagSaturation, galleryTagValue) = ColourToHsv(tag.Colour, galleryTagHue);
        galleryTagHex = $"#{tag.Colour:X6}";
    }

    private void UpdateGalleryTagHex()
    {
        string hex = galleryTagHex.Trim().TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
            (galleryTagHue, galleryTagSaturation, galleryTagValue) = ColourToHsv(colour, galleryTagHue);
    }
}
