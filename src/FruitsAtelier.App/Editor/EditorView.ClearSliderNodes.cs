using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static bool HasInternalSliderControls(CurveTrack track) => track.Nodes.Count >= 2
        && (track.Nodes.Count > 2 || track.Nodes.Any(node => node.HandleIn != default
            || node.HandleOut != default || node.OutgoingCurve is not null));

    private bool CanClearSliderNodes => ClipboardInteractionReady && !notesLocked && !SliderConversionBusy
        && Document.Tracks.Any(track => IsObjectSelected(track.Id) && HasInternalSliderControls(track));

    private void ClearSliderNodes()
    {
        if (!CanClearSliderNodes) return;
        var tracks = Document.Tracks.Where(track => IsObjectSelected(track.Id) && HasInternalSliderControls(track)).ToArray();
        var selected = FlagTargets();
        if (!Edit(L.Get("slider.clearInternalNodes"), () =>
        {
            foreach (var track in tracks)
            {
                track.Nodes.RemoveRange(1, track.Nodes.Count - 2);
                track.Kind = CurveKind.Linear;
                foreach (var node in track.Nodes)
                {
                    node.HandleIn = node.HandleOut = default;
                    node.OutgoingKind = null;
                    node.OutgoingCurve = null;
                }
            }
        })) return;
        SelectObjects(selected);
    }
}
