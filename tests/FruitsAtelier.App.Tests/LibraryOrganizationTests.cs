using System.Text.Json.Nodes;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryOrganizationTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "library-organization", Guid.NewGuid().ToString("N")));
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), Songs = Path.Combine(root, "osu", "Songs") };
        string a = WriteMap("Alpha", "Zulu artist", "Alice", 2);
        string b = WriteMap("Beta", "Alpha artist", "Bob", 0);
        WriteMap("Gamma", "Middle artist", "Alice", 2);
        File.SetLastWriteTimeUtc(Path.Combine(a, "map.osu"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(b, "map.osu"), new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string otherMode = Path.Combine(a, "taiko.osu");
        File.WriteAllText(otherMode, "osu file format v14\n[General]\nMode:1\n[HitObjects]\n");
        File.SetLastWriteTimeUtc(otherMode, new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var db = new LibraryDatabase(settings.Workspace, settings.Songs); db.Scan();
        using (var sorted = db.SearchSnapshot("", organization: new(Sort: LibraryOrder.Artist)))
            Check(sorted.Page(0).Select(r => r.Map.Title).SequenceEqual(new[] { "Beta", "Gamma", "Alpha" }), "artist order uses the selected field");
        using (var sorted = db.SearchSnapshot("", organization: new(Sort: LibraryOrder.Title, Descending: true)))
            Check(sorted.Page(0)[0].Map.Title == "Gamma", "descending title order");
        using (var grouped = db.SearchSnapshot("", organization: new(Group: LibraryGrouping.Creator, Collapsed: new[] { "A" })))
        {
            Check(grouped.SetCount == 3 && grouped.Count == 3, "collapsed groups retain total and omit their member rows");
            Check(grouped.Page(0)[0] is { Header: "A", Count: 2 }, "group count counts sets");
            Check(grouped.FindIndex(a) == -1 && grouped.Page(0).Any(r => r.Map.Title == "Beta"), "collapsed members do not occupy indexed positions");
        }
        var now = DateTime.UtcNow;
        var visits = new Dictionary<string, long>(WorkspaceSynchronization.Paths) { [a] = now.Ticks, [b] = now.AddDays(-1).Ticks };
        using (var recent = db.SearchSnapshot("", favourites: visits.Keys.ToArray(), organization: new(LibraryGrouping.RecentVisited, LibraryOrder.RecentVisited, true, visits, Today: now.ToLocalTime().Date)))
        {
            Check(recent.SetCount == 2 && recent.Page(0).Where(r => r.Header is null).First().Map.Title == "Alpha", "recent membership and order");
            Check(recent.Page(0).Where(r => r.Header is not null).Select(r => r.Header).SequenceEqual(new[] { "0", "1" }), "local calendar buckets");
        }
        using (var added = db.SearchSnapshot("", organization: new(Group: LibraryGrouping.DateAdded)))
            Check(added.Page(0).Any(r => r.Header == "4" && r.Count == 2), "existing sets retain historical source modification dates");
        db.ClearDerivedCache(); db.Scan();
        using (var added = db.SearchSnapshot("", organization: new(Group: LibraryGrouping.DateAdded)))
            Check(added.Page(0).Any(r => r.Header == "4" && r.Count == 2), "rebuilding indexes reads the same historical dates");
        using (var added = db.SearchSnapshot("", organization: new(Sort: LibraryOrder.DateAdded)))
            Check(added.Page(0)[0].Map.Title == "Alpha", "set date includes earliest .osu modification time from other modes");

        var browser = LibraryBrowser.Create(db, "", false, null, null, 0);
        var collapsedOptions = new LibraryOrganization(Group: LibraryGrouping.Creator, CollapseByDefault: true);
        Check(browser.CanReorder(db, "", false, null, collapsedOptions), "unchanged metadata can be reused");
        var collapsedBrowser = browser.Reorder(collapsedOptions, null, null, 0).GetAwaiter().GetResult(); browser.Retire();
        Check(collapsedBrowser.Count == 2 && Math.Abs(collapsedBrowser.Extent * 86 - 60) < .01, "collapsed virtual extent uses compact headers");
        var expandedBrowser = collapsedBrowser.Reorder(collapsedOptions with { Expanded = new[] { "A" } }, null, null, 0).GetAwaiter().GetResult(); collapsedBrowser.Retire();
        Check(expandedBrowser.Count == 4 && expandedBrowser.IndexAtOffset(31f / 86) == 1, "expanded compact groups map offsets to correct card rows");
        Check(Math.Abs((expandedBrowser.OffsetOf(2) - expandedBrowser.OffsetOf(1)) * 86 - 86) < .01, "map card row heights stay unchanged");
        File.SetLastWriteTimeUtc(otherMode, new DateTime(1989, 1, 1, 0, 0, 0, DateTimeKind.Utc)); db.Scan();
        Check(!expandedBrowser.CanReorder(db, "", false, null, collapsedOptions), "file changes invalidate reused metadata");
        expandedBrowser.Retire();

        string localRoot = Path.Combine(root, "LocalWorkspace");
        var document = new MapDocument { Name = "Local set", IsDemo = false };
        document.OriginalSections.Add(new OsuSection { Name = "Metadata", Lines = { "Title:Local title", "Artist:Local artist", "Creator:Local creator" } });
        WorkspaceProject.Create(localRoot, BeatmapProject.FromDocuments([document]), "");
        var localDb = new LibraryDatabase(localRoot, ""); localDb.ReindexProjects();
        using (var local = localDb.SearchSnapshot("", projectsOnly: true, organization: new(Group: LibraryGrouping.Artist)))
            Check(local.Page(0)[0].Header == "L" && local.Page(0)[1].Map.Artist == "Local artist", "workspace-only sets use saved metadata for grouping");

        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int width in new[] { 980, 1440 }) { RunUi(locale, width); Console.WriteLine($"PASS Library organization {locale} {width}"); }
        }
        finally { L.SetLanguage(language); }

        string WriteMap(string title, string artist, string creator, int mode)
        {
            string folder = Path.Combine(settings.Songs, title); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "map.osu"), $"osu file format v14\n[General]\nMode:{mode}\n[Metadata]\nTitle:{title}\nArtist:{artist}\nCreator:{creator}\nBeatmapID:{(title == "Alpha" ? 123 : 0)}\nBeatmapSetID:{(title == "Alpha" ? 456 : 0)}\nVersion:Rain\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:");
            return folder;
        }

        void RunUi(string locale, int width)
        {
            L.SetLanguage(locale);
            string memory = Path.Combine(settings.Workspace, "library-view.json");
            if (File.Exists(memory)) File.Delete(memory);
            var view = new EditorView(false); var canvas = new RecordingCanvas();
            try
            {
                view.InitializeLibrary(true, settings); Wait(3);
                var original = view.Document.DeepClone();
                Check(Label("library.collections").Y > Label("library.recentVisited").Y + 40, "collections separated from built-in categories");
                ClickText(L.Get("library.groupBy", L.Get("library.group.None")));
                ClickText(L.Get("library.group.Creator")); Wait(3);
                Check(!canvas.Texts.Any(t => t.X == 314 && t.Value == "Alpha"), "groups start collapsed");
                Check(canvas.Texts.Any(t => t.X == width - 320 && t.Value == "Alpha"), "collapsing groups retains selected map details");
                var aHeader = canvas.Texts.First(t => t.Value == L.Get("library.groupCount", "A", 2));
                var bHeader = canvas.Texts.First(t => t.Value == L.Get("library.groupCount", "B", 1));
                Check(Math.Abs(bHeader.Y - aHeader.Y - 30) < .01, "collapsed header rows use 30 logical pixels");
                ClickText(L.Get("library.groupCount", "A", 2)); Wait(3);
                Check(canvas.Texts.Any(t => t.X == 314 && t.Value == "Alpha"), "header expands its maps");
                ClickText(L.Get("library.groupCount", "A", 2)); Wait(3);
                Check(!canvas.Texts.Any(t => t.X == 314 && t.Value == "Alpha"), "header click collapses members");
                ClickText(L.Get("library.groupBy", L.Get("library.group.Creator")));
                ClickText(L.Get("library.group.None")); Wait(3);
                ClickText(L.Get("library.sortBy", L.Get("library.order.Title")));
                ClickText(L.Get("library.order.Artist")); Wait(3);
                Check(canvas.Texts.First(t => t.X == 314 && new[] { "Alpha", "Beta", "Gamma" }.Contains(t.Value)).Value == "Beta", "sort dropdown changes visible order");
                ClickText(L.Get("library.sortBy", L.Get("library.order.Artist")));
                ClickText(L.Get("library.order.Title")); Wait(3);
                var beta = canvas.Texts.First(t => t.X == 314 && t.Value == "Beta");
                view.PointerDown(beta.X, beta.Y, 0, false, false); view.PointerUp(beta.X, beta.Y, 0); Paint();
                string? visitedUrl = null; bool editingOpened = false;
                view.RequestVisitOsuWeb = url => visitedUrl = url; view.RequestLibraryOpen = (_, _) => editingOpened = true;
                var web = Label("library.visitWeb");
                Click("library.visitWeb"); view.PointerDoubleClick(web.X + 2, web.Y + 2, false, false); Paint();
                Check(visitedUrl == "https://osu.ppy.sh/beatmapsets/456#fruits/123" && !editingOpened, "web button opens local-ID URL without entering editor");
                Check(canvas.Texts.Any(t => t.X == width - 320 && t.Value == "Beta"), "web button does not select its card");
                Check(canvas.Texts.Count(t => t.Value == L.Get("library.visitWeb")) == 1, "maps without IDs have no web button");
                var title = canvas.Texts.First(t => t.X == 314 && t.Value == "Alpha");
                view.PointerDown(title.X, title.Y, 2, false, false); Paint(); Click("library.addCollection");
                Check(!view.CanDropAudio && !view.CanDropLibraryFiles, "membership dialog isolates file drops");
                Click("library.newCollection"); Click("library.collectionSave");
                Check(canvas.Texts.Any(t => t.Value == L.Get("library.collectionNameInvalid")), "empty collection names are rejected");
                view.PasteLibraryText("Practice"); Click("library.collectionSave");
                Check(canvas.Texts.Any(t => t.Value == "Practice"), "create returns to membership chooser");
                Click("library.collectionDone"); Wait(3);
                ClickText("Practice"); Wait(1);
                Check(canvas.Texts.Any(t => t.X == 314 && t.Value == "Alpha"), "collection filters to chosen set");
                view.CloseLibrary();
                view = new EditorView(false); view.InitializeLibrary(true, settings); Wait(1);
                Check(canvas.Texts.Any(t => t.Value == "Practice"), "collection membership and navigation survive restart");
                var collection = canvas.Texts.First(t => t.Value == "Practice");
                view.PointerDown(collection.X, collection.Y, 2, false, false); Paint(); Click("library.renameCollection");
                view.KeyDown(65, true, false); view.PasteLibraryText("References"); Click("library.collectionSave"); Wait(1);
                Check(canvas.Texts.Any(t => t.Value == "References"), "collection rename");
                var set = canvas.Texts.First(t => t.X == 314 && t.Value == "Alpha");
                view.PointerDown(set.X, set.Y, 2, false, false); Paint(); Click("library.addCollection");
                ClickText("References"); Click("sliderBatch.cancel"); Wait(1);
                Check(view.LibrarySetTotal == 1, "cancel discards membership changes");
                view.PointerDown(set.X, set.Y, 2, false, false); Paint(); Click("library.removeCollection"); Wait(0);
                Check(original.ContentEquals(view.Document) && !view.IsDirty, "organization leaves beatmap content and undo untouched");
                Click("library.all"); Wait(3);
                set = canvas.Texts.First(t => t.X == 314 && t.Value == "Alpha");
                view.PointerDown(set.X, set.Y, 0, false, false); view.PointerUp(set.X, set.Y, 0);
                Check(JsonNode.Parse(File.ReadAllText(memory))!["Visits"]!.AsObject().Count == 0, "selection is not a visit");
                view.RequestLibraryOpen = (map, difficulty) => view.BeginLibraryOpen(map, () => view.CloseLibrary(), difficulty);
                view.PointerDoubleClick(set.X, set.Y, false, false);
                for (int i = 0; i < 1000 && (!view.HasEditorProject || view.LibraryOpening); i++) { Paint(); Thread.Sleep(10); }
                Check(view.HasEditorProject && !view.LibraryOpening, "successful entry opens project");
                view.ShowLibrary(); Paint(); Click("library.recentVisited"); Wait(1);
                ClickText(L.Get("library.groupCount", L.Get("library.period.0"), 1)); Wait(1);
                Check(canvas.Texts.Any(t => t.X == 314 && t.Value == "Alpha"), "successful open records recent visit");
                view.CloseLibrary();
                view = new EditorView(false); view.InitializeLibrary(true, settings); Wait(1);
                Check(Label("library.recentVisited").Bold, "recent category survives restart");
                var reference = canvas.Texts.First(t => t.Value == "References");
                view.PointerDown(reference.X, reference.Y, 2, false, false); Paint(); Click("library.deleteCollection"); Wait(1);
                Check(!canvas.Texts.Any(t => t.Value == "References") && Directory.Exists(a), "deleting collection retains source files");
            }
            finally { view.CloseLibrary(); }

            RecordingCanvas.Label Label(string key) => canvas.Texts.Last(t => t.Value == L.Get(key));
            void Click(string key) => ClickText(L.Get(key));
            void ClickText(string text)
            {
                var label = canvas.Texts.Last(t => t.Value == text);
                view.PointerDown(label.X + 2, label.Y + 2, 0, false, false); view.PointerUp(label.X + 2, label.Y + 2, 0); Paint();
            }
            void Paint() { canvas.Clear(); view.Render(canvas, width, 740); }
            void Wait(int count)
            {
                for (int i = 0; i < 1000; i++)
                {
                    Paint(); if (i > 40 && !view.LibraryLoading && view.LibrarySetTotal == count) return;
                    Thread.Sleep(10);
                }
                throw new Exception("Library organization did not settle: " + string.Join(" | ", canvas.Texts.Select(t => t.Value)));
            }
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
