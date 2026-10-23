using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal Rect ImportSliderModeBounds { get; private set; }
    internal Rect ConfirmFaImportBounds { get; private set; }

    private static string ImportSliderModeKey(ImportSliderConversionMode mode) => mode switch
    {
        ImportSliderConversionMode.KeepLegacy => "settings.importSliderKeep",
        ImportSliderConversionMode.ConvertToFSlider => "settings.importSliderAlways",
        _ => "settings.importSliderAsk"
    };

    private float DrawMapImportSettings(ICanvas c, float top)
    {
        c.Line(SettingsContentX, top, SettingsContentX + SettingsContentWidth, top, Grid);
        c.Text(L.Get("settings.mapImport"), SettingsContentX, top + 12, SettingsSectionSize, Foreground, SettingsContentWidth, true);
        ImportSliderModeBounds = new(SettingsContentX, top + 44, SettingsContentWidth, SettingsControlHeight);
        SettingsButton(c, ImportSliderModeBounds, L.Get(ImportSliderModeKey(draftImportSliderConversion)) + " ▾", () =>
        {
            contextItems.Clear();
            foreach (var mode in Enum.GetValues<ImportSliderConversionMode>())
                contextItems.Add(new(L.Get(ImportSliderModeKey(mode)), () => draftImportSliderConversion = mode));
            contextBounds = new(ImportSliderModeBounds.X, Math.Clamp(ImportSliderModeBounds.Bottom, 0, Math.Max(0, height - ContextMenuHeight)),
                ImportSliderModeBounds.Width, ContextMenuHeight);
        });
        ConfirmFaImportBounds = new(SettingsContentX, top + 84, SettingsContentWidth, SettingsControlHeight);
        TimingCheck(c, ConfirmFaImportBounds, "settings.importConfirmFa", draftConfirmCompensatedImports,
            () => draftConfirmCompensatedImports = !draftConfirmCompensatedImports);
        return top + 116;
    }
}
