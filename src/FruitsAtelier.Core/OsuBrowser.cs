namespace FruitsAtelier.Core;

public enum BrowserCommand { Back, Forward, Refresh, External }
public sealed record BrowserState(string? Url = null, bool Back = false, bool Forward = false, bool Loading = false, bool Failed = false);

public static class OsuBrowser
{
    public static bool IsWebsite(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
            && uri.Host.Equals("osu.ppy.sh", StringComparison.OrdinalIgnoreCase)
            && uri.UserInfo.Length == 0;

    public static bool IsWebLink(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.UserInfo.Length == 0;

    public static (long Set, long Map, bool Discussion) PageContext(string? url)
    {
        if (url is null || !IsWebsite(url)) return default;
        var uri = new Uri(url);
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "beatmapsets" || !long.TryParse(parts[1], out long set) || set <= 0) return default;
        bool discussion = parts.Length > 2 && parts[2] == "discussion";
        string? mapText = discussion ? (parts.Length > 3 ? parts[3] : null) : uri.Fragment.Split('/').ElementAtOrDefault(1);
        long.TryParse(mapText, out long map);
        return (set, Math.Max(0, map), discussion);
    }

    public static string ProfileDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FruitsAtelier", "Browser", "osu");
}
