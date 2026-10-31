using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Platform.Storage;

namespace FruitsAtelier.Mac;

internal static class Program
{
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> OpenFiles = new();
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<string> OpenLinks = new();
    internal static string[] Arguments = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        foreach (string link in args.Where(FruitsAtelier.Core.EditorTimestamp.IsOsuLink)) OpenLinks.Enqueue(link);
        FruitsAtelier.Localization.Strings.SetLanguage(args.Contains("--smoke-check") ? "en" : FruitsAtelier.Localization.LanguagePreference.ReadLanguage());
        try { return AppBuilder.Configure<MacApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); }
        catch (Exception error) { MacPaths.Log(error.ToString()); Console.Error.WriteLine(error); return 1; }
    }
}
internal sealed class MacApplication : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activation)
            activation.Activated += (_, args) =>
            {
                if (args is ProtocolActivatedEventArgs protocol && FruitsAtelier.Core.EditorTimestamp.IsOsuLink(protocol.Uri.OriginalString))
                    Program.OpenLinks.Enqueue(protocol.Uri.OriginalString);
                if (args is FileActivatedEventArgs files)
                    foreach (var item in files.Files)
                        if (item.TryGetLocalPath() is { } path && (FruitsAtelier.App.Platform.FazArchive.IsPackage(path) || FruitsAtelier.App.Platform.FadArchive.IsPackage(path))) Program.OpenFiles.Enqueue(path);
            };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MacWindow(Program.Arguments.FirstOrDefault(File.Exists), Program.Arguments.Contains("--smoke-check"));
        base.OnFrameworkInitializationCompleted();
    }
}
internal static class MacPaths
{
    public static string Artifacts { get; } = FindArtifacts();
    private static string FindArtifacts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FruitsAtelier.sln"))) return Path.Combine(directory.FullName, "artifacts");
            directory = directory.Parent;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "FruitsAtelier");
    }
    public static void Log(string text)
    {
        try { Directory.CreateDirectory(Path.Combine(Artifacts, "logs")); File.AppendAllText(Path.Combine(Artifacts, "logs", "macos.log"), $"{DateTimeOffset.Now:O} {text}\n"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }
}
