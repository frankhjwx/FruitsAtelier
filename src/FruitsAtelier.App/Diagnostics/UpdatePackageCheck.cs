using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace FruitsAtelier.App.Diagnostics;

internal static class UpdatePackageCheck
{
    internal static int Run(string feed, string report)
    {
        try
        {
            var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feed)), new UpdateOptions { ExplicitChannel = "win-x64" });
            if (!manager.IsInstalled) throw new InvalidOperationException("Not running a portable installation.");
            var update = manager.CheckForUpdatesAsync().GetAwaiter().GetResult() ?? throw new InvalidOperationException("No newer package in the test feed.");
            manager.DownloadUpdatesAsync(update).GetAwaiter().GetResult();
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease, ["--package-check", report]);
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(report, JsonSerializer.Serialize(new { success = false, error = e.ToString() }));
            return 1;
        }
    }
}
