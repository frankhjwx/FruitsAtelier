using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace FruitsAtelier.App.Platform;

internal static class FazFileAssociation
{
    private const string ProgId = "FruitsAtelier.ProjectArchive";
    private static readonly string Pipe = "FruitsAtelier-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
    internal static readonly ConcurrentQueue<string> Pending = new();
    internal static readonly ConcurrentQueue<string> PendingLinks = new();

    internal static void EnsureRegistered()
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        if (EnsureRegistered(classes, Environment.ProcessPath!, Path.Combine(AppContext.BaseDirectory, "assets", "branding", "faz-file.ico")))
            SHChangeNotify(0x08000000, 0, 0, 0);
    }

    internal static bool EnsureRegistered(RegistryKey classes, string executable, string iconPath)
    {
        bool changed = false;
        Ensure(".faz", ProgId);
        Ensure(ProgId, "Fruits Atelier Project");
        Ensure(ProgId + @"\DefaultIcon", "\"" + iconPath + "\"");
        Ensure(ProgId + @"\shell\open\command", "\"" + executable + "\" \"%1\"");
        const string difficultyId = "FruitsAtelier.DifficultyArchive";
        Ensure(".fad", difficultyId);
        Ensure(difficultyId, "Fruits Atelier Difficulty");
        Ensure(difficultyId + @"\DefaultIcon", "\"" + Path.Combine(Path.GetDirectoryName(iconPath)!, "fad-file.ico") + "\"");
        Ensure(difficultyId + @"\shell\open\command", "\"" + executable + "\" \"%1\"");
        return changed;

        void Ensure(string path, string value)
        {
            using (var existing = classes.OpenSubKey(path))
                if (existing?.GetValue("") as string == value) return;
            using var key = classes.CreateSubKey(path);
            key.SetValue("", value);
            changed = true;
        }
    }
    internal static bool Forward(string? path, string? channel = null)
    {
        if (path is null || !FazArchive.IsPackage(path) && !FadArchive.IsPackage(path) && !FruitsAtelier.Core.EditorTimestamp.IsOsuLink(path)) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", channel ?? Pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(500);
            using var writer = new StreamWriter(client); writer.WriteLine(FruitsAtelier.Core.EditorTimestamp.IsOsuLink(path) ? path : Path.GetFullPath(path)); writer.Flush(); return true;
        }
        catch (Exception error) when (error is IOException or TimeoutException) { return false; }
    }
    internal static async Task Listen(CancellationToken cancellation, string? channel = null)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(channel ?? Pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellation);
                using var reader = new StreamReader(server);
                string? line = await reader.ReadLineAsync(cancellation);
                if (line is { Length: <= 32768 } && FruitsAtelier.Core.EditorTimestamp.IsOsuLink(line) && !line.Any(char.IsControl)) PendingLinks.Enqueue(line);
                if (line is { Length: <= 32768 } && (FazArchive.IsPackage(line) || FadArchive.IsPackage(line)) && File.Exists(line)) Pending.Enqueue(line);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
    }
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
