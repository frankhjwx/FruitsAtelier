using System.Buffers.Binary;

namespace FruitsAtelier.Core;

public sealed record DiscussionImage(string Path, int Width, int Height);

public sealed partial class OsuDiscussionClient
{
    public async Task<DiscussionImage?> CacheImageAsync(string url, string directory, CancellationToken cancellation = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.') || uri.IsLoopback
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        string path = System.IO.Path.Combine(directory, key + ".image");
        if (File.Exists(path))
        {
            var saved = await ReadImageAsync(path, cancellation).ConfigureAwait(false);
            if (saved is not null) return saved;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        byte[] bytes = await ReadAsync(request, 8 * 1024 * 1024, cancellation,
            TimeSpan.FromSeconds(Math.Min(10, requestTimeout.TotalSeconds))).ConfigureAwait(false);
        var size = ImageSize(bytes);
        if (!ValidSize(size)) return null;
        await WriteCacheAsync(path, bytes, cancellation).ConfigureAwait(false);
        return new(path, size.Width, size.Height);
    }

    public static async Task<DiscussionImage?> ReadImageAsync(string path, CancellationToken cancellation = default)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) return null;
        var size = ImageSize(await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false));
        return ValidSize(size) ? new(path, size.Width, size.Height) : null;
    }

    private static bool ValidSize((int Width, int Height) size)
        => size.Width is > 0 and <= 4096 && size.Height is > 0 and <= 4096 && (long)size.Width * size.Height <= 8 * 1024 * 1024;

    private static (int Width, int Height) ImageSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24) return default;
        if (data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return (BinaryPrimitives.ReadInt32BigEndian(data[16..20]), BinaryPrimitives.ReadInt32BigEndian(data[20..24]));
        if (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]));
        if (data[0] != 255 || data[1] != 216) return default;
        int position = 2;
        while (position + 4 < data.Length)
        {
            if (data[position++] != 255) return default;
            while (position < data.Length && data[position] == 255) position++;
            if (position + 3 >= data.Length) return default;
            int marker = data[position++];
            if (marker is 217 or 218) return default;
            if (marker is 1 or >= 208 and <= 215) continue;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            if (length < 2 || position + length > data.Length) return default;
            if (marker is >= 192 and <= 195 or >= 197 and <= 199 or >= 201 and <= 203 or >= 205 and <= 207)
                return length >= 7 ? (BinaryPrimitives.ReadUInt16BigEndian(data[(position + 5)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(position + 3)..])) : default;
            position += length;
        }
        return default;
    }
}
