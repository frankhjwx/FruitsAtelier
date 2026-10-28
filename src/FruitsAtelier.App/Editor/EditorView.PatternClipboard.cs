using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action? RequestPasteObjects { get; set; }
    private string? copiedPatternText;
    private (PatternSnapshot Pattern, EditorHistory History, double Time)? pendingPatternPaste;
    private PatternSnapshot? pendingPatternImport;

    private void CopyPatternText(PatternSnapshot pattern)
    {
        try
        {
            copiedPatternText = PatternClipboard.Encode(pattern);
            RequestCopyText?.Invoke(copiedPatternText);
            StatusMessage = L.Get("editor.status.objectsCopied", pattern.ObjectCount);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { StatusMessage = L.Get("gallery.failed", error.Message); }
    }

    private void RequestClipboardPaste()
    {
        if (RequestPasteObjects is { } request) request();
        else PasteClipboardText(copiedPatternText ?? "");
    }

    public void PasteClipboardText(string text)
    {
        if (!HasEditorProject || LibraryVisible || TimingPageVisible || !CanDropAudio || !ClipboardInteractionReady
            || notesLocked || IsEditingText) return;
        if (!PatternClipboard.IsPatternText(text)) { PasteSelection(); return; }
        if (PatternEditingActive || PatternEditingBusy || !CanStartGalleryPlacement || galleryIoTask is not null
            || pendingPatternPaste is not null || pendingPatternImport is not null) return;
        try
        {
            var pattern = PatternClipboard.Decode(text);
            if (galleryStore is null && !string.IsNullOrWhiteSpace(LibrarySettings.Workspace))
                ConfigurePatternGallery(Path.Combine(LibrarySettings.Workspace, "Patterns"));
            if (galleryStore is null) return;
            pendingPatternPaste = (pattern, history, playhead);
            EnsurePatternGalleryLoaded();
            PumpPatternClipboard();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { StatusMessage = L.Get("gallery.failed", error.Message); }
    }

    private void PumpPatternClipboard()
    {
        if (pendingPatternImport is { } incoming)
        {
            if (!HasEditorProject || LibraryVisible || !PanelVisible(DockPanel.PatternGallery))
            { pendingPatternImport = null; return; }
            if (!PatternGalleryInputAvailable || galleryBounds.Width <= 0 || galleryIoTask is not null) return;
            pendingPatternImport = null;
            ShowGalleryImportDestination(folder =>
            {
                if (galleryStore is not { } store || galleryIoTask is not null) return;
                StartGalleryIo(() => ReloadGallery(store, L.Get("gallery.imported",
                    store.Import(new([incoming], incoming.Tags), incoming.Name, folder))));
            });
            return;
        }
        if (pendingPatternPaste is not { } pending) return;
        if (!ReferenceEquals(pending.History, history) || !HasEditorProject || LibraryVisible)
        { pendingPatternPaste = null; return; }
        if (!galleryLoaded || galleryIoTask is not null) return;
        pendingPatternPaste = null;
        var pattern = pending.Pattern;
        var existing = galleryPatterns.FirstOrDefault(p => p.Id == pattern.Id && p.HasSameGeometry(pattern))
            ?? galleryPatterns.FirstOrDefault(p => p.HasSameGeometry(pattern));
        if (existing is not null) { PastePattern(existing, pending.Time); return; }
        AddDockPanel(DockPanel.PatternGallery, false);
        var group = PanelGroup(DockPanel.PatternGallery);
        group.Active = DockPanel.PatternGallery; group.Collapsed = false;
        pendingPatternImport = pattern;
    }

    private void PastePattern(PatternSnapshot pattern, double time)
    {
        if (!CanStartGalleryPlacement) return;
        int order;
        try { order = NextClipboardSourceOrder(pattern.ObjectCount); }
        catch (InvalidOperationException error) { StatusMessage = error.Message; return; }
        CancelPatternGalleryInteraction();
        galleryFocused = false;
        var before = Document.DeepClone();
        galleryDragTarget = before; galleryDragHistory = history;
        int version = galleryDragVersion;
        StatusMessage = L.Get("gallery.preparing");
        StartGalleryInsertion(before, version,
            () => PatternPlacement.Prepare(pattern, before, time).CreateDocument(firstSourceOrder: order));
    }
}
