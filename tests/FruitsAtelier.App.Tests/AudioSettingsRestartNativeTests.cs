#if WINDOWS
using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;

internal static class AudioSettingsRestartNativeTests
{
    internal static void Run(string executable)
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/audio-restart-native", Guid.NewGuid().ToString("N")));
        var project = BeatmapProject.FromDocuments([
            new MapDocument { Name = "First", DurationMs = 5000 },
            new MapDocument { Name = "Second", DurationMs = 5000 }
        ]);
        var session = WorkspaceProject.Create(Path.Combine(root, "workspace"), project, "");
        foreach (bool library in new[] { false, true })
        {
            string token = Path.Combine(root, $"resume-{library}.json");
            File.WriteAllText(token, JsonSerializer.Serialize(new AudioSettingsRestartState(int.MaxValue,
                library ? null : session.Directory, library ? 0 : 1, library ? 0 : 1500, .75, library, 200)));
            var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--audio-settings-resume-check");
            start.ArgumentList.Add(token);
            start.Environment["FRUITSATELIER_AUDIO_DIAGNOSTICS"] = "1";
            start.Environment["FRUITSATELIER_AUDIO_LOG_DIRECTORY"] = Path.Combine(root, $"logs-{library}");
            using var child = Process.Start(start)!;
            if (!child.WaitForExit(30000) || child.ExitCode != 0 || File.Exists(token))
                throw new Exception("Native audio settings restore failed.");
            if (!File.ReadAllText(Path.Combine(root, $"logs-{library}", "editor.log"))
                .Contains("Audio settings resume check passed.")) throw new Exception("Native resume completion was not recorded.");
            Console.WriteLine($"PASS Native Audio settings restore: library={library}, project={!library}, speed=.75");
        }
    }
}
#endif
