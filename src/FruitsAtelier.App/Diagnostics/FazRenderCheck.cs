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
        FazArchive.Export(BeatmapProject.FromDocuments([new MapDocument { Name = "FA archive fixture", IsDemo = false }]), archive, false, false, settings.Workspace);
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
            Paint(true);
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
