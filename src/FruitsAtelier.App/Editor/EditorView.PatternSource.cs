using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Task<LibraryMap?>? patternSourceTask;
    private PatternSnapshot? patternSourceSnapshot;
    private EditorHistory? patternSourceOwner;
    private bool patternSourcePreservesOriginal;
    private bool PatternSourceNeedsRedraw => patternSourceTask is not null;

    private void RequestPatternSourceNavigation(PatternSnapshot pattern)
    {
        if (patternSourceTask is not null || LibraryOpening || PatternEditingBusy) return;
        if (PatternEditingActive)
        {
            RequestExitPatternEditing(() => LocatePatternSource(pattern, true), retainOriginalForNavigation: true);
            return;
        }
        LocatePatternSource(pattern, false);
    }

    private void LocatePatternSource(PatternSnapshot pattern, bool preservesOriginal)
    {
        if (!PrepareFileOperation()) return;
        int index = difficulties.FindIndex(d => MatchesPatternSource(pattern.Source, d.History.Document));
        if (index >= 0)
        {
            ConfirmPatternSourceNavigation(() =>
            {
                if (SwitchDifficulty(index)) SeekTo(pattern.Source.StartTimeMs);
            }, preservesOriginal);
            return;
        }
        var store = galleryStore;
        string workspace = LibrarySettings.Workspace, songs = LibrarySettings.Songs;
        patternSourceSnapshot = pattern; patternSourceOwner = history;
        patternSourcePreservesOriginal = preservesOriginal;
        StatusMessage = L.Get("gallery.sourceLoading");
        patternSourceTask = Task.Run(() =>
        {
            string? hint = store?.SourcePath(pattern.Id);
            if (hint is not null && File.Exists(hint))
            {
                if (Path.GetExtension(hint).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                {
                    var local = LibraryDatabase.ReadMetadata(hint);
                    if (local is not null && MatchesPatternSource(pattern.Source, local)) return local;
                }
                else if (Path.GetDirectoryName(hint) is { } directory && File.Exists(Path.Combine(directory, WorkspaceProject.ManifestName)))
                {
                    var local = ProjectSerializer.ReadFile(hint);
                    if (MatchesPatternSource(pattern.Source, local))
                        return new LibraryMap(hint, directory, pattern.Source.Title, "", pattern.Source.Artist, "",
                            pattern.Source.Creator, pattern.Source.Difficulty, "", "", "", "", directory);
                }
            }
            var database = new LibraryDatabase(workspace, songs);
            var candidates = database.AssociationCandidates(pattern.CreateDocument())
                .Where(m => MatchesPatternSource(pattern.Source, m)).ToArray();
            if (candidates.Length > 1) throw new InvalidOperationException(L.Get("gallery.sourceAmbiguous"));
            return candidates.FirstOrDefault();
        });
    }

    private void PumpPatternSource()
    {
        if (patternSourceTask is not { IsCompleted: true } task) return;
        patternSourceTask = null;
        var pattern = patternSourceSnapshot; patternSourceSnapshot = null;
        var owner = patternSourceOwner; patternSourceOwner = null;
        try
        {
            var map = task.GetAwaiter().GetResult();
            if (pattern is null || !ReferenceEquals(owner, history) || PatternEditingActive) return;
            if (map is null) { ShowError(L.Get("gallery.sourceMissing")); return; }
            ConfirmPatternSourceNavigation(() => BeginLibraryOpen(map, () =>
            {
                RequestDifficultyChanged?.Invoke();
                SeekTo(pattern.Source.StartTimeMs);
            }, selectDifficulty: true), patternSourcePreservesOriginal);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { ShowError(error.Message); }
    }

    private void ConfirmPatternSourceNavigation(Action navigate, bool preservesOriginal)
    {
        if (!PrepareFileOperation()) return;
        if (preservesOriginal || !IsDirty) { navigate(); return; }
        ShowDiscardConfirmation(answer =>
        {
            if (answer == 7) navigate();
            else if (answer == 6) BeginWorkspaceSave(saved => { if (saved) navigate(); });
        });
    }

    private static bool MatchesPatternSource(PatternSource source, MapDocument document)
    {
        string Get(string key) => OsuBeatmapReader.Setting(document, "Metadata", key) ?? "";
        if (source.BeatmapId > 0 && long.TryParse(Get("BeatmapID"), out long id) && id == source.BeatmapId) return true;
        return source.Title.Length > 0 && source.Title == (OsuBeatmapReader.Setting(document, "Metadata", "Title") ?? document.Name) && source.Artist == Get("Artist")
            && source.Creator == Get("Creator") && source.Difficulty == Get("Version");
    }

    private static bool MatchesPatternSource(PatternSource source, LibraryMap map)
        => source.BeatmapId > 0 && source.BeatmapId == map.BeatmapId
            || source.Title.Length > 0 && source.Title == map.Title && source.Artist == map.Artist
                && source.Creator == map.Creator && source.Difficulty == map.Difficulty;
}
