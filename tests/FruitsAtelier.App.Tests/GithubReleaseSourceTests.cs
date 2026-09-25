#if WINDOWS
using System.Text;
using System.Text.Json;
using FruitsAtelier.App.Updates;
using Velopack.Logging;
using Velopack.Sources;

internal static class GithubReleaseSourceTests
{
    private const string Repository = "https://github.com/example/editor";
    private const string ListUrl = "https://api.github.com/repos/example/editor/releases?per_page=10&page=1";
    private const string DetailUrl = "https://api.github.com/repos/example/editor/releases/2";
    private const string FeedName = "releases.win-x64.json";
    private const string PackageName = "FruitsAtelier-0.8.5-win-x64-full.nupkg";
    private static readonly string[] Complete = [FeedName, PackageName];

    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        foreach (string[] summaryAssets in new[] { Array.Empty<string>(), [FeedName], [PackageName], Complete })
        {
            var downloader = new Downloader();
            downloader.Responses[ListUrl] = JsonSerializer.Serialize(new[]
            {
                Release(2, summaryAssets),
                Release(3, [], prerelease: true),
                Release(4, [], draft: true),
                Release(1, ["legacy-portable.zip"])
            });
            downloader.Responses[DetailUrl] = JsonSerializer.Serialize(Release(2, Complete));
            downloader.Responses["https://api.github.com/repos/example/editor/releases/1"] = JsonSerializer.Serialize(Release(1, ["legacy-portable.zip"]));
            downloader.Responses[DownloadUrl(FeedName)] = Feed();
            var source = new GithubReleaseSource(Repository, "win-x64", downloader);
            var feed = await source.GetReleaseFeed(new NullVelopackLogger(), "FruitsAtelier", "win-x64");
            Check(feed.Assets.Length == 1 && feed.Assets[0].Version.ToString() == "0.8.5", "Incomplete summaries must not hide the stable update; legacy ZIP-only releases remain supported");
            Check(feed.Assets[0].SHA256 == new string('a', 64), "The package checksum from the feed is retained");
            Check(downloader.Requests.Contains(DetailUrl) == (summaryAssets.Length < 2), "Only incomplete summaries need release-ID lookup");
            Check(!downloader.Requests.Any(url => url.EndsWith("/3") || url.EndsWith("/4")), "Drafts and prereleases are excluded before fetching details");
            await source.DownloadReleaseEntry(new NullVelopackLogger(), feed.Assets[0], "unused", _ => { }, CancellationToken.None);
            Check(downloader.DownloadedUrl == DownloadUrl(PackageName), "Download uses the recovered release asset's public URL");
        }

        foreach (var detail in new[]
        {
            JsonSerializer.Serialize(Release(2, [])),
            JsonSerializer.Serialize(Release(2, [FeedName])),
            JsonSerializer.Serialize(Release(2, [PackageName])),
            JsonSerializer.Serialize(Release(99, Complete)),
            JsonSerializer.Serialize(Release(2, Complete, draft: true)),
            JsonSerializer.Serialize(Release(2, Complete, prerelease: true))
        })
        {
            var downloader = MissingSummary();
            downloader.Responses[DetailUrl] = detail;
            await ExpectFailure<InvalidDataException>(downloader);
        }
        var offline = MissingSummary();
        offline.FailingUrl = DetailUrl;
        await ExpectFailure<HttpRequestException>(offline);
        var malformed = MissingSummary();
        malformed.Responses[DetailUrl] = "null";
        await ExpectFailure<JsonException>(malformed);

        var empty = new Downloader();
        empty.Responses[ListUrl] = "[]";
        var noReleases = await new GithubReleaseSource(Repository, "win-x64", empty)
            .GetReleaseFeed(new NullVelopackLogger(), "FruitsAtelier", "win-x64");
        Check(noReleases.Assets.Length == 0, "A genuinely empty repository has no update");
    }

    private static Downloader MissingSummary()
    {
        var downloader = new Downloader();
        downloader.Responses[ListUrl] = JsonSerializer.Serialize(new[] { Release(2, []) });
        return downloader;
    }

    private static async Task ExpectFailure<T>(Downloader downloader) where T : Exception
    {
        try
        {
            await new GithubReleaseSource(Repository, "win-x64", downloader)
                .GetReleaseFeed(new NullVelopackLogger(), "FruitsAtelier", "win-x64");
        }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}, not a successful no-update result.");
    }

    private static object Release(long id, string[] assets, bool prerelease = false, bool draft = false) => new
    {
        id, name = $"Release {id}", draft, prerelease,
        published_at = "2026-09-23T08:00:00Z",
        assets = assets.Select(name => new { name, browser_download_url = DownloadUrl(name) }).ToArray()
    };

    private static string DownloadUrl(string name) => $"{Repository}/releases/download/v0.8.5/{name}";

    private static string Feed() => JsonSerializer.Serialize(new
    {
        Assets = new[] { new { PackageId = "FruitsAtelier", Version = "0.8.5", Type = "Full", FileName = PackageName,
            SHA1 = new string('a', 40), SHA256 = new string('a', 64), Size = 123 } }
    });

    private sealed class Downloader : IFileDownloader
    {
        public Dictionary<string, string> Responses { get; } = [];
        public List<string> Requests { get; } = [];
        public string? FailingUrl { get; set; }
        public string? DownloadedUrl { get; private set; }

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Requests.Add(url);
            Check(headers is null || !headers.ContainsKey("Authorization"), "Public release checks must not require credentials");
            if (url == FailingUrl) throw new HttpRequestException("offline");
            return Task.FromResult(Responses.TryGetValue(url, out var response) ? response : throw new Exception($"Unexpected request: {url}"));
        }

        public async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
            => Encoding.UTF8.GetBytes(await DownloadString(url, headers, timeout));

        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null,
            double timeout = 30, CancellationToken cancelToken = default)
        {
            DownloadedUrl = url;
            return Task.CompletedTask;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
#endif
