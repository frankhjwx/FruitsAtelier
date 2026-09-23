using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool SongSetupVisible { get; private set; }
    public Action? RequestPasteSongSetup { get; set; }
    public int SongSetupInputSession { get; private set; }
    private int songTab, songColour, songDrag = -1;
    private bool songCountdownOpen;
    private string songField = "", songError = "";
    private bool songCustomColours;
    private double songHue, songSaturation, songValue;
    private readonly Dictionary<string, string> songValues = [];
    private readonly Dictionary<string, string> songInitial = [];
    private readonly Dictionary<string, Rect> songFieldBounds = [];
    private readonly List<uint> songColours = [];
    private readonly List<(Rect Bounds, string Key)> songSliders = [];
    private Rect songPalette, songHueTrack;
    private static readonly string[] SongMetadata = ["ArtistUnicode", "Artist", "TitleUnicode", "Title", "Creator", "Version", "Source", "Tags"];
    private static readonly string[] SongDifficulty = ["HPDrainRate", "CircleSize", "ApproachRate", "OverallDifficulty"];
    private static readonly string[] SongDesign = ["Countdown", "CountdownOffset", "WidescreenStoryboard", "LetterboxInBreaks", "EpilepsyWarning"];
    private static readonly string[] SongCountdownSpeeds = ["song.countdownOff", "song.countdownNormal", "song.countdownHalf", "song.countdownDouble"];
    internal Rect SongSetupButtonBounds => new(SkinSelectorBounds.X - 126, 6, 120, 28);
    internal Rect SongSetupBounds => new((width - Math.Min(840, width - 32)) / 2,
        (height - Math.Min(580, height - 32)) / 2, Math.Min(840, width - 32), Math.Min(580, height - 32));
    internal IReadOnlyDictionary<string, Rect> SongSetupFieldBounds => songFieldBounds;

    internal void OpenSongSetup()
    {
        if (LibraryVisible || IsTestplaying || !HasEditorProject || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        menu = -1; languageMenuOpen = false; contextItems.Clear();
        songValues.Clear(); songInitial.Clear(); songField = songError = "";
        songTab = songColour = 0; songDrag = -1; songCountdownOpen = false; SongSetupInputSession++;
        foreach (string key in SongMetadata) songValues[key] = SongSetup.Get(Document, "Metadata", key);
        songValues["ArtistUnicode"] = SongSetup.Get(Document, "Metadata", "ArtistUnicode", songValues["Artist"]);
        if (string.IsNullOrWhiteSpace(songValues["ArtistUnicode"])) songValues["ArtistUnicode"] = songValues["Artist"];
        songValues["TitleUnicode"] = SongSetup.Get(Document, "Metadata", "TitleUnicode", SongSetup.Get(Document, "Metadata", "Title", Document.Name));
        if (string.IsNullOrWhiteSpace(songValues["TitleUnicode"])) songValues["TitleUnicode"] = SongSetup.Get(Document, "Metadata", "Title", Document.Name);
        songValues["Version"] = CurrentDifficultyName;
        songValues["HPDrainRate"] = SongSetup.Get(Document, "Difficulty", "HPDrainRate", "5");
        songValues["OverallDifficulty"] = SongSetup.Get(Document, "Difficulty", "OverallDifficulty", "5");
        songValues["CircleSize"] = Document.CircleSize.ToString(CultureInfo.InvariantCulture);
        songValues["ApproachRate"] = Document.ApproachRate.ToString(CultureInfo.InvariantCulture);
        foreach (string key in SongDesign) songValues[key] = SongSetup.Get(Document, "General", key, key == "Countdown" ? "1" : "0");
        foreach (var pair in songValues) songInitial[pair.Key] = pair.Value;
        songColours.Clear(); songColours.AddRange(SongSetup.Colours(Document));
        songCustomColours = songColours.Count > 0;
        if (!songCustomColours) songColours.AddRange(new uint[] { 0xFFC000, 0x00CA00, 0x127CFF, 0xF21839 });
        SelectSongColour(0);
        SongSetupVisible = true; hits.Clear(); fields.Clear();
    }

    private void CloseSongSetup()
    {
        SongSetupVisible = false; songField = ""; songDrag = -1; songCountdownOpen = false; SongSetupInputSession++;
        hits.Clear(); fields.Clear();
    }

    private static bool NeedsRomanisation(string value) => value.Any(c => c > 127);
    private bool SongFieldEnabled(string key) => key switch
    {
        "Artist" => NeedsRomanisation(songValues["ArtistUnicode"]),
        "Title" => NeedsRomanisation(songValues["TitleUnicode"]),
        "Hex" => songCustomColours,
        "CountdownOffset" => songValues["Countdown"] != "0",
        _ => true
    };

    private void DrawSongSetup(ICanvas c)
    {
        if (!SongSetupVisible) return;
        hits.Clear(); fields.Clear(); songFieldBounds.Clear(); songSliders.Clear();
        var r = SongSetupBounds;
        c.Fill(new(0, 0, width, height), Background, opacity: .7f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("song.title"), r.X + 22, r.Y + 16, 19, Foreground, r.Width - 80, true);
        Button(c, new(r.Right - 54, r.Y + 10, 32, 28), "×", CloseSongSetup);
        string[] tabs = ["song.general", "song.difficulty", "song.colours", "song.design"];
        for (int i = 0; i < tabs.Length; i++)
        {
            int tab = i;
            Button(c, new(r.X + 22 + i * 140, r.Y + 52, 132, 32), L.Get(tabs[i]), () =>
            { songTab = tab; songField = ""; songError = ""; songCountdownOpen = false; SongSetupInputSession++; }, songTab == i);
        }
        c.Line(r.X + 22, r.Y + 96, r.Right - 22, r.Y + 96, Grid);
        if (songTab == 0)
        {
            for (int i = 0; i < SongMetadata.Length; i++) SongTextField(c, SongMetadata[i], r.Y + 112 + i * 40);
        }
        else if (songTab == 1)
        {
            for (int i = 0; i < SongDifficulty.Length; i++)
            {
                string key = SongDifficulty[i]; float y = r.Y + 132 + i * 76;
                SongTextField(c, key, y, compact: true);
                var track = new Rect(r.X + 240, y + 9, r.Width - 378, 20);
                double.TryParse(songValues[key], NumberStyles.Float, CultureInfo.InvariantCulture, out double value);
                c.Line(track.X, track.Y + 10, track.Right, track.Y + 10, Grid, 4);
                c.Circle(track.X + (float)Math.Clamp(value / 10, 0, 1) * track.Width, track.Y + 10, 7, Accent);
                songSliders.Add((track, key));
            }
        }
        else if (songTab == 2) DrawSongColours(c, r);
        else
        {
            int.TryParse(songValues["Countdown"], out int countdown);
            var selector = new Rect(r.X + 22, r.Y + 120, r.Width - 44, 36);
            c.Fill(selector, Surface, 4); c.Stroke(selector, songCountdownOpen ? Accent : Grid, radius: 4);
            c.Text(L.Get(SongCountdownSpeeds[Math.Clamp(countdown, 0, SongCountdownSpeeds.Length - 1)]),
                selector.X + 10, selector.Y + 9, 13, Foreground, selector.Width - 50);
            c.Text("▾", selector.Right - 27, selector.Y + 8, 14, Foreground, 18);
            hits.Add(new(selector, () => { songField = ""; songCountdownOpen = !songCountdownOpen; }, true));
            SongTextField(c, "CountdownOffset", r.Y + 176);
            for (int i = 2; i < SongDesign.Length; i++)
            {
                string key = SongDesign[i]; bool enabled = songValues[key] == "1";
                ToggleSwitch(c, new(r.X + 22, r.Y + 238 + (i - 2) * 56, r.Width - 44, 38),
                    L.Get($"song.{key}"), enabled, () => songValues[key] = enabled ? "0" : "1");
            }
            if (songCountdownOpen)
            {
                var menu = new Rect(selector.X, selector.Bottom + 2, selector.Width, 12 + SongCountdownSpeeds.Length * 32);
                c.Fill(new(menu.X + 3, menu.Y + 4, menu.Width, menu.Height), 0x11151B, 5);
                c.Fill(menu, Surface, 5); c.Stroke(menu, Grid, 1, 5);
                for (int i = 0; i < SongCountdownSpeeds.Length; i++)
                {
                    var row = new Rect(menu.X + 6, menu.Y + 6 + i * 32, menu.Width - 12, 30);
                    if (row.Contains(mouseX, mouseY)) c.Fill(row, 0x343E4D, 4);
                    c.Text(L.Get(SongCountdownSpeeds[i]), row.X + 9, row.Y + 7, 12, i == countdown ? Accent : Foreground, row.Width - 18);
                }
            }
        }
        c.Text(songError, r.X + 22, r.Bottom - 84, 12, Error, r.Width - 44);
        c.Line(r.X + 22, r.Bottom - 60, r.Right - 22, r.Bottom - 60, Grid);
        Button(c, new(r.Right - 220, r.Bottom - 46, 92, 32), L.Get("mac.cancel"), CloseSongSetup);
        Button(c, new(r.Right - 116, r.Bottom - 46, 94, 32), L.Get("song.ok"), ApplySongSetup, true);
    }

    private void SongTextField(ICanvas c, string key, float y, bool compact = false)
    {
        var r = SongSetupBounds;
        c.Text(L.Get($"song.{key}"), r.X + 22, y + 9, 13, SongFieldEnabled(key) ? Foreground : Muted, 206);
        var box = new Rect(compact ? r.Right - 116 : r.X + 240, y, compact ? 94 : r.Width - 262, 32);
        songFieldBounds[key] = box;
        bool enabled = SongFieldEnabled(key), focused = enabled && songField == key;
        if (enabled) { c.Fill(box, Surface, 4); c.Stroke(box, focused ? Accent : Grid, radius: 4); }
        string value = !enabled && key is "Artist" or "Title" ? songValues[key + "Unicode"] : songValues[key];
        string inputKey = "song:" + key;
        if (enabled) DrawInputText(c, new(box.X + 9, box.Y + 7, box.Width - 18, 20), value, 13, focused, inputKey);
        else c.Text(value, box.X + 9, box.Y + 7, 13, 0xB8C2CE, box.Width - 18);
        hits.Add(new(box, () => { songField = key; SongSetupInputSession++; FocusInput(inputKey, value, mouseX); }, enabled));
    }

    private void ApplySongSetup()
    {
        foreach (string key in SongDifficulty)
            if (!double.TryParse(songValues[key], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || !double.IsFinite(value) || value < 0 || value > 10)
            { songError = L.Get("song.invalidDifficulty"); songTab = 1; return; }
        if (!int.TryParse(songValues["CountdownOffset"], out int offset) || offset < 0)
        { songError = L.Get("song.invalidOffset"); songTab = 3; return; }
        if (string.IsNullOrWhiteSpace(songValues["TitleUnicode"]) || string.IsNullOrWhiteSpace(songValues["Version"]))
        { songError = L.Get("song.required"); songTab = 0; return; }
        if (songCustomColours && !CommitSongHex()) { songTab = 2; return; }
        foreach (string key in new[] { "Artist", "Title" })
            if (songValues[key + "Unicode"] != songInitial[key + "Unicode"] && !NeedsRomanisation(songValues[key + "Unicode"]))
                songValues[key] = songValues[key + "Unicode"];
        var original = Document.DeepClone();
        var changed = SongSetup.SharedKeys.Where(k => songValues[k] != songInitial[k]).ToArray();
        var owner = history;
        var prior = difficulties.ToDictionary(d => d.History, d => (
            Values: changed.ToDictionary(k => k, k => OsuBeatmapReader.Setting(d.History.Document, "Metadata", k)),
            Name: d.History.Document.Name));
        var afterValues = changed.ToDictionary(k => k, k => songValues[k]);
        bool nameChanged = songValues["TitleUnicode"] != songInitial["TitleUnicode"];
        string newName = songValues["TitleUnicode"];
        void RestoreRelated(bool redo, MapDocument before, MapDocument after)
        {
            var restoring = changed.Where(key => OsuBeatmapReader.Setting(before, "Metadata", key) != OsuBeatmapReader.Setting(after, "Metadata", key)).ToArray();
            bool restoreName = nameChanged && before.Name != after.Name;
            if (restoring.Length == 0 && !restoreName) return;
            foreach (var session in difficulties.Where(d => d.History != owner))
            {
                var saved = prior.GetValueOrDefault(session.History, prior[owner]);
                session.History.RebaseSharedMetadata(target =>
                {
                    foreach (string key in restoring)
                        SongSetup.Set(target, "Metadata", key, redo ? afterValues[key] : saved.Values[key]);
                    if (restoreName) target.Name = redo ? newName : saved.Name;
                });
            }
        }
        if (!Edit(L.Get("song.title"), () =>
        {
            foreach (string key in SongMetadata.Where(k => songValues[k] != songInitial[k])) SongSetup.Set(Document, "Metadata", key, songValues[key]);
            if (songValues["TitleUnicode"] != songInitial["TitleUnicode"]) Document.Name = songValues["TitleUnicode"];
            foreach (string key in SongDifficulty.Where(k => songValues[k] != songInitial[k])) SongSetup.Set(Document, "Difficulty", key, songValues[key]);
            Document.CircleSize = double.Parse(songValues["CircleSize"], CultureInfo.InvariantCulture);
            Document.ApproachRate = double.Parse(songValues["ApproachRate"], CultureInfo.InvariantCulture);
            foreach (string key in SongDesign.Where(k => songValues[k] != songInitial[k])) SongSetup.Set(Document, "General", key, songValues[key]);
            SongSetup.SetColours(Document, songCustomColours ? songColours : Array.Empty<uint>());
            OsuBeatmapReader.Validate(Document);
        }, RestoreRelated)) { songError = StatusMessage; return; }
        RestoreRelated(true, original, Document);
        CloseSongSetup();
    }

    private void SongSetupPointerDown(float x, float y, int button, bool shift)
    {
        if (button != 0) return;
        if (songCountdownOpen)
        {
            var selector = new Rect(SongSetupBounds.X + 22, SongSetupBounds.Y + 120, SongSetupBounds.Width - 44, 36);
            var menu = new Rect(selector.X, selector.Bottom + 2, selector.Width, 12 + SongCountdownSpeeds.Length * 32);
            if (menu.Contains(x, y))
            {
                for (int i = 0; i < SongCountdownSpeeds.Length; i++)
                    if (new Rect(menu.X + 6, menu.Y + 6 + i * 32, menu.Width - 12, 30).Contains(x, y))
                    { songValues["Countdown"] = i.ToString(CultureInfo.InvariantCulture); songCountdownOpen = false; return; }
                return;
            }
            if (!selector.Contains(x, y)) { songCountdownOpen = false; return; }
        }
        if (songTab == 2 && songCustomColours && (songPalette.Contains(x, y) || songHueTrack.Contains(x, y)))
        { songField = ""; songDrag = songPalette.Contains(x, y) ? 4 : 5; MoveSongSetup(x, y, shift); return; }
        for (int i = 0; i < songSliders.Count; i++)
            if (songSliders[i].Bounds.Contains(x, y)) { songField = ""; songDrag = i; MoveSongSetup(x, y, shift); return; }
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return; }
        songField = "";
    }

    private void MoveSongSetup(float x, float y, bool shift)
    {
        if (songDrag < 0) return;
        if (songDrag is >= 0 and < 4 && songDrag < songSliders.Count)
        {
            var slider = songSliders[songDrag];
            double value = Math.Round(Math.Clamp((x - slider.Bounds.X) / slider.Bounds.Width, 0, 1) * 10, shift ? 1 : 0);
            songValues[slider.Key] = value.ToString(CultureInfo.InvariantCulture);
        }
        else if (songDrag is 4 or 5)
        {
            if (songDrag == 4)
            { songSaturation = Math.Clamp((x - songPalette.X) / songPalette.Width, 0, 1); songValue = 1 - Math.Clamp((y - songPalette.Y) / songPalette.Height, 0, 1); }
            else songHue = Math.Clamp((x - songHueTrack.X) / songHueTrack.Width, 0, .999999) * 360;
            songColours[songColour] = SongHsv(songHue, songSaturation, songValue);
            songValues["Hex"] = $"#{songColours[songColour]:X6}";
        }
        songError = "";
    }

    private void SongSetupKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { if (songCountdownOpen) songCountdownOpen = false; else CloseSongSetup(); return; }
        if (key == 13) { ApplySongSetup(); return; }
        if (key == 9)
        {
            songCountdownOpen = false;
            var keys = songFieldBounds.Keys.Where(SongFieldEnabled).ToArray();
            if (keys.Length > 0)
            {
                int index = Array.IndexOf(keys, songField);
                songField = keys[index < 0 ? shift ? keys.Length - 1 : 0 : (index + (shift ? keys.Length - 1 : 1)) % keys.Length];
                FocusInput("song:" + songField, songValues[songField], 0, selectAll: true); SongSetupInputSession++;
            }
            return;
        }
        if (songField.Length == 0 || !SongFieldEnabled(songField)) return;
        if (ctrl && key == 86) { RequestPasteSongSetup?.Invoke(); return; }
        string value = songValues[songField];
        if (InputKey("song:" + songField, ref value, key, ctrl, shift, 4096))
        { songValues[songField] = value; songError = ""; }
    }

    public void PasteSongSetupText(string text, int session)
    {
        if (!SongSetupVisible || ErrorVisible || DiscardConfirmationVisible || session != SongSetupInputSession
            || songField.Length == 0 || !SongFieldEnabled(songField)) return;
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        string value = InsertInput("song:" + songField, songValues[songField], text, 4096);
        songValues[songField] = value; songError = "";
        if (songField == "Hex" && value.TrimStart('#').Length == 6) CommitSongHex();
    }
}
