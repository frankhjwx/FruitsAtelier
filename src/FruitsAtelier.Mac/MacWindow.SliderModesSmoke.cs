using Avalonia;
using Avalonia.Media.Imaging;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private async Task SliderModesSmoke(string folder)
    {
        Width = 1280; Height = 900;
        await Task.Delay(150);
        View.LoadDocument(new MapDocument { DurationMs = 10000, ApproachRate = 8 });
        View.CloseLibrary(); View.SetSliderEditingMode(SliderEditingMode.OsuLegacy);
        View.KeyDown(66, false, false);
        editor.Refresh(); await Task.Delay(100); Capture("slider-modes-empty.png");
        Click(125, 200); Click(250, 240);
        var end = Screen(375, 200);
        View.PointerMove(end.X, end.Y, false, false);
        View.PointerDown(end.X, end.Y, 2, false, false); View.PointerUp(end.X, end.Y, 2);
        View.KeyDown(66, false, false);
        if (View.Document.Tracks.Count != 1 || View.Document.Tracks[0].Nodes[0].OutgoingCurve?.Kind != ControlCurveKind.CircularArc)
            throw new InvalidOperationException("Native legacy drafting did not produce a circular arc.");
        var saved = View.Document.DeepClone();
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            foreach (var mode in Enum.GetValues<SliderEditingMode>())
            {
                View.SetSliderEditingMode(mode); editor.Refresh(); await Task.Delay(100);
                Capture($"slider-{mode}-{language}.png");
                if (!saved.ContentEquals(View.Document)) throw new InvalidOperationException("Mode or language switching changed a slider.");
            }
        }
        View.SetSliderEditingMode(SliderEditingMode.OsuLegacy);
        Click(250, 240);
        Width = 980; Height = 620;
        await Task.Delay(150); editor.Refresh(); await Task.Delay(80);
        Capture("slider-modes-narrow.png");
        View.Wheel((float)editor.Bounds.Width - 20, 400, -240, false);
        editor.Refresh(); await Task.Delay(80); Capture("slider-modes-narrow-scrolled.png");
        ProjectSerializer.WriteFile(View.Document, Path.Combine(folder, "slider-modes.catchproj"));
        var restored = ProjectSerializer.ReadFile(Path.Combine(folder, "slider-modes.catchproj"));
        if (!View.Document.ContentEquals(restored)) throw new InvalidOperationException("Native shared slider round-trip failed.");

        (float X, float Y) Screen(double time, double x) =>
            (View.PlayfieldBounds.X + (float)(x / 512) * View.PlayfieldBounds.Width,
                View.CanvasPlotBounds.Bottom - (float)((time - View.ViewStartMs) * View.PixelsPerMs));
        void Click(double time, double x)
        {
            var p = Screen(time, x);
            View.PointerDown(p.X, p.Y, 0, false, false); View.PointerUp(p.X, p.Y, 0);
            editor.Refresh();
        }
        void Capture(string name)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96));
            bitmap.Render(editor); bitmap.Save(Path.Combine(folder, name));
        }
    }
}
