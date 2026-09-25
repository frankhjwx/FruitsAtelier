using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int firstDifficultyTab;
    private float tabRemainder, tabDragX, tabDragStartOffset, tabAvailable;
    private float[] tabWidths = [];
    private bool tabPointer, tabMoved, tabOverflow;
    private int tabPressed;
    private readonly HashSet<int> truncatedTabs = [];

    private bool BeginTabPointer(float x, float y)
    {
        if (!tabOverflow || menu >= 0 || contextItems.Count > 0) return false;
        int hit = difficultyTabTargets.FindIndex(t => t.Bounds.Contains(x, y));
        if (hit < 0) return false;
        tabPointer = true; tabMoved = false; tabPressed = difficultyTabTargets[hit].Index;
        tabDragX = x; tabDragStartOffset = tabWidths.Take(firstDifficultyTab).Sum(w => w + 6) + tabRemainder;
        return true;
    }
    private void MoveTabPointer(float x)
    {
        if (Math.Abs(x - tabDragX) > 4) tabMoved = true;
        if (!tabMoved) return;
        float offset = Math.Clamp(tabDragStartOffset + tabDragX - x, 0, Math.Max(0, tabWidths.Sum(w => w + 6) - 6 - tabAvailable));
        firstDifficultyTab = 0;
        while (firstDifficultyTab < tabWidths.Length - 1 && offset >= tabWidths[firstDifficultyTab] + 6)
            offset -= tabWidths[firstDifficultyTab++] + 6;
        tabRemainder = offset; revealDifficultyTabs = false;
    }
    public Action<string>? RequestOpenExternalPath { get; set; }
    private readonly List<(Rect Bounds, int Index)> difficultyTabTargets = [];

    private bool DifficultyTabContext(float x, float y)
    {
        var target = difficultyTabTargets.FindIndex(t => t.Bounds.Contains(x, y));
        if (target < 0) return false;
        var difficulty = difficulties[difficultyTabTargets[target].Index];
        var entry = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulty.Id);
        string? osu = entry?.Source ?? difficulty.History.Document.SourcePath;
        if (osu is not null && !Path.GetExtension(osu).Equals(".osu", StringComparison.OrdinalIgnoreCase)) osu = null;
        string? catchdiff = entry is null || WorkspaceSession is null ? null : Path.Combine(WorkspaceSession.Directory, entry.File);
        string? folder = catchdiff is not null ? Path.GetDirectoryName(catchdiff) : osu is not null ? Path.GetDirectoryName(osu) : null;
        string? songsFolder = (osu ?? entry?.ExportTarget) is { } source ? Path.GetDirectoryName(source) : null;
        contextItems.Clear(); menu = -1;
        contextItems.Add(new(L.Get("project.openOsu"), () => RequestOpenExternalPath?.Invoke(osu!), File.Exists(osu)));
        contextItems.Add(new(L.Get("project.openCatchdiff"), () => RequestOpenExternalPath?.Invoke(catchdiff!), File.Exists(catchdiff)));
        contextItems.Add(new(L.Get("project.openFolder"), () => RequestOpenExternalPath?.Invoke(folder!), Directory.Exists(folder)));
        contextItems.Add(new(L.Get("project.openSongsFolder"), () => RequestOpenExternalPath?.Invoke(songsFolder!), Directory.Exists(songsFolder)));
        float menuHeight = 12 + contextItems.Count * 32;
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 240)), Math.Clamp(y, 0, Math.Max(0, height - menuHeight)), 240, menuHeight);
        return true;
    }
    private bool revealDifficultyTabs = true;
    private void DrawDifficultyTooltip(ICanvas c)
    {
        if (LibraryVisible || ExportVisible || ErrorVisible || DiscardConfirmationVisible || SliderDialogVisible
            || TimeJumpVisible || languageMenuOpen || menu >= 0 || contextItems.Count > 0 || drag != DragKind.None) return;
        int hovered = difficultyTabTargets.FindIndex(t => t.Bounds.Contains(mouseX, mouseY));
        if (hovered < 0) return;
        string name = difficulties[difficultyTabTargets[hovered].Index].Name;
        if (!truncatedTabs.Contains(difficultyTabTargets[hovered].Index) || tabPointer) return;
        float maxWidth = Math.Min(640, width - 32);
        var lines = new List<string>();
        string line = "";
        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (line.Length > 0 && c.MeasureText(line + element, 12) > maxWidth - 20)
            { lines.Add(line); line = ""; }
            line += element;
        }
        lines.Add(line);
        float boxWidth = Math.Min(maxWidth, lines.Max(s => c.MeasureText(s, 12)) + 20), boxHeight = lines.Count * 18 + 14;
        float x = Math.Clamp(mouseX + 14, 8, Math.Max(8, width - boxWidth - 8));
        float y = Math.Clamp(mouseY + 20, 8, Math.Max(8, height - boxHeight - 8));
        c.Fill(new(x, y, boxWidth, boxHeight), Surface, 5);
        c.Stroke(new(x, y, boxWidth, boxHeight), Muted, 1, 5);
        for (int i = 0; i < lines.Count; i++) c.Text(lines[i], x + 10, y + 7 + i * 18, 12, Foreground, boxWidth - 20);
    }
    private Rect difficultyTabStrip, difficultyAddButton;

    private void OpenDifficultyChooser()
    {
        contextItems.Clear(); menu = -1;
        for (int i = 0; i < difficulties.Count; i++)
        {
            int target = i;
            contextItems.Add(new(difficulties[i].Name, () => SwitchDifficulty(target), true,
                Color: i == activeDifficulty ? Accent : null));
        }
        contextBounds = new(12, 88, 280, 12 + contextItems.Count * 32);
    }
    private int visibleDifficultyTabs = 1;
    private static readonly string catchIconPath = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "osu", "RulesetCatch.png");

    private bool RatingEditInProgress => SliderConversionBusy || draftTrack != Guid.Empty || draftBanana != Guid.Empty
        || drag is DragKind.Objects or DragKind.SliderObject or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.DraftHandle or DragKind.BananaStart or DragKind.BananaEnd;
    private static readonly SemaphoreSlim ratingWorkers = new(1);
    public double? CurrentStarRating => DifficultyRating(activeDifficulty);
    public bool CurrentStarRatingFailed => difficulties[activeDifficulty].RatingFailed;
    public bool CurrentStarRatingRefreshing => RatingRefreshing(activeDifficulty);
    public bool StarRatingsRefreshing
    {
        get
        {
            // Hidden tabs must also retire completed tasks, otherwise the host redraws forever.
            for (int i = 0; i < difficulties.Count; i++)
                if (difficulties[i].RatingTask is { IsCompleted: true }) _ = DifficultyRating(i);
            return difficulties.Any(d => d.RatingTask is not null) || RatingEditInProgress;
        }
    }

    private bool RatingRefreshing(int index) => difficulties[index].RatingTask is not null
        || index == activeDifficulty && RatingEditInProgress;

    private double? DifficultyRating(int index)
    {
        var session = difficulties[index];
        var document = session.History.Document;
        bool matches = session.RatingSnapshot is not null && session.RatingCompensation == compensateTinyDroplets
            && session.RatingSnapshot.ContentEquals(document);
        if (session.RatingTask is { IsCompleted: true } completed)
        {
            session.RatingTask = null;
            if (!matches) session.RatingSnapshot = null;
            if (matches)
            {
                double? result = completed.IsCompletedSuccessfully ? completed.Result : null;
                session.RatingFailed = result is null;
                if (result is { } stars) session.Stars = stars;
                else SetNotice(L.Get("project.starCalculationFailed", session.Name));
            }
        }
        // Keep the previous complete result while a curve or banana shower is unfinished.
        if (index == activeDifficulty && RatingEditInProgress) return session.Stars ?? 0;
        if (session.RatingTask is null && !matches)
        {
            var snapshot = document.DeepClone();
            bool compensation = compensateTinyDroplets;
            session.RatingSnapshot = snapshot; session.RatingCompensation = compensation; session.RatingFailed = false;
            var cancellation = session.RatingCancellation.Token;
            session.RatingTask = Task.Run(async () =>
            {
                try
                {
                    await ratingWorkers.WaitAsync(cancellation);
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var converted = CatchStreamConverter.Convert(snapshot, compensation);
                        if (!converted.Success) return (double?)null;
                        var exported = OsuBeatmapWriter.Serialize(snapshot, compensation);
                        var objects = exported.ObjectSequenceMatches ? exported.PlayableObjects : converted.Objects;
                        return (double?)CatchDifficultyCalculator.Calculate(objects, snapshot.CircleSize).StarRating;
                    }
                    finally { ratingWorkers.Release(); }
                }
                catch (OperationCanceledException) { return null; }
                catch (Exception) { return null; }
            });
        }
        return session.Stars ?? 0;
    }

    private static void DrawRatingSpinner(ICanvas c, float x, float y)
    {
        double angle = Environment.TickCount64 / 140.0;
        for (int i = 0; i < 8; i++)
        {
            double a = angle + i * Math.PI / 4;
            c.Line(x + (float)Math.Cos(a) * 3, y + (float)Math.Sin(a) * 3,
                x + (float)Math.Cos(a) * 5, y + (float)Math.Sin(a) * 5, Accent, 1.5f, (i + 1) / 8f);
        }
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

    private static string FitTabName(ICanvas c, string name, float available)
    {
        if (c.MeasureText(name, 12, true) <= available) return name;
        int[] elements = System.Globalization.StringInfo.ParseCombiningCharacters(name);
        for (int length = elements.Length - 1; length > 0; length--)
            if (c.MeasureText(name[..elements[length]] + "…", 12, true) <= available) return name[..elements[length]] + "…";
        return "…";
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
        difficultyTabTargets.Clear();
        difficultyTabStrip = new(12, 40, Math.Max(220, width - 24), 44);
        c.Fill(new(0, 40, width, 44), 0x191E26);
        c.Line(0, 83, width, 83, Grid);
        truncatedTabs.Clear();
        string[] names = difficulties.Select(d => d.Name).ToArray();
        float[] widths = names.Select(name => (float)Math.Ceiling(c.MeasureText(name, 12, true)) + 102).ToArray();
        float budget = difficultyTabStrip.Width - 38 - (widths.Length - 1) * 6;
        if (widths.Sum() > budget)
        {
            if (difficulties.Count > 8)
                for (int i = 0; i < names.Length; i++) widths[i] = Math.Min(widths[i], (float)Math.Ceiling(c.MeasureText(TabName(names[i]), 12, true)) + 102);
            else
            {
                float low = 132, high = widths.Max();
                for (int n = 0; n < 24; n++)
                {
                    float cap = (low + high) / 2;
                    if (widths.Sum(w => Math.Min(w, cap)) > budget) high = cap; else low = cap;
                }
                for (int i = 0; i < widths.Length; i++) widths[i] = Math.Min(widths[i], low);
            }
        }
        bool overflow = widths.Sum() + (widths.Length - 1) * 6 > difficultyTabStrip.Width - 38;
        float available = difficultyTabStrip.Width - (overflow ? 102 : 38);
        for (int i = 0; i < widths.Length; i++)
        {
            widths[i] = Math.Min(widths[i], available);
            names[i] = FitTabName(c, names[i], widths[i] - 102);
            if (names[i] != difficulties[i].Name) truncatedTabs.Add(i);
        }
        tabWidths = widths; tabAvailable = available; tabOverflow = overflow;
        if (!overflow) { firstDifficultyTab = 0; tabRemainder = 0; }
        firstDifficultyTab = Math.Clamp(firstDifficultyTab, 0, difficulties.Count - 1);
        int CountVisible(int first)
        {
            float used = -tabRemainder;
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
            tabRemainder = 0;
            if (activeDifficulty < firstDifficultyTab) firstDifficultyTab = activeDifficulty;
            while (activeDifficulty >= firstDifficultyTab + CountVisible(firstDifficultyTab)) firstDifficultyTab++;
            revealDifficultyTabs = false;
        }
        visibleDifficultyTabs = CountVisible(firstDifficultyTab);
        float x = difficultyTabStrip.X;
        if (overflow)
        {
            Button(c, new(x, 48, 30, 28), "‹", () => { firstDifficultyTab = Math.Max(0, firstDifficultyTab - 1); tabRemainder = 0; }, enabled: firstDifficultyTab > 0 || tabRemainder > 0);
            x += 32;
        }
        float tabLeft = x;
        if (overflow) c.Clip(new(tabLeft, 40, available, 44));
        x -= tabRemainder;
        for (int index = firstDifficultyTab; index < difficulties.Count && x < tabLeft + available; index++)
        {
            int target = index;
            bool active = index == activeDifficulty;
            var rect = new Rect(x, 46, widths[index], 38);
            var hitRect = new Rect(Math.Max(tabLeft, rect.X), rect.Y, Math.Max(0, Math.Min(tabLeft + available, rect.Right) - Math.Max(tabLeft, rect.X)), rect.Height);
            difficultyTabTargets.Add((hitRect, index));
            double? stars = DifficultyRating(index);
            uint colour = DifficultyColour(stars);
            bool hover = rect.Contains(mouseX, mouseY);
            if (active || hover) DrawChromeTab(c, rect, active ? Panel : 0x2B3542u);
            else if (index + 1 != activeDifficulty) c.Line(rect.Right + 3, 56, rect.Right + 3, 73, Grid);
            // A light backing keeps even the official black (9★+) icon readable on dark chrome.
            if (stars >= 6.7) c.Circle(x + 19, 62, 10, 0xE7EBF2);
            c.Image(catchIconPath, new(x + 9, 52, 20, 20), colour);
            c.Text(names[index], x + 36, 54, 12, active ? Foreground : Muted, rect.Width - 101, active);
            c.Text(stars is null ? L.Get("project.starsUnavailable") : L.Get("project.stars", stars.Value),
                rect.Right - 59, 55, 10, active ? Foreground : Muted, 47);
            if (RatingRefreshing(index)) DrawRatingSpinner(c, rect.Right - 9, 106);
            else if (difficulties[index].RatingFailed) c.Text("!", rect.Right - 12, 54, 12, Error, 10, true);
            else if (difficulties[index].History.IsDirty) c.Circle(rect.Right - 9, 62, 2.5f, Gold);
            hits.Add(new(hitRect, () => SwitchDifficulty(target), true));
            x = rect.Right + 6;
        }
        if (overflow)
        {
            c.Unclip(); x = tabLeft + available + 6;
            Button(c, new(x, 48, 30, 28), "›", () => { firstDifficultyTab = Math.Min(difficulties.Count - 1, firstDifficultyTab + 1); tabRemainder = 0; },
                enabled: firstDifficultyTab + visibleDifficultyTabs < difficulties.Count);
            x += 32;
        }
        difficultyAddButton = new(x, 48, 30, 28);
        Button(c, difficultyAddButton, "+", () => menu = menu == 3 ? -1 : 3, menu == 3);
    }
}
