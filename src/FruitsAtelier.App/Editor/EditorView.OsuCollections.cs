using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private OsuCollectionCatalog? osuCollections;
    private Task<OsuCollectionCatalog>? osuCollectionsTask;
    private DateTime nextOsuCollectionCheck;
    private bool IsOsuCollection => libraryCollection?.StartsWith("osu:", StringComparison.Ordinal) == true;
    private IEnumerable<string> AllLibraryCollectionIds => libraryMemory.Collections.Select(c => c.Id)
        .Concat(osuCollections?.Collections.Select(c => c.Id) ?? []);

    private void PumpOsuCollections()
    {
        if (osuCollectionsTask is { IsCompleted: true })
        {
            var result = osuCollectionsTask.GetAwaiter().GetResult();
            osuCollectionsTask = null;
            if (result.Root == LibrarySettings.OsuRoot && result != osuCollections)
            {
                bool changed = result.Root != osuCollections?.Root || result.Fingerprint != osuCollections?.Fingerprint;
                osuCollections = result;
                if (IsOsuCollection && !result.Collections.Any(c => c.Id == libraryCollection)) SwitchLibraryCategory(false);
                if (changed) InvalidateLibraryOrganization();
            }
        }
        if (!LibraryVisible || osuCollectionsTask is not null || DateTime.UtcNow < nextOsuCollectionCheck) return;
        string root = LibrarySettings.OsuRoot;
        var previous = osuCollections;
        nextOsuCollectionCheck = DateTime.UtcNow.AddSeconds(3);
        osuCollectionsTask = Task.Run(() => OsuCollectionCatalog.Read(root, previous));
    }
}
