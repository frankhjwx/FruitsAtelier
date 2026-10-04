using FruitsAtelier.Localization;

internal static class LocalizationTests
{
    public static void FallbackAndFormats()
    {
        var catalog = LocalizationCatalog.Parse("""{"format":"{{Value}} {0:F3} / {1:0.######}","fallback":"English {0}"}""",
            """{"format":"{{数值}} {0:F3} / {1:0.######}"}""");
        Equal("{数值} 1.235 / 2.345679", catalog.Get("zh-CN", "format", 1.23456, 2.3456789));
        Equal("English user name", catalog.Get("zh-CN", "fallback", "user name"));
        Equal("[unknown.key]", catalog.Get("en", "unknown.key"));
        Check(catalog.Validate().Count == 1 && catalog.Validate()[0].Contains("missing key"), "Missing translation was not reported.");
    }

    public static void InvalidTablesAndPlaceholders()
    {
        var catalog = LocalizationCatalog.Parse("""{"different":"{0}/{2}","invalid":"{0"}""",
            """{"different":"{1}/{2}","invalid":"ok","extra":"extra"}""");
        var errors = catalog.Validate();
        Check(errors.Any(error => error.Contains("Placeholder mismatch")), "Argument index mismatch was missed.");
        Check(errors.Any(error => error.Contains("Invalid format")), "Malformed source format was accepted.");
        Check(errors.Any(error => error.Contains("unknown key")), "Unknown translation key was accepted.");
        Reject(() => LocalizationCatalog.Parse("""{"x":"a","x":"b"}""", "{}"));
        Reject(() => LocalizationCatalog.Parse("""{"x":5}""", "{}"));
    }

    public static void LanguageDiscovery()
    {
        var catalog = LocalizationCatalog.Parse(new Dictionary<string, string>
        {
            ["zh-CN"] = """{"value":"中文 {0:F1}"}""",
            ["fr"] = """{"value":"Français {0:F1}"}""",
            ["en"] = """{"value":"English {0:F1}"}"""
        });
        Check(catalog.AvailableLanguages.SequenceEqual(new[] { "en", "fr", "zh-CN" }), "Language tables were not discovered in stable order.");
        Equal("Français 1,5", catalog.Get("fr", "value", 1.5));
        Check(catalog.Validate().Count == 0, "Valid added language was rejected.");
    }

    public static void EmbeddedCatalogAndSwitching()
    {
        string original = Strings.Language;
        int changes = 0;
        void Changed() => changes++;
        Strings.LanguageChanged += Changed;
        try
        {
            Check(Strings.Validate().Count == 0, string.Join("; ", Strings.Validate()));
            string[] required = ["en", "zh-CN", "zh-TW", "ja", "ko", "ru", "es", "fr", "pl", "nl", "fil", "id", "th"];
            Check(required.All(Strings.AvailableLanguages.Contains), "Embedded languages missing.");
            foreach (string locale in required)
            {
                Strings.SetLanguage(locale);
                foreach (string key in new[] { "tools.fruit", "tools.fslider", "tools.newComboAbbreviation", "ui.ar", "ui.cs", "ui.dpb", "conversion.streamTab", "conversion.stackTab", "preview.normal", "preview.easy", "preview.hardRock" })
                {
                    string label = Strings.Get(key);
                    Strings.SetLanguage("en");
                    Equal(Strings.Get(key), label);
                    Strings.SetLanguage(locale);
                }
            }
            Strings.SetLanguage("zh-CN");
            string message = Strings.Get("files.saved", "演示/user.catchproj");
            string audioError = Strings.Get("audio.unavailable", Strings.Localized(Strings.Get("audio.fileMissing")));
            Strings.SetLanguage("en");
            int before = changes;
            Strings.SetLanguage("en");
            Check(changes == before, "Selecting the same language raised another event.");
            Equal("Project saved: 演示/user.catchproj", Strings.Reformat(message));
            Equal("Audio unavailable: The audio file does not exist", Strings.Reformat(audioError));
            Equal("untracked user text", Strings.Reformat("untracked user text"));
            Equal("English", Strings.Get("meta.languageName"));
            Strings.SetLanguage("zh-CN");
            Equal(message, Strings.Reformat(message));
        }
        finally { Strings.LanguageChanged -= Changed; Strings.SetLanguage(original); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (FormatException) { return; }
        throw new InvalidOperationException("Invalid language table was accepted.");
    }
    private static void Equal(string expected, string actual) => Check(expected == actual, $"Expected '{expected}', got '{actual}'.");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
