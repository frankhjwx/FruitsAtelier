using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool MergeDialogVisible { get; private set; }
    private Guid[] mergeTargets = [];
    private bool mergeCurved, mergeAllowsLinear;
    private string mergeError = "";

    private void BreakSelectedStreams()
    {
        if (!ClipboardInteractionReady || notesLocked) return;
        var ids = ClipboardSelectedParentIds();
        Guid[] fruits = [];
        if (Edit(L.Get("stream.breakFruits"), () =>
        {
            fruits = ObjectStructureEditing.BreakStreams(Document, ids);
            ValidateStructureConversion();
        })) { SelectObjects(fruits); tool = Tool.Select; legacyButtonSlider = Guid.Empty; }
    }

    private void ClearSelectedInternalAnchors()
    {
        if (!ClipboardInteractionReady || notesLocked) return;
        var ids = ClipboardSelectedParentIds();
        if (!Document.Tracks.Any(t => ids.Contains(t.Id)) && !Document.ImportedSliders.Any(t => ids.Contains(t.Id))) return;
        if (Edit(L.Get("slider.clearInternal"), () =>
        {
            foreach (var id in Document.ImportedSliders.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArray())
                ConvertImportedSlider(id);
            foreach (var track in Document.Tracks.Where(t => ids.Contains(t.Id))) ObjectStructureEditing.ClearInternalAnchors(track);
            ValidateStructureConversion();
        })) { SelectObjects(ids); legacyButtonSlider = Guid.Empty; }
    }

    private void ValidateStructureConversion()
    {
        var converted = CatchStreamConverter.Convert(Document);
        if (!converted.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), converted.Diagnostics));
    }

    private void OpenMergeDialog()
    {
        if (!ClipboardInteractionReady || notesLocked) return;
        var chosen = ObjectStructureEditing.MergeSelection(Document, ClipboardSelectedParentIds());
        if (chosen.Error is not null) { StatusMessage = chosen.Error; return; }
        mergeTargets = chosen.Ids; mergeAllowsLinear = chosen.AllowsLinear;
        mergeCurved = !mergeAllowsLinear; mergeError = "";
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        MergeDialogVisible = true; legacyButtonSlider = Guid.Empty;
    }

    private void ApplyMerge()
    {
        Guid id = Guid.Empty;
        if (Edit(L.Get("merge.title"), () => id = ObjectStructureEditing.Merge(Document, mergeTargets, mergeCurved,
            Document.DerandomizeDroplets ?? LibrarySettings.DerandomizeDroplets).Id))
        { MergeDialogVisible = false; SelectObjects([id]); tool = Tool.Select; }
        else mergeError = L.Get("merge.failed", StatusMessage);
    }

    private void DrawMergeDialog(ICanvas c)
    {
        if (!MergeDialogVisible) return;
        hits.Clear();
        float w = Math.Min(560, width - 32), x = (width - w) / 2, y = (height - 324) / 2;
        c.Fill(new(x, y, w, 324), Panel, 8); c.Stroke(new(x, y, w, 324), Grid, radius: 8);
        c.Text(L.Get("merge.title"), x + 18, y + 17, 16, Foreground, w - 36, true);
        c.Text(L.Get("merge.description"), x + 18, y + 49, 12, Muted, w - 36);
        float choiceWidth = (w - 42) / 2;
        if (mergeAllowsLinear) Button(c, new(x + 18, y + 99, choiceWidth, 32), L.Get("merge.linear"), () => mergeCurved = false, !mergeCurved);
        Button(c, new(x + (mergeAllowsLinear ? 24 + choiceWidth : 18), y + 99, choiceWidth, 32), L.Get("merge.curved"), () => mergeCurved = true, mergeCurved);
        c.Text(L.Get("merge.saveWarning"), x + 18, y + 149, 12, Error, w - 36);
        if (mergeError.Length > 0) c.Text(mergeError, x + 18, y + 218, 11, Error, w - 36);
        Button(c, new(x + w - 194, y + 274, 80, 32), L.Get("mac.cancel"), () => MergeDialogVisible = false);
        Button(c, new(x + w - 106, y + 274, 88, 32), L.Get("merge.confirm"), ApplyMerge, true);
    }
}
