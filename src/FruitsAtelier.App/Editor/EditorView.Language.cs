using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Rendering;
using System.Globalization;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<string>? RequestLanguagePreference { get; set; }
    private string renderedLanguage = L.Language;
    private bool languageMenuOpen;
    private Rect languageButtonBounds, languageMenuBounds;
    private int languageSelection;
    private int languageFirstRow, languageVisibleRows;
    private string? pendingLanguage;
    private static string LanguageName(string code) => code switch
    {
        "zh-CN" => L.Get("language.simplifiedChinese"),
        "zh-TW" => L.Get("language.traditionalChinese"),
        _ => CultureInfo.GetCultureInfo(code).NativeName
    };

    private void DrawLanguageButton(ICanvas c, Rect bounds)
    {
        languageButtonBounds = bounds;
        c.Fill(bounds, Surface, 4); c.Stroke(bounds, Grid, radius: 4);
        Button(c, bounds, LanguageName(L.Language) + " ▾", () =>
        {
            if (editField >= 0 && !CommitField()) return;
            languageMenuOpen = !languageMenuOpen;
            languageSelection = L.AvailableLanguages.ToList().IndexOf(L.Language);
            languageFirstRow = languageSelection;
            menu = -1; contextItems.Clear();
        }, languageMenuOpen, fontSize: librarySettingsOpen ? SettingsTextSize : 12, bold: false, textPadding: librarySettingsOpen ? SettingsControlPadding : 9,
            textRightPadding: librarySettingsOpen ? SettingsControlPadding : 6);
    }
    private void DrawLanguageMenu(ICanvas c)
    {
        if (!languageMenuOpen) return;
        float below = Math.Max(0, height - languageButtonBounds.Bottom - 8);
        float above = Math.Max(0, languageButtonBounds.Y - 8);
        bool openAbove = below < 12 + L.AvailableLanguages.Count * 34 && above > below;
        languageVisibleRows = Math.Min(L.AvailableLanguages.Count, Math.Max(1, (int)(((openAbove ? above : below) - 12) / 34)));
        languageFirstRow = Math.Clamp(languageFirstRow, 0, L.AvailableLanguages.Count - languageVisibleRows);
        float menuHeight = 12 + languageVisibleRows * 34;
        languageMenuBounds = new(Math.Clamp(languageButtonBounds.X, 4, Math.Max(4, width - languageButtonBounds.Width - 4)),
            Math.Max(4, openAbove ? languageButtonBounds.Y - menuHeight - 4 : languageButtonBounds.Bottom + 4), languageButtonBounds.Width, menuHeight);
        c.Fill(languageMenuBounds, Surface, 5); c.Stroke(languageMenuBounds, Grid, radius: 5);
        for (int rowIndex = 0; rowIndex < languageVisibleRows; rowIndex++)
        {
            int i = languageFirstRow + rowIndex;
            string code = L.AvailableLanguages[i];
            var row = new Rect(languageMenuBounds.X + 6, languageMenuBounds.Y + 6 + rowIndex * 34, languageMenuBounds.Width - 12, 32);
            if (row.Contains(mouseX, mouseY) || i == languageSelection) c.Fill(row, 0x343E4D, 4);
            c.Text((code == L.Language ? "✓ " : "") + LanguageName(code), row.X + 8, row.Y + 8, librarySettingsOpen ? SettingsTextSize : 12, Foreground, row.Width - 16);
        }
    }
    private bool LanguagePointerDown(float x, float y, int button)
    {
        if (!languageMenuOpen) return false;
        if (button == 0 && languageButtonBounds.Contains(x, y)) { languageMenuOpen = false; return true; }
        if (languageMenuBounds.Contains(x, y))
        {
            float rowY = y - languageMenuBounds.Y - 6;
            int row = (int)(rowY / 34);
            if (button == 0 && rowY >= 0 && row < languageVisibleRows) SelectLanguage(L.AvailableLanguages[languageFirstRow + row]);
            return true;
        }
        languageMenuOpen = false;
        return false;
    }
    private void KeepLanguageSelectionVisible()
    {
        int rows = Math.Max(1, languageVisibleRows);
        if (languageSelection < languageFirstRow) languageFirstRow = languageSelection;
        else if (languageSelection >= languageFirstRow + rows) languageFirstRow = languageSelection - rows + 1;
    }
    private void SelectLanguage(string code)
    {
        languageMenuOpen = false;
        if (code != "en")
        {
            pendingLanguage = code;
            hits.Clear(); fields.Clear();
            return;
        }
        ApplyLanguage(code);
    }
    private void ApplyLanguage(string code)
    {
        L.SetLanguage(code);
        if (!FirstRunSetupVisible) RequestLanguagePreference?.Invoke(code);
        RefreshLanguage();
    }
    private void AnswerLanguageNotice(bool accept)
    {
        string? code = pendingLanguage;
        pendingLanguage = null;
        hits.Clear(); fields.Clear();
        if (accept && code is not null) ApplyLanguage(code);
    }
    private void DrawLanguageNotice(ICanvas c)
    {
        if (pendingLanguage is null) return;
        float w = Math.Min(560, width - 48);
        var lines = WrapSyncText(c, L.Get("language.machineTranslationNotice", LanguageName(pendingLanguage)), w - 48);
        float h = 132 + lines.Length * 22, x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("language.machineTranslationTitle"), x + 24, y + 24, 20, Foreground, w - 48, true);
        for (int i = 0; i < lines.Length; i++) c.Text(lines[i].Text, x + 24, y + 64 + i * 22, 15, Foreground, w - 48);
        float buttonWidth = (w - 64) / 2;
        Button(c, new(x + 24, y + h - 56, buttonWidth, 36), L.Get("mac.cancel"), () => AnswerLanguageNotice(false));
        Button(c, new(x + 40 + buttonWidth, y + h - 56, buttonWidth, 36), L.Get("language.continue"), () => AnswerLanguageNotice(true), true);
    }

    private void RefreshLanguage()
    {
        if (renderedLanguage == L.Language) return;
        renderedLanguage = L.Language;
        convertedSnapshot = null;
        menu = -1;
        contextItems.Clear();
        editField = -1;
        fieldError = L.Reformat(fieldError);
        if (!AudioReady) AudioNotice = L.Reformat(AudioNotice);
        StatusMessage = L.Get("ui.languageChanged");
    }
}
