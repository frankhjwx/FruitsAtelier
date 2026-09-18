using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryNavigationTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-navigation-" + Guid.NewGuid());
        var settings = new LibrarySettings { Workspace = root };
        var view = new EditorView(loadDemo: false);
        var canvas = new RecordingCanvas();
        try
        {
            for (int i = 0; i < 24; i++) WorkspaceProject.Create(root, BeatmapProject.FromDocuments([new MapDocument { Name = "Navigation " + i.ToString("D2"), IsDemo = false }]), "");
            view.InitializeLibrary(true, settings); Settle();
            Check(!view.HasEditorProject && !view.Document.IsDemo && view.Document.Fruits.Count == 0, "startup has no demo");
            view.KeyDown(27, false, false); Check(view.LibraryVisible, "Escape on home does not open an empty editor");
            Click(L.Get("library.projects")); Settle();
            Check(!canvas.Texts.Any(t => t.Value == L.Get("library.refresh") || t.Value == L.Get("library.new")), "sidebar contains only categories");
            Check(canvas.Texts.Any(t => t.Value == "Search"), "search placeholder stays concise");
            var first = Titles();
            view.Wheel(300, 300, -240, false); Paint();
            Check(!Titles().SequenceEqual(first), "wheel scrolls sets");
            var scrolled = Titles();
            view.PointerDown(320, 350, 0, false, false); Check(view.WantsCapture, "drag captures pointer");
            view.PointerMove(320, 180, false, false); view.PointerUp(320, 180, 0); Paint();
            Check(!view.WantsCapture && !Titles().SequenceEqual(scrolled), "drag scrolls sets and releases capture");
            view.PointerDown(647, 560, 0, false, false); view.PointerMove(647, 570, false, false); view.PointerUp(647, 570, 0); Paint();
            Check(Titles().Any(t => t.Value == "Navigation 23"), "scrollbar reaches last set");
            var selected = Titles().First(t => t.Y >= 180 && t.Y < 480);
            view.RequestLibraryOpen = map => view.LoadWorkspace(WorkspaceProject.Open(map.ProjectPath!));
            view.PointerDown(selected.X + 2, selected.Y + 2, 0, false, false); view.PointerUp(selected.X + 2, selected.Y + 2, 0);
            Paint(); var position = Titles();
            view.PointerDoubleClick(selected.X + 2, selected.Y + 2, false, false); Paint();
            Check(!view.LibraryVisible && view.HasEditorProject, "map opens directly");
            view.ChangeAudioPath("unsaved.ogg"); var before = view.Document.DeepClone();
            view.KeyDown(27, false, false); Settle();
            Check(view.LibraryVisible && view.IsDirty && view.Document.ContentEquals(before), "Escape returns without losing edits");
            Check(Titles().SequenceEqual(position), "return restores selected map position: " + string.Join("; ", position.Select(t => t.Value + "@" + t.Y)) + " -> " + string.Join("; ", Titles().Select(t => t.Value + "@" + t.Y)));
            view.RefreshLibrary(); Settle(); Check(Titles().SequenceEqual(position), "scan preserves position");
            Click(L.Get("library.editor")); Paint();
            Click(L.Get("ui.view")); Click(L.Get("ui.edit"));
            Check(canvas.Texts.Any(t => t.Value == L.Get("sliderBatch.menu")), "one click switches open menus");
            view.KeyDown(27, false, false); Paint();
            Check(!view.LibraryVisible, "Escape first dismisses the active menu");
            view.KeyDown(27, false, false); Settle();
            var settingsText = canvas.Texts.Single(t => t.Value == L.Get("library.settings"));
            float hoverX = settingsText.X + 3, hoverY = settingsText.Y + 3;
            view.PointerMove(hoverX, hoverY, false, false); Paint();
            Check(canvas.Fills.Any(f => f.Bounds.Contains(hoverX, hoverY) && f.Color == 0x3D495A), "library header responds to hover");
            view.PointerDown(240, 180, 2, false, false); Paint();
            Check(canvas.Texts.Any(t => t.Value == L.Get("library.new")) && canvas.Texts.Any(t => t.Value == L.Get("library.importFile")), "right-click offers creation and import");
            view.KeyDown(27, false, false); Paint();
            view.CloseLibrary();
            view = new EditorView(loadDemo: false); view.InitializeLibrary(true, settings); Settle();
            Check(Titles().SequenceEqual(position), "restarting restores category and scroll");

            void Paint() { canvas.Clear(); view.Render(canvas, 980, 620); }
            RecordingCanvas.Label[] Titles() => canvas.Texts.Where(t => t.X == 314 && t.Value.StartsWith("Navigation ", StringComparison.Ordinal)).ToArray();
            void Click(string text)
            {
                var label = canvas.Texts.Last(t => t.Value == text);
                view.PointerDown(label.X + 2, label.Y + 2, 0, false, false); view.PointerUp(label.X + 2, label.Y + 2, 0); Paint();
            }
            void Settle()
            {
                for (int i = 0; i < 250; i++) { Paint(); if (i > 25 && !view.LibraryLoading) return; Thread.Sleep(10); }
                throw new Exception("Library did not settle");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
