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
    private bool backgroundDimDragging, backgroundDimSliderDraft, backgroundDimDragDraft;
    private int backgroundDimDragStart;
    private bool backgroundDimMappingCanvas;

    private bool BeginBackgroundDimDrag(float x, float y, int button)
    {
        if (librarySettingsOpen && settingsColourIndex >= 0) return false;
        bool visible = backgroundDimSliderDraft
            ? librarySettingsOpen && settingsCategory == (backgroundDimMappingCanvas ? SettingsCategory.MappingCanvas : SettingsCategory.Testplay) : TestplayPauseMenuVisible;
        if ((FirstRunSetupVisible || librarySettingsOpen && settingsCategory == SettingsCategory.MappingCanvas) && !workspaceScrollBounds.Contains(x, y)) return false;
        if (!visible || button != 0 || backgroundDimSliderBounds.Width <= 0 || !backgroundDimSliderBounds.Contains(x, y)) return false;
        backgroundDimDragging = true;
        backgroundDimDragDraft = backgroundDimSliderDraft;
        backgroundDimDragStart = LibrarySettings.BackgroundDim;
        UpdateBackgroundDimDrag(x);
        return true;
    }

    private void UpdateBackgroundDimDrag(float x)
    {
        int value = (int)Math.Round(Math.Clamp((x - backgroundDimSliderBounds.X) / backgroundDimSliderBounds.Width, 0, 1) * 100);
        if (backgroundDimDragDraft && backgroundDimMappingCanvas) draftMappingCanvasBackgroundDim = value;
        else if (backgroundDimDragDraft) draftBackgroundDim = value;
        else LibrarySettings.BackgroundDim = value;
    }

    private void FinishBackgroundDimDrag()
    {
        if (!backgroundDimDragging) return;
        backgroundDimDragging = false;
        if (!backgroundDimDragDraft && LibrarySettings.BackgroundDim != backgroundDimDragStart) RequestViewPreference?.Invoke();
    }

    private void RefreshBeatmapBackground()
    {
        beatmapBackground = null;
        try
        {
            if (OsuTimeline.BackgroundFilename(Document) is not { } name) return;
            string? origin = Document.SourcePath ?? Document.AudioPath;
            string? directory = origin is null ? WorkspaceSession?.Directory : Path.GetDirectoryName(Path.GetFullPath(origin));
            if (directory is not null) beatmapBackground = Path.GetFullPath(Path.Combine(directory, name));
        }
        catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or NotSupportedException) { }
    }

    private void DropBackgroundImage(string path)
    {
        if (OsuTimeline.BackgroundFilename(Document) is null) { ImportBackgroundImage(path); return; }
        ShowDiscardConfirmation(answer => { if (answer == 7) ImportBackgroundImage(path); });
        replaceBackgroundConfirmation = true;
    }

    private void ImportBackgroundImage(string path)
    {
        if (Document.SourcePath is null && Document.AudioPath is null && WorkspaceSession is null)
        {
            BeginWorkspaceSave(saved => { if (saved) ImportBackgroundImage(path); });
            return;
        }
        try
        {
            string? origin = Document.SourcePath ?? Document.AudioPath;
            string directory = origin is null ? WorkspaceSession!.Directory : Path.GetDirectoryName(Path.GetFullPath(origin))!;
            string name = Path.GetFileName(path);
            string target = Path.Combine(directory, name);
            if (!string.Equals(Path.GetFullPath(path), target, StringComparison.OrdinalIgnoreCase))
            {
                string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
                for (int suffix = 1; File.Exists(target); suffix++)
                    target = Path.Combine(directory, $"{stem} ({suffix}){extension}");
                File.Copy(path, target, overwrite: false);
            }
            name = Path.GetFileName(target);
            Edit(L.Get("background.replace"), () => OsuTimeline.SetBackgroundFilename(Document, name));
            RefreshBeatmapBackground();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { ShowError(e.Message); }
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

    private void DrawBeatmapBackground(ICanvas c, Rect bounds, float dim, bool mappingCanvas = false)
    {
        if (!mappingCanvas && LibrarySettings.ForceBackgroundDim) { c.Fill(bounds, 0); return; }
        if (beatmapBackground is not null && c.BackgroundImage(beatmapBackground, bounds))
            c.Fill(bounds, mappingCanvas ? Background : 0, opacity: dim);
    }

    private void DrawBackgroundDimSetting(ICanvas c, Rect bounds, bool draft, float opacity = 1, bool mappingCanvas = false)
    {
        int value = mappingCanvas ? draftMappingCanvasBackgroundDim : draft ? draftBackgroundDim : LibrarySettings.BackgroundDim;
        backgroundDimMappingCanvas = mappingCanvas;
        if (draft) c.Fill(bounds, Surface, 4, opacity);
        backgroundDimSliderDraft = draft;
        backgroundDimSliderBounds = new(bounds.X + 36, bounds.Y, bounds.Width - 72, bounds.Height);
        var slider = backgroundDimSliderBounds;
        c.Fill(new(slider.X, slider.Y, slider.Width * value / 100f, slider.Height), 0xFFFFFF, 4, .18f * opacity);
        c.StrokeOpacity(slider, 0xFFFFFF, 1, 4, .65f * opacity);
        string label = L.Get("settings.backgroundDim", value);
        float textSize = draft ? SettingsTextSize : 14;
        float labelWidth = Math.Min(c.MeasureText(label, textSize), bounds.Width - 72);
        c.TextOpacity(label, bounds.X + (bounds.Width - labelWidth) / 2,
            bounds.Y + (bounds.Height - textSize - 4) / 2, textSize, Foreground, labelWidth, false, opacity);
        Control(new(bounds.X, bounds.Y, 30, bounds.Height), "−", -5, value > 0);
        Control(new(bounds.Right - 30, bounds.Y, 30, bounds.Height), "+", 5, value < 100);
        void Control(Rect r, string text, int delta, bool enabled)
        {
            if (draft && opacity >= 1) { SettingsButton(c, r, text, () => Change(delta), enabled: enabled); return; }
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
            if (mappingCanvas) draftMappingCanvasBackgroundDim = Math.Clamp(draftMappingCanvasBackgroundDim + delta, 0, 100);
            else if (draft) draftBackgroundDim = Math.Clamp(draftBackgroundDim + delta, 0, 100);
            else { LibrarySettings.BackgroundDim += delta; RequestViewPreference?.Invoke(); }
        }
    }
}
