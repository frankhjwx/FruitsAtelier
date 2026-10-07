using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Platform;

namespace FruitsAtelier.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        try
        {
            if (args.Length == 3 && args[0] == "--update-package-check") return Diagnostics.UpdatePackageCheck.Run(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--package-check") return Diagnostics.PackageCheck.Run(args[1]);
            if (args.Contains("--m2-check")) return Diagnostics.M2Check.Run(args.Where(p => File.Exists(p) && Path.GetExtension(p).Equals(".osz", StringComparison.OrdinalIgnoreCase)));
            AudioSettingsRestartState? resumeAudio = args.Length == 2 && args[0] is "--resume-audio-settings" or "--audio-settings-resume-check"
                ? EditorWindow.ReadAudioSettingsRestart(args[1]) : null;
            if (!args.Contains("--render-check") && !args.Contains("--testplay-render-check") && !args.Contains("--profile-map") && !args.Contains("--audio-settings-resume-check"))
            {
                FruitsAtelier.Core.LibrarySettings diagnosticSettings;
                try { diagnosticSettings = FruitsAtelier.Core.LibrarySettings.Load(); }
                catch { diagnosticSettings = new(); }
                Audio.AudioDiagnosticCapture.Configure(diagnosticSettings);
            }
            L.SetLanguage(FruitsAtelier.Localization.LanguagePreference.ReadLanguage());
            using var window = new EditorWindow();
            if (args.Length is 2 or 3 && args[0] == "--profile-map") return window.Run(profileMap: args[1],
                profileStartMs: args.Length == 3 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 70000);
            return window.Run(args.Contains("--render-check") || args.Contains("--testplay-render-check") || args.Contains("--audio-settings-resume-check"), resumeAudio is null ? args.FirstOrDefault(File.Exists) : null,
                testplayCheck: args.Contains("--testplay-render-check"), firstRunSetup: args.Contains("--first-run-setup"), resumeAudio: resumeAudio);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception.ToString());
            if (!args.Contains("--render-check") && !args.Contains("--testplay-render-check") && !args.Contains("--m2-check") && !args.Contains("--package-check") && !args.Contains("--profile-map") && !args.Contains("--audio-settings-resume-check"))
                Native.ShowError(0, L.Get("window.startFailed", exception.Message, AppLog.Path), L.Get("app.name"));
            return 1;
        }
        finally { AppLog.Close(); }
    }
}

internal static class AppLog
{
    // Background writers must never update the window's UI-thread counters.
    [ThreadStatic] internal static Editor.EditorPerformanceMetrics? Performance;
    public static string Path { get; } = FindPath();
    private static readonly Audio.AsyncDiagnosticTextLog writer = new(Path,
        Audio.AudioDiagnosticLog.Requested ? 16 * 1024 * 1024 : long.MaxValue);
    internal static long Dropped => writer.Dropped;
    internal static bool Failed => writer.Failed;
    internal static bool LimitReached => writer.LimitReached;
    internal static void Close() => writer.Dispose();
    private static string FindPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        var directory = root is not null ? System.IO.Path.Combine(root.FullName, "artifacts", "logs")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FruitsAtelier", "logs");
        if (Audio.AudioDiagnosticLog.CaptureDirectory is { Length: > 0 } capture) directory = capture;
        return System.IO.Path.Combine(directory, "editor.log");
    }
    public static void Write(string text)
    {
        long start = Performance?.Start() ?? 0;
        try { writer.Write($"{DateTimeOffset.Now:O} {text}{Environment.NewLine}"); }
        finally { Performance?.End(Editor.EditorPerformanceStage.LogWrite, start); }
    }
}
