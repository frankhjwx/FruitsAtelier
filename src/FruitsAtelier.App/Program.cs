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
            if (args.Contains("--m2-check")) return Diagnostics.M2Check.Run(args.Where(p => File.Exists(p) && Path.GetExtension(p).Equals(".osz", StringComparison.OrdinalIgnoreCase)));
            L.SetLanguage(FruitsAtelier.Localization.LanguagePreference.ReadLanguage());
            using var window = new EditorWindow();
            return window.Run(args.Contains("--render-check"), args.FirstOrDefault(File.Exists));
        }
        catch (Exception exception)
        {
            AppLog.Write(exception.ToString());
            if (!args.Contains("--render-check") && !args.Contains("--m2-check"))
                Native.MessageBox(0, L.Get("window.startFailed", exception.Message, AppLog.Path), L.Get("app.name"), 0x10);
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
        var directory = System.IO.Path.Combine(root?.FullName ?? AppContext.BaseDirectory, "artifacts", "logs");
        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, "editor.log");
    }
    public static void Write(string text)
    {
        try { File.AppendAllText(Path, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
