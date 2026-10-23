using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly List<(Rect Bounds, string Name)> galleryTagOrderRows = [];
    private string? galleryTagOrderPressed;
    private float galleryTagOrderStartX, galleryTagOrderStartY;
    private bool galleryTagReordering;
    private int galleryTagOrderSlot;
    private DateTimeOffset galleryTagOrderPageAfter;

    private void CancelGalleryTagOrder()
    {
        galleryTagOrderPressed = null;
        galleryTagReordering = false;
    }

    private bool BeginGalleryTagOrder(float x, float y)
    {
        if (galleryTagsPattern != Guid.Empty || !CanEditGalleryTags || GalleryTagAutosavePending) return false;
        var row = galleryTagOrderRows.FirstOrDefault(r => r.Bounds.Contains(x, y));
        if (row.Name is null) return false;
        galleryTagOrderPressed = row.Name;
        galleryTagOrderStartX = x; galleryTagOrderStartY = y;
        galleryTagOrderPageAfter = timeProvider.GetUtcNow();
        textEditor.Field = "";
        return true;
    }

    private bool MoveGalleryTagOrder(float x, float y)
    {
        if (galleryTagOrderPressed is null) return false;
        if (Math.Abs(x - galleryTagOrderStartX) + Math.Abs(y - galleryTagOrderStartY) >= ContentDragThreshold)
            galleryTagReordering = true;
        int rows = Math.Max(1, (int)((GalleryTagListBounds.Height - 80) / 36));
        int offset = galleryTagOrderRows.Count(r => y >= r.Bounds.Y + r.Bounds.Height / 2);
        galleryTagOrderSlot = Math.Min(galleryTags.Length, galleryTagPage * rows + offset);
        return true;
    }

    private void AdvanceGalleryTagOrderPage()
    {
        if (!galleryTagReordering || timeProvider.GetUtcNow() < galleryTagOrderPageAfter) return;
        int change = GalleryTagPreviousBounds.Contains(mouseX, mouseY) ? -1 : GalleryTagNextBounds.Contains(mouseX, mouseY) ? 1 : 0;
        if (change == 0) return;
        int rows = Math.Max(1, (int)((GalleryTagListBounds.Height - 80) / 36));
        galleryTagPage = Math.Clamp(galleryTagPage + change, 0, Math.Max(0, (galleryTags.Length - 1) / rows));
        galleryTagOrderPageAfter = timeProvider.GetUtcNow().AddMilliseconds(500);
    }

    private bool EndGalleryTagOrder(float x, float y)
    {
        if (galleryTagOrderPressed is not { } name) return false;
        MoveGalleryTagOrder(x, y);
        bool moved = galleryTagReordering;
        CancelGalleryTagOrder();
        if (!moved)
        {
            if (galleryTagOrderRows.Any(r => r.Name == name && r.Bounds.Contains(x, y)))
                SelectGalleryTag(galleryTags.FirstOrDefault(t => t.Name == name));
            return true;
        }
        if (!GalleryTagListBounds.Contains(x, y) || !CanEditGalleryTags || galleryStore is not { } store) return true;
        var order = galleryTags.Select(t => t.Name).ToList();
        int previous = order.IndexOf(name);
        if (previous < 0) return true;
        order.RemoveAt(previous);
        int target = Math.Clamp(galleryTagOrderSlot - (previous < galleryTagOrderSlot ? 1 : 0), 0, order.Count);
        order.Insert(target, name);
        if (previous != target)
            StartGalleryIo(() => { store.ReorderTags(order); return ReloadGallery(store, "", true); });
        return true;
    }

    private void DrawGalleryTagOrderMarker(ICanvas c)
    {
        if (!galleryTagReordering || galleryTagOrderRows.Count == 0) return;
        MoveGalleryTagOrder(mouseX, mouseY);
        int rows = Math.Max(1, (int)((GalleryTagListBounds.Height - 80) / 36));
        int slot = Math.Clamp(galleryTagOrderSlot - galleryTagPage * rows, 0, galleryTagOrderRows.Count);
        float y = slot < galleryTagOrderRows.Count ? galleryTagOrderRows[slot].Bounds.Y - 2 : galleryTagOrderRows[^1].Bounds.Bottom + 2;
        c.Line(GalleryTagListBounds.X + 5, y, GalleryTagListBounds.Right - 5, y, Accent, 2);
    }
}
