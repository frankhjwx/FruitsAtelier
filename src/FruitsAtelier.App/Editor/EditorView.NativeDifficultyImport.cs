using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private (string Name, bool CanOverwrite)? nativeDifficultyConflict;

    private void BeginNativeDifficultyImport(string path, bool compressed)
    {
        if (RejectPatternProjectOperation()) return;
        if (!HasEditorProject || LibraryVisible) { ShowError(L.Get("project.importDifficultyOpenFirst")); return; }
        if (DiscardConfirmationVisible || !PrepareFileOperation()) return;
        RequestPausePlayback?.Invoke();
        var owner = history;
        var read = Task.Run(() =>
        {
            WorkspaceProject.RejectLinks(path);
            return compressed ? ProjectSerializer.Read(WorkspaceHistoryFile.ReadText(path)) : ProjectSerializer.ReadFile(path);
        });
        StartFazTask(read, () =>
        {
            if (ReferenceEquals(owner, history)) OfferNativeDifficultyImport(read.GetAwaiter().GetResult(), path);
        });
    }

    private void OfferNativeDifficultyImport(MapDocument document, string path)
    {
        string? version = OsuBeatmapReader.Setting(document, "Metadata", "Version");
        string name = string.IsNullOrWhiteSpace(version)
            ? Path.GetFileNameWithoutExtension(WorkspaceHistoryFile.LogicalPath(path)) : version.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = L.Get("project.defaultDifficulty", difficulties.Count + 1);
        var matches = difficulties.Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
        {
            ApplyNativeDifficulty(document, name, null);
            return;
        }

        var target = matches.Length == 1 ? matches[0] : matches.FirstOrDefault(d => ReferenceEquals(d, difficulties[activeDifficulty]));
        ShowDiscardConfirmation(answer =>
        {
            nativeDifficultyConflict = null;
            try
            {
                if (answer == 7 && target is not null) ApplyNativeDifficulty(document, target.Name, target);
                if (answer == 6)
                {
                    string unique = name;
                    for (int suffix = 2; difficulties.Any(d => string.Equals(d.Name, unique, StringComparison.OrdinalIgnoreCase)); suffix++)
                        unique = $"{name} ({suffix})";
                    ApplyNativeDifficulty(document, unique, null);
                }
            }
            catch (Exception error) { ShowError(L.Reformat(error.Message)); }
        });
        nativeDifficultyConflict = (name, target is not null);
    }

    private void ApplyNativeDifficulty(MapDocument document, string name, DifficultySession? target)
    {
        if (target is null && difficulties.Count >= 256) throw new InvalidDataException(L.Get("project.invalid"));
        var resourceContext = target?.History.Document ?? Document;
        string? resourceDirectory = FadArchive.ResourceDirectory(resourceContext);
        document.SourcePath = target is not null ? resourceContext.SourcePath
            : resourceDirectory is null ? null : Path.Combine(resourceDirectory, WorkspaceProject.ResourceRootMarker);
        document.AudioPath = resourceContext.AudioPath;
        SongSetup.Set(document, "General", "AudioFilename", OsuBeatmapReader.Setting(resourceContext, "General", "AudioFilename"));
        SongSetup.Set(document, "Metadata", "Version", name);
        SongSetup.Set(document, "Metadata", "BeatmapID",
            target is null ? "0" : SongSetup.Get(resourceContext, "Metadata", "BeatmapID", "0"));
        SongSetup.Set(document, "Metadata", "BeatmapSetID", OsuBeatmapReader.Setting(resourceContext, "Metadata", "BeatmapSetID"));
        WorkspaceTimingSynchronization.NormalizeDocument(document);
        var incoming = new ProjectDifficulty { Name = name, Document = document, FaOnly = target?.FaOnly ?? false };
        if (target is not null) incoming.Id = target.Id;
        int unassigned = FadArchive.UnassignUnavailableSections(document, difficulties.Where(d => d.Id != incoming.Id).Select(d => d.Id).ToHashSet());
        ApplyImportedDifficulty(incoming, HitsoundResourcePlan.Empty, L.Get("project.import"));
        SetFadNotice(L.Get("fad.imported", name), unassigned);
    }

    private void DrawNativeDifficultyConflict(ICanvas c)
    {
        if (nativeDifficultyConflict is not { } conflict) return;
        float w = Math.Min(580, width - 32);
        var help = conflict.CanOverwrite ? [] : WrapSyncText(c, L.Get("project.importDifficultyAmbiguous"), w - 48);
        float h = 208 + 22 * help.Length;
        float x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(0, 0, width, height), Background, opacity: .65f);
        c.Fill(new(x, y, w, h), Panel, 8);
        c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("project.importDifficultyConflict", conflict.Name), x + 24, y + 22, 20, Foreground, w - 48, true);
        float row = y + 64;
        foreach (var line in help) { c.Text(line.Text, x + 24, row, 14, Muted, w - 48); row += 22; }
        Button(c, new(x + 24, row, w - 48, 34), L.Get("project.importDifficultyOverwrite"), () => AnswerDiscard(7), enabled: conflict.CanOverwrite);
        Button(c, new(x + 24, row + 42, w - 48, 34), L.Get("project.importDifficultyCreate"), () => AnswerDiscard(6), enabled: difficulties.Count < 256);
        Button(c, new(x + 24, row + 84, w - 48, 34), L.Get("mac.cancel"), () => AnswerDiscard(2), true);
    }
}
