using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ShortcutRoutingTests
{
    public static void TimingPage()
    {
        var ui = new Ui();
        ui.LoadDocument(Map());
        ui.Key('A', ctrl: true);
        var initial = ui.View.Document.DeepClone();
        ui.Key('Q');
        var edited = ui.View.Document.DeepClone();
        ui.Key(114);
        Check(ui.View.TimingPageVisible && ui.View.SelectedObjectIds.Count == 1, "Timing retains the Compose selection.");
        ui.Key(37, ctrl: true, shift: true);
        ui.Key(39, ctrl: true, shift: true);
        Check(ui.View.Document.ContentEquals(edited), "Timing must not nudge hidden objects.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(initial), "Blocked Timing nudges must not enter undo history.");
        ui.Key('Y', ctrl: true);
        Check(ui.View.Document.ContentEquals(edited), "Blocked Timing nudges must preserve redo.");
        ui.Key(39, ctrl: true);
        Check(ui.View.PlayheadMs == 500, "Timing Ctrl+Right still seeks the first bookmark.");
        ui.Key(39, ctrl: true);
        Check(ui.View.PlayheadMs == 2000, "Timing Ctrl+Right still seeks the next bookmark.");
        ui.Key(37, ctrl: true);
        Check(ui.View.PlayheadMs == 500, "Timing Ctrl+Left still seeks the previous bookmark.");
        ui.Key(38, ctrl: true, shift: true);
        Check(Math.Abs(ui.View.PlaybackSpeed - 1.05) < .001, "Timing fine playback speed remains available.");
        ui.Key(112);
        ui.Key('A', ctrl: true);
        double x = ui.View.Document.Fruits.Single().X;
        ui.Key(39, ctrl: true, shift: true);
        Check(ui.View.Document.Fruits.Single().X == x + 1, "Compose horizontal nudge remains available.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(edited), "Compose nudge remains undoable.");
    }

    public static void Modifiers()
    {
        var ui = new Ui();
        ui.LoadDocument(Map());
        ui.Key('A', ctrl: true);
        var before = ui.View.Document.DeepClone();
        int playback = 0;
        ui.View.RequestTogglePlayback = () => playback++;
        double time = ui.View.PlayheadMs;
        foreach (int key in new[] { (int)'C', 'X', 'Z', 'V', 'J', 'K', 35, 38, 40 })
        {
            ui.Key(key, shift: true);
            Check(ui.View.Document.ContentEquals(before) && ui.View.PlayheadMs == time && playback == 0,
                $"Unsupported Shift+{key} must not edit, seek or play.");
        }
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "Ignored Shift combinations must not enter history.");
        ui.Key(39, shift: true);
        Check(ui.View.PlayheadMs == time + 500, "Shift+Right retains four-subdivision seeking.");
        ui.Key('3', shift: true);
        Check(ui.View.SnapDivisor == 3, "Shift+number retains Snap selection.");

        ui.Key(117);
        ui.Key('A', ctrl: true);
        string copied = "";
        ui.View.RequestCopyText = value => copied = value;
        ui.Key('C', ctrl: true);
        string originalRows = copied;
        foreach (var chord in new (int Key, bool Ctrl, bool Shift, bool Alt)[] { ('I', true, true, false),
            ('X', true, true, false), (46, false, true, false), (46, true, false, false), ('I', true, false, true) })
        {
            ui.View.SetModifiers(chord.Alt, chord.Shift);
            ui.Key(chord.Key, chord.Ctrl, chord.Shift);
            ui.View.SetModifiers(false, false);
            ui.Key('C', ctrl: true);
            Check(copied == originalRows, "Unsupported F6 modifiers must not delete or cut timing rows.");
        }
        ui.Key('I', ctrl: true);
        ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
        Check(copied != originalRows, "Plain Ctrl+I must still delete eligible timing rows.");
        ui.Key('Z', ctrl: true); ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
        Check(copied == originalRows, "Timing draft deletion must remain undoable.");
        ui.Key('Z', ctrl: true, shift: true); ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
        Check(copied != originalRows, "Ctrl+Shift+Z must retain Timing draft redo.");
        ui.Key(27);
        Check(ui.View.Document.ContentEquals(before), "Cancelling F6 preserves the live map.");
    }

    public static void Export()
    {
        var ui = new Ui(); ui.LoadDocument(Map());
        int exports = 0;
        ui.View.RequestExport = () => exports++;
        void ExportKey(bool shift = false)
        {
            ui.View.SetModifiers(true, shift);
            ui.Key('E', ctrl: true, shift: shift);
            ui.View.SetModifiers(false, false);
        }
        ExportKey();
        ui.Key(114); ExportKey();
        Check(exports == 2 && ui.View.TimingPageVisible, "Compose and Timing both dispatch Ctrl+Alt+E.");
        ExportKey(shift: true); ui.Key('E', ctrl: true);
        Check(exports == 2, "Only the exact export chord is accepted.");
        var field = ui.View.TimingFields.Single(f => f.Key == "page.bpm").Bounds;
        ui.Click(field.X + 8, field.Y + 8); ExportKey();
        Check(exports == 2, "An active Timing field keeps keyboard focus.");
        ui.Key(27); ui.Key(117); ExportKey();
        Check(exports == 2 && ui.View.TimingSetupVisible, "Export must not escape the F6 modal.");
        ui.Key(27); ui.Key(115); ExportKey();
        Check(exports == 2 && ui.View.SongSetupVisible, "Export must not escape Song Setup.");
        ui.Key(27); ExportKey();
        Check(exports == 3, "Closing the modal restores Timing export.");
    }

    public static void LanguageMenu()
    {
        string previous = L.Language;
        try
        {
            foreach (string language in L.AvailableLanguages)
            {
                L.SetLanguage(language);
                var ui = new Ui(); ui.LoadDocument(Map());
                var before = ui.View.Document.DeepClone();
                string? saved = null;
                ui.View.RequestLanguagePreference = value => saved = value;
                ui.View.OpenSettings(); ui.Paint();
                ui.ClickText(L.Get("settings.appearance"));
                void Open() => ui.ClickText(System.Globalization.CultureInfo.GetCultureInfo(L.Language).NativeName + " ▾");
                Open(); ui.Key(40); ui.Key(27);
                Check(!ui.View.LibraryVisible && saved is null && L.Language == language
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.appearance")),
                    "Escape closes only the language menu and does not apply its highlighted language.");
                Open(); ui.Key(38); ui.Key(40); ui.Key(40);
                string expected = L.AvailableLanguages[(L.AvailableLanguages.ToList().IndexOf(language) + 1) % L.AvailableLanguages.Count];
                ui.Key(13);
                Check(L.Language == expected && saved == expected && !ui.View.LibraryVisible,
                    "Language arrows wrap and Enter persists the choice without leaving Settings.");
                Open(); ui.Key(116); ui.Key('F', ctrl: true); ui.Key(27);
                Check(!ui.View.LibraryVisible && L.Language == expected, "The dropdown consumes background Library shortcuts.");
                ui.Key(27);
                Check(!ui.View.LibraryVisible && ui.View.Document.ContentEquals(before) && !ui.View.IsDirty,
                    "The next Escape closes Settings; language changes preserve beatmap content.");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.ContentEquals(before), "Language navigation must not enter content history.");
            }
        }
        finally { L.SetLanguage(previous); }
    }

    private static MapDocument Map() => OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Editor]\nBookmarks:500,2000\n[TimingPoints]\n0,500,4,1,0,100,1,0\n1000,-100,4,1,0,100,0,0\n[HitObjects]\n128,192,1200,1,0,0:0:0:0:\n");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
