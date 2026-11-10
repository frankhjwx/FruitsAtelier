using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private double[] previewComboTimes = [];
    private float flashlightScale = 1, previewFlashlightScale = 1;
    private double flashlightTime = double.NaN, previewFlashlightTime = double.NaN;
    private int PreviewComboAt(double time)
    {
        int low = 0, high = previewComboTimes.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (previewComboTimes[middle] <= time) low = middle + 1; else high = middle;
        }
        return low;
    }

    // ppy/osu 48c4800e CatchModFlashlight; size and transitions match osu!stable.
    // MIT license retained in Core/Gameplay/LICENSE.osu.txt.
    private void DrawCatchFlashlight(ICanvas c, Rect bounds, float x, float y, float fieldWidth, int combo, double time, bool live)
    {
        bool inBreak = live ? InTestplayBreak : previewObjects.Count == 0 || time < previewObjects[0].TimeMs - 2000
            || time > previewObjects[^1].TimeMs || breakPeriods.Any(b => time >= b.StartMs && time <= b.EndMs);
        float target = inBreak ? 1.538f : combo >= 200 ? .770f : combo >= 100 ? .885f : 1;
        ref float scale = ref (live ? ref flashlightScale : ref previewFlashlightScale);
        ref double previous = ref (live ? ref flashlightTime : ref previewFlashlightTime);
        if (!double.IsFinite(previous) || time < previous || time - previous > 1000) scale = target;
        else scale += Math.Clamp(target - scale, -(float)(time - previous) * .001154f, (float)(time - previous) * .001154f);
        previous = time;
        c.Flashlight(bounds, x, y, 203.125f * fieldWidth / 512 * scale);
    }

    private void DrawTestplayModIcons(ICanvas c, Rect stage)
    {
        float scale = stage.Height / 768;
        float x = width - 16 * scale;
        foreach (var choice in TestplayModChoices.Reverse())
        {
            if (!LibrarySettings.TestplayMods.HasFlag(choice.Mod)) continue;
            if (skin?.MenuTexture("selection-mod-" + choice.Texture) is not { } texture) continue;
            float w = texture.PixelWidth / (float)texture.Density * scale;
            float h = texture.PixelHeight / (float)texture.Density * scale;
            c.Image(texture.FilePath, new(x - w, stage.Y + 120 * stage.Height / 768, w, h));
            x -= 65 * scale;
        }
    }
}
