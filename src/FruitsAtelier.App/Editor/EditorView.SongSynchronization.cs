using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SongSyncValues(string Name, string?[] Metadata, string?[] Design, uint[] Colours)
    {
        internal static SongSyncValues Capture(MapDocument document) => new(document.Name,
            SongSetup.SyncMetadataKeys.Select(k => OsuBeatmapReader.Setting(document, "Metadata", k)).ToArray(),
            SongDesign.Select(k => OsuBeatmapReader.Setting(document, "General", k)).ToArray(), SongSetup.Colours(document));

        internal bool Matches(SongSyncValues other) => Name == other.Name && Metadata.SequenceEqual(other.Metadata)
            && Design.SequenceEqual(other.Design) && Colours.SequenceEqual(other.Colours);

        internal void Apply(EditorHistory history, SongSyncValues expected)
        {
            var current = Capture(history.Document);
            var metadata = Enumerable.Range(0, Metadata.Length).Where(i => current.Metadata[i] == expected.Metadata[i]).ToArray();
            var design = Enumerable.Range(0, Design.Length).Where(i => current.Design[i] == expected.Design[i]).ToArray();
            bool name = current.Name == expected.Name, colours = current.Colours.SequenceEqual(expected.Colours);
            // Later difficulty-local edits supersede the older set-wide operation for those fields.
            history.RebaseSharedMetadata(document =>
            {
                if (name) document.Name = Name;
                foreach (int i in metadata) SongSetup.Set(document, "Metadata", SongSetup.SyncMetadataKeys[i], Metadata[i]);
                foreach (int i in design) SongSetup.Set(document, "General", SongDesign[i], Design[i]);
                if (colours) SongSetup.SetColours(document, Colours);
            });
        }
    }

    private float SongSyncButtonHeight(ICanvas c, float width)
        => Math.Max(32, WrapSyncText(c, L.Get("song.syncGeneral"), width - 18).Length * 20 + 12);

    private void DrawSongSyncButton(ICanvas c, Rect bounds)
    {
        bool enabled = difficulties.Count > 1 && !history.HasActiveTransaction && !SynchronizationBusy;
        Button(c, bounds, "", SynchronizeSongSetup, enabled: enabled);
        var lines = WrapSyncText(c, L.Get("song.syncGeneral"), bounds.Width - 18);
        for (int i = 0; i < lines.Length; i++)
            c.Text(lines[i].Text, bounds.X + 9, bounds.Y + 6 + i * 20, 15, enabled ? Foreground : Muted, bounds.Width - 18);
    }

    private void SynchronizeSongSetup()
    {
        if (difficulties.Count < 2 || difficulties.Any(d => d.History.HasActiveTransaction) || SynchronizationBusy) return;
        committingLiveSong = true;
        try { ApplySongSetup(); }
        finally { committingLiveSong = false; }
        if (songError.Length > 0) return;
        var source = SongSyncValues.Capture(Document);
        var targets = difficulties.Where(d => d.History != history)
            .Select(d => (History: d.History, Before: SongSyncValues.Capture(d.History.Document)))
            .Where(d => !source.Matches(d.Before)).ToArray();
        if (targets.Length == 0) return;
        history.EditRelatedDocuments(L.Get("song.syncGeneral"), forward =>
        {
            foreach (var target in targets)
                if (difficulties.Any(d => d.History == target.History))
                    (forward ? source : target.Before).Apply(target.History, forward ? target.Before : source);
        });
        songField = ""; SongSetupInputSession++;
    }
}
