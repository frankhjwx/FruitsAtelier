using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<float, float>? RequestAudioVolume { get; set; }
    public Action? RequestAudioPreference { get; set; }
    private int volumeDrag = -1;
    public bool VolumeDialogVisible { get; private set; }
    private Rect VolumeDialogBounds => new((width - Math.Min(540, width - 32)) / 2, (height - 260) / 2, Math.Min(540, width - 32), 260);
    public Rect VolumeSliderBounds(int channel)
        => new(VolumeDialogBounds.X + 24, VolumeDialogBounds.Y + 84 + channel * 64, VolumeDialogBounds.Width - 48, 24);

    internal void OpenVolumeDialog()
    {
        if (LibraryVisible || IsTestplaying || !PrepareFileOperation()) return;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        VolumeDialogVisible = true;
        hits.Clear(); fields.Clear();
    }

    private void CloseVolumeDialog()
    {
        FinishVolumeDrag();
        VolumeDialogVisible = false;
        hits.Clear();
    }

    private void DrawVolumeDialog(ICanvas c)
    {
        if (!VolumeDialogVisible) return;
        hits.Clear(); fields.Clear();
        var r = VolumeDialogBounds;
        c.Fill(new(r.X + 4, r.Y + 5, r.Width, r.Height), 0x11151B, 8);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("volume.title"), r.X + 24, r.Y + 17, 16, Foreground, r.Width - 130, true);
        Button(c, new(r.Right - 92, r.Y + 10, 76, 30), L.Get("ui.close"), CloseVolumeDialog);
        DrawVolumeControls(c);
    }

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
            c.Text(L.Get(labels[i]) + "  " + L.Get("ui.zoomPercent", values[i]), rect.X, rect.Y - 26, 12, Foreground, rect.Width);
            c.Fill(new(rect.X, rect.Y + 10, rect.Width, 4), Grid, 2);
            c.Fill(new(rect.X, rect.Y + 10, rect.Width * values[i] / 100, 4), Accent, 2);
            c.Circle(rect.X + rect.Width * values[i] / 100, rect.Y + 12, 6, Accent);
        }
    }

    private bool BeginVolumeDrag(float x, float y, int button)
    {
        if (!VolumeDialogVisible || button != 0) return false;
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
