using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private AudioTransport audio = new();
    private readonly HitsoundPlayer hitsounds = new(AppLog.Write);
    private string? projectPath;
    private static string Artifacts => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(AppLog.Path)!, ".."));

    private void ConfigureFiles()
    {
        view.RequestPausePlayback = () => audio.Pause();
        audio.Hitsounds = hitsounds;
        view.RequestAudioVolume = (song, hit) => { audio.SongVolume = song; hitsounds.Volume = hit; };
        view.RequestAudioPreference = () => FileOperation(() => view.LibrarySettings.Save());
        view.RequestViewPreference = () => FileOperation(() => view.LibrarySettings.Save());
        view.ApplyAudioVolume();
        view.HitsoundLookaheadMs = 250;
        view.RequestScheduleHitsound = hitsounds.Schedule;
        view.RequestHitsound = hitsounds.PlayImmediate;
        view.RequestPrepareHitsound = hitsounds.Prepare;
        view.RequestPreloadHitsounds = documents => hitsounds.PreloadProject(documents, view.HitsoundSkinFolders);
        view.PreloadProjectHitsounds();
        view.RequestStopHitsounds = hitsounds.Stop;
        view.RequestLanguagePreference = language => FileOperation(() => FruitsAtelier.Localization.LanguagePreference.SaveLanguage(language));
        view.RequestOpen = () => ConfirmDiscard(() =>
        {
            string? path = MapFileDialog.Select(hwnd, false, L.Get("files.open"), MapFileDialog.OpenFilter);
            if (path is not null) OpenPath(path);
        });
        view.RequestNewProject = () => ConfirmDiscard(() =>
        {
            ResetAudio(); projectPath = null; view.NewProject(); view.CloseLibrary();
        });
        view.RequestImportDifficulty = () => FileOperation(() =>
        {
            if (!view.PrepareFileOperation()) return;
            string? path = MapFileDialog.Select(hwnd, false, L.Get("project.import"), MapFileDialog.OsuFilter);
            if (path is not null)
            {
                var document = OsuBeatmapReader.ReadFile(path);
                LibraryOperations.ImportFolder(Path.GetDirectoryName(path)!, view.LibrarySettings);
                view.AddDifficulty(document);
            }
        });
        view.RequestDifficultyChanged = () =>
        {
            ResetAudio();
            if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) { audio.Load(view.Document.AudioPath); audio.Seek(view.PlayheadMs); }
        };
        view.RequestSave = () => FileOperation(view.SaveCurrentDifficulty);
        view.RequestExport = view.ShowWorkspaceExport;
        ConfigureLibrary();
        view.RequestTogglePlayback = () =>
        {
            if (audio.IsPlaying) { audio.Pause(); view.ResetHitsounds(); }
            else { view.PrimeScheduledHitsounds(audio.PositionMs >= audio.DurationMs - 1 ? 0 : audio.PositionMs); audio.Play(); }
            PollAudio();
        };
        view.RequestPlaybackSpeed = speed => audio.SetPlaybackSpeed(speed);
        view.RequestSeek = time =>
        {
            if (!audio.CanPlay) return;
            if (audio.IsPlaying) view.PrimeScheduledHitsounds(Math.Clamp(time, 0, audio.DurationMs));
            else view.ResetHitsounds();
            audio.Seek(time);
        };
    }

    private void FileOperation(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException or Microsoft.Data.Sqlite.SqliteException)
        {
            view.SetNotice(L.Get("files.failed", L.Localized(error.Message)));
            AppLog.Write(error.ToString());
            if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
            view.ShowError(L.Reformat(error.Message));
        }
        UpdateTitle(); Invalidate();
    }

    private void OpenPath(string path)
    {
        if (Path.GetExtension(path).Equals(".catchdiff", StringComparison.OrdinalIgnoreCase))
        {
            view.LoadWorkspace(WorkspaceProject.Open(Path.GetDirectoryName(path)!));
            ResetAudio(); if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) audio.Load(view.Document.AudioPath); return;
        }
        var session = LibraryOperations.ImportPath(path, view.LibrarySettings);
        view.LoadWorkspace(session);
        view.RefreshLibrary();
        projectPath = Path.GetExtension(path).Equals(".catchproj", StringComparison.OrdinalIgnoreCase) ? path : null;
        ResetAudio();
        if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) audio.Load(view.Document.AudioPath);
        AppLog.Write($"Opened project: {path}; difficulties={session.Project.Difficulties.Count}");
    }

    private bool SaveProject()
    {
        if (!view.PrepareFileOperation()) return false;
        return view.SaveWorkspace();
    }

    internal static void CopyResources(MapDocument document, string destinationDirectory, MapDocument exportedDocument)
        => BeatmapResources.Copy(document, destinationDirectory, exportedDocument);

    private static string SafeName(string name)
    {
        string result = new(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).Take(100).ToArray());
        return string.IsNullOrWhiteSpace(result) ? L.Get("files.untitled") : result.Trim().TrimEnd('.');
    }

    private void ResetAudio() { view.ResetHitsounds(); audio.Dispose(); audio = new AudioTransport { Hitsounds = hitsounds }; audio.SetPlaybackSpeed(view.PlaybackSpeed); view.ApplyAudioVolume(); }

    private void PollAudio()
    {
        if (view.LibraryVisible || view.WorkspaceSession is not null || view.SliderConversionBusy || view.StarRatingsRefreshing) Invalidate();
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
        audio.TracePresentation(view.PlayheadMs, view.AudioPlaying, state);
        view.UpdateTransport(state.PositionMs, state.DurationMs, state.CanPlay, state.IsPlaying, state.IsLoading,
            error, state.FilePath, state.PositionTimestampMs, state.OutputBufferAheadMs);
        Invalidate();
    }
}
