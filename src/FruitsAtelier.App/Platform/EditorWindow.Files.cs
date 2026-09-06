using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private AudioTransport audio = new();
    private string? projectPath;
    private static string Artifacts => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(AppLog.Path)!, ".."));

    private void ConfigureFiles()
    {
        view.RequestOpen = () => FileOperation(() =>
        {
            if (!ConfirmDiscard()) return;
            string? path = MapFileDialog.Select(hwnd, false, L.Get("files.open"), MapFileDialog.OpenFilter);
            if (path is not null) OpenPath(path);
        });
        view.RequestNewProject = () => FileOperation(() =>
        {
            if (!ConfirmDiscard()) return;
            ResetAudio(); projectPath = null; view.NewProject();
        });
        view.RequestImportDifficulty = () => FileOperation(() =>
        {
            if (!view.PrepareFileOperation()) return;
            string? path = MapFileDialog.Select(hwnd, false, L.Get("project.import"), MapFileDialog.OsuFilter);
            if (path is not null) view.AddDifficulty(OsuBeatmapReader.ReadFile(path));
        });
        view.RequestDifficultyChanged = () =>
        {
            ResetAudio();
            if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) { audio.Load(view.Document.AudioPath); audio.Seek(view.PlayheadMs); }
        };
        view.RequestSave = () => FileOperation(() => SaveProject(false));
        view.RequestSaveAs = () => FileOperation(() => SaveProject(true));
        view.RequestExport = () => FileOperation(ExportBeatmap);
        view.RequestAudio = () => FileOperation(() =>
        {
            if (!view.PrepareFileOperation()) return;
            string? path = MapFileDialog.Select(hwnd, false, L.Get("files.audio"), MapFileDialog.AudioFilter, view.Document.AudioPath);
            if (path is null) return;
            view.ChangeAudioPath(path);
            audio.Load(path);
        });
        view.RequestTogglePlayback = () => { if (audio.IsPlaying) audio.Pause(); else audio.Play(); PollAudio(); };
        view.RequestSeek = time => { if (audio.CanPlay) audio.Seek(time); };
    }

    private void FileOperation(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException)
        {
            view.SetNotice(L.Get("files.failed", L.Localized(error.Message)));
            AppLog.Write(error.ToString());
            Native.MessageBox(hwnd, error.Message, L.Get("files.incomplete"), 0x10);
        }
        UpdateTitle(); Invalidate();
    }

    private void OpenPath(string path)
    {
        var project = BeatmapArchive.OpenProject(path, Path.Combine(Artifacts, "beatmaps"));
        view.LoadProject(project);
        projectPath = Path.GetExtension(path).Equals(".catchproj", StringComparison.OrdinalIgnoreCase) ? path : null;
        ResetAudio();
        if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) audio.Load(view.Document.AudioPath);
        AppLog.Write($"Opened project: {path}; difficulties={project.Difficulties.Count}");
    }

    private bool SaveProject(bool saveAs)
    {
        if (!view.PrepareFileOperation()) return false;
        string? destination = projectPath;
        if (saveAs || destination is null)
        {
            string folder = Path.Combine(Artifacts, "projects");
            Directory.CreateDirectory(folder);
            destination = MapFileDialog.Select(hwnd, true, L.Get("files.saveProject"), MapFileDialog.ProjectFilter,
                destination ?? Path.Combine(folder, SafeName(view.ProjectName) + ".catchproj"), "catchproj");
        }
        if (destination is null) return false;
        ProjectSerializer.WriteFile(view.CaptureProject(), destination);
        projectPath = destination;
        view.MarkSaved();
        view.SetNotice(L.Get("files.saved", destination));
        return true;
    }

    private void ExportBeatmap()
    {
        if (!view.PrepareFileOperation()) return;
        string folder = Path.Combine(Artifacts, "exports", SafeName(view.Document.Name));
        Directory.CreateDirectory(folder);
        string? destination = MapFileDialog.Select(hwnd, true, L.Get("files.export"), MapFileDialog.OsuFilter,
            Path.Combine(folder, SafeName(view.Document.Name + " [" + view.CurrentDifficultyName + "]") + ".osu"), "osu");
        if (destination is null) return;
        var result = OsuBeatmapWriter.Serialize(view.Document, view.CompensateTinyDroplets);
        CopyResources(view.Document, Path.GetDirectoryName(destination)!, result.ReadBack);
        OsuBeatmapWriter.WriteFile(view.Document, destination, view.CompensateTinyDroplets);
        view.SetNotice(result.ObjectSequenceMatches
            ? L.Get("files.exportMatched", result.MaxConvertedXError, result.MaxConvertedTimeErrorMs)
            : L.Get("files.exportChanged"));
        if (result.Diagnostics.Count > 0)
            Native.MessageBox(hwnd, string.Join("\n", result.Diagnostics.Take(8)), L.Get("files.diagnostics"), 0x40);
    }

    internal static void CopyResources(MapDocument document, string destinationDirectory, MapDocument exportedDocument)
        => BeatmapResources.Copy(document, destinationDirectory, exportedDocument);

    private static string SafeName(string name)
    {
        string result = new(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).Take(100).ToArray());
        return string.IsNullOrWhiteSpace(result) ? L.Get("files.untitled") : result.Trim().TrimEnd('.');
    }

    private void ResetAudio() { audio.Dispose(); audio = new AudioTransport(); }

    private void PollAudio()
    {
        if (!string.Equals(audio.FilePath, view.Document.AudioPath, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(view.Document.AudioPath)) ResetAudio();
            else audio.Load(view.Document.AudioPath);
        }
        var state = audio.State;
        string? error = state.Error is null ? null : L.Reformat(state.Error);
        bool changed = state.IsPlaying || state.IsLoading || view.AudioReady != state.CanPlay
            || view.AudioPlaying != state.IsPlaying || view.AudioLoading != state.IsLoading
            || Math.Abs(view.AudioDurationMs - state.DurationMs) > 0.5
            || error is not null && error != view.AudioNotice
            || state.CanPlay && Math.Abs(view.PlayheadMs - state.PositionMs) > 1;
        if (!changed) return;
        view.UpdateTransport(state.PositionMs, state.DurationMs, state.CanPlay, state.IsPlaying, state.IsLoading, error, state.FilePath);
        Invalidate();
    }
}
