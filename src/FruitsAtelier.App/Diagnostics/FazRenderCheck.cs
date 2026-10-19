using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class FazRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/faz-native"); Directory.CreateDirectory(folder);
        string archive = Path.Combine(folder, "fixture.faz");
        var settings = new LibrarySettings { Workspace = Path.Combine(folder, "Workspace"), Songs = Path.Combine(folder, "Songs") };
        Directory.CreateDirectory(settings.Songs);
        string? source = null;
        for (int i = 0; i < 3; i++)
        {
            string song = Path.Combine(settings.Songs, "Local project " + (i + 1)); Directory.CreateDirectory(song);
            string map = Path.Combine(song, "Hard.osu");
            File.WriteAllText(map, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:FA archive fixture\nArtist:Artist\nCreator:Mapper\nVersion:Hard\nBeatmapID:123\nBeatmapSetID:456\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n");
            source ??= map;
        }
        FazArchive.Export(BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source!)]), archive, false, false, settings.Workspace);
        var taskField = typeof(EditorView).GetField("fazTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (var size in new[] { (980, 620), (1440, 900) })
        foreach (int dpi in new[] { 96, 144 })
        {
            L.SetLanguage(locale);
            var view = new EditorView(false); view.InitializeLibrary(true, settings);
            canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
            view.BeginFazOpen(archive, () => { });
            var timeout = Stopwatch.StartNew();
            while (taskField.GetValue(view) is not null && timeout.Elapsed < TimeSpan.FromSeconds(20)) { Paint(); Thread.Sleep(10); }
            if (view.ErrorVisible || !view.DiscardConfirmationVisible) throw new InvalidOperationException("FAZ modal did not open.");
            float x = (size.Item1 - 720) / 2f, y = (size.Item2 - 530) / 2f;
            view.PointerDown(x + 350, y + 100, 0, false, false); view.PointerUp(x + 350, y + 100, 0);
            Paint();
            view.PointerDown(x + 350, y + 200, 0, false, false); view.PointerUp(x + 350, y + 200, 0);
            if (typeof(EditorView).GetField("fazSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view) is null)
                throw new InvalidOperationException("FAZ project card was not selected.");
            Paint(true);
            view.PointerDown(x + 650, y + 360, 0, false, false); view.PointerUp(x + 650, y + 360, 0);
            Paint();
            if ((int)typeof(EditorView).GetField("fazPage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)! != 1)
                throw new InvalidOperationException("FAZ project cards did not page.");
            view.KeyDown(27, false, false);
            if (view.DiscardConfirmationVisible) throw new InvalidOperationException("FAZ modal did not cancel.");
            void Paint(bool capture = false)
            {
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                canvas.End(capture: !capture ? null : () =>
                {
                    var frame = canvas.CapturePixels();
                    using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{locale}-{size.Item1}-{dpi}.bmp")));
                    writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                    writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                });
            }
        }
        AppLog.Write("FAZ native rendering and modal input checks passed.");
    }
}
