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
        })) { SelectObjects(fruits); tool = Tool.Select; }
    }

    private void ClearSelectedInternalAnchors()
    {
        if (!ClipboardInteractionReady || notesLocked || RejectReadOnlySelection()) return;
        var ids = ClipboardSelectedParentIds();
        if (!Document.Tracks.Any(t => ids.Contains(t.Id)) && !Document.ImportedSliders.Any(t => ids.Contains(t.Id))) return;
        if (Edit(L.Get("slider.clearInternal"), () =>
        {
            foreach (var id in Document.ImportedSliders.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToArray())
                ConvertImportedSlider(id);
            foreach (var track in Document.Tracks.Where(t => ids.Contains(t.Id))) ObjectStructureEditing.ClearInternalAnchors(track);
            ValidateStructureConversion();
        })) { SelectObjects(ids); }
    }

    private void ValidateStructureConversion()
    {
        var converted = CatchStreamConverter.Convert(Document);
        if (!converted.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), converted.Diagnostics));
    }

    private bool inspectorMergeExpanded;
    private void ShowMergeInspector()
    {
        if (!ClipboardInteractionReady || notesLocked) return;
        ShowSliderInspector(); RefreshSliderInspector();
        inspectorMergeExpanded = true;
        inspectorCollapsedSections.Remove(4);
        inspectorScroll = float.MaxValue;
    }
    private void ApplyMerge()
    {
        Guid id = Guid.Empty;
        if (Edit(L.Get("merge.title"), () => id = ObjectStructureEditing.Merge(Document, mergeTargets, mergeCurved,
            Document.DerandomizeDroplets ?? LibrarySettings.DerandomizeDroplets).Id))
        { MergeDialogVisible = false; SelectObjects([id]); tool = Tool.Select; }
        else mergeError = L.Get("merge.failed", StatusMessage);
    }

    private (string Text, float Width, string[] Lines) mergeWarningLayout;
    private string[] MergeWarningLines(ICanvas c, float width)
    {
        string text = L.Get("merge.saveWarning");
        if (mergeWarningLayout.Text == text && mergeWarningLayout.Width == width) return mergeWarningLayout.Lines;
        var lines = new List<string>();
        while (text.Length > 0)
        {
            int count = 1;
            while (count < text.Length && c.MeasureText(text[..(count + 1)], 13) <= width) count++;
            int space = text.LastIndexOf(' ', count - 1, count);
            if (count < text.Length && space > 0) count = space;
            lines.Add(text[..count]); text = text[count..].TrimStart();
        }
        mergeWarningLayout = (L.Get("merge.saveWarning"), width, lines.ToArray());
        return mergeWarningLayout.Lines;
    }
    private float MergeControlsHeight(ICanvas c, float width)
        => (width < 280 && mergeAllowsLinear ? 138 : 100) + MergeWarningLines(c, width).Length * 20
            + (mergeError.Length > 0 ? WrapSyncText(c, mergeError, width).Length * 20 + 8 : 0);

    private void DrawMergeControls(ICanvas c, Rect r, bool enabled)
    {
        c.Text(L.Get("merge.title"), r.X, r.Y + 4, 13, Foreground, r.Width, true);
        bool vertical = r.Width < 280;
        float choiceWidth = vertical ? r.Width : (r.Width - 8) / 2;
        if (mergeAllowsLinear) Button(c, new(r.X, r.Y + 28, choiceWidth, 30), L.Get("merge.linear"), () => mergeCurved = false, !mergeCurved, enabled);
        Button(c, new(r.X + (mergeAllowsLinear && !vertical ? choiceWidth + 8 : 0), r.Y + (mergeAllowsLinear && vertical ? 66 : 28), mergeAllowsLinear ? choiceWidth : r.Width, 30),
            L.Get("merge.curved"), () => mergeCurved = true, mergeCurved, enabled);
        float y = r.Y + (mergeAllowsLinear && vertical ? 104 : 66);
        foreach (string line in MergeWarningLines(c, r.Width))
        { c.Text(line, r.X, y, 13, Muted, r.Width); y += 20; }
        if (mergeError.Length > 0)
            foreach (var line in WrapSyncText(c, mergeError, r.Width))
            { c.Text(line.Text, r.X, y, 13, Error, r.Width); y += 20; }
        Button(c, new(r.X, y + 4, r.Width, 30), L.Get("merge.confirm"), ApplyMerge, true, enabled);
    }
}
