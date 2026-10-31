namespace FruitsAtelier.App.Media;

internal readonly record struct MediaFileVersion(long Length, long Modified, long Created)
{
    internal static bool SamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try { return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return false; }
    }
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
