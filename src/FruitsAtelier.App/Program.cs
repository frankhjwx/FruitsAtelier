using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Platform;

namespace FruitsAtelier.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--package-check") return Diagnostics.PackageCheck.Run(args[1]);
            if (args.Contains("--m2-check")) return Diagnostics.M2Check.Run(args.Where(p => File.Exists(p) && Path.GetExtension(p).Equals(".osz", StringComparison.OrdinalIgnoreCase)));
            L.SetLanguage(FruitsAtelier.Localization.LanguagePreference.ReadLanguage());
            using var window = new EditorWindow();
            if (args.Length == 2 && args[0] == "--profile-map") return window.Run(profileMap: args[1]);
            return window.Run(args.Contains("--render-check"), args.FirstOrDefault(File.Exists));
        }
        catch (Exception exception)
        {
            AppLog.Write(exception.ToString());
            if (!args.Contains("--render-check") && !args.Contains("--m2-check") && !args.Contains("--package-check") && !args.Contains("--profile-map"))
                Native.ShowError(0, L.Get("window.startFailed", exception.Message, AppLog.Path), L.Get("app.name"));
            return 1;
        }
    }
}

internal static class AppLog
{
    public static string Path { get; } = FindPath();
    private static string FindPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        var directory = root is not null ? System.IO.Path.Combine(root.FullName, "artifacts", "logs")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FruitsAtelier", "logs");
        return System.IO.Path.Combine(directory, "editor.log");
    }
    public static void Write(string text)
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!); File.AppendAllText(Path, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
