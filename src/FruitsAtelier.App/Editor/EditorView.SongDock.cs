using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapDocument? songDockSnapshot;
    private Rect songDockBounds;
    private float songDockScroll, songDockContentHeight;
    private bool drawingSongDock;
    internal Rect SongDockViewport => songDockBounds;
    private bool SongDockEditing => DockContentInputAvailable && PanelVisible(DockPanel.SongSetup)
        && ReferenceEquals(songDockSnapshot, convertedSnapshot);
    private bool SongDockFocused => SongDockEditing && songField.Length > 0;

    internal void ShowSongDock()
    {
        if (LibraryVisible || IsTestplaying || TimingPageVisible || !HasEditorProject) return;
        AddDockPanel(DockPanel.SongSetup, false);
        var group = PanelGroup(DockPanel.SongSetup);
        group.Active = DockPanel.SongSetup; group.Collapsed = false;
        InitializeSongSetup(); songDockSnapshot = convertedSnapshot; songDockScroll = 0;
        menu = -1; contextItems.Clear();
    }

    private void DrawSongDock(ICanvas c, Rect bounds)
    {
        if (SongSetupVisible) return;
        AdoptLiveDock(ref songDockSnapshot, ref liveSongSnapshot);
        if (!ReferenceEquals(songDockSnapshot, convertedSnapshot))
        {
            InitializeSongSetup(); songDockSnapshot = convertedSnapshot; songDockScroll = 0;
        }
        songDockBounds = bounds; drawingSongDock = true;
        songFieldBounds.Clear(); songSliders.Clear();
        PrepareSongTags(c);
        string[] tabs = ["song.general", "song.difficulty", "song.colours", "song.design", "song.faSettings"];
        songTab = Math.Clamp(songTab, 0, 4);
        const float categoryHeight = 44;
        float bodyHeight = songTab switch { 0 => 7 * 72 + Math.Max(64, songTagRows.Count * (songTagSize + 5) + 14) + 38,
            1 => SongDifficulty.Length * 82, 2 => 650, 3 => 320, _ => 470 };
        songDockContentHeight = categoryHeight + bodyHeight + 32;
        songDockScroll = Math.Clamp(songDockScroll, 0, Math.Max(0, songDockContentHeight - bounds.Height));
        float y = bounds.Y - songDockScroll;
        int firstHit = hits.Count;
        c.Clip(bounds);
        float column = (bounds.Width - 6) / 2;
        float categoryWidth = (bounds.Width - 3 * (tabs.Length - 1)) / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            int tab = i;
            var category = new Rect(bounds.X + i * (categoryWidth + 3), y, categoryWidth, 32);
            Button(c, category,
                FitTabName(c, L.Get(tabs[i]), Math.Max(1, category.Width - 16)),
                () => { songTab = tab; songField = songError = ""; songDockScroll = 0; SongSetupInputSession++; }, active: songTab == tab, fontSize: 12);
        }
        y += categoryHeight;
        if (songTab == 0)
            for (int i = 0; i < SongMetadata.Length; i++) SongTextField(c, SongMetadata[i], y + i * 72);
        else if (songTab == 1)
            for (int i = 0; i < SongDifficulty.Length; i++) SongRangeSlider(c, SongDifficulty[i], y + i * 82, 10);
        else if (songTab == 2) DrawSongDockColours(c, y);
        else if (songTab == 3)
        {
            for (int i = 0; i < SongCountdownSpeeds.Length; i++)
            {
                int choice = i;
                Button(c, new(bounds.X + i % 2 * (column + 6), y + i / 2 * 38, column, 32), L.Get(SongCountdownSpeeds[i]),
                    () => songValues["Countdown"] = choice.ToString(), songValues["Countdown"] == i.ToString(), fontSize: 12);
            }
            SongTextField(c, "CountdownOffset", y + 80);
            for (int i = 2; i < SongDesign.Length; i++)
            {
                string key = SongDesign[i]; bool enabled = songValues[key] == "1";
                ToggleSwitch(c, new(bounds.X, y + 162 + (i - 2) * 50, bounds.Width, 38), L.Get($"song.{key}"), enabled,
                    () => songValues[key] = enabled ? "0" : "1");
            }
        }
        else if (songTab == 4) DrawFaSettings(c, new(bounds.X, y, bounds.Width, bodyHeight), true);
        y += bodyHeight;
        c.Text(songError, bounds.X, y + 4, 12, Error, bounds.Width);
        c.Unclip(); drawingSongDock = false;
        for (int i = hits.Count - 1; i >= firstHit; i--)
            if (hits[i].Bounds.Y < bounds.Y || hits[i].Bounds.Bottom > bounds.Bottom) hits.RemoveAt(i);
        if (songDockContentHeight > bounds.Height)
        {
            float thumb = Math.Max(20, bounds.Height * bounds.Height / songDockContentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + songDockScroll / (songDockContentHeight - bounds.Height) * (bounds.Height - thumb), 3, thumb), Muted, 1);
        }
    }

    private void DrawSongDockColours(ICanvas c, float y)
    {
        var r = songDockBounds;
        Button(c, new(r.X, y, r.Width, 32), L.Get("song.customColours"), () => songCustomColours = !songCustomColours, songCustomColours, fontSize: 12);
        float cell = (r.Width - 6) / 2;
        for (int i = 0; i < songColours.Count; i++)
        {
            int index = i;
            var box = new Rect(r.X + i % 2 * (cell + 6), y + 44 + i / 2 * 42, cell, 34);
            c.Fill(box, songColours[i], 4, songCustomColours ? 1 : .4f);
            c.Stroke(box, i == songColour ? Foreground : Grid, 2, 4);
            hits.Add(new(box, () => { if (CommitSongHex()) SelectSongColour(index); }, songCustomColours));
        }
        Button(c, new(r.X, y + 220, cell, 32), L.Get("song.addColour"), () =>
        { if (CommitSongHex()) { songColours.Add(SongHsv(songColours.Count * 47 % 360, .65, .95)); SelectSongColour(songColours.Count - 1); } }, enabled: songCustomColours && songColours.Count < 8, fontSize: 12);
        Button(c, new(r.X + cell + 6, y + 220, cell, 32), L.Get("song.removeColour"), () =>
        { songColours.RemoveAt(songColour); SelectSongColour(Math.Min(songColour, songColours.Count - 1)); }, enabled: songCustomColours && songColours.Count > 1, fontSize: 12);
        songPalette = new(r.X, y + 270, r.Width, 180);
        for (int sy = 0; sy < 20; sy++)
        for (int sx = 0; sx < 32; sx++)
            c.Fill(new(songPalette.X + sx * r.Width / 32, songPalette.Y + sy * 9, r.Width / 32 + .5f, 9.5f),
                SongHsv(songHue, sx / 31d, 1 - sy / 19d), opacity: songCustomColours ? 1 : .35f);
        c.Circle(songPalette.X + (float)songSaturation * r.Width, songPalette.Y + (float)(1 - songValue) * 180, 5, Foreground, false, 2);
        songHueTrack = new(r.X, y + 464, r.Width, 22);
        for (int i = 0; i < 60; i++) c.Fill(new(r.X + i * r.Width / 60, songHueTrack.Y, r.Width / 60 + .5f, 22), SongHsv(i * 6, 1, 1));
        float hueX = r.X + (float)(songHue / 360) * r.Width;
        c.Stroke(new(hueX - 3, songHueTrack.Y - 2, 6, 26), Foreground, 2);
        SongTextField(c, "Hex", y + 506);
    }

    private bool SongDockPointer(float x, float y, int button, bool shift)
    {
        if (!SongDockEditing || menu >= 0 || contextItems.Count > 0) return false;
        if (!songDockBounds.Contains(x, y)) { songField = ""; return false; }
        SongSetupPointerDown(x, y, button, shift); return true;
    }
}
