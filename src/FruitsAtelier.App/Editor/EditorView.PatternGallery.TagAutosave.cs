using System.Globalization;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed class GalleryTagEdit(PatternTag tag)
    {
        public PatternTag Saved = tag, Desired = tag;
        public DateTimeOffset Due;
        public bool Pending => Saved != Desired;
    }
    private readonly List<GalleryTagEdit> galleryTagEdits = [];
    private GalleryTagEdit? galleryCurrentTagEdit;
    private (GalleryTagEdit Edit, PatternTag Sent)? galleryTagSaving;
    private bool GalleryTagAutosavePending => galleryTagSaving is not null || galleryTagEdits.Any(e => e.Pending);

    private PatternTag GalleryTagDisplay(PatternTag tag)
        => galleryTagEdits.FirstOrDefault(e => e.Saved.Name == tag.Name)?.Desired ?? tag;

    private void QueueGalleryTagAutosave()
    {
        if (galleryTagCreating || galleryCurrentTagEdit is not { } edit) return;
        string name = galleryTagName.Trim(), hex = galleryTagHex.Trim().TrimStart('#');
        if (!ValidGalleryTagName(name)) { RejectGalleryTagAutosave(edit, L.Get("core.pattern.invalid")); return; }
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        { RejectGalleryTagAutosave(edit, L.Get("gallery.invalidColour")); return; }
        if (galleryTags.Any(t => t.Name != edit.Saved.Name && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            || galleryTagEdits.Any(e => e != edit && string.Equals(e.Desired.Name, name, StringComparison.OrdinalIgnoreCase)))
        { RejectGalleryTagAutosave(edit, L.Get("gallery.tagExists")); return; }
        galleryTagError = "";
        edit.Desired = new(name, colour);
        edit.Due = timeProvider.GetUtcNow().AddMilliseconds(250);
    }

    private void RejectGalleryTagAutosave(GalleryTagEdit edit, string error)
    {
        edit.Desired = galleryTagSaving is { } write && write.Edit == edit ? write.Sent : edit.Saved;
        galleryTagError = error;
    }

    private void FlushGalleryTagAutosave()
    {
        if (galleryCurrentTagEdit is { } edit) edit.Due = DateTimeOffset.MinValue;
    }

    private void PumpGalleryTagAutosave()
    {
        if (galleryIoTask is not null || PatternEditingBusy || galleryTagDrag != 0 || galleryStore is not { } store) return;
        var edit = galleryTagEdits.FirstOrDefault(e => e.Pending && e.Due <= timeProvider.GetUtcNow());
        if (edit is null) return;
        string from = edit.Saved.Name;
        var sent = edit.Desired;
        galleryTagSaving = (edit, sent);
        StartGalleryIo(() => { store.EditTag(from, sent); return ReloadGallery(store, "", true); });
    }

    private void CompleteGalleryTagAutosave(bool success, string error = "")
    {
        if (galleryTagSaving is not { } write) return;
        galleryTagSaving = null;
        if (success)
        {
            write.Edit.Saved = write.Sent;
            if (galleryCurrentTagEdit == write.Edit) galleryTagSelected = write.Sent.Name;
        }
        else
        {
            write.Edit.Desired = write.Edit.Saved;
            if (galleryCurrentTagEdit == write.Edit) galleryTagError = error;
        }
        galleryTagEdits.RemoveAll(e => !e.Pending && e != galleryCurrentTagEdit);
    }

    private void ForgetGalleryTagEdit(PatternTag tag)
    {
        galleryTagEdits.RemoveAll(e => e.Saved.Name == tag.Name);
        galleryCurrentTagEdit = null;
    }
}
