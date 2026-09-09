using Avalonia;
using Avalonia.Media.Imaging;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private async Task SliderConversionSmoke(string folder)
    {
        var document = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n160,192,1000,2,0,B|220:250|300:192,1,200\n");
        View.LoadDocument(document); View.CloseLibrary(); View.OfferSliderConversion(false);
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language); editor.Refresh(); await Task.Delay(80); Capture("slider-import-" + language + ".png");
        }
        View.AnswerSliderImport(true); editor.Refresh(); Capture("slider-converting.png");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (View.SliderConversionBusy && DateTime.UtcNow < deadline) { editor.Refresh(); await Task.Delay(20); }
        editor.Refresh();
        if (View.Document.Tracks.Count != 1 || View.Document.ImportedSliders.Count != 0) throw new InvalidOperationException("Batch conversion did not publish a fitted FSlider.");
        View.KeyDown(90, true, false); editor.Refresh();
        if (View.Document.ImportedSliders.Count != 1) throw new InvalidOperationException("Batch conversion undo failed.");
        L.SetLanguage("en"); editor.Refresh(); await Task.Delay(80); View.PointerDown(180, 18, 0, false, false); editor.Refresh(); Capture("slider-edit-menu.png");
        View.PointerDown(180, 18, 0, false, false); // Close the Edit menu.
        View.KeyDown(66, false, false); editor.Refresh();
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language); editor.Refresh(); await Task.Delay(80);
            Capture("anchor-snap-" + language + ".png");
            float optionX = (float)editor.Bounds.Width - (editor.Bounds.Width < 1100 ? 224 : 270) + 24;
            View.PointerDown(optionX, 225, 0, false, false); View.PointerUp(optionX, 225, 0); editor.Refresh();
            if (!View.AnchorSnapEnabled) throw new InvalidOperationException("Anchor snap checkbox did not enable snapping.");
            Capture("anchor-snap-checked-" + language + ".png");
            View.PointerDown(optionX, 225, 0, false, false); View.PointerUp(optionX, 225, 0); editor.Refresh();
            if (View.AnchorSnapEnabled) throw new InvalidOperationException("Anchor snap checkbox did not disable snapping.");
        }
        void Capture(string name)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96));
            bitmap.Render(editor); bitmap.Save(Path.Combine(folder, name));
        }
    }
}
