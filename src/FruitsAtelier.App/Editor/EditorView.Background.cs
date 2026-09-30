using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? beatmapBackground;
    private float backgroundDimFrom, backgroundDimTarget;
    private double backgroundDimChangedAt;
    private Rect backgroundDimSliderBounds;
    private bool backgroundDimDragging;
    private int backgroundDimDragStart;

    private bool BeginBackgroundDimDrag(float x, float y, int button)
    {
        if (!TestplayPauseMenuVisible || button != 0 || backgroundDimSliderBounds.Width <= 0 || !backgroundDimSliderBounds.Contains(x, y)) return false;
        backgroundDimDragging = true;
        backgroundDimDragStart = LibrarySettings.BackgroundDim;
        UpdateBackgroundDimDrag(x);
        return true;
    }

    private void UpdateBackgroundDimDrag(float x)
        => LibrarySettings.BackgroundDim = (int)Math.Round(Math.Clamp(
            (x - backgroundDimSliderBounds.X) / backgroundDimSliderBounds.Width, 0, 1) * 100);

    private void FinishBackgroundDimDrag()
    {
        if (!backgroundDimDragging) return;
        backgroundDimDragging = false;
        if (LibrarySettings.BackgroundDim != backgroundDimDragStart) RequestViewPreference?.Invoke();
    }

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

    private void DrawBackgroundDimSetting(ICanvas c, Rect bounds, bool draft, float opacity = 1)
    {
        int value = draft ? draftBackgroundDim : LibrarySettings.BackgroundDim;
        if (draft) c.Fill(bounds, Surface, 4, opacity);
        else
        {
            backgroundDimSliderBounds = new(bounds.X + 36, bounds.Y, bounds.Width - 72, bounds.Height);
            var slider = backgroundDimSliderBounds;
            c.Fill(new(slider.X, slider.Y, slider.Width * value / 100f, slider.Height), 0xFFFFFF, 4, .18f * opacity);
            c.StrokeOpacity(slider, 0xFFFFFF, 1, 4, .65f * opacity);
        }
        string label = L.Get("settings.backgroundDim", value);
        float labelWidth = Math.Min(c.MeasureText(label, 14), bounds.Width - 72);
        c.TextOpacity(label, draft ? bounds.X + 36 : bounds.X + (bounds.Width - labelWidth) / 2,
            bounds.Y + 11, 14, Foreground, draft ? bounds.Width - 72 : labelWidth, false, opacity);
        Control(new(bounds.X, bounds.Y, 30, bounds.Height), "−", -5, value > 0);
        Control(new(bounds.Right - 30, bounds.Y, 30, bounds.Height), "+", 5, value < 100);
        void Control(Rect r, string text, int delta, bool enabled)
        {
            if (draft && opacity >= 1) { Button(c, r, text, () => Change(delta), enabled: enabled); return; }
            if (enabled && r.Contains(mouseX, mouseY))
            {
                c.Fill(r, 0x3D495A, 4, opacity);
            }
            c.StrokeOpacity(r, draft ? 0x71849Au : 0xFFFFFFu, 1, 4, opacity * (enabled ? 1 : .35f));
            c.TextOpacity(text, r.X + 9, r.Y + (r.Height - 16) / 2, 12, enabled ? Foreground : 0x5B6777u, r.Width - 15, false, opacity);
            hits.Add(new(r, () => Change(delta), enabled));
        }
        void Change(int delta)
        {
            if (draft) draftBackgroundDim = Math.Clamp(draftBackgroundDim + delta, 0, 100);
            else { LibrarySettings.BackgroundDim += delta; RequestViewPreference?.Invoke(); }
        }
    }
}
