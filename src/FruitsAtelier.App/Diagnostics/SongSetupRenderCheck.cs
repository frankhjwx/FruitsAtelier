using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class SongSetupRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/song-setup-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { Name = "Miracle∞Hinacle", IsDemo = false };
                foreach (var pair in new[] { ("ArtistUnicode", "IOSYS"), ("Artist", "IOSYS"), ("TitleUnicode", map.Name),
                    ("Title", "Miracle-Hinacle"), ("Creator", "Yumeno Himiko"), ("Version", "Salad"), ("Tags", "ミラクル∞ヒナクル") })
                    SongSetup.Set(map, "Metadata", pair.Item1, pair.Item2);
                var editor = new EditorView(false);
                editor.LoadProject(BeatmapProject.FromDocuments([map, map.DeepClone()]));
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint(); editor.ShowSongDock(); Paint("general");
                var bounds = editor.SongDockViewport;
                foreach (var field in editor.SongSetupFieldBounds.Values)
                    if (field.Width <= 0 || field.X < bounds.X || field.Right > bounds.Right + .1f)
                        throw new InvalidOperationException("General input exceeds panel width.");
                editor.Wheel(bounds.X + 20, bounds.Y + 80, -12000, false, false); Paint("general-bottom");
                if (editor.IsDirty) throw new InvalidOperationException("Rendering General changed content.");
                void Paint(string? name = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height);
                        output.Write((ushort)1); output.Write((ushort)32); output.Write(0); output.Write(frame.Pixels.Length);
                        output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Song Setup native checks passed.");
    }
}
