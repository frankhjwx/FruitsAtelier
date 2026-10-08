using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private bool screenshotBusy;

    private async void CopyScreenshot()
    {
        if (screenshotBusy) return;
        screenshotBusy = true;
        try
        {
            if (Clipboard is null) throw new InvalidOperationException();
            double scale = RenderScaling;
            using var bitmap = new RenderTargetBitmap(new PixelSize(
                (int)Math.Ceiling(editor.Bounds.Width * scale), (int)Math.Ceiling(editor.Bounds.Height * scale)),
                new Vector(96 * scale, 96 * scale));
            bitmap.Render(editor);
            using var png = new MemoryStream();
            bitmap.Save(png);
            await Clipboard.SetValueAsync(DataFormat.CreateBytesPlatformFormat("public.png"), png.ToArray());
            View.SetNotice(L.Get("screenshot.copied"));
        }
        catch (Exception error) { View.SetNotice(L.Get("screenshot.failed", error.Message)); }
        finally { screenshotBusy = false; editor.Refresh(); }
    }
}
