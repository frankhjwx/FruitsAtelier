using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using Microsoft.Data.Sqlite;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FruitsAtelier.App.Diagnostics;

internal static class PackageCheck
{
    // Runs the distributed executable without a window, audio device, or user workspace.
    internal static int Run(string reportPath)
    {
        var checks = new List<string>();
        string? error = null;
        try
        {
            string runtime = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
            if (!runtime.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The executable is using an installed runtime instead of the packaged runtime.");
            checks.Add("Bundled .NET runtime");
            var updater = new Updates.VelopackBackend();
            if (!updater.IsInstalled) throw new InvalidOperationException("Portable update installation was not detected.");
            checks.Add("Portable update installation");
            using (var database = new SqliteConnection("Data Source=:memory:"))
            {
                database.Open();
                using var command = database.CreateCommand();
                command.CommandText = "SELECT sqlite_version()";
                if (command.ExecuteScalar() is not string) throw new InvalidOperationException("SQLite did not load.");
            }
            checks.Add("Native SQLite");
            if (Localization.Strings.Validate().Count != 0) throw new InvalidOperationException("Localization validation failed.");
            checks.Add("Embedded localization");
            foreach (string asset in new[] { "branding/mark.png", "branding/app-icon.ico", "icons/tools/select.png", "icons/tools/fslider.png" })
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", asset))) throw new FileNotFoundException(asset);
            checks.Add("Editor assets");
            var failures = new List<string>();
            using (var sounds = new HitsoundPlayer(failures.Add))
            {
                foreach (int bank in new[] { 1, 2, 3 })
                foreach (string name in new[] { "hitnormal", "hitwhistle", "hitfinish", "hitclap", "slidertick" })
                {
                    string file = HitsoundDefaults.Find(bank, name) ?? throw new FileNotFoundException($"{bank}/{name}");
                    sounds.Prepare(new(CatchObjectKind.Fruit, file, 1, name, bank));
                }
                if (HitsoundDefaults.Find(1, "catch-banana") is not { } banana) throw new FileNotFoundException("catch-banana");
                sounds.Prepare(new(CatchObjectKind.Banana, banana, 1));
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
            checks.Add("Packaged hitsound decoding (silent)");
            var tempo = new TempoSampleProvider(new SignalGenerator(44100, 2) { Gain = 0 }, .5);
            if (tempo.Read(new float[1024], 0, 1024) != 1024) throw new InvalidOperationException("Tempo processing failed.");
            checks.Add("Tempo processing (silent)");
            var map = DemoMap.Create();
            if (!CatchStreamConverter.Convert(map).Success) throw new InvalidOperationException("Demo conversion failed.");
            checks.Add("Beatmap conversion");
        }
        catch (Exception exception) { error = exception.ToString(); }
        var report = new
        {
            success = error is null,
            version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            runtime = RuntimeEnvironment.GetRuntimeDirectory(), architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            checks, error
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }
}
