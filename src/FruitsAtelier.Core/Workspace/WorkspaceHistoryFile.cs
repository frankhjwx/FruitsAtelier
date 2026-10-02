using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class WorkspaceHistoryFile
{
    public const string Extension = ".catchbackup";
    private static readonly byte[] Magic = "FACB\u0001\0\0\0"u8.ToArray();

    public static string LogicalPath(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path[..^Extension.Length] : path;
    public static bool Exists(string path) => File.Exists(path + Extension) || File.Exists(path);
    public static string ReadText(string path)
    {
        using var reader = new StreamReader(new MemoryStream(Read(path)), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static byte[] Read(string path)
    {
        lock (WorkspaceProject.Gate) return ReadCore(path);
    }

    private static byte[] ReadCore(string path)
    {
        string compressed = path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;
        if (!File.Exists(compressed))
        {
            WorkspaceProject.RejectLinks(path);
            if (new FileInfo(path).Length > Limit(path)) throw Invalid();
            return File.ReadAllBytes(path);
        }
        WorkspaceProject.RejectLinks(compressed);
        using var input = File.OpenRead(compressed);
        if (input.Length < 44) throw Invalid();
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)) throw Invalid();
        int size = reader.ReadInt32();
        if (size < 0 || size > Limit(path)) throw Invalid();
        byte[] checksum = reader.ReadBytes(32);
        if (checksum.Length != 32) throw Invalid();
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        byte[] bytes = new byte[size];
        try { brotli.ReadExactly(bytes); }
        catch (EndOfStreamException error) { throw new InvalidDataException(L.Get("project.invalid"), error); }
        if (brotli.ReadByte() != -1 || !SHA256.HashData(bytes).SequenceEqual(checksum)) throw Invalid();
        return bytes;
    }

    public static void WriteProject(BeatmapProject project, string path) => Write(path, Encoding.UTF8.GetBytes(ProjectSerializer.Serialize(project, path)));

    public static void Write(string path, byte[] bytes)
    {
        if (bytes.Length > Limit(path)) throw new InvalidDataException(L.Get("core.project.writeLimit"));
        string target = path + Extension;
        WorkspaceProject.RejectLinks(target);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
                writer.Write(Magic); writer.Write(bytes.Length); writer.Write(SHA256.HashData(bytes));
                using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true)) brotli.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static bool Compressible(string path) => Path.GetExtension(path).ToLowerInvariant() is ".catchdiff" or ".catchproj" or ".catchsync" or ".osu";
    internal static long CompactInBackground(string path, string stagingDirectory, CancellationToken cancellation, Action? afterCompression = null)
    {
        byte[] original;
        lock (WorkspaceProject.Gate)
        {
            cancellation.ThrowIfCancellationRequested();
            WorkspaceProject.RejectLinks(path);
            if (!File.Exists(path)) return 0;
            original = ReadLegacy(path);
        }
        string staging = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        string stagedFile = staging + Extension, target = path + Extension;
        try
        {
            Write(staging, original);
            afterCompression?.Invoke();
            cancellation.ThrowIfCancellationRequested();
            if (!ReadCore(stagedFile).SequenceEqual(original)) throw Invalid();
            if (new FileInfo(stagedFile).Length >= original.Length) return 0;
            lock (WorkspaceProject.Gate)
            {
                cancellation.ThrowIfCancellationRequested();
                WorkspaceProject.RejectLinks(path); WorkspaceProject.RejectLinks(target);
                // Retention may remove this round, or another writer may replace either copy during compression.
                if (!File.Exists(path) || !ReadLegacy(path).SequenceEqual(original)) return 0;
                if (File.Exists(target))
                {
                    if (!ReadCore(target).SequenceEqual(original)) return 0;
                }
                else File.Move(stagedFile, target);
                if (!ReadCore(target).SequenceEqual(original)) throw Invalid();
                cancellation.ThrowIfCancellationRequested();
                long saved = original.Length - new FileInfo(target).Length;
                if (saved <= 0) return 0;
                File.Delete(path);
                return saved;
            }
        }
        finally { if (File.Exists(stagedFile)) File.Delete(stagedFile); }
    }
    internal static long Compact(string path)
    {
        WorkspaceProject.RejectLinks(path);
        byte[] original = ReadLegacy(path);
        Write(path, original);
        // Keep the original until the published compressed copy passes the same reader used for recovery.
        if (!Read(path + Extension).SequenceEqual(original)) throw Invalid();
        long saved = new FileInfo(path).Length - new FileInfo(path + Extension).Length;
        if (saved <= 0)
        {
            File.Delete(path + Extension);
            return 0;
        }
        File.Delete(path);
        return saved;
    }
    private static byte[] ReadLegacy(string path)
    {
        if (new FileInfo(path).Length > ProjectSerializer.MaximumFileBytes) throw Invalid();
        return File.ReadAllBytes(path);
    }
    // Export receipts embed both project authoring and synchronization baselines as JSON strings.
    private static int Limit(string path) => Path.GetExtension(LogicalPath(path)).Equals(".json", StringComparison.OrdinalIgnoreCase)
        ? 4 * ProjectSerializer.MaximumFileBytes : ProjectSerializer.MaximumFileBytes;
    private static InvalidDataException Invalid() => new(L.Get("project.invalid"));
}
