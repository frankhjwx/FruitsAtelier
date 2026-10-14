using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool? songRandomizeAll;
    private bool songDerandomizeDroplets, songDerandomizeHardRock;
    private CurveTrack? RandomizeSelectedTrack => SelectedTrack is { StreamSnapDivisor: null } track ? track : null;
    private bool CanRandomizeDroplets => ClipboardInteractionReady && !notesLocked && !Document.DerandomizeFSliderDroplets;

    private static void SetDropletRandomization(CurveTrack track, bool enabled)
    {
        if (track.DropletRandomization is null && !enabled) return;
        track.DropletRandomization ??= new();
        track.DropletRandomization.Enabled = enabled;
    }

    internal void RandomizeAllDroplets(bool enabled)
    {
        if (!CanRandomizeDroplets) return;
        Edit(L.Get(enabled ? "randomize.enableAll" : "randomize.disableAll"), () =>
        {
            foreach (var track in Document.Tracks.Where(t => t.StreamSnapDivisor is null)) SetDropletRandomization(track, enabled);
        });
    }

    internal void ToggleSelectedDropletRandomization()
    {
        if (!CanRandomizeDroplets || RandomizeSelectedTrack is not { } selected) return;
        Guid id = selected.Id;
        bool enabled = selected.DropletRandomization is not { Enabled: true };
        Edit(L.Get("randomize.title"), () => SetDropletRandomization(Document.Tracks.Single(t => t.Id == id), enabled));
    }

}
