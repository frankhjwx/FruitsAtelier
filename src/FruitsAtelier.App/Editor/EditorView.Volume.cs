using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<float, float>? RequestAudioVolume { get; set; }
    public Action? RequestAudioPreference { get; set; }
    private int volumeDrag = -1;
    public Rect VolumeSliderBounds(int channel)
        => new(32 + channel * Math.Min(220, (width - 64) / 3), 535, Math.Min(220, (width - 64) / 3) - 24, 24);

    public void ApplyAudioVolume() => RequestAudioVolume?.Invoke(
        LibrarySettings.MasterVolume * LibrarySettings.SongVolume / 10000f,
        LibrarySettings.MasterVolume * LibrarySettings.HitsoundVolume / 10000f);

    private void DrawVolumeControls(ICanvas c)
    {
        int[] values = [LibrarySettings.MasterVolume, LibrarySettings.SongVolume, LibrarySettings.HitsoundVolume];
        string[] labels = ["volume.all", "volume.song", "volume.hitsound"];
        for (int i = 0; i < 3; i++)
        {
            var rect = VolumeSliderBounds(i);
            c.Text(L.Get(labels[i]) + "  " + L.Get("ui.zoomPercent", values[i]), rect.X, 510, 12, Foreground, rect.Width);
            c.Fill(new(rect.X, rect.Y + 10, rect.Width, 4), Grid, 2);
            c.Fill(new(rect.X, rect.Y + 10, rect.Width * values[i] / 100, 4), Accent, 2);
            c.Circle(rect.X + rect.Width * values[i] / 100, rect.Y + 12, 6, Accent);
        }
    }

    private bool BeginVolumeDrag(float x, float y, int button)
    {
        if (!LibraryVisible || !librarySettingsOpen || updatesPage || button != 0) return false;
        for (int i = 0; i < 3; i++)
            if (VolumeSliderBounds(i).Contains(x, y))
            { volumeDrag = i; libraryField = bindingCapture = -1; UpdateVolumeDrag(x); return true; }
        return false;
    }

    private void UpdateVolumeDrag(float x)
    {
        var rect = VolumeSliderBounds(volumeDrag);
        int value = (int)Math.Round(Math.Clamp((x - rect.X) / rect.Width, 0, 1) * 100);
        if (volumeDrag == 0) LibrarySettings.MasterVolume = value;
        else if (volumeDrag == 1) LibrarySettings.SongVolume = value;
        else LibrarySettings.HitsoundVolume = value;
        ApplyAudioVolume();
    }

    private void FinishVolumeDrag()
    {
        if (volumeDrag < 0) return;
        volumeDrag = -1;
        RequestAudioPreference?.Invoke();
    }
}
