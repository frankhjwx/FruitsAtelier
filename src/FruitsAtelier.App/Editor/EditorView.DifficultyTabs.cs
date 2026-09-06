using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int firstDifficultyTab;
    private bool revealDifficultyTabs = true;
    private Rect difficultyTabStrip, difficultyAddButton;
    private int visibleDifficultyTabs = 1;
    private static readonly string catchIconPath = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "osu", "RulesetCatch.png");

    public double? CurrentStarRating => DifficultyRating(activeDifficulty);

    private double? DifficultyRating(int index)
    {
        var session = difficulties[index];
        var document = session.History.Document;
        if (index == activeDifficulty && (draftTrack != Guid.Empty || draftBanana != Guid.Empty)) return null;
        if (session.RatingSnapshot is not null && session.RatingCompensation == compensateTinyDroplets
            && session.RatingSnapshot.ContentEquals(document)) return session.Stars;
        var converted = index == activeDifficulty ? Conversion : CatchStreamConverter.Convert(document, compensateTinyDroplets);
        session.Stars = converted.Success ? CatchDifficultyCalculator.Calculate(converted.Objects, document.CircleSize).StarRating : null;
        session.RatingSnapshot = document.DeepClone();
        session.RatingCompensation = compensateTinyDroplets;
        return session.Stars;
    }

    // osu!web's published star-rating colour stops; gamma-correct RGB interpolation.
    internal static uint DifficultyColour(double? stars)
    {
        if (stars is null || stars < 0.1) return 0xAAAAAA;
        double[] stops = [0.1, 1.25, 2, 2.5, 3.3, 4.2, 4.9, 5.8, 6.7, 7.7, 9];
        uint[] colours = [0x4290FB, 0x4FC0FF, 0x4FFFD5, 0x7CFF4F, 0xF6F05C, 0xFF8068, 0xFF4E6F, 0xC645B8, 0x6563DE, 0x18158E, 0x000000];
        if (stars >= 9) return 0;
        int right = Array.FindIndex(stops, s => s >= stars);
        if (right <= 0) return colours[0];
        double t = (stars.Value - stops[right - 1]) / (stops[right] - stops[right - 1]);
        uint result = 0;
        foreach (int shift in new[] { 16, 8, 0 })
        {
            double a = (colours[right - 1] >> shift) & 255, b = (colours[right] >> shift) & 255;
            result |= (uint)Math.Round(Math.Pow(Math.Pow(a, 2.2) * (1 - t) + Math.Pow(b, 2.2) * t, 1 / 2.2), MidpointRounding.AwayFromZero) << shift;
        }
        return result;
    }

    private void RevealDifficultyTab() => revealDifficultyTabs = true;

    private static string TabName(string name)
    {
        int[] characters = System.Globalization.StringInfo.ParseCombiningCharacters(name);
        return characters.Length > 16 ? name[..characters[16]] + "…" : name;
    }

    private static void DrawChromeTab(ICanvas c, Rect rect, uint colour)
    {
        const float radius = 8;
        c.Fill(rect, colour, radius);
        c.Fill(new(rect.X, rect.Y + radius, rect.Width, rect.Height - radius), colour);
        // Chrome-style concave shoulders join the tab to the content below.
        foreach (bool left in new[] { true, false })
        {
            var shoulder = new Rect(left ? rect.X - radius : rect.Right, rect.Bottom - radius, radius, radius);
            c.Clip(shoulder);
            c.Fill(shoulder, colour);
            c.Circle(left ? shoulder.X : shoulder.Right, shoulder.Y, radius, 0x191E26);
            c.Unclip();
        }
    }

    private void DrawDifficultyTabs(ICanvas c)
    {
        difficultyTabStrip = new(12, 84, Math.Max(220, width - 24), 44);
        c.Fill(new(0, 84, width, 44), 0x191E26);
        c.Line(0, 127, width, 127, Grid);
        string[] names = difficulties.Select(d => TabName(d.Name)).ToArray();
        float[] widths = names.Select(name => (float)Math.Ceiling(c.MeasureText(name, 12, true)) + 102).ToArray();
        bool overflow = widths.Sum() + (widths.Length - 1) * 6 > difficultyTabStrip.Width - 38;
        float available = difficultyTabStrip.Width - (overflow ? 102 : 38);
        firstDifficultyTab = Math.Clamp(firstDifficultyTab, 0, difficulties.Count - 1);
        int CountVisible(int first)
        {
            float used = 0;
            int count = 0;
            for (int i = first; i < widths.Length; i++)
            {
                if (count > 0 && used + widths[i] > available) break;
                used += widths[i] + 6; count++;
            }
            return Math.Max(1, count);
        }
        if (revealDifficultyTabs)
        {
            if (activeDifficulty < firstDifficultyTab) firstDifficultyTab = activeDifficulty;
            while (activeDifficulty >= firstDifficultyTab + CountVisible(firstDifficultyTab)) firstDifficultyTab++;
            revealDifficultyTabs = false;
        }
        visibleDifficultyTabs = CountVisible(firstDifficultyTab);
        float x = difficultyTabStrip.X;
        if (overflow)
        {
            Button(c, new(x, 92, 30, 28), "‹", () => firstDifficultyTab = Math.Max(0, firstDifficultyTab - 1), enabled: firstDifficultyTab > 0);
            x += 32;
        }
        for (int index = firstDifficultyTab; index < firstDifficultyTab + visibleDifficultyTabs; index++)
        {
            int target = index;
            bool active = index == activeDifficulty;
            var rect = new Rect(x, 90, widths[index], 38);
            double? stars = DifficultyRating(index);
            uint colour = DifficultyColour(stars);
            bool hover = rect.Contains(mouseX, mouseY);
            if (active || hover) DrawChromeTab(c, rect, active ? Panel : 0x2B3542u);
            else if (index + 1 != activeDifficulty) c.Line(rect.Right + 3, 100, rect.Right + 3, 117, Grid);
            // A light backing keeps even the official black (9★+) icon readable on dark chrome.
            if (stars >= 6.7) c.Circle(x + 19, 106, 10, 0xE7EBF2);
            c.Image(catchIconPath, new(x + 9, 96, 20, 20), colour);
            c.Text(names[index], x + 36, 98, 12, active ? Foreground : Muted, rect.Width - 101, active);
            c.Text(stars is null ? L.Get("project.starsUnavailable") : L.Get("project.stars", stars.Value),
                rect.Right - 59, 99, 10, active ? Foreground : Muted, 47);
            if (difficulties[index].History.IsDirty) c.Circle(rect.Right - 9, 106, 2.5f, Gold);
            hits.Add(new(rect, () => SwitchDifficulty(target), true));
            x = rect.Right + 6;
        }
        if (overflow)
        {
            Button(c, new(x, 92, 30, 28), "›", () => firstDifficultyTab = Math.Min(difficulties.Count - 1, firstDifficultyTab + 1),
                enabled: firstDifficultyTab + visibleDifficultyTabs < difficulties.Count);
            x += 32;
        }
        difficultyAddButton = new(x, 92, 30, 28);
        Button(c, difficultyAddButton, "+", () => menu = menu == 3 ? -1 : 3, menu == 3);
    }
}
