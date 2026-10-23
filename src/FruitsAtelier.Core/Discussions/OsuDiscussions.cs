using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FruitsAtelier.Core;

public enum DiscussionCategory { Reviews, GeneralAll, General, Timeline }
public sealed record DiscussionGroup(string Name, string Label, uint Colour, IReadOnlyList<string> Modes);
public sealed record DiscussionUser(long Id, string Name, string AvatarUrl, IReadOnlyList<DiscussionGroup> Groups);
public sealed record DiscussionPost(long Id, long UserId, string Message, bool System, DateTimeOffset? CreatedAt);
public sealed record BeatmapDiscussion(long Id, long? BeatmapId, long? ParentId, long UserId, double? Timestamp,
    string MessageType, bool Resolved, bool CanResolve, DateTimeOffset? CreatedAt, IReadOnlyList<DiscussionPost> Posts)
{
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? LastPostAt { get; init; }
    public int UpVotes { get; init; }
    public int DownVotes { get; init; }
}
public sealed record DiscussionReference(int Start, int Length, double TimeMs, IReadOnlyList<int> Numbers);
public sealed record DiscussionUrl(int Start, int Length, string Url);
public sealed record DiscussionImageReference(int Start, int Length, string Url);
public sealed record DiscussionReviewBlock(string Text, long? DiscussionId);

public sealed class OsuDiscussionSnapshot(long setId, long creatorId, IReadOnlyList<BeatmapDiscussion> discussions,
    IReadOnlyDictionary<long, DiscussionUser> users, IReadOnlyDictionary<long, long[]> owners)
{
    public long BeatmapSetId { get; } = setId;
    public long CreatorId { get; } = creatorId;
    public IReadOnlyList<BeatmapDiscussion> Discussions { get; } = discussions;
    public IReadOnlyDictionary<long, DiscussionUser> Users { get; } = users;
    public IReadOnlyDictionary<long, long[]> Owners { get; } = owners;

    public BeatmapDiscussion[] For(DiscussionCategory category, long beatmapId)
        => Discussions.Where(d => d.ParentId is null && (category switch
        {
            DiscussionCategory.Reviews => d.MessageType == "review",
            DiscussionCategory.GeneralAll => d.MessageType != "review" && d.BeatmapId is null,
            DiscussionCategory.General => d.MessageType != "review" && d.BeatmapId == beatmapId && d.Timestamp is null,
            _ => d.MessageType != "review" && d.BeatmapId == beatmapId && d.Timestamp is not null
        })).OrderBy(d => category == DiscussionCategory.Timeline ? d.Timestamp : null)
          .ThenBy(d => d.CreatedAt).ThenBy(d => d.Id).ToArray();

    public bool IsMapper(long userId, long? beatmapId)
        => userId == CreatorId || beatmapId is { } id && Owners.TryGetValue(id, out var ids) && ids.Contains(userId);
}

public static class DiscussionText
{
    private static readonly Regex image = new(@"!\[[^\]\r\n]*\]\((?<url>https?://(?:[^()\s<>]|\([^()\s<>]*\))+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static DiscussionImageReference[] Images(string text) => image.Matches(text).Cast<Match>()
        .Where(match => Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host))
        .Select(match => new DiscussionImageReference(match.Index, match.Length, match.Groups["url"].Value)).ToArray();
    private static readonly Regex url = new("https?://[^\\s<>\"`]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static DiscussionUrl[] Urls(string text)
    {
        var result = new List<DiscussionUrl>();
        foreach (Match match in url.Matches(text))
        {
            string value = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '。', '，', '；', '！', '？');
            while (value.Length > 0 && value[^1] is ')' or ']' or '}')
            {
                char close = value[^1], open = close == ')' ? '(' : close == ']' ? '[' : '{';
                if (value.Count(c => c == close) <= value.Count(c => c == open)) break;
                value = value[..^1];
            }
            if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host)
                && parsed.Scheme is "http" or "https") result.Add(new(match.Index, value.Length, value));
        }
        return result.ToArray();
    }

    private static readonly Regex timestamp = new(@"(?<![0-9:])(?<m>[0-9]{2,}):(?<s>[0-5][0-9])[:.](?<ms>[0-9]{3})(?:[ \t]*\((?<n>[0-9]+(?:[ \t]*,[ \t]*[0-9]+)*)\))?",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static DiscussionReference[] References(string text)
    {
        var result = new List<DiscussionReference>();
        var urls = Urls(text);
        foreach (Match match in timestamp.Matches(text))
        {
            if (urls.Any(link => match.Index < link.Start + link.Length && match.Index + match.Length > link.Start)) continue;
            if (!long.TryParse(match.Groups["m"].Value, out long minutes) || minutes > int.MaxValue / 60000) continue;
            double time = minutes * 60000 + int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture) * 1000
                + int.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture);
            var numbers = new List<int>();
            foreach (string value in match.Groups["n"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(value.Trim(), out int n) && n > 0) numbers.Add(n);
            result.Add(new(match.Index, match.Length, time, numbers.ToArray()));
        }
        return result.ToArray();
    }

    public static DiscussionReviewBlock[] ReviewBlocks(string message)
    {
        try
        {
            using var json = JsonDocument.Parse(message);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return [new(message, null)];
            return json.RootElement.EnumerateArray().Select(block => block.TryGetProperty("type", out var type)
                && type.GetString() == "embed" && block.TryGetProperty("discussion_id", out var id) && id.TryGetInt64Safe(out long number)
                    ? new DiscussionReviewBlock("", number)
                    : new DiscussionReviewBlock(block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : block.GetRawText(), null)).ToArray();
        }
        catch (JsonException) { return [new(message, null)]; }
    }

    public static bool? ResolvedSystemMessage(DiscussionPost post)
    {
        if (!post.System) return null;
        try
        {
            using var json = JsonDocument.Parse(post.Message);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var type) && type.GetString() == "resolved"
                && root.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        }
        catch (JsonException) { }
        return null;
    }
}

/// <summary>Reads the same anonymous, complete discussion snapshot as the public website.</summary>
public sealed partial class OsuDiscussionClient
{
    private const int MaximumResponseBytes = 32 * 1024 * 1024;
    private static readonly HttpClient shared = CreateClient();
    private readonly HttpClient http;
    private readonly TimeSpan requestTimeout;
    public OsuDiscussionClient(HttpClient? http = null, TimeSpan? requestTimeout = null)
    { this.http = http ?? shared; this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(35); }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(35) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FruitsAtelier/1.2 (+https://github.com/frankhjwx/FruitsAtelier)");
        return client;
    }

    public Task<OsuDiscussionSnapshot> LoadAsync(long setId, CancellationToken cancellation = default)
        => LoadAsync(setId, null, cancellation);

    public async Task<OsuDiscussionSnapshot> LoadAsync(long setId, string? cacheDirectory, CancellationToken cancellation = default)
    {
        if (setId <= 0) throw new ArgumentOutOfRangeException(nameof(setId));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://osu.ppy.sh/beatmapsets/{setId}/discussion");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        byte[] bytes = await ReadAsync(request, MaximumResponseBytes, cancellation).ConfigureAwait(false);
        var snapshot = Parse(System.Text.Encoding.UTF8.GetString(bytes), setId);
        if (cacheDirectory is not null)
        {
            try { await WriteCacheAsync(CachePath(cacheDirectory, setId), bytes, cancellation).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        return snapshot;
    }

    public async Task<OsuDiscussionSnapshot?> ReadCachedAsync(long setId, string directory, CancellationToken cancellation = default)
    {
        try
        {
            string path = CachePath(directory, setId);
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumResponseBytes) return null;
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
            return bytes.Length <= MaximumResponseBytes ? Parse(System.Text.Encoding.UTF8.GetString(bytes), setId) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException) { return null; }
    }

    private static string CachePath(string directory, long setId)
        => Path.Combine(directory, setId.ToString(CultureInfo.InvariantCulture) + ".json");

    private static async Task WriteCacheAsync(string path, byte[] bytes, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(pending, bytes, cancellation).ConfigureAwait(false);
            File.Move(pending, path, overwrite: true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private async Task<byte[]> ReadAsync(HttpRequestMessage request, int maximum, CancellationToken cancellation, TimeSpan? budget = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(budget ?? requestTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Discussion response exceeds the size limit.");
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var target = new MemoryStream();
            byte[] buffer = new byte[16384];
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (target.Length + read > maximum) throw new InvalidDataException("Discussion response exceeds the size limit.");
                target.Write(buffer, 0, read);
            }
            return target.ToArray();
        }
        catch (OperationCanceledException error) when (!cancellation.IsCancellationRequested)
        { throw new HttpRequestException("Discussion request timed out.", error); }
    }

    public async Task<string?> CacheAvatarAsync(string url, string directory, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host is not ("a.ppy.sh" or "osu.ppy.sh")) return null;
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        string path = Path.Combine(directory, key + ".png");
        if (File.Exists(path)) return path;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        byte[] bytes = await ReadAsync(request, 2 * 1024 * 1024, cancellation, TimeSpan.FromSeconds(Math.Min(10, requestTimeout.TotalSeconds))).ConfigureAwait(false);
        bool png = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71;
        bool jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        bool gif = bytes.Length >= 6 && System.Text.Encoding.ASCII.GetString(bytes, 0, 3) == "GIF";
        if (!png && !jpeg && !gif) return null;
        Directory.CreateDirectory(directory);
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(pending, bytes, cancellation).ConfigureAwait(false);
            File.Move(pending, path, overwrite: true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
        return path;
    }

    public static OsuDiscussionSnapshot Parse(string json, long expectedSetId)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var set = document.RootElement;
        if (set.TryGetProperty("beatmapset", out var nested)) set = nested;
        if (Long(set, "id") != expectedSetId || !set.TryGetProperty("discussions", out var discussionArray)
            || discussionArray.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid discussion snapshot.");
        var users = new Dictionary<long, DiscussionUser>();
        foreach (var user in Array(set, "related_users"))
        {
            long id = Long(user, "id");
            var groups = new List<DiscussionGroup>();
            foreach (var group in Array(user, "groups"))
            {
                uint colour = uint.TryParse(JsonString(group, "colour").TrimStart('#'), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uint parsed) ? parsed & 0xffffff : 0xAE90D5;
                groups.Add(new(JsonString(group, "name"), JsonString(group, "short_name"), colour,
                    Array(group, "playmodes").Select(v => v.GetString() ?? "").ToArray()));
            }
            if (id > 0) users[id] = new(id, JsonString(user, "username"), JsonString(user, "avatar_url"), groups.ToArray());
        }
        var owners = new Dictionary<long, long[]>();
        foreach (var beatmap in Array(set, "beatmaps"))
            owners[Long(beatmap, "id")] = Array(beatmap, "owners").Select(o => Long(o, "id")).ToArray();
        var discussions = new List<BeatmapDiscussion>();
        foreach (var discussion in discussionArray.EnumerateArray())
        {
            if (NullableLong(discussion, "deleted_by_id") is not null || !string.IsNullOrEmpty(JsonString(discussion, "deleted_at"))) continue;
            var posts = new Dictionary<long, DiscussionPost>();
            foreach (var post in Array(discussion, "posts"))
            {
                if (!string.IsNullOrEmpty(JsonString(post, "deleted_at"))) continue;
                long id = Long(post, "id");
                string message = post.TryGetProperty("message", out var body)
                    ? body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : body.GetRawText() : "";
                posts[id] = new(id, Long(post, "user_id"), message, Bool(post, "system"), Date(post, "created_at"));
            }
            // A partial payload must fail rather than silently presenting an empty thread as complete.
            if (!discussion.TryGetProperty("posts", out var allPosts) || allPosts.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Discussion posts are missing.");
            discussions.Add(new(Long(discussion, "id"), NullableLong(discussion, "beatmap_id"), NullableLong(discussion, "parent_id"),
                Long(discussion, "user_id"), NullableLong(discussion, "timestamp"), JsonString(discussion, "message_type"),
                Bool(discussion, "resolved"), Bool(discussion, "can_be_resolved"), Date(discussion, "created_at"),
                posts.Values.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToArray()) { UpdatedAt = Date(discussion, "updated_at"), LastPostAt = Date(discussion, "last_post_at"), UpVotes = VoteCount(discussion, "up"), DownVotes = VoteCount(discussion, "down") });
        }
        return new(expectedSetId, Long(set, "user_id"), discussions.OrderBy(d => d.Timestamp).ThenBy(d => d.Id).ToArray(), users, owners);
    }

    private static int VoteCount(JsonElement discussion, string key)
        => discussion.TryGetProperty("votes", out var votes) && votes.ValueKind == JsonValueKind.Object
            ? (int)Math.Clamp(Long(votes, key), 0, int.MaxValue) : 0;

    private static IEnumerable<JsonElement> Array(JsonElement value, string key)
        => value.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];
    private static string JsonString(JsonElement value, string key)
        => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static long Long(JsonElement value, string key) => NullableLong(value, key) ?? 0;
    private static long? NullableLong(JsonElement value, string key)
        => value.TryGetProperty(key, out var item) && item.TryGetInt64Safe(out long result) ? result : null;
    private static bool Bool(JsonElement value, string key)
        => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;
    private static DateTimeOffset? Date(JsonElement value, string key)
        => DateTimeOffset.TryParse(JsonString(value, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
}

internal static class DiscussionJsonExtensions
{
    internal static bool TryGetInt64Safe(this JsonElement value, out long result)
    { result = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result); }
}
