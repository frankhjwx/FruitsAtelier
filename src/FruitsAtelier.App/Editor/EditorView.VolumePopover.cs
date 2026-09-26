using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool volumePopoverOpen;
    private double volumePopoverOpenedMs, volumePopoverTouchedMs;
    private int volumePopoverDrag = -1;
    private bool volumeShortcutHeld;
    private int volumeChannel;
    private bool CanUseVolumePopover => !LibraryVisible && !ExportVisible && !updatesPage
        && !ErrorVisible && !DiscardConfirmationVisible && !SliderDialogVisible
        && !SongSetupVisible && !DistanceSnapDialogVisible && !VolumeDialogVisible
        && !TimeJumpVisible && !StreamDialogVisible && !IsEditingText && drag == DragKind.None;

    private double VolumeNowMs => timeProvider.GetTimestamp() * 1000d / timeProvider.TimestampFrequency;
    public Rect VolumeButtonBounds => new(width - 95, height - 27, 88, 26);
    public Rect VolumePopoverBounds => new(Math.Max(8, VolumeButtonBounds.Right - 244),
        Math.Max(42, VolumeButtonBounds.Y - 229), 244, 221);
    public Rect VolumeBarBounds(int channel)
    {
        if (channel is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(channel));
        var popup = VolumePopoverBounds;
        return new(popup.X + 23 + channel * 74, popup.Y + 49, 48, 116);
    }

    public bool VolumePopoverVisible
    {
        get
        {
            AdvanceVolumePopover();
            return volumePopoverOpen;
        }
    }

    public bool VolumePopoverNeedsRedraw => VolumePopoverVisible && volumePopoverDrag < 0
        && !volumeShortcutHeld && !VolumePopoverBounds.Contains(mouseX, mouseY)
        && (IsTestplaying || !VolumeButtonBounds.Contains(mouseX, mouseY))
        || volumePopoverOpen && VolumePopoverOpacity < 1;

    private float VolumePopoverOpacity
    {
        get
        {
            double now = VolumeNowMs;
            double fadeIn = Math.Clamp((now - volumePopoverOpenedMs) / 120, 0, 1);
            double fadeOutStart = Math.Max(volumePopoverOpenedMs + 120, volumePopoverTouchedMs + 800);
            double fadeOut = Math.Clamp((now - fadeOutStart) / 150, 0, 1);
            return (float)(fadeIn * (1 - fadeOut));
        }
    }

    private void AdvanceVolumePopover()
    {
        if (!volumePopoverOpen) return;
        if (!CanUseVolumePopover) { CloseVolumePopover(); return; }
        if (volumePopoverDrag >= 0 || volumeShortcutHeld
            || VolumePopoverBounds.Contains(mouseX, mouseY)
            || !IsTestplaying && VolumeButtonBounds.Contains(mouseX, mouseY))
            volumePopoverTouchedMs = VolumeNowMs;
        if (VolumePopoverOpacity <= 0 && VolumeNowMs - volumePopoverOpenedMs >= 120)
            volumePopoverOpen = false;
    }

    internal void OpenVolumePopover()
    {
        if (!CanUseVolumePopover) return;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        volumePopoverOpen = true;
        volumePopoverOpenedMs = volumePopoverTouchedMs = VolumeNowMs;
    }

    private void CloseVolumePopover()
    {
        EndVolumePopoverPointer(mouseX, mouseY, 0);
        volumePopoverOpen = false;
        volumeShortcutHeld = false;
    }

    public bool BeginVolumePopoverPointer(float x, float y, int button)
    {
        if (button != 0 || !CanUseVolumePopover || IsTestplaying) return false;
        if (VolumeButtonBounds.Contains(x, y))
        {
            if (volumePopoverOpen) CloseVolumePopover(); else OpenVolumePopover();
            return true;
        }
        if (!VolumePopoverVisible) return false;
        if (!VolumePopoverBounds.Contains(x, y))
        {
            CloseVolumePopover();
            return false;
        }
        volumePopoverTouchedMs = VolumeNowMs;
        for (int channel = 0; channel < 3; channel++)
        {
            if (!VolumeBarBounds(channel).Contains(x, y)) continue;
            volumeChannel = volumePopoverDrag = channel;
            UpdateVolumePopoverDrag(y);
            break;
        }
        return true;
    }

    public void MoveVolumePopoverPointer(float x, float y)
    {
        mouseX = x; mouseY = y;
        if (volumePopoverDrag >= 0)
        {
            UpdateVolumePopoverDrag(y);
        }
        else if (volumePopoverOpen && (VolumePopoverBounds.Contains(x, y) || VolumeButtonBounds.Contains(x, y)))
        {
            volumePopoverTouchedMs = VolumeNowMs;
            SelectHoveredVolumeChannel(x, y);
        }
    }

    private void SelectHoveredVolumeChannel(float x, float y)
    {
        if (volumePopoverDrag >= 0) return;
        for (int channel = 0; channel < 3; channel++)
            if (VolumeBarBounds(channel).Contains(x, y))
            {
                volumeChannel = channel;
                return;
            }
    }

    private bool HandleVolumePopoverWheel(float x, float y, float delta)
    {
        if (!CanUseVolumePopover || !VolumePopoverVisible || !VolumePopoverBounds.Contains(x, y)) return false;
        mouseX = x; mouseY = y;
        SelectHoveredVolumeChannel(x, y);
        AdjustVolumeWheel(delta);
        return true;
    }

    public bool EndVolumePopoverPointer(float x, float y, int button)
    {
        if (volumePopoverDrag < 0 || button != 0) return false;
        mouseX = x; mouseY = y;
        UpdateVolumePopoverDrag(y);
        volumePopoverDrag = -1;
        RequestAudioPreference?.Invoke();
        volumePopoverTouchedMs = VolumeNowMs;
        return true;
    }

    private void UpdateVolumePopoverDrag(float y)
    {
        var bar = VolumeBarBounds(volumePopoverDrag);
        SetVolumeChannel(volumePopoverDrag, (int)Math.Round(Math.Clamp((bar.Bottom - y) / bar.Height, 0, 1) * 100));
        volumePopoverTouchedMs = VolumeNowMs;
    }

    public bool AdjustVolumeShortcut(int key, bool alt)
    {
        if (!alt || !CanUseVolumePopover || key is not (37 or 38 or 39 or 40)) return false;
        if (!volumePopoverOpen) OpenVolumePopover();
        if (!volumePopoverOpen) return false;
        volumeShortcutHeld = true;
        if (key == 37) volumeChannel = Math.Max(0, volumeChannel - 1);
        else if (key == 39) volumeChannel = Math.Min(2, volumeChannel + 1);
        else
        {
            int value = VolumeChannelValue(volumeChannel) + (key == 38 ? 5 : -5);
            SetVolumeChannel(volumeChannel, Math.Clamp(value, 0, 100));
            RequestAudioPreference?.Invoke();
        }
        volumePopoverTouchedMs = VolumeNowMs;
        return true;
    }

    private void AdjustVolumeWheel(float delta)
    {
        if (delta == 0 || !CanUseVolumePopover) return;
        if (!volumePopoverOpen) OpenVolumePopover();
        int value = VolumeChannelValue(volumeChannel) + (delta > 0 ? 5 : -5);
        SetVolumeChannel(volumeChannel, Math.Clamp(value, 0, 100));
        RequestAudioPreference?.Invoke();
        volumePopoverTouchedMs = VolumeNowMs;
    }

    public void ReleaseVolumeShortcut(int key)
    {
        if (key is not (37 or 38 or 39 or 40)) return;
        volumeShortcutHeld = false;
        volumePopoverTouchedMs = VolumeNowMs;
    }

    private int VolumeChannelValue(int channel) => channel switch
    {
        0 => LibrarySettings.MasterVolume,
        1 => LibrarySettings.SongVolume,
        _ => LibrarySettings.HitsoundVolume
    };

    private void SetVolumeChannel(int channel, int value)
    {
        if (VolumeChannelValue(channel) == value) return;
        if (channel == 0) LibrarySettings.MasterVolume = value;
        else if (channel == 1) LibrarySettings.SongVolume = value;
        else LibrarySettings.HitsoundVolume = value;
        ApplyAudioVolume();
    }

    private void DrawVolumeButton(ICanvas c)
    {
        var button = VolumeButtonBounds;
        c.Fill(button, button.Contains(mouseX, mouseY) || volumePopoverOpen ? Surface : Panel, 4);
        c.Text(L.Get("volume.title"), button.X + 8, button.Y + 5, 11, Foreground, button.Width - 16);
    }

    private void DrawVolumePopover(ICanvas c)
    {
        AdvanceVolumePopover();
        if (!volumePopoverOpen) return;
        float opacity = VolumePopoverOpacity;
        var popup = VolumePopoverBounds;
        c.Fill(new(popup.X + 3, popup.Y + 4, popup.Width, popup.Height), 0x11151B, 7, opacity);
        c.Fill(popup, Panel, 7, opacity);
        c.TextOpacity(L.Get("volume.title"), popup.X + 14, popup.Y + 13, 13, Foreground, popup.Width - 28, true, opacity);
        string[] labels = ["volume.master", "volume.music", "volume.effect"];
        for (int channel = 0; channel < 3; channel++)
        {
            var bar = VolumeBarBounds(channel);
            int value = VolumeChannelValue(channel);
            c.Fill(bar, Surface, 5, opacity);
            float fill = bar.Height * value / 100;
            c.Fill(new(bar.X, bar.Bottom - fill, bar.Width, fill), Accent, 5, opacity);
            bool active = channel == volumeChannel;
            if (active) c.StrokeOpacity(bar, Foreground, 3, 5, opacity);
            c.TextOpacity(L.Get(labels[channel]), bar.X - 10, bar.Bottom + 8, 11,
                active ? Accent : Foreground, bar.Width + 20, active, opacity);
            c.TextOpacity(L.Get("ui.zoomPercent", value), bar.X + 2, bar.Bottom + 27, 11,
                active ? Foreground : Muted, bar.Width + 14, active, opacity);
        }
    }
}
