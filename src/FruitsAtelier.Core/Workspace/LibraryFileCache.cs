using System.Security.Cryptography;
using System.Text;

namespace FruitsAtelier.Core;

// Call only on workers: source validation and cache access both touch the filesystem.
public sealed class LibraryFileCache(string category, string version, string? directory = null)
{
    private readonly string root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FruitsAtelier", "Cache", category);
    private string CachePath(string source)
    {
        string path = Path.GetFullPath(source);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(version + "\n" + path))) + ".cache");
    }
    public byte[]? Read(string source)
    {
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists) return null;
            using var stream = File.OpenRead(CachePath(source));
            if (stream.Length is < 16 or > 1024 * 1024) return null;
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt64() != info.LastWriteTimeUtc.Ticks || reader.ReadInt64() != info.Length) return null;
            return reader.ReadBytes((int)stream.Length - 16);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
    public (long Stamp, long Size) Fingerprint(string source)
    {
        var info = new FileInfo(source);
        return (info.LastWriteTimeUtc.Ticks, info.Length);
    }
    public void Write(string source, (long Stamp, long Size) fingerprint, byte[] data)
    {
        string? temporary = null;
        try
        {
            if (Fingerprint(source) != fingerprint) return;
            Directory.CreateDirectory(root);
            string path = CachePath(source);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temporary)))
            {
                writer.Write(fingerprint.Stamp); writer.Write(fingerprint.Size); writer.Write(data);
            }
            File.Move(temporary, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
