using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class NewDifficultyRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var view = new EditorView(false); view.LoadDocument(DemoMap.Create()); Paint();
                float addX = view.DifficultyTabBounds(0).Right + 16;
                Click(addX, 62); Click(addX, 98);
                if (!view.DiscardConfirmationVisible) throw new InvalidOperationException("Difficulty dialog did not open.");
                Paint("blank");
                float x = (size.Item1 - 660) / 2f, y = (size.Item2 - 552) / 2f;
                Click(x + 32, y + 140); Paint("copy");
                Click(x + 200, y + 240); Paint("difficulty-dropdown");
                view.KeyDown(27, false, false); Paint();
                Click(x + 450, y + 180); Paint("templates-empty");
                Click(x + 60, y + 350); Paint("manager-empty");
                float managerX = (size.Item1 - 860) / 2f;
                Click(managerX + 60, y + 462);
                Click(managerX + 248 + 8 * 36 + 16, y + 414); Paint("manager-current");
                view.KeyDown(13, false, false); Paint("template-saved");
                Click(x + 200, y + 240); Paint("template-dropdown");
                view.KeyDown(27, false, false); Paint();
                Click(x + 32, y + 140); Paint("templates-disabled");
                Click(x + 32, y + 140);
                if (view.LibrarySettings.MapTemplates.Count != 1 || view.IsDirty
                    || view.LibrarySettings.MapTemplates[0].Icon != MapTemplateIcon.Pink)
                    throw new InvalidOperationException("Template save changed the map or failed.");
                view.KeyDown(27, false, false); Paint();
                if (view.DiscardConfirmationVisible || view.DifficultyCount != 1)
                    throw new InvalidOperationException("Cancel did not discard difficulty creation.");
                view.KeyDown('A', true, false); view.KeyDown('C', true, false);
                if (!view.AddDifficulty()) throw new InvalidOperationException("Native clipboard target did not open.");
                view.Document.SliderTickRate = 3;
                view.KeyDown('V', true, false); Paint("cross-difficulty-paste");
                if (view.Document.Fruits.Count == 0 || view.Document.Tracks.Count == 0 || view.Document.SliderTickRate != 3)
                    throw new InvalidOperationException("Native cross-difficulty clipboard shortcuts failed.");
                view.KeyDown('Z', true, false); Paint();
                if (view.Document.Fruits.Count > 0 || view.Document.Tracks.Count > 0)
                    throw new InvalidOperationException("Native cross-difficulty paste did not undo as one operation.");
                string root = Path.GetFullPath("artifacts/tests/new-difficulty-native/library");
                view.InitializeLibrary(true, new LibrarySettings { Workspace = root, FirstRunSetupCompleted = true }); Paint();
                view.OpenMapTemplates(); Paint("library-manager");
                Click(managerX + 60, y + 420); Paint("library-new-template");
                view.KeyDown(27, false, false); Paint();
                if (view.LibrarySettings.MapTemplates.Count != 0)
                    throw new InvalidOperationException("Library template cancellation saved a draft.");
                void Click(float px, float py)
                {
                    view.PointerDown(px, py, 0, false, false); view.PointerUp(px, py, 0); Paint();
                }
                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/new-difficulty-native"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}-{dpi}.bmp")));
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
