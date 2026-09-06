using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<string>? RequestLanguagePreference { get; set; }
    private string renderedLanguage = L.Language;

    private void CycleLanguage()
    {
        if (editField >= 0 && !CommitField()) return;
        var languages = L.AvailableLanguages;
        int current = languages.ToList().IndexOf(L.Language);
        L.SetLanguage(languages[(current + 1) % languages.Count]);
        RequestLanguagePreference?.Invoke(L.Language);
        RefreshLanguage();
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
