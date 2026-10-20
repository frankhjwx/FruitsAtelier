using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FruitsAtelier.Core;
using Microsoft.Win32;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal static class OsuLinkAssociation
{
    internal const string ProgId = "FruitsAtelier.OsuLink";
    internal static string Command(string executable) => $"\"{executable}\" \"%1\"";
    internal static string? EffectiveCommand()
    {
        uint size = 0;
        AssocQueryString(0, 1, "osu", "open", null, ref size);
        if (size == 0 || size > 32768) return null;
        var buffer = new StringBuilder((int)size);
        return AssocQueryString(0, 1, "osu", "open", buffer, ref size) == 0 ? buffer.ToString() : null;
    }

    internal static bool IsDefault => TryTarget(EffectiveCommand(), out string path, out _) && IsSelf(path);
    internal static bool IsSelf(string path) => string.Equals(Path.GetFullPath(path), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(path).Equals("FruitsAtelier.App", StringComparison.OrdinalIgnoreCase);

    internal static bool TryTarget(string? command, out string executable, out string[] arguments)
    {
        executable = ""; arguments = [];
        if (string.IsNullOrWhiteSpace(command)) return false;
        nint argv = CommandLineToArgvW(command, out int count);
        if (argv == 0) return false;
        try
        {
            var parts = new string[count];
            for (int i = 0; i < count; i++) parts[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!;
            if (parts.Length == 0 || !Path.IsPathFullyQualified(parts[0])) return false;
            executable = parts[0]; arguments = parts.Skip(1).ToArray();
            return true;
        }
        finally { LocalFree(argv); }
    }

    internal sealed record Target(string Path, string[] Arguments);

    internal static Target? Resolve(string id, RegistryKey? classes = null)
    {
        if (string.IsNullOrEmpty(id) || id.Contains('\\') || id.Contains('/')) return null;
        using var command = (classes ?? Registry.ClassesRoot).OpenSubKey(id + @"\shell\open\command");
        if (!TryTarget(command?.GetValue("") as string, out string path, out string[] arguments)
            || !File.Exists(path) || IsSelf(path)) return null;
        return new(path, arguments);
    }

    internal static bool TargetReady(OsuLinkPreferences preferences) => Resolve(preferences.TargetId) is not null;

    internal static ProcessStartInfo ForwardInfo(string url, OsuLinkPreferences preferences, RegistryKey? classes = null)
    {
        if (!EditorTimestamp.IsOsuLink(url) || EditorTimestamp.IsEditorLink(url) || url.Length > 32768 || url.Any(char.IsControl))
            throw new InvalidOperationException(L.Get("osuLinks.invalid"));
        var target = Resolve(preferences.TargetId, classes) ?? throw new InvalidOperationException(L.Get("osuLinks.targetInvalid"));
        var info = new ProcessStartInfo(target.Path) { UseShellExecute = false, WorkingDirectory = System.IO.Path.GetDirectoryName(target.Path)! };
        bool replaced = false;
        foreach (string argument in target.Arguments)
        {
            replaced |= System.Text.RegularExpressions.Regex.IsMatch(argument, "%[1l]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            info.ArgumentList.Add(System.Text.RegularExpressions.Regex.Replace(argument, "%[1l]", _ => url, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        if (!replaced) info.ArgumentList.Add(url);
        return info;
    }

    internal static void Forward(string url) => Process.Start(ForwardInfo(url, OsuLinkPreferences.Load()));
    internal static void ClearBinding()
    {
        if (IsDefault) Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=FruitsAtelier") { UseShellExecute = true });
    }

    internal static void Bind()
    {
        RegisterCandidate();
        Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=FruitsAtelier") { UseShellExecute = true });
    }

    internal static void RegisterCandidate()
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        Register(classes, Environment.ProcessPath!);
        RegisterCapabilities();
    }

    internal static void Register(RegistryKey classes, string executable)
    {
        Set(ProgId, "URL:FruitsAtelier osu! link"); Set(ProgId, "", "URL Protocol");
        Set(ProgId + @"\shell\open\command", Command(executable));
        SHChangeNotify(0x08000000, 0, 0, 0);
        void Set(string path, string value, string name = "") { using var key = classes.CreateSubKey(path); key.SetValue(name, value); }
    }

    private static void RegisterCapabilities()
    {
        using var capabilities = Registry.CurrentUser.CreateSubKey(@"Software\FruitsAtelier\Capabilities");
        capabilities.SetValue("ApplicationName", "FruitsAtelier");
        capabilities.SetValue("ApplicationDescription", "FruitsAtelier");
        using var protocols = capabilities.CreateSubKey("URLAssociations"); protocols.SetValue("osu", ProgId);
        using var apps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications");
        apps.SetValue("FruitsAtelier", @"Software\FruitsAtelier\Capabilities");
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, uint query, string association, string? extra, StringBuilder? output, ref uint length);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
