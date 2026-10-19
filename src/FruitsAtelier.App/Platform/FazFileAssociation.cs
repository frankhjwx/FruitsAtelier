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

    internal static void Register()
    {
        string executable = Environment.ProcessPath!;
        using var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.faz");
        extension.SetValue("", ProgId);
        using var type = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId);
        type.SetValue("", "Fruits Atelier Project");
        using var icon = type.CreateSubKey("DefaultIcon");
        icon.SetValue("", "\"" + Path.Combine(AppContext.BaseDirectory, "assets", "branding", "faz-file.ico") + "\"");
        using var command = type.CreateSubKey(@"shell\open\command");
        command.SetValue("", "\"" + executable + "\" \"%1\"");
        SHChangeNotify(0x08000000, 0, 0, 0);
    }
    internal static void RefreshRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.faz");
        if (key?.GetValue("") as string == ProgId) Register();
    }
    internal static bool Forward(string? path, string? channel = null)
    {
        if (path is null || !FazArchive.IsPackage(path)) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", channel ?? Pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(500);
            using var writer = new StreamWriter(client); writer.WriteLine(Path.GetFullPath(path)); writer.Flush(); return true;
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
                if (line is { Length: <= 32768 } && FazArchive.IsPackage(line) && File.Exists(line)) Pending.Enqueue(line);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
    }
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
