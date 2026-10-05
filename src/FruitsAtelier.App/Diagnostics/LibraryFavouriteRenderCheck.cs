using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class LibraryFavouriteRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height)
    {
        canvas.Begin();
        bool starLoaded = canvas.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "library", "favourite-star.png"), new(220, 175, 20, 20));
        canvas.End();
        if (!starLoaded) throw new InvalidOperationException("Native favourite star image could not be drawn.");
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "favourites-native", Guid.NewGuid().ToString()));
                WorkspaceProject.Create(root, BeatmapProject.FromDocuments([new MapDocument { Name = "Favourite card", IsDemo = false }]), "");
                var view = new EditorView(false);
                view.InitializeLibrary(true, new LibrarySettings { Workspace = root }); Wait();
                Click(40, 150); Wait();
                view.PointerDown(320, 190, 2, false, false); Paint();
                Click(340, 210); Wait();
                Click(40, 196); Wait();
                if (view.LibrarySetTotal != 1) throw new InvalidOperationException("Native favourite category did not show its card.");
                view.PointerDown(320, 190, 2, false, false); Paint();
                Click(340, 210); Wait();
                if (view.LibrarySetTotal != 0 || view.IsDirty) throw new InvalidOperationException("Native favourite removal did not clear the list without content edits.");
                view.CloseLibrary();
                AppLog.Write($"Library favourites native check passed: {locale}, {width}x{height}, context action, star rendering and category removal.");

                void Paint() { canvas.Begin(); view.Render(canvas, width, height); canvas.End(); }
                void Click(float x, float y)
                {
                    view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint();
                }
                void Wait()
                {
                    for (int i = 0; i < 400; i++)
                    {
                        Paint();
                        if (i > 80 && !view.LibraryLoading) return;
                        Thread.Sleep(10);
                    }
                    throw new InvalidOperationException("Native favourite library did not settle.");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
