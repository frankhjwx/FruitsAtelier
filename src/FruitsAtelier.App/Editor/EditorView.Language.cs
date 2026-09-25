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
    private static string LanguageName(string code) => CultureInfo.GetCultureInfo(code).NativeName;

    private void DrawLanguageButton(ICanvas c, Rect bounds)
    {
        languageButtonBounds = bounds;
        c.Fill(bounds, Surface, 4); c.Stroke(bounds, Grid, radius: 4);
        Button(c, bounds, LanguageName(L.Language) + " ▾", () =>
        {
            if (editField >= 0 && !CommitField()) return;
            languageMenuOpen = !languageMenuOpen;
            languageSelection = L.AvailableLanguages.ToList().IndexOf(L.Language);
            menu = -1; contextItems.Clear();
        }, languageMenuOpen, fontSize: librarySettingsOpen ? SettingsTextSize : 12, bold: false);
    }
    private void DrawLanguageMenu(ICanvas c)
    {
        if (!languageMenuOpen) return;
        languageMenuBounds = new(languageButtonBounds.X, languageButtonBounds.Bottom + 4, languageButtonBounds.Width, 12 + L.AvailableLanguages.Count * 34);
        c.Fill(languageMenuBounds, Surface, 5); c.Stroke(languageMenuBounds, Grid, radius: 5);
        for (int i = 0; i < L.AvailableLanguages.Count; i++)
        {
            string code = L.AvailableLanguages[i];
            var row = new Rect(languageMenuBounds.X + 6, languageMenuBounds.Y + 6 + i * 34, languageMenuBounds.Width - 12, 32);
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
            int index = (int)((y - languageMenuBounds.Y - 6) / 34);
            if (button == 0 && index >= 0 && index < L.AvailableLanguages.Count) SelectLanguage(L.AvailableLanguages[index]);
            return true;
        }
        languageMenuOpen = false;
        return false;
    }
    private void SelectLanguage(string code)
    {
        languageMenuOpen = false;
        L.SetLanguage(code); RequestLanguagePreference?.Invoke(code); RefreshLanguage();
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
