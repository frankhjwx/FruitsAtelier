using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const float MappingCanvasContentHeight = 424;
    private float MappingCanvasSettingsTop => SettingsTop - workspaceScroll;

    private void DrawMappingCanvasSettings(ICanvas c)
    {
        workspaceScrollBounds = new(SettingsContentX, SettingsTop + 116, SettingsContentWidth + 12, SettingsBounds.Height - 208);
        workspaceScroll = Math.Clamp(workspaceScroll, 0, Math.Max(0, MappingCanvasContentHeight - workspaceScrollBounds.Height));
        float top = MappingCanvasSettingsTop, available = SettingsContentWidth;
        int firstHit = hits.Count;
        c.Clip(workspaceScrollBounds);
        DrawBackgroundDimSetting(c, new(SettingsContentX, top + 128, Math.Min(360, available), SettingsControlHeight), true, mappingCanvas: true);
        SettingsButton(c, new(SettingsContentX, top + 184, available, SettingsControlHeight),
            L.Get(draftReverseCanvasScroll ? "settings.reverseCanvasScrollOn" : "settings.reverseCanvasScrollOff"),
            () => draftReverseCanvasScroll = !draftReverseCanvasScroll, draftReverseCanvasScroll);
        DrawIndicatorColours(c);
        c.Text(L.Get("settings.canvasGridColours"), SettingsContentX, top + 414, SettingsSectionSize, Foreground, available - 190, true);
        SettingsButton(c, new(SettingsContentX + available - 174, top + 408, 174, SettingsControlHeight), L.Get("settings.canvasGridReset"), () =>
        {
            draftSettingsColours[4] = LibrarySettings.DefaultCanvasGridColour;
            draftSettingsColours[5] = LibrarySettings.DefaultCanvasCentreLineColour;
            draftCanvasGridOpacity = LibrarySettings.DefaultCanvasGridOpacity;
        });
        float cellWidth = (available - 12) / 2;
        string[] names = ["settings.canvasGridLine", "settings.canvasCentreLine"];
        for (int i = 0; i < 2; i++)
        {
            int index = i + 4;
            var bounds = new Rect(SettingsContentX + i * (cellWidth + 12), top + 448, cellWidth, SettingsControlHeight);
            c.Fill(bounds, Surface, 4); c.Stroke(bounds, Grid, radius: 4);
            var swatch = new Rect(bounds.X + 7, bounds.Y + 6, 20, 20);
            c.Fill(swatch, draftSettingsColours[index], 3, draftCanvasGridOpacity / 100f); c.Stroke(swatch, Grid, radius: 3);
            c.Text(L.Get(names[i]), bounds.X + 36, bounds.Y + 7.5f, SettingsTextSize, Foreground, cellWidth - 116);
            c.Text($"#{draftSettingsColours[index]:X6}", bounds.Right - 82, bounds.Y + 7.5f, SettingsTextSize, Muted, 76);
            hits.Add(new(bounds, () => OpenSettingsColourPicker(index), true));
        }
        var opacity = new Rect(SettingsContentX, top + 496, Math.Min(360, available), SettingsControlHeight);
        SettingsButton(c, new(opacity.X, opacity.Y, 30, opacity.Height), "−",
            () => draftCanvasGridOpacity = Math.Max(0, draftCanvasGridOpacity - 5), enabled: draftCanvasGridOpacity > 0);
        SettingsButton(c, new(opacity.X + 36, opacity.Y, opacity.Width - 72, opacity.Height), L.Get("settings.canvasGridOpacity", draftCanvasGridOpacity) + " ▾",
            () => OpenCanvasGridOpacityMenu(opacity));
        SettingsButton(c, new(opacity.Right - 30, opacity.Y, 30, opacity.Height), "+",
            () => draftCanvasGridOpacity = Math.Min(100, draftCanvasGridOpacity + 5), enabled: draftCanvasGridOpacity < 100);
        c.Unclip();
        ClipSettingsHits(firstHit);
        DrawSettingsScrollbar(c, MappingCanvasContentHeight);
    }

    private void OpenCanvasGridOpacityMenu(Rect bounds)
    {
        languageMenuOpen = false; menu = -1; contextItems.Clear(); libraryField = bindingCapture = -1;
        for (int i = 0; i <= 100; i += 10)
        {
            int value = i;
            contextItems.Add(new((draftCanvasGridOpacity == value ? "✓ " : "") + L.Get("settings.canvasGridOpacity", value),
                () => draftCanvasGridOpacity = value));
        }
        float menuHeight = 12 + contextItems.Count * 32;
        contextBounds = new(bounds.X + 36, Math.Clamp(bounds.Bottom + 4, 0, Math.Max(0, height - menuHeight)), bounds.Width - 72, menuHeight);
    }
}
