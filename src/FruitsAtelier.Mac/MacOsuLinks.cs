using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

internal static class MacOsuLinks
{
    private const string BundleId = "io.github.frankhjwx.FruitsAtelier";
    private const string Services = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    internal static bool IsDefault => DefaultBundle() == BundleId;
    internal static string DefaultBundle()
    {
        nint scheme = CFStringCreateWithCString(0, "osu", Utf8);
        try { nint value = LSCopyDefaultHandlerForURLScheme(scheme); try { return ReadString(value); } finally { if (value != 0) CFRelease(value); } }
        finally { CFRelease(scheme); }
    }
    internal static string? Resolve(string id)
    {
        if (string.IsNullOrEmpty(id) || id == BundleId) return null;
        nint identifier = CFStringCreateWithCString(0, id, Utf8);
        nint urls = 0;
        try
        {
            urls = LSCopyApplicationURLsForBundleIdentifier(identifier, 0);
            if (urls == 0 || CFArrayGetCount(urls) == 0) return null;
            nint path = CFURLCopyFileSystemPath(CFArrayGetValueAtIndex(urls, 0), 0);
            try { string value = ReadString(path); return Directory.Exists(value) ? value : null; }
            finally { if (path != 0) CFRelease(path); }
        }
        finally { CFRelease(identifier); if (urls != 0) CFRelease(urls); }
    }
    internal static bool TargetReady(OsuLinkPreferences preferences) => Resolve(preferences.TargetId) is not null;

    internal static IReadOnlyList<OsuLinkApplication> Discover()
    {
        nint scheme = CFStringCreateWithCString(0, "osu", Utf8), handlers = 0;
        var apps = new List<OsuLinkApplication>();
        try
        {
            handlers = LSCopyAllHandlersForURLScheme(scheme);
            if (handlers == 0) return apps;
            for (nint i = 0; i < CFArrayGetCount(handlers); i++)
            {
                string id = ReadString(CFArrayGetValueAtIndex(handlers, i));
                if (Resolve(id) is not { } path) continue;
                byte[] bytes = Encoding.UTF8.GetBytes(path);
                nint url = CFURLCreateFromFileSystemRepresentation(0, bytes, bytes.Length, true);
                nint bundle = CFBundleCreate(0, url);
                try
                {
                    string name = Metadata(bundle, "CFBundleDisplayName");
                    if (name.Length == 0) name = Metadata(bundle, "CFBundleName");
                    if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
                    string icon = Metadata(bundle, "CFBundleIconFile");
                    if (!Path.HasExtension(icon)) icon += ".icns";
                    icon = CacheIcon(Path.Combine(path, "Contents", "Resources", icon));
                    apps.Add(new(id, name, icon));
                }
                finally { if (bundle != 0) CFRelease(bundle); CFRelease(url); }
            }
        }
        finally { CFRelease(scheme); if (handlers != 0) CFRelease(handlers); }
        return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    private static string Metadata(nint bundle, string name)
    {
        if (bundle == 0) return "";
        nint key = CFStringCreateWithCString(0, name, Utf8);
        try { return ReadString(CFBundleGetValueForInfoDictionaryKey(bundle, key)); }
        finally { CFRelease(key); }
    }
    private static string CacheIcon(string source)
    {
        string fallback = Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png");
        if (!File.Exists(source)) return fallback;
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FruitsAtelier", "cache", "osu-link-icons");
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source + File.GetLastWriteTimeUtc(source).Ticks)));
        string output = Path.Combine(cache, key + ".png");
        if (File.Exists(output)) return output;
        try
        {
            Directory.CreateDirectory(cache);
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/sips")
            { ArgumentList = { "-s", "format", "png", source, "--out", output }, RedirectStandardOutput = true, RedirectStandardError = true });
            if (process is null) return fallback;
            if (!process.WaitForExit(5000)) { process.Kill(); return fallback; }
            return process.ExitCode == 0 && File.Exists(output) ? output : fallback;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return fallback; }
    }
    internal static void Forward(string link)
    {
        if (!EditorTimestamp.IsOsuLink(link) || EditorTimestamp.IsEditorLink(link) || link.Length > 32768 || link.Any(char.IsControl))
            throw new InvalidOperationException(L.Get("osuLinks.invalid"));
        var preferences = OsuLinkPreferences.Load();
        string path = Resolve(preferences.TargetId) ?? throw new InvalidOperationException(L.Get("osuLinks.targetInvalid"));
        Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-a", path, link } });
    }
    private static string ReadString(nint value)
    {
        if (value == 0) return "";
        var buffer = new byte[32768];
        return CFStringGetCString(value, buffer, buffer.Length, Utf8) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : "";
    }
    [DllImport(Services)] private static extern nint LSCopyDefaultHandlerForURLScheme(nint scheme);
    [DllImport(Services)] private static extern nint LSCopyAllHandlersForURLScheme(nint scheme);
    [DllImport(Services)] private static extern nint LSCopyApplicationURLsForBundleIdentifier(nint bundle, nint error);
    [DllImport(Foundation)] private static extern nint CFArrayGetCount(nint array);
    [DllImport(Foundation)] private static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    [DllImport(Foundation)] private static extern nint CFURLCopyFileSystemPath(nint url, int style);
    [DllImport(Foundation)] private static extern nint CFBundleGetValueForInfoDictionaryKey(nint bundle, nint key);
    [DllImport(Foundation)] private static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(nint value, byte[] buffer, nint length, uint encoding);
    [DllImport(Foundation)] private static extern nint CFURLCreateFromFileSystemRepresentation(nint allocator, byte[] buffer, nint length, [MarshalAs(UnmanagedType.I1)] bool directory);
    [DllImport(Foundation)] private static extern nint CFBundleCreate(nint allocator, nint url);
    [DllImport(Foundation)] private static extern void CFRelease(nint value);
}
