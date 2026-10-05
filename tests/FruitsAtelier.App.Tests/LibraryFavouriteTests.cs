using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryFavouriteTests
{
    public static void Run()
    {
        string language = L.Language;
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (int width in new[] { 980, 1280 })
        {
            L.SetLanguage(locale);
            string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "favourites", Guid.NewGuid().ToString()));
            var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), Songs = Path.Combine(root, "osu", "Songs") };
            string folder = Path.Combine(settings.Songs, "Song");
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "map.osu");
            File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Favourite song\nArtist:Artist\nCreator:Mapper\nVersion:Rain\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:");
            var local = WorkspaceProject.Create(settings.Workspace, BeatmapProject.FromDocuments([new MapDocument { Name = "Local favourite", IsDemo = false }]), settings.Songs);
            var view = new EditorView(loadDemo: false);
            var canvas = new RecordingCanvas();
            try
            {
                view.InitializeLibrary(true, settings); Settle(1);
                var original = view.Document.DeepClone();
                Toggle("library.addFavourite");
                Check(StarCount() == 1, "star image on upper-left of song card");
                Click("library.projects"); Settle(1); Toggle("library.addFavourite");
                Click("library.favourites"); Settle(2);
                Check(view.LibrarySetTotal == 2, "favourites includes songs and local projects");
                Check(StarCount() == 2, "both favourites are marked");
                Check(original.ContentEquals(view.Document) && !view.IsDirty, "favourites leaves beatmap content unchanged");
                view.LoadWorkspace(WorkspaceProject.Open(local.Directory)); view.ShowLibrary(); Settle(2);
                Check(canvas.Texts.Single(t => t.Value == L.Get("library.favourites")).Bold, "returning from a local favourite retains its category");
                view.CloseLibrary();
                view = new EditorView(loadDemo: false); view.InitializeLibrary(true, settings); Settle(2);
                Check(view.LibrarySetTotal == 2, "restart restores favourites category and entries");
                LibraryMap? opened = null;
                view.RequestLibraryOpen = map => opened = map;
                view.PointerDoubleClick(320, 190, false, false);
                Check(opened is not null, "favourite card opens through host");
                var imported = LibraryOperations.ImportPath(source, settings);
                view.RefreshLibrary(); Settle(2);
                Check(view.LibrarySetTotal == 2, "associated project replaces song without duplicate");
                var db = new LibraryDatabase(settings.Workspace, settings.Songs);
                using (var snapshot = db.SearchSnapshot("Favourite song", favourites: new[] { folder, local.Directory }))
                {
                    Check(snapshot.Count == 1 && snapshot.Page(0)[0].Map.ProjectPath == imported.Directory, "favourite search preserves project association");
                    Check(snapshot.Difficulties(snapshot.Page(0)[0], 0).Count == 1, "favourite project exposes its difficulty");
                }
                Toggle("library.removeFavourite"); Settle(1);
                Check(view.LibrarySetTotal == 1, "removal updates active favourites list");
                Toggle("library.removeFavourite"); Settle(0);
                Check(view.LibrarySetTotal == 0, "removing last favourite clears list");
                view.CloseLibrary();
                view = new EditorView(loadDemo: false); view.InitializeLibrary(true, settings); Settle(0);
                Check(view.LibrarySetTotal == 0, "removals survive restart");
                Click("library.projects"); Settle(2);
                Check(StarCount() == 0, "project cards no longer show favourite stars");
            }
            finally { view.CloseLibrary(); }

            void Paint() { canvas.Clear(); view.Render(canvas, width, 620); }
            int StarCount() => canvas.Images.Count(i => Path.GetFileName(i.Path) == "favourite-star.png" && i.Bounds.X == 219.6f);
            void Settle(int count)
            {
                for (int i = 0; i < 1000; i++)
                {
                    Paint();
                    if (i > 80 && !view.LibraryLoading && view.LibrarySetTotal == count) return;
                    Thread.Sleep(10);
                }
                throw new Exception("Favourite library did not settle: " + string.Join(" | ", canvas.Texts.Select(t => t.Value)));
            }
            void Click(string key)
            {
                var text = canvas.Texts.Last(t => t.Value == L.Get(key));
                view.PointerDown(text.X + 2, text.Y + 2, 0, false, false);
                view.PointerUp(text.X + 2, text.Y + 2, 0); Paint();
            }
            void Toggle(string key)
            {
                view.PointerDown(320, 190, 2, false, false); Paint();
                Check(canvas.Texts.Any(t => t.Value == L.Get(key)), "missing favourite action: " + string.Join(" | ", canvas.Texts.Select(t => t.Value)));
                var action = canvas.Texts.Last(t => t.Value == L.Get(key));
                var open = canvas.Texts.Last(t => t.Value == L.Get("library.start") || t.Value == L.Get("library.continue"));
                Check(action.Y < open.Y, "favourite action precedes open action");
                Click(key); Paint();
            }
        }
        L.SetLanguage(language);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
