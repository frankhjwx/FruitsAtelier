using System.Text.Json;
using System.Text.Json.Serialization;
using Velopack.Sources;

namespace FruitsAtelier.App.Updates;

internal sealed class GithubReleaseSource(string repository, string channel, IFileDownloader? downloader = null)
    : GithubSource(repository, null, false, downloader)
{
    protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
    {
        var root = new Uri(GetApiBaseUrl(RepoUri), $"repos{RepoUri.AbsolutePath}/releases");
        var headers = GetRequestHeaders("application/vnd.github.v3+json");
        string json = await Downloader.DownloadString(root + "?per_page=10&page=1", headers).ConfigureAwait(false);
        var releases = JsonSerializer.Deserialize<Release[]>(json) ?? throw new JsonException("Missing GitHub releases list.");
        List<GithubRelease> result = [];
        foreach (var summary in releases.Where(r => !r.Draft && (includePrereleases || !r.Prerelease)).OrderByDescending(r => r.PublishedAt))
        {
            var release = summary;
            if (!HasFeed(release) || !HasPackage(release))
            {
                // GitHub's list can omit assets that are present on the release-ID endpoint.
                if (release.Id <= 0) throw new InvalidDataException("GitHub release has no valid ID.");
                json = await Downloader.DownloadString(root + $"/{release.Id}", headers).ConfigureAwait(false);
                release = JsonSerializer.Deserialize<Release>(json) ?? throw new JsonException("Missing GitHub release details.");
                if (release.Id != summary.Id || release.Draft || (!includePrereleases && release.Prerelease))
                    throw new InvalidDataException("GitHub release changed during the update check.");
                if (release.Assets is not { Length: > 0 } || HasFeed(release) != HasPackage(release))
                    throw new InvalidDataException("GitHub release has incomplete update assets.");
            }
            result.Add(release);
        }
        return result.ToArray();
    }

    private bool HasFeed(GithubRelease release) => release.Assets?.Any(a =>
        string.Equals(a.Name, $"releases.{channel}.json", StringComparison.OrdinalIgnoreCase)) == true;

    private bool HasPackage(GithubRelease release) => release.Assets?.Any(a =>
        a.Name?.EndsWith($"-{channel}-full.nupkg", StringComparison.OrdinalIgnoreCase) == true) == true;

    private sealed class Release : GithubRelease
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
    }
}
