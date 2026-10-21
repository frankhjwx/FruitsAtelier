using System.Security.Cryptography;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class OsuCollectionTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "osu-collections", Guid.NewGuid().ToString("N")));
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), OsuRoot = Path.Combine(root, "osu") };
        string set = Path.Combine(settings.Songs, "Subset"); Directory.CreateDirectory(set);
        string selected = WriteMap("selected", "Chosen difficulty");
        WriteMap("excluded", "Excluded difficulty");
        string database = Path.Combine(settings.OsuRoot, "osu!.db"), collections = Path.Combine(settings.OsuRoot, "collection.db");
        string hash = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(selected)));
        WriteCollections(collections, "Practice", hash.ToLowerInvariant());
        Check(!OsuCollectionCatalog.Read(settings.OsuRoot).Available, "collections hidden without osu!.db");
        WriteDatabase(database, 20260924, (selected, hash));
        foreach (int version in new[] { 20130101, 20180101, 20240101, 20260924 })
        {
            WriteDatabase(database, version, (selected, hash));
            Check(OsuCollectionCatalog.Read(settings.OsuRoot).Collections.Single().Paths.Single() == selected, "database version layout " + version);
        }
        File.AppendAllText(selected, "\n// Source edited after osu cached the collection identity.\n");
        var catalog = OsuCollectionCatalog.Read(settings.OsuRoot);
        Check(catalog.Available && catalog.Collections.Single().Name == "Practice", "official collection format is readable");
        Check(ReferenceEquals(catalog, OsuCollectionCatalog.Read(settings.OsuRoot, catalog)), "unchanged collection metadata reuses the snapshot");
        var db = new LibraryDatabase(settings.Workspace, settings.Songs); db.Scan();
        var options = new LibraryOrganization(DifficultyPaths: catalog.Collections[0].Paths);
        using (var subset = db.SearchSnapshot("", organization: options))
        {
            var row = subset.Page(0).Single();
            Check(row.Count == 1 && subset.Difficulties(row, 0).Single().Difficulty == "Chosen difficulty", "set count and details retain only collected difficulties");
        }
        using (var empty = db.SearchSnapshot("", organization: new(DifficultyPaths: [])))
            Check(empty.SetCount == 0, "empty collection never expands to all maps");
        using (var excluded = db.SearchSnapshot("Excluded", organization: options))
            Check(excluded.SetCount == 0, "search cannot bring back uncollected difficulty");
        FruitsAtelier.App.Platform.LibraryOperations.Open(db.Search("")[0], settings);
        db.ReindexProjects();
        using (var associated = db.SearchSnapshot("", organization: options))
        {
            var row = associated.Page(0).Single();
            Check(row.Map.ProjectPath is not null && row.Count == 1 && associated.Difficulties(row, 0).Count == 1,
                "associated workspace project does not broaden collection difficulty membership");
        }
        var browser = LibraryBrowser.Create(db, "", false, null, null, 0, organization: options);
        Check(!browser.CanReorder(db, "", false, null, new()), "collection results cannot be reused for all songs");
        Check(!browser.CanReorder(db, "", false, null, new(DifficultyPaths: [])), "changed difficulty membership invalidates cached filtering");
        browser.Retire(); browser.Retire();
        Check(!browser.CanReorder(db, "", false, null, options), "retired browsers cannot reuse disposed snapshots");
        browser.Pump(); browser.RequestDetails(0); browser.RequestVisible(0, 1);
        var transfer = LibraryBrowser.Create(db, "", false, null, null, 0, organization: options);
        var reordered = transfer.Reorder(options with { Sort = LibraryOrder.Artist }, null, null, 0);
        transfer.Retire(); transfer.Retire();
        var owner = reordered.GetAwaiter().GetResult();
        Check(owner.Count == 1, "retirement waits for in-flight snapshot ownership transfer");
        owner.RequestDetails(0);
        for (int i = 0; i < 100 && owner.Loading; i++) { owner.Pump(); Thread.Sleep(5); }
        owner.Pump();
        Check(owner.Error is null && owner.Detail(0)?.Difficulty == "Chosen difficulty", "transferred snapshot stays usable after prior owner retires");
        owner.Retire(); owner.Retire();
        var disposed = db.SearchSnapshot(""); disposed.Dispose(); disposed.CancelReferenceDiscovery(); disposed.Dispose();
        byte[] original = File.ReadAllBytes(collections);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int width in new[] { 980, 1440 }) RunUi(locale, width);
        }
        finally { L.SetLanguage(language); }
        Check(File.ReadAllBytes(collections).SequenceEqual(original), "library never modifies osu collections");
        File.WriteAllBytes(collections, original[..^1]);
        Check(!OsuCollectionCatalog.Read(settings.OsuRoot).Available, "truncated collection files are unavailable without partial results");
        File.WriteAllBytes(collections, original);
        File.WriteAllBytes(database, [1, 2, 3]);
        Check(!OsuCollectionCatalog.Read(settings.OsuRoot).Available, "invalid osu database hides the section");
        File.Delete(database);
        Check(!OsuCollectionCatalog.Read(settings.OsuRoot, catalog).Available, "removed osu!.db hides cached collections");
        Console.WriteLine("PASS osu collections: official format, missing databases, subset difficulties, read-only UI and refresh");

        string WriteMap(string file, string name)
        {
            string path = Path.Combine(set, file + ".osu");
            File.WriteAllText(path, $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Subset\nArtist:Artist\nCreator:Mapper\nVersion:{name}\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
            return path;
        }
        void RunUi(string locale, int width)
        {
            L.SetLanguage(locale);
            string memory = Path.Combine(settings.Workspace, "library-view.json");
            File.WriteAllText(memory, "{\"Collections\":[{\"Id\":\"fa\",\"Name\":\"FA practice\",\"Keys\":[]}]}");
            var view = new EditorView(false); var canvas = new RecordingCanvas();
            try
            {
                view.InitializeLibrary(true, settings); Wait(() => canvas.Texts.Any(t => t.Value == "Practice"));
                Click("Practice"); Wait(() => view.LibrarySetTotal == 1 && canvas.Texts.Any(t => t.Value == "Chosen difficulty"));
                Check(!canvas.Texts.Any(t => t.Value == "Excluded difficulty"), "osu collection details exclude other diffs");
                var row = canvas.Texts.First(t => t.Value == "Practice");
                view.PointerDown(row.X + 2, row.Y + 2, 2, false, false); Paint();
                Check(!canvas.Texts.Any(t => t.Value == L.Get("library.renameCollection") || t.Value == L.Get("library.deleteCollection")), "osu collection has no edit menu");
                var card = canvas.Texts.First(t => t.X == 314 && t.Value == "Subset");
                view.PointerDown(card.X + 2, card.Y + 2, 2, false, false); Paint();
                Check(!canvas.Texts.Any(t => t.Value == L.Get("library.removeCollection")), "osu membership has no remove action");
                Click(L.Get("library.addCollection"));
                Check(canvas.Texts.Any(t => t.Value == "FA practice") && !canvas.Texts.Any(t => t.Value == "☑"), "membership dialog offers only FA collections");
                view.KeyDown(27, false, false); Paint();
                File.Delete(database);
                Wait(() => !canvas.Texts.Any(t => t.Value == L.Get("library.osuCollections")));
                Check(canvas.Texts.Any(t => t.Value == "FA practice"), "FA collections remain available without osu database");
                WriteDatabase(database, 20260924, (selected, hash));
                Wait(() => canvas.Texts.Any(t => t.Value == "Practice"));
            }
            finally { view.CloseLibrary(); }
            void Paint() { canvas.Clear(); view.Render(canvas, width, 740); }
            void Click(string text)
            {
                var label = canvas.Texts.Last(t => t.Value == text);
                view.PointerDown(label.X + 2, label.Y + 2, 0, false, false); view.PointerUp(label.X + 2, label.Y + 2, 0); Paint();
            }
            void Wait(Func<bool> condition)
            {
                for (int i = 0; i < 1000; i++) { Paint(); if (i > 40 && !view.LibraryLoading && condition()) return; Thread.Sleep(10); }
                throw new Exception("osu collection UI did not settle: " + string.Join(" | ", canvas.Texts.Select(t => t.Value)));
            }
        }
    }
    internal static void WriteCollections(string path, string name, params string[] hashes)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(20260924); writer.Write(1); Write(name); writer.Write(hashes.Length);
        foreach (string hash in hashes) Write(hash);
        void Write(string value) { writer.Write((byte)0x0b); writer.Write(value); }
    }
    internal static void WriteDatabase(string path, int version, params (string Path, string Hash)[] maps)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(version); Zero(4 + 1 + 8); Write(""); writer.Write(maps.Length);
        foreach (var map in maps)
        {
            if (version < 20191106) Zero(4);
            for (int i = 0; i < 7; i++) Write("");
            Write(map.Hash); Write(Path.GetFileName(map.Path));
            Zero(1 + 6 + 8 + (version < 20140609 ? 4 : 16) + 8);
            if (version >= 20140609)
                for (int i = 0; i < 4; i++)
                {
                    writer.Write(1); writer.Write((byte)0x08); writer.Write(0);
                    writer.Write(version < 20250107 ? (byte)0x0d : (byte)0x0c);
                    if (version < 20250107) writer.Write(1.0); else writer.Write(1.0f);
                }
            Zero(12); writer.Write(0); Zero(12 + 4 + 2 + 4); writer.Write((byte)2);
            Write(""); Write(""); Zero(2); Write(""); Zero(1 + 8 + 1);
            Write(Path.GetFileName(Path.GetDirectoryName(map.Path)!));
            Zero(8 + 5 + (version < 20140609 ? 2 : 0) + 4 + 1);
        }
        Zero(4);
        void Zero(int count) => writer.Write(new byte[count]);
        void Write(string value) { writer.Write((byte)0x0b); writer.Write(value); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
