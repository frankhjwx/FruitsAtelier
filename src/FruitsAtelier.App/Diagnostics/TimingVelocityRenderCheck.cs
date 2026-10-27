using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class TimingVelocityRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height, int dpi)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n1000,-50,4,2,2,35,0,1\n2000,-25,4,2,2,35,0,1\n[HitObjects]\n100,192,1000,2,0,L|240:192,1,140\n100,192,2000,2,0,L|240:192,1,140\n");
                WorkspaceTimingSynchronization.NormalizeDocument(map);
                var view = new EditorView(false); view.LoadDocument(map); view.CloseLibrary();
                view.KeyDown(114, false, false); Paint("hidden");
                Click(view.TimingSvDisplayBounds); Paint("visible-locked");
                if (!view.ShowTimingSv || !view.TimingSvLocked || view.IsDirty) throw new InvalidOperationException("Display toggle changed timing content or lock.");
                view.UpdateTransport(1000, 10000, true, false, false, null, null);
                view.OpenTimingSetup(); Paint("compound-locked");
                var originalWindow = view.TimingSetupBounds;
                view.PointerDown(originalWindow.Right - 2, originalWindow.Bottom - 2, 0, false, false);
                view.PointerMove(originalWindow.X, originalWindow.Y, false, false); Paint();
                view.PointerUp(originalWindow.X, originalWindow.Y, 0); Paint("half-size");
                var halfWindow = view.TimingSetupBounds;
                if (Math.Abs(halfWindow.Width - originalWindow.Width * .5f) > .01f
                    || Math.Abs(halfWindow.Height - originalWindow.Height * .5f) > .01f)
                    throw new InvalidOperationException("Timing corner did not stop at 50% scale.");
                view.PointerDown(halfWindow.X + 20, halfWindow.Y + 10, 0, false, false);
                view.PointerMove(halfWindow.X + 50, halfWindow.Y + 30, false, false); Paint();
                view.PointerUp(halfWindow.X + 50, halfWindow.Y + 30, 0); Paint("half-size-moved");
                if (view.TimingFields.Any(f => f.Key == "sv")) throw new InvalidOperationException("Locked SV accepts input.");
                Click(view.TimingSvLockBounds); Paint("compound-unlocked");
                if (!view.TimingFields.Any(f => f.Key == "sv")) throw new InvalidOperationException("Unlocked SV input missing.");
                view.KeyDown(27, false, false);
                view.UpdateTransport(2000, 10000, true, false, false, null, null);
                view.OpenTimingSetup(); Paint(); Click(view.TimingSvLockBounds); Paint("pure-locked");
                if (view.TimingFields.Count != 0 || view.IsDirty) throw new InvalidOperationException("Pure SV is editable while locked.");
                view.KeyDown(27, false, false);
                view.NewProject();

                void Click(Rect bounds)
                {
                    view.PointerDown(bounds.X + 10, bounds.Y + 10, 0, false, false);
                    view.PointerUp(bounds.X + 10, bounds.Y + 10, 0);
                }
                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/timing-sv-native"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{width}x{height}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
