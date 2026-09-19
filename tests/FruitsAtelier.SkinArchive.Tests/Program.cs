using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Skinning;

var tests = new (string Name, Action<string> Run)[]
{
    ("Root and nested skin packages extract only Catch assets and reuse completed hashes", ImportAndReuse),
    ("All archive paths reject traversal, absolute paths and Windows aliases", MaliciousPaths),
    ("Case-insensitive duplicates and multiple skin roots are rejected", AmbiguousEntries),
    ("Declared individual and cumulative expansion limits are enforced before extraction", ExpansionLimits),
    ("Oversized archive is rejected before ZIP parsing", PackageLimit),
    ("Incomplete or damaged cache is never reused or overwritten", IncompleteCache),
    ("Invalid skin leaves no published cache or temporary files", InvalidSkin),
    ("Unix symlink entries are rejected even when not selected", SymbolicEntries)
};

string testBase = Path.GetFullPath("artifacts/tests/skin-archive");
Directory.CreateDirectory(testBase);
int failures = 0;
foreach (var test in tests)
{
    string root = Path.Combine(testBase, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try { test.Run(root); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.WriteLine($"FAIL {test.Name}: {error}"); }
    finally
    {
        string resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(testBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Test cleanup escaped the dedicated artifact directory.");
        Directory.Delete(resolved, recursive: true);
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} skin archive tests passed.");
return failures == 0 ? 0 : 1;

static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZxkAAAAASUVORK5CYII=");

static void ImportAndReuse(string root)
{
    foreach (string wrapper in new[] { "", "one/", "one/two/three/" })
    {
        string archive = MakeZip(root, [(wrapper + "skin.ini", "[General]\nName: Import fixture"u8.ToArray()),
            (wrapper + "fruit-pear.png", Png()), (wrapper + "reversearrow.png", Png()),
            (wrapper + "reversearrow@2x.png", Png()), (wrapper + "custom-0@2x.png", Png()),
            (wrapper + "normal-hitnormal.wav", FruitsAtelier.Core.HitsoundSamples.CreateWave(FruitsAtelier.Core.CatchObjectKind.Fruit)),
            (wrapper + "ignored.exe", "not executable"u8.ToArray())]);
        string cache = Path.Combine(root, "cache");
        string folder = SkinArchive.Import(archive, cache);
        string key = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant();
        True(folder == Path.Combine(cache, "v4-" + key), "Cache is not keyed by extraction version and archive contents.");
        True(CatchSkin.TryLoad(folder, out var skin, out _) && skin!.Name == "Import fixture", "Extracted skin cannot be loaded.");
        True(!File.Exists(Path.Combine(folder, "ignored.exe")), "Unselected package content was extracted.");
        True(Directory.GetFiles(folder).Length == 7 && File.Exists(Path.Combine(folder, "custom-0@2x.png")), "Combo glyphs were not extracted.");
        True(File.Exists(Path.Combine(folder, "normal-hitnormal.wav")), "Skin hitsound was not extracted.");
        True(File.Exists(Path.Combine(folder, "reversearrow.png")) && File.Exists(Path.Combine(folder, "reversearrow@2x.png")), "Reverse arrow textures were not extracted.");
        DateTime completed = File.GetLastWriteTimeUtc(Path.Combine(folder, ".complete"));
        True(SkinArchive.Import(archive, cache) == folder, "Identical package did not reuse its cache.");
        True(File.GetLastWriteTimeUtc(Path.Combine(folder, ".complete")) == completed, "Cache reuse rewrote the completion marker.");
    }
}

static void MaliciousPaths(string root)
{
    foreach (string path in new[] { "../ignored.txt", "wrap/../../fruit-apple.png", "/fruit-apple.png", "C:/fruit-apple.png",
        "C:fruit-apple.png", "\\\\server\\share\\fruit-apple.png", "wrap\\..\\fruit-apple.png", "fruit-apple.png:stream",
        "wrap/../", "wrap/NUL.txt", "wrap /fruit-apple.png", "wrap/fruit-apple.png." })
    {
        string archive = MakeZip(root, [("fruit-pear.png", Png()), (path, Png())]);
        Reject(() => SkinArchive.Import(archive, Path.Combine(root, "cache")));
        True(!File.Exists(Path.Combine(root, "ignored.txt")), "Traversal wrote outside cache.");
    }
    True(!Directory.Exists(Path.Combine(root, "cache")), "Rejected path packages created a cache.");
}

static void AmbiguousEntries(string root)
{
    foreach (var names in new[]
    {
        new[] { "fruit-pear.png", "FRUIT-PEAR.PNG" },
        new[] { "wrap/fruit-pear.png", "WRAP/fruit-pear.png" },
        new[] { "one/fruit-pear.png", "two/fruit-apple.png" }
    })
    {
        string archive = MakeZip(root, names.Select(name => (name, Png())).ToArray());
        Reject(() => SkinArchive.Import(archive, Path.Combine(root, "cache")));
    }
}

static void ExpansionLimits(string root)
{
    string single = MakeZip(root, [("fruit-pear.png", Png())]);
    PatchDeclaredSize(single, (uint)SkinArchive.MaxFileBytes + 1);
    Reject(() => SkinArchive.Import(single, Path.Combine(root, "cache")), "16 MiB");
    string total = MakeZip(root, new[] { "pear", "apple", "grapes", "orange", "drop" }
        .Select(name => ($"fruit-{name}.png", Png())).ToArray());
    PatchDeclaredSize(total, (uint)SkinArchive.MaxFileBytes);
    Reject(() => SkinArchive.Import(total, Path.Combine(root, "cache")), "64 MiB");
    True(!Directory.Exists(Path.Combine(root, "cache")), "Expansion-limit rejection extracted files.");
    string understated = MakeZip(root, [("fruit-pear.png", Png())]);
    PatchDeclaredSize(understated, 1);
    Reject(() => SkinArchive.Import(understated, Path.Combine(root, "cache")));
    True(!Directory.Exists(Path.Combine(root, "cache")) || !Directory.EnumerateFileSystemEntries(Path.Combine(root, "cache")).Any(),
        "A file larger than its declared size was published or left temporary data.");
}

static void PackageLimit(string root)
{
    string archive = Path.Combine(root, "oversized.osk");
    using (var file = File.Create(archive)) file.SetLength(SkinArchive.MaxArchiveBytes + 1);
    Reject(() => SkinArchive.Import(archive, Path.Combine(root, "cache")), "256 MiB");
    True(!Directory.Exists(Path.Combine(root, "cache")), "Oversized package created a cache.");
}

static void IncompleteCache(string root)
{
    string archive = MakeZip(root, [("fruit-pear.png", Png()), ("fruit-apple.png", Png())]);
    string cache = Path.Combine(root, "cache");
    string key = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant();
    string partial = Path.Combine(cache, "v4-" + key);
    Directory.CreateDirectory(partial);
    File.WriteAllText(Path.Combine(partial, "sentinel.txt"), "preserve");
    Reject(() => SkinArchive.Import(archive, cache));
    True(File.ReadAllText(Path.Combine(partial, "sentinel.txt")) == "preserve", "Incomplete existing cache was overwritten.");
    string complete = SkinArchive.Import(archive, Path.Combine(root, "other-cache"));
    File.Delete(Path.Combine(complete, "fruit-apple.png"));
    Reject(() => SkinArchive.Import(archive, Path.Combine(root, "other-cache")));
}

static void InvalidSkin(string root)
{
    string archive = MakeZip(root, [("skin.ini", "[General]\nName: Invalid"u8.ToArray()), ("fruit-pear.png", "broken"u8.ToArray())]);
    string cache = Path.Combine(root, "cache");
    Reject(() => SkinArchive.Import(archive, cache));
    True(!Directory.EnumerateFileSystemEntries(cache).Any(), "Failed import left a staged or published cache.");
}

static void SymbolicEntries(string root)
{
    string archive = MakeZip(root, [("fruit-pear.png", Png())]);
    using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
    {
        var link = zip.CreateEntry("ignored-link");
        link.ExternalAttributes = unchecked((int)0xA1FF0000);
        using var stream = new StreamWriter(link.Open());
        stream.Write("../../outside");
    }
    Reject(() => SkinArchive.Import(archive, Path.Combine(root, "cache")));
}

static string MakeZip(string root, (string Name, byte[] Content)[] entries)
{
    string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".osk");
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var item in entries)
    {
        var entry = zip.CreateEntry(item.Name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(item.Content);
    }
    return path;
}

static void PatchDeclaredSize(string archive, uint size)
{
    byte[] bytes = File.ReadAllBytes(archive);
    int patched = 0;
    for (int i = 0; i + 46 <= bytes.Length; i++)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) != 0x02014b50) continue;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24), size);
        patched++;
    }
    True(patched > 0, "Test archive has no central directory to patch.");
    File.WriteAllBytes(archive, bytes);
}

static void Reject(Action action, string? expectedMessage = null)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException)
    {
        if (expectedMessage is not null && !error.Message.Contains(expectedMessage))
            throw new Exception($"Expected rejection containing '{expectedMessage}', got '{error.Message}'.");
        return;
    }
    throw new Exception("Expected unsafe/invalid package rejection.");
}

static void True(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
