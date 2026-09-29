using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? beatmapBackground;
    private float backgroundDimFrom, backgroundDimTarget;
    private double backgroundDimChangedAt;

    private void RefreshBeatmapBackground()
    {
        beatmapBackground = null;
        try
        {
            if (OsuTimeline.BackgroundFilename(Document) is not { } name) return;
            string? origin = Document.SourcePath ?? Document.AudioPath;
            if (origin is not null) beatmapBackground = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(origin))!, name));
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or NotSupportedException) { }
    }

    // osu! UserDimContainer/BreakTracker, 48c4800e (MIT, Core/Gameplay/LICENSE.osu.txt).
    private bool InTestplayBreak => playhead < testplayGameplayStart ||
        testplayFrame?.JudgedCount == previewObjects.Count ||
        breakPeriods.Any(b => b.EndMs - b.StartMs >= 650 && playhead >= b.StartMs && playhead <= b.EndMs - 325);

    private float CurrentBackgroundDim
    {
        get
        {
            float t = (float)Math.Clamp((TestplayRealtime - backgroundDimChangedAt) / 800, 0, 1);
            return backgroundDimFrom + (backgroundDimTarget - backgroundDimFrom) * (1 - MathF.Pow(1 - t, 5));
        }
    }

    private void UpdateBackgroundDim()
    {
        float target = Math.Max(0, LibrarySettings.BackgroundDim / 100f - (InTestplayBreak ? .3f : 0));
        if (target == backgroundDimTarget) return;
        backgroundDimFrom = CurrentBackgroundDim;
        backgroundDimTarget = target;
        backgroundDimChangedAt = TestplayRealtime;
    }

    private void DrawBeatmapBackground(ICanvas c, Rect bounds, float dim)
    {
        if (beatmapBackground is not null && c.BackgroundImage(beatmapBackground, bounds))
            c.Fill(bounds, 0, opacity: dim);
    }

    private void DrawBackgroundDimSetting(ICanvas c, Rect bounds, bool draft)
    {
        int value = draft ? draftBackgroundDim : LibrarySettings.BackgroundDim;
        c.Fill(bounds, Surface, 4);
        string label = L.Get("settings.backgroundDim", value);
        c.Text(label, bounds.X + 36, bounds.Y + 11, 14, Foreground, bounds.Width - 72);
        Button(c, new(bounds.X, bounds.Y, 30, bounds.Height), "−", () => Change(-5), enabled: value > 0);
        Button(c, new(bounds.Right - 30, bounds.Y, 30, bounds.Height), "+", () => Change(5), enabled: value < 100);
        void Change(int delta)
        {
            if (draft) draftBackgroundDim = Math.Clamp(draftBackgroundDim + delta, 0, 100);
            else { LibrarySettings.BackgroundDim += delta; RequestViewPreference?.Invoke(); }
        }
    }
}
