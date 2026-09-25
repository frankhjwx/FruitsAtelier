using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly HashSet<Guid> pendingImplicitSliderConversions = [];
    private void ChangeReverseCount(Guid id, int change)
    {
        if (Document.Tracks.FirstOrDefault(t => t.Id == id) is not { } track) return;
        Edit(L.Get(change > 0 ? "editor.command.addReverse" : "editor.command.removeReverse"), () =>
        {
            track.SpanCount = Math.Clamp(track.SpanCount + change, 1, 9000);
            Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
        });
    }

    private void ExtendSlider(Guid id, MapPoint target)
    {
        if (Document.Tracks.FirstOrDefault(t => t.Id == id) is not { } track || track.Nodes.Count < 2) return;
        if (target.TimeMs < CurveMath.EndTimeMs(track) + CurveMath.MinimumAnchorSpacingMs) return;
        var node = new Anchor { TimeMs = target.TimeMs, X = target.X };
        if (!Edit(L.Get("editor.command.extendSlider"), () =>
        {
            // Preserve all existing segments and incoming endpoint handles.
            // The added segment belongs to the base path, so every repeat extends.
            track.Nodes[^1].HandleOut = default;
            track.Nodes[^1].OutgoingKind = CurveKind.Linear;
            track.Nodes.Add(node);
            Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
        })) return;
        Select(node.Id, track.Id);
        tool = Tool.Slider;
    }

    private void EditImportedSlider()
    {
        if (SelectedImportedSlider is { } slider) EditImportedSlider(slider.Id);
    }

    private void EditImportedSlider(Guid id)
    {
        if (!Document.ImportedSliders.Any(slider => slider.Id == id)) return;
        if (Document.DerandomizeDroplets is null)
        {
            OfferSingleSliderConversion(id);
            return;
        }
        ConvertSelectedImportedSlider(id, Document.DerandomizeDroplets.Value);
    }

    private ImportedSliderEditResult ConvertImportedSlider(Guid id, CatchConversionCache? cache = null)
    {
        bool derandomize = Document.DerandomizeDroplets ?? LibrarySettings.DerandomizeDroplets;
        var result = ImportedSliderEditing.ConvertToTrack(Document, id, cache, derandomize);
        if (drag != DragKind.None && Document.DerandomizeDroplets is null) pendingImplicitSliderConversions.Add(id);
        return result;
    }

    private void ConvertSelectedImportedSlider(Guid id, bool derandomizeDroplets)
    {
        string notice = "";
        if (!Edit(L.Get("editor.command.editImportedSlider"), () =>
        {
            var result = ImportedSliderEditing.ConvertToTrack(Document, id, derandomizeDroplets: derandomizeDroplets);
            Document.DerandomizeDroplets = derandomizeDroplets;
            notice = string.Join(L.Get("editor.diagnostics.separator"), result.Diagnostics);
        })) return;
        Select(id, id);
        tool = Tool.Slider;
        StatusMessage = notice.Length == 0 ? L.Get("editor.status.importedSliderEditable") : notice;
    }
}
