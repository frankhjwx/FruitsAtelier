using System.Reflection;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class LibraryOrganizationRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/library-organization-native");
        string root = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), Songs = Path.Combine(root, "osu", "Songs") };
        var names = new[] { "A New Beginning", "After the Rain", "Blue Hour", "Beyond the Clouds", "Crystal Garden", "Daylight" };
        var paths = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            string directory = Path.Combine(settings.Songs, names[i]); Directory.CreateDirectory(directory); paths.Add(directory);
            File.WriteAllText(Path.Combine(directory, "map.osu"), $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:{names[i]}\nArtist:Garden Ensemble\nBeatmapID:{(i == 0 ? 123 : 0)}\nBeatmapSetID:456\nCreator:{(i < 2 ? "Alice" : i < 4 ? "Bob" : "Citrus")}\nVersion:Rain\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:");
        }
        WriteOsuCollectionFixture(settings.OsuRoot, paths[0]);
        Directory.CreateDirectory(settings.Workspace);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (1440, 900), (980, 620) })
            {
                L.SetLanguage(locale);
                File.WriteAllText(Path.Combine(settings.Workspace, "library-view.json"), JsonSerializer.Serialize(new
                {
                    Songs = new { Group = LibraryGrouping.Creator },
                    Collections = new[]
                    {
                        new { Id = "practice", Name = "CWC practice", Keys = paths.Take(3).ToArray() },
                        new { Id = "reference", Name = "Mapping references", Keys = paths.Skip(3).ToArray() },
                        new { Id = "edit", Name = "To edit", Keys = paths.Take(1).ToArray() }
                    },
                    Visits = new Dictionary<string, long> { [paths[0]] = DateTime.UtcNow.Ticks, [paths[1]] = DateTime.UtcNow.AddDays(-1).Ticks }
                }));
                var view = new EditorView(false);
                try
                {
                    canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                    view.InitializeLibrary(true, settings); Wait(6); Capture("groups");
                    Invoke("ToggleLibraryGroup", "A"); Wait(6); Capture("expanded");
                    Invoke("ShowLibraryOrderMenu", new Rect(size.Item1 < 1188 ? 412 : size.Item1 - 770, 134, 174, 28), false);
                    Paint(); Capture("sort"); view.KeyDown(27, false, false); Paint();
                    var database = new LibraryDatabase(settings.Workspace, settings.Songs);
                    var map = database.Search("A New Beginning").First();
                    Invoke("ShowCollectionDialog", map); Paint(); Capture("collections"); view.KeyDown(27, false, false); Paint();
                    var imported = OsuCollectionCatalog.Read(settings.OsuRoot).Collections.Single();
                    Invoke("SwitchLibraryCategory", false, false, false, imported.Id); Wait(1);
                    typeof(EditorView).GetField("collectionsScroll", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, 128f);
                    Paint(); Capture("osu-collection");
                    view.PointerDown(40, 244, 0, false, false); view.PointerUp(40, 244, 0); Wait(2); Capture("recent");
                }
                finally { view.CloseLibrary(); }

                void Invoke(string name, params object[] values) => typeof(EditorView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, values);
                void Paint() { canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End(); }
                void Wait(int count)
                {
                    for (int i = 0; i < 1000; i++) { Paint(); if (i > 40 && !view.LibraryLoading && view.LibrarySetTotal == count) return; Thread.Sleep(10); }
                    throw new InvalidOperationException("Library organization native rendering did not settle.");
                }
                void Capture(string name)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
            AppLog.Write("Library organization native rendering passed: groups, sort, collections and recent visits at two sizes and three DPI values in English and Chinese.");
        }
        finally { L.SetLanguage(language); }
    }
    private static void WriteOsuCollectionFixture(string root, string directory)
    {
        string path = Path.Combine(directory, "map.osu");
        File.WriteAllText(Path.Combine(directory, "other.osu"), File.ReadAllText(path).Replace("Version:Rain", "Version:Uncollected difficulty"));
        string hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(path)));
        using (var writer = new BinaryWriter(File.Create(Path.Combine(root, "collection.db"))))
        {
            writer.Write(20260924); writer.Write(1); writer.Write((byte)0x0b); writer.Write("osu! practice");
            writer.Write(1); writer.Write((byte)0x0b); writer.Write(hash);
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(root, "osu!.db"))))
        {
            writer.Write(20260924); Zero(4 + 1 + 8); Write(""); writer.Write(1);
            for (int i = 0; i < 7; i++) Write("");
            Write(hash); Write("map.osu"); Zero(1 + 6 + 8 + 16 + 8);
            for (int i = 0; i < 4; i++) writer.Write(0);
            Zero(12); writer.Write(0); Zero(12 + 4 + 2 + 4 + 1);
            Write(""); Write(""); Zero(2); Write(""); Zero(1 + 8 + 1); Write(Path.GetFileName(directory));
            Zero(8 + 5 + 4 + 1 + 4);
            void Zero(int count) => writer.Write(new byte[count]);
            void Write(string value) { writer.Write((byte)0x0b); writer.Write(value); }
        }
    }

}
