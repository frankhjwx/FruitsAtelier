using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool? songRandomizeAll;
    private CurveTrack? RandomizeSelectedTrack => SelectedTrack is { StreamSnapDivisor: null } track ? track : null;
    private bool CanRandomizeDroplets => ClipboardInteractionReady && !notesLocked;

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

    private void OpenDropletRandomizationMenu()
    {
        contextItems.Clear();
        contextItems.Add(new(L.Get("randomize.enableAll"), () => RandomizeAllDroplets(true), CanRandomizeDroplets));
        contextItems.Add(new(L.Get("randomize.disableAll"), () => RandomizeAllDroplets(false), CanRandomizeDroplets));
        AddContextSeparator();
        contextItems.Add(new(L.Get(RandomizeSelectedTrack?.DropletRandomization is { Enabled: true }
            ? "randomize.disableSelected" : "randomize.enableSelected"), ToggleSelectedDropletRandomization,
            CanRandomizeDroplets && RandomizeSelectedTrack is not null));
        contextItems.Add(new(L.Get("randomize.resetAdjustments"), () =>
        {
            if (RandomizeSelectedTrack is not { } selected) return;
            Guid id = selected.Id;
            Edit(L.Get("randomize.resetAdjustments"), () => Document.Tracks.Single(t => t.Id == id).DropletRandomization?.Adjustments.Clear());
        }, CanRandomizeDroplets && RandomizeSelectedTrack?.DropletRandomization?.Adjustments.Count > 0));
        AddContextSeparator();
        contextItems.Add(new(L.Get("randomize.settings"), () => { OpenSongSetup(); songTab = 4; }));
        contextBounds = new(Math.Clamp(mouseX, 8, Math.Max(8, width - 308)),
            Math.Clamp(mouseY, 8, Math.Max(8, height - ContextMenuHeight - 8)), 300, ContextMenuHeight);
    }
}
