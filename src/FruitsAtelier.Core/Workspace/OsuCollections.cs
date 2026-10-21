using System.Security.Cryptography;
using System.Text;

namespace FruitsAtelier.Core;

public sealed record OsuCollection(string Id, string Name, IReadOnlyList<string> Hashes, IReadOnlyList<string> Paths);
public sealed record OsuCollectionCatalog(string Root, string Fingerprint, bool Available, IReadOnlyList<OsuCollection> Collections)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    // Format: https://github.com/ppy/osu/wiki/Legacy-database-file-structure#collectiondb
    // See LegacyCollectionImporter at ppy/osu revision 44819244af116e8ae08fe2cbd29c6e489b5c9d4e.
    public static OsuCollectionCatalog Read(string root, OsuCollectionCatalog? previous = null)
    {
        var unavailable = new OsuCollectionCatalog(root, "", false, []);
        if (string.IsNullOrWhiteSpace(root)) return unavailable;
        try
        {
            var database = new FileInfo(Path.Combine(root, "osu!.db"));
            var collections = new FileInfo(Path.Combine(root, "collection.db"));
            if (!database.Exists || !collections.Exists) return unavailable;
            string fingerprint = $"{database.LastWriteTimeUtc.Ticks}:{database.Length}:{collections.LastWriteTimeUtc.Ticks}:{collections.Length}";
            using var osu = new FileStream(database.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (osu.Length < 4) return unavailable;
            using var file = new FileStream(collections.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length is < 8 or > 64 * 1024 * 1024) return unavailable;
            if (previous is { Available: true } && previous.Root == root && previous.Fingerprint == fingerprint) return previous;
            using var reader = new BinaryReader(file, Utf8);
            reader.ReadInt32();
            int count = Count(100_000);
            var result = new List<OsuCollection>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string name = ReadString(4096);
                int members = Count(1_000_000);
                var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < members; j++)
                {
                    string hash = ReadString(32);
                    if (hash.Length != 32 || !hash.All(char.IsAsciiHexDigit)) throw new InvalidDataException();
                    hashes.Add(hash.ToUpperInvariant());
                }
                int occurrence = occurrences.GetValueOrDefault(name);
                occurrences[name] = occurrence + 1;
                string id = "osu:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ":" + occurrence;
                result.Add(new(id, name, hashes.ToArray(), []));
            }
            if (file.Position != file.Length) return unavailable;
            var paths = ReadBeatmapPaths(osu, Path.Combine(root, "Songs"), result.SelectMany(c => c.Hashes).ToHashSet(StringComparer.OrdinalIgnoreCase));
            return new(root, fingerprint, true, result.Select(c => c with
            {
                Paths = c.Hashes.Where(paths.ContainsKey).SelectMany(h => paths[h]).Distinct(WorkspaceSynchronization.Paths).ToArray()
            }).ToArray());

            int Count(int maximum)
            {
                int value = reader.ReadInt32();
                if (value < 0 || value > maximum || value > file.Length - file.Position) throw new InvalidDataException();
                return value;
            }
            string ReadString(int maximum)
            {
                byte marker = reader.ReadByte();
                if (marker == 0) return "";
                if (marker != 0x0b) throw new InvalidDataException();
                int length = reader.Read7BitEncodedInt();
                if (length < 0 || length > maximum || length > file.Length - file.Position) throw new InvalidDataException();
                return Utf8.GetString(reader.ReadBytes(length));
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FormatException or System.Security.SecurityException or OverflowException)
        { return unavailable; }
    }

    private static Dictionary<string, List<string>> ReadBeatmapPaths(Stream stream, string songs, HashSet<string> wanted)
    {
        using var reader = new BinaryReader(stream, Utf8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version < 20070101) throw new InvalidDataException();
        Skip(4 + 1 + 8); ReadString();
        int maps = Count(2_000_000);
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < maps; i++)
        {
            if (version < 20191106) Skip(4);
            for (int j = 0; j < 7; j++) ReadString();
            string hash = ReadString(), filename = ReadString();
            Skip(1 + 6 + 8 + (version < 20140609 ? 4 : 16) + 8);
            if (version >= 20140609)
                for (int j = 0; j < 4; j++) Skip((long)Count(1_000_000) * (version < 20250107 ? 14 : 10));
            Skip(12); Skip((long)Count(1_000_000) * 17);
            Skip(12 + 4 + 2 + 4 + 1);
            ReadString(); ReadString(); Skip(2); ReadString(); Skip(1 + 8 + 1);
            string folder = ReadString();
            Skip(8 + 5 + (version < 20140609 ? 2 : 0) + 4 + 1);
            if (!wanted.Contains(hash)) continue;
            // Collection hashes identify osu!'s cached records, even when an editor has changed the file since osu! last scanned it.
            folder = folder.Replace('\\', Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(songs, folder, filename));
            if (Path.IsPathRooted(folder) || Path.IsPathRooted(filename) || Path.GetFileName(filename) != filename || filename.Contains('\\')
                || !WorkspaceProject.Within(songs, path)) throw new InvalidDataException();
            if (!result.TryGetValue(hash, out var paths)) result[hash] = paths = [];
            paths.Add(path);
        }
        Skip(4);
        if (stream.Position != stream.Length) throw new InvalidDataException();
        return result;

        void Skip(long size)
        {
            if (size < 0 || size > stream.Length - stream.Position) throw new InvalidDataException();
            stream.Seek(size, SeekOrigin.Current);
        }
        int Count(int maximum)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum || count > stream.Length - stream.Position) throw new InvalidDataException();
            return count;
        }
        string ReadString()
        {
            byte marker = reader.ReadByte();
            if (marker == 0) return "";
            if (marker != 0x0b) throw new InvalidDataException();
            int length = reader.Read7BitEncodedInt();
            if (length < 0 || length > 1024 * 1024 || length > stream.Length - stream.Position) throw new InvalidDataException();
            return Utf8.GetString(reader.ReadBytes(length));
        }
    }
}
