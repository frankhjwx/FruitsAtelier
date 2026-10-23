using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;

internal static class PatternFolderTests
{
    internal static void Run()
    {
        string directory = Path.GetFullPath(Path.Combine("artifacts/tests/pattern-folders", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 200 });
        var pattern = PatternSnapshot.Capture(map, map.Fruits.Select(f => f.Id), "First");
        PatternArchive.WriteFile([pattern], Path.Combine(directory, "gallery.fap"));
        var store = new PatternGalleryStore(directory);
        Check(store.LoadContents().Folders!.Single() is { Name: "Default" } folder && folder.PatternIds.SequenceEqual([pattern.Id]), "Legacy entries migrate into Default");
        Guid a = store.CreateFolder("A"), b = store.CreateFolder("B");
        store.TransferPatterns([pattern.Id], a, true);
        var contents = store.LoadContents();
        Guid copy = contents.Folders!.Single(f => f.Id == a).PatternIds.Single();
        Check(copy != pattern.Id && contents.Patterns.Single(p => p.Id == copy).CreateDocument().ContentEquals(pattern.CreateDocument()), "Copy preserves geometry with an independent identity");
        store.TransferPatterns([pattern.Id], b, false);
        store.Replace(pattern.WithName("Changed"));
        store.AddTag(new("Unused", 0x123456));
        store.EditTag("Unused", new("Unassigned", 0xFFFFFF));
        store.ReorderTags(["Unassigned"]);
        contents = new PatternGalleryStore(directory).LoadContents();
        Check(contents.Folders!.Single(f => f.Id == PatternFolder.DefaultId).PatternIds.Count == 0
            && contents.Folders!.Single(f => f.Id == b).PatternIds.SequenceEqual([pattern.Id])
            && contents.Patterns.Single(p => p.Id == copy).Name == "First", "Move, rename and tag edits retain membership and copy isolation");
        string package = Path.Combine(directory, "folders.fap");
        store.Export(package, folderIds: [a, b]);
        var exported = PatternArchive.ReadContentsFile(package);
        Check(exported.Folders!.Select(f => f.Name).SequenceEqual(["A", "B"]) && exported.Patterns.Count == 2, "Selected folders export their contents");
        store.Import(package); store.Import(package);
        Check(store.LoadContents().Folders!.Select(f => f.Name).Contains("A (2)") && store.Load().Count == 6, "Repeated imports suffix conflicting folder names");
        store.Import(package, a);
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 3, "Import destination combines source folders into the selected folder");
        store.MergeFolders([a, b, PatternFolder.DefaultId], PatternFolder.DefaultId);
        contents = store.LoadContents();
        Check(contents.Folders!.Count == 5 && contents.Folders!.Single(f => f.Id == PatternFolder.DefaultId).PatternIds.Count == 4
            && contents.Patterns.Count == 8, "Merge retains every pattern and removes merged non-default folders");
        Guid target = store.CreateFolder("Target");
        store.MergeFolders([PatternFolder.DefaultId, target], target);
        Check(store.LoadContents().Folders!.Single(f => f.Id == PatternFolder.DefaultId).PatternIds.Count == 0, "Merging Default into another folder preserves empty Default");
        byte[] before = File.ReadAllBytes(Path.Combine(directory, "gallery.fap"));
        Expect(() => store.RenameFolder(PatternFolder.DefaultId, "Other"));
        Expect(() => store.DeleteFolder(PatternFolder.DefaultId));
        Expect(() => store.DeleteFolder(Guid.NewGuid()));
        Expect(() => store.CreateFolder("default"));
        Expect(() => store.TransferPatterns([Guid.NewGuid()], target, false));
        Expect(() => store.Import(package, Guid.NewGuid()));
        Expect(() => store.CreateFolder("\n"));
        Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "gallery.fap"))), "Failed operations leave the archive unchanged");
        var retained = store.LoadContents();
        var deletedIds = retained.Folders!.Single(f => f.Id == target).PatternIds.ToHashSet();
        store.DeleteFolder(target);
        contents = new PatternGalleryStore(directory).LoadContents();
        Check(contents.Folders!.All(f => f.Id != target)
            && contents.Patterns.Select(p => p.Id).SequenceEqual(retained.Patterns.Where(p => !deletedIds.Contains(p.Id)).Select(p => p.Id))
            && contents.Tags.SequenceEqual(retained.Tags), "Folder deletion must remove only its contents and retain shared tags");
        Guid emptyDelete = store.CreateFolder("Empty delete");
        store.DeleteFolder(emptyDelete);
        Check(store.LoadContents().Folders!.All(f => f.Id != emptyDelete), "Empty folder deletion did not persist");
        using var invalid = new MemoryStream();
        Expect(() => PatternArchive.Write([pattern], invalid, folders: [new(Guid.NewGuid(), "Bad", [pattern.Id, pattern.Id])]));
        using var missing = new MemoryStream();
        Expect(() => PatternArchive.Write([pattern], missing, folders: [new(Guid.NewGuid(), "Bad", [])]));
        var legacyStore = new PatternGalleryStore(Path.Combine(directory, "Legacy"));
        legacyStore.Add(pattern);
        string named = Path.Combine(directory, "My patterns.fap"); legacyStore.Export(named);
        Check(PatternArchive.ReadContentsFile(named).Folders!.Single().Name == "My patterns", "Flat exports acquire the export filename");
        Guid empty = legacyStore.CreateFolder("Empty");
        legacyStore.Export(named, folderIds: [empty]);
        Check(PatternArchive.ReadContentsFile(named).Folders!.Single().PatternIds.Count == 0, "Empty folders survive sharing");
        string legacy = Path.Combine(directory, "Old pack.fap"); PatternArchive.WriteFile([pattern], legacy);
        legacyStore.Import(legacy);
        Check(legacyStore.LoadContents().Folders!.Any(f => f.Name == "Old pack"), "Legacy imports acquire the package filename");
        Console.WriteLine("PASS Pattern folders: migration, transfers, merges, sharing and atomic validation");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Expect(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { return; }
        throw new Exception("Expected rejected folder operation");
    }
}
