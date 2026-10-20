using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class SliderImportRenderCheck
{
    internal static void Run(D2DCanvas canvas, string folder)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 6000, DerandomizeFSliderDroplets = true };
                var track = new CurveTrack { Kind = CurveKind.Linear, DropletMode = SliderDropletMode.Derandomize };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 400 }]);
                map.Tracks.Add(track);
                var imported = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(map).Text);
                var editor = new EditorView(false);
                editor.LibrarySettings.DerandomizeDroplets = false;
                editor.NewProject(); editor.AddDifficulty(imported);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Wait();
                if (!editor.SliderImportSourcePromptVisible) throw new InvalidOperationException("Missing compensation source prompt.");
                Paint("source");
                float right = (size.Item1 + Math.Min(700, size.Item1 - 40)) / 2f;
                float buttonY = (size.Item2 + 300) / 2f - 40;
                editor.PointerDown(right - 90, buttonY, 0, false, false);
                editor.PointerUp(right - 90, buttonY, 0); Wait();
                if (!editor.Document.DerandomizeFSliderDroplets || !editor.SliderImportPromptVisible)
                    throw new InvalidOperationException("Source Yes did not initialize Derandomize.");
                Paint("conversion");
                float left = (size.Item1 - Math.Min(700, size.Item1 - 40)) / 2f;
                editor.PointerDown(left + 30, (size.Item2 - 300) / 2f + 194, 0, false, false);
                editor.PointerUp(left + 30, (size.Item2 - 300) / 2f + 194, 0); Paint("remember");
                editor.PointerDown(right - 90, buttonY, 0, false, false);
                editor.PointerUp(right - 90, buttonY, 0); Wait();
                if (editor.Document.Tracks.Count != 1) throw new InvalidOperationException("Native conversion button did not preserve the prepared source.");
                if (editor.LibrarySettings.ImportSliderConversion != ImportSliderConversionMode.ConvertToFSlider)
                    throw new InvalidOperationException("Native remember checkbox did not retain the answer.");
                editor.ShowRandomDock(); Paint("settings");
                var native = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:0\n[TimingPoints]\n0,500\n[HitObjects]\n100,192,1000,2,0,P|400:100|250:350,1,400");
                native.SliderImportState = SliderImportState.Pending;
                native.DerandomizeFSliderDroplets = true;
                var initialized = SliderImportEditing.Initialize(native, false);
                if (!initialized.Success) throw new InvalidOperationException(string.Join("; ", initialized.Diagnostics));
                editor.LoadDocument(native);
                editor.UpdateTransport(1000, 6000, true, false, false, null, null);
                Paint("native-path");
                editor.SupportsDisplayMode = editor.SupportsFullscreen = true;
                editor.OpenSettings(); Paint();
                var settings = editor.SettingsBounds;
                editor.Wheel(settings.Right - 60, settings.Y + 200, -12000, false); Paint("map-import-settings");
                var confirm = editor.ConfirmFaImportBounds;
                if (confirm.Y < settings.Y + 116 || confirm.Bottom >= settings.Bottom - 116)
                    throw new InvalidOperationException("Native Map Import settings are outside the scroll viewport.");
                editor.PointerDown(confirm.X + 12, confirm.Y + 12, 0, false, false);
                editor.PointerUp(confirm.X + 12, confirm.Y + 12, 0); Paint("map-import-draft");
                editor.KeyDown(27, false, false);
                if (!editor.LibrarySettings.ConfirmCompensatedImports) throw new InvalidOperationException("Closing Settings applied an unconfirmed import draft.");
                Console.WriteLine($"PASS native map import settings {locale} {size.Item1}x{size.Item2} {dpi} DPI");

                void Wait()
                {
                    var end = DateTime.UtcNow.AddSeconds(30);
                    while (editor.SliderConversionBusy && DateTime.UtcNow < end) { Paint(); Thread.Sleep(5); }
                    Paint();
                    if (editor.SliderConversionBusy) throw new InvalidOperationException("Import validation timed out.");
                }
                void Paint(string? state = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: state is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"import-{locale}-{dpi}-{size.Item1}-{state}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
