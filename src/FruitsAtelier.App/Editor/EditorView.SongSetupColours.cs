using System.Globalization;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void DrawSongColours(ICanvas c, Rect r)
    {
        Button(c, new(r.X + 22, r.Y + 110, 290, 32), L.Get("song.customColours"),
            () => songCustomColours = !songCustomColours, songCustomColours);
        for (int i = 0; i < songColours.Count; i++)
        {
            int index = i;
            var box = new Rect(r.X + 22 + i % 2 * 145, r.Y + 158 + i / 2 * 48, 135, 38);
            c.Fill(box, songColours[i], 4, songCustomColours ? 1 : .4f);
            c.Stroke(box, i == songColour ? Foreground : Grid, i == songColour ? 2 : 1, 4);
            uint colour = songColours[i];
            uint ink = ((colour >> 16 & 255) * 299 + (colour >> 8 & 255) * 587 + (colour & 255) * 114) > 145000 ? 0x171A20u : 0xFFFFFFu;
            c.Text(L.Get("song.combo", i + 1), box.X + 12, box.Y + 10, 13, ink, box.Width - 24, true);
            hits.Add(new(box, () => { if (CommitSongHex()) SelectSongColour(index); }, songCustomColours));
        }
        Button(c, new(r.X + 22, r.Y + 376, 135, 32), L.Get("song.addColour"), () =>
        { if (!CommitSongHex()) return; songColours.Add(SongHsv(songColours.Count * 47 % 360, .65, .95)); SelectSongColour(songColours.Count - 1); }, enabled: songCustomColours && songColours.Count < 8);
        Button(c, new(r.X + 167, r.Y + 376, 135, 32), L.Get("song.removeColour"), () =>
        { songColours.RemoveAt(songColour); SelectSongColour(Math.Min(songColour, songColours.Count - 1)); }, enabled: songCustomColours && songColours.Count > 1);
        songPalette = new(r.X + 338, r.Y + 158, r.Width - 360, 216);
        for (int sy = 0; sy < 24; sy++)
        for (int sx = 0; sx < 40; sx++)
            c.Fill(new(songPalette.X + sx * songPalette.Width / 40, songPalette.Y + sy * 9,
                songPalette.Width / 40 + .5f, 9.5f), SongHsv(songHue, sx / 39d, 1 - sy / 23d), opacity: songCustomColours ? 1 : .35f);
        c.Circle(songPalette.X + (float)songSaturation * songPalette.Width, songPalette.Y + (float)(1 - songValue) * songPalette.Height, 5, Foreground, false, 2);
        songHueTrack = new(songPalette.X, songPalette.Bottom + 14, songPalette.Width, 22);
        for (int i = 0; i < 60; i++)
            c.Fill(new(songHueTrack.X + i * songHueTrack.Width / 60, songHueTrack.Y, songHueTrack.Width / 60 + .5f, 22), SongHsv(i * 6, 1, 1));
        float hueX = songHueTrack.X + (float)(songHue / 360) * songHueTrack.Width;
        c.Stroke(new(hueX - 3, songHueTrack.Y - 2, 6, 26), Foreground, 2);
        SongTextField(c, "Hex", r.Y + 430);
    }

    private bool CommitSongHex()
    {
        string text = songValues["Hex"].Trim().TrimStart('#');
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint colour))
        { songError = L.Get("song.invalidHex"); return false; }
        if (songColours[songColour] != colour) { songColours[songColour] = colour; SetSongHsv(colour); }
        songError = ""; return true;
    }

    private void SelectSongColour(int index)
    {
        songColour = index; songField = ""; SongSetupInputSession++;
        songValues["Hex"] = $"#{songColours[index]:X6}";
        SetSongHsv(songColours[index]);
    }

    private void SetSongHsv(uint colour)
    {
        (songHue, songSaturation, songValue) = ColourToHsv(colour, songHue);
    }

    private static (double Hue, double Saturation, double Value) ColourToHsv(uint colour, double fallbackHue)
    {
        double r = (colour >> 16 & 255) / 255d, g = (colour >> 8 & 255) / 255d, b = (colour & 255) / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        double hue = delta > 0 ? ((max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60 + 360) % 360 : fallbackHue;
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    private static uint SongHsv(double hue, double saturation, double value)
    {
        double c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x)
        };
        return (uint)Math.Round((r + m) * 255) << 16 | (uint)Math.Round((g + m) * 255) << 8 | (uint)Math.Round((b + m) * 255);
    }
}
