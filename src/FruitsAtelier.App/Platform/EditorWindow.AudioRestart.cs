using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed record AudioSettingsRestartState(int ParentProcessId, string? WorkspaceDirectory,
    int Difficulty, double PositionMs, double PlaybackSpeed, bool LibraryVisible, float Scroll);

internal sealed partial class EditorWindow
{
    private bool audioSettingsRestarting;

    private void RestartAudioSettings(float scroll)
    {
        if (audioSettingsRestarting || !view.PrepareFileOperation()) return;
        if (view.HasEditorProject && (view.IsDirty || view.WorkspaceSession is null))
        {
            SaveProject(() => RestartAudioSettings(scroll));
            return;
        }
        FileOperation(() =>
        {
            view.SaveLibraryMemory();
            var state = new AudioSettingsRestartState(Environment.ProcessId, view.WorkspaceSession?.Directory,
                view.ActiveDifficultyIndex, view.PlayheadMs, view.PlaybackSpeed, view.LibraryVisible, scroll);
            string token = Path.Combine(Path.GetDirectoryName(AppLog.Path)!, $"audio-settings-restart-{Guid.NewGuid():N}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(token)!);
            File.WriteAllText(token, JsonSerializer.Serialize(state));
            try
            {
                string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application executable is unavailable.");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(Program).Assembly.Location);
                start.ArgumentList.Add("--resume-audio-settings");
                start.ArgumentList.Add(token);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Application restart failed.");
                audioSettingsRestarting = true;
                Native.DestroyWindow(hwnd);
            }
            catch { File.Delete(token); view.OpenAudioDiagnosticSettings(scroll); throw; }
        });
    }

    internal static AudioSettingsRestartState ReadAudioSettingsRestart(string token)
    {
        var state = JsonSerializer.Deserialize<AudioSettingsRestartState>(File.ReadAllText(token))
            ?? throw new InvalidDataException("Invalid audio settings restart state.");
        if (state.ParentProcessId <= 0 || state.Difficulty < 0 || !double.IsFinite(state.PositionMs)
            || state.PositionMs < 0 || !double.IsFinite(state.PlaybackSpeed) || state.PlaybackSpeed is < .1 or > 1.5
            || !float.IsFinite(state.Scroll) || state.Scroll < 0)
            throw new InvalidDataException("Invalid audio settings restart state.");
        try
        {
            using var parent = Process.GetProcessById(state.ParentProcessId);
            if (!parent.WaitForExit(10000)) throw new IOException("Previous application instance has not exited.");
        }
        catch (ArgumentException) { }
        File.Delete(token);
        return state;
    }

    private void RestoreAudioSettings(AudioSettingsRestartState state)
    {
        if (state.WorkspaceDirectory is { } directory)
        {
            view.LoadWorkspace(WorkspaceProject.Open(directory));
            view.RestoreAudioDiagnosticDifficulty(state.Difficulty);
            ResetAudio();
            if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) audio.Load(view.Document.AudioPath);
        }
        view.SetPlaybackSpeed(state.PlaybackSpeed);
        view.RestoreAudioDiagnosticPosition(state.PositionMs);
        if (state.LibraryVisible && !view.LibraryVisible) view.ShowLibrary();
        view.OpenAudioDiagnosticSettings(state.Scroll);
    }
}
