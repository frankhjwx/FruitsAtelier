using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FruitsAtelier.Core;
using Microsoft.Win32;
using Vortice.WIC;

namespace FruitsAtelier.App.Platform;

internal static class OsuLinkApplications
{
    internal static IReadOnlyList<OsuLinkApplication> Discover(string? iconCache = null)
        => Discover([Registry.CurrentUser, Registry.LocalMachine], Registry.ClassesRoot,
            iconCache ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FruitsAtelier", "cache", "osu-link-icons"));

    internal static IReadOnlyList<OsuLinkApplication> Discover(IEnumerable<RegistryKey> hives, RegistryKey classes, string iconCache)
    {
        var apps = new Dictionary<string, OsuLinkApplication>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in hives)
        {
            using var registered = hive.OpenSubKey(@"Software\RegisteredApplications");
            if (registered is null) continue;
            foreach (string registeredName in registered.GetValueNames())
            {
                if (registered.GetValue(registeredName) is not string capabilityPath) continue;
                using var capability = hive.OpenSubKey(capabilityPath);
                using var urls = capability?.OpenSubKey("URLAssociations");
                if (urls?.GetValue("osu") is not string id || apps.ContainsKey(id)) continue;
                var target = OsuLinkAssociation.Resolve(id, classes);
                if (target is null) continue;
                string name = Localized(capability?.GetValue("ApplicationName") as string)
                    ?? Localized(capability?.GetValue("ApplicationDescription") as string)
                    ?? registeredName;
                if (name == "osu!" && registeredName.Contains("stable", StringComparison.OrdinalIgnoreCase)) name = "osu!stable";
                string icon = ExtractIcon(target.Path, iconCache);
                apps[id] = new(id, name, icon);
            }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string? Localized(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith('@')) return value;
        var buffer = new StringBuilder(1024);
        return SHLoadIndirectString(value, buffer, (uint)buffer.Capacity, 0) == 0 ? buffer.ToString() : null;
    }

    private static string ExtractIcon(string executable, string cache)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable + File.GetLastWriteTimeUtc(executable).Ticks)));
        string destination = Path.Combine(cache, key + ".png");
        if (File.Exists(destination)) return destination;
        nint large = 0, small = 0;
        try
        {
            if (ExtractIconEx(executable, 0, out large, out small, 1) == 0 || large == 0)
                return Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png");
            Directory.CreateDirectory(cache);
            using var factory = new IWICImagingFactory();
            using var bitmap = factory.CreateBitmapFromHICON(large);
            using var stream = factory.CreateStream(destination, FileAccess.Write);
            using var encoder = factory.CreateEncoder(ContainerFormat.Png);
            encoder.Initialize(stream);
            using var frame = encoder.CreateNewFrame(out var options);
            using (options)
            {
                frame.Initialize(options);
                bitmap.GetSize(out uint width, out uint height); frame.SetSize(width, height);
                frame.SetPixelFormat(Vortice.WIC.PixelFormat.Format32bppBGRA);
                frame.WriteSource(bitmap); frame.Commit(); encoder.Commit();
            }
            return destination;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SharpGen.Runtime.SharpGenException)
        { return Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"); }
        finally { if (large != 0) DestroyIcon(large); if (small != 0) DestroyIcon(small); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out nint large, out nint small, uint count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, uint size, nint reserved);
}
