namespace FruitsAtelier.App.Media;

internal readonly record struct MediaFileVersion(long Length, long Modified, long Created)
{
    internal static MediaFileVersion Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new(file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks) : default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return default; }
    }
}
