using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class FadRenderCheck
{
    public static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/fad-native"); Directory.CreateDirectory(folder);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            foreach (float dpi in new[] { 96f, 144f, 192f })
            {
                L.SetLanguage(locale);
                var local = new MapDocument { Name = "FAD collaboration", IsDemo = false, DurationMs = 5000 };
                SongSetup.Set(local, "Metadata", "Version", "Guest"); SongSetup.Set(local, "Metadata", "Artist", "Mapper");
                local.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
                local.Fruits.Add(new() { TimeMs = 1000, X = 100 });
                var difficulty = new ProjectDifficulty { Name = "Guest", Document = local };
                var view = new EditorView(false);
                view.LibrarySettings.Workspace = Path.Combine(folder, "workspace"); view.LibrarySettings.Songs = "";
                view.LoadProject(new() { Name = local.Name, Difficulties = [difficulty] });
                view.CloseLibrary();
                canvas.Resize((int)(size.Item1 * dpi / 96), (int)(size.Item2 * dpi / 96), dpi);
                Paint();
                Field("menu").SetValue(view, 0); Paint("file-menu");
                Field("menu").SetValue(view, -1);
                view.PointerDown(100, 54, 2, false, false); view.PointerUp(100, 54, 2); Paint("difficulty-menu");
                view.KeyDown(27, false, false);
                view.ShowFazExportChoice(); Paint("faz-choice"); view.KeyDown(27, false, false);
                var incoming = local.DeepClone(); incoming.Fruits[0].X = 400;
                SongSetup.Set(incoming, "Metadata", "Creator", "Guest mapper");
                var merge = WorkspaceSynchronization.CompareNativeDifficulty(local, incoming, Path.Combine(folder, "guest.fad"), false);
                var comparison = typeof(EditorView).GetMethod("PrepareSyncComparison", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [merge, false, null]);
                ((Dictionary<Guid, WorkspaceMerge>)Field("syncMerges").GetValue(view)!)[difficulty.Id] = merge;
                var comparisons = (System.Collections.IDictionary)Field("syncComparisons").GetValue(view)!;
                comparisons[difficulty.Id] = comparison;
                Field("fadPending").SetValue(view, (new ProjectDifficulty { Id = difficulty.Id, Name = "Guest", Document = incoming }, HitsoundResourcePlan.Empty));
                typeof(EditorView).GetMethod("BeginSyncReview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [0]);
                Paint("merge-fields"); Field("syncTab").SetValue(view, "Objects"); Paint("merge-objects");
                view.KeyDown(27, false, false); view.MarkSaved(); view.ShowLibrary();
                view.BeginFadImport(Path.Combine(folder, "guest.fad")); Paint("library-error");
                view.StopFileMonitoring();

                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{locale}-{size.Item1}-{dpi}-{name}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        Console.WriteLine("PASS native FAD menus, FAZ export choices, merge review and library errors");
    }
    private static FieldInfo Field(string name) => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
}
