using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private PatternFolder? galleryDeletingFolder;
    internal Rect GalleryFolderDeleteChoiceBounds(int row)
    {
        float w = Math.Min(520, width - 32);
        return new((width - w) / 2 + 24, (height - 292) / 2 + 124 + row * 50, w - 48, 40);
    }

    private void RequestDeleteGalleryFolder(Guid id)
    {
        if (!CanEditGalleryTags || galleryStore is not { } store || id == PatternFolder.DefaultId
            || DiscardConfirmationVisible || galleryFolders.FirstOrDefault(f => f.Id == id) is not { } folder) return;
        int generation = galleryGeneration;
        if (folder.PatternIds.Count == 0) { Delete(false); return; }
        ShowDiscardConfirmation(answer => { if (answer is 6 or 7) Delete(answer == 6); });
        galleryDeletingFolder = folder;

        void Delete(bool keepPatterns)
        {
            if (!CanEditGalleryTags || !ReferenceEquals(store, galleryStore) || generation != galleryGeneration) return;
            StartGalleryIo(() => { store.DeleteFolder(id, keepPatterns); return ReloadGallery(store, "", keepPatterns); });
        }
    }

    private void DrawGalleryFolderDeletion(ICanvas c)
    {
        if (galleryDeletingFolder is not { } folder) return;
        float w = Math.Min(520, width - 32), x = (width - w) / 2, y = (height - 292) / 2;
        c.Fill(new(0, 0, width, height), Background, opacity: .65f);
        c.Fill(new(x, y, w, 292), Panel, 8); c.Stroke(new(x, y, w, 292), Accent, 2, 8);
        c.Text(L.Get("gallery.deleteFolder"), x + 24, y + 22, 20, Foreground, w - 48, true);
        c.Text(folder.Name, x + 24, y + 57, 14, Foreground, w - 48);
        c.Text(L.Get("gallery.deleteFolderPrompt", folder.PatternIds.Count), x + 24, y + 86, 13, Muted, w - 48);
        Button(c, GalleryFolderDeleteChoiceBounds(0), L.Get("gallery.keepPatterns"), () => AnswerDiscard(6));
        Button(c, GalleryFolderDeleteChoiceBounds(1), L.Get("gallery.deleteFolderPatterns"), () => AnswerDiscard(7));
        Button(c, GalleryFolderDeleteChoiceBounds(2), L.Get("mac.cancel"), () => AnswerDiscard(2), true);
    }
}
