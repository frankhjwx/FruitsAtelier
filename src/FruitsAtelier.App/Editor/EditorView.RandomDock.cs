using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record RandomDraft(bool Derandomize, bool HardRock, double Strength, int Seed);
    private MapDocument? randomDockSnapshot;
    private bool randomDerandomize, randomHardRock;
    private string[] randomNumbers = ["20", "0"];
    private int randomField = -1;
    private Rect randomBounds, randomStrengthBounds;
    private float randomScroll;
    private bool randomDragging;
    private RandomDraft? randomBaseline;
    private string randomError = "";
    private const float RandomContentHeight = 480;
    internal Rect RandomDockViewport => randomBounds;
    internal Rect RandomDockValueBounds(int field) => new(randomBounds.X, randomBounds.Y - randomScroll + (field == 0 ? 142 : 244),
        field == 0 ? Math.Min(78, randomBounds.Width * .35f) : randomBounds.Width, 32);
    private bool RandomDockEditing => DockContentInputAvailable && PanelVisible(DockPanel.RandomizeDroplets)
        && ReferenceEquals(randomDockSnapshot, convertedSnapshot);
    private bool RandomDockFocused => RandomDockEditing && randomField >= 0;
    internal bool RandomDockValid => RandomDockEditing && ReadRandomDraft() is not null;

    internal void ShowRandomDock()
    {
        AddDockPanel(DockPanel.RandomizeDroplets, false);
        var group = PanelGroup(DockPanel.RandomizeDroplets); group.Active = DockPanel.RandomizeDroplets; group.Collapsed = false;
        menu = -1; contextItems.Clear(); randomDockSnapshot = null;
    }
    private RandomDraft? ReadRandomDraft()
    {
        if (!double.TryParse(randomNumbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var strength)
            || !double.IsFinite(strength) || strength < 0 || strength > 100
            || !int.TryParse(randomNumbers[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) return null;
        return new(randomDerandomize, randomHardRock, strength, seed);
    }
    private void SetRandomDraft(MapDocument target, RandomDraft draft)
    {
        if (target.DerandomizeFSliderDroplets != draft.Derandomize) target.RandomizeNewSliders = !draft.Derandomize;
        target.DerandomizeFSliderDroplets = draft.Derandomize;
        target.DerandomizeDropletsForHardRock = draft.HardRock;
        target.RandomizeDropletStrength = draft.Strength; target.RandomizeDropletSeed = draft.Seed;
    }
    private void PumpRandomDock()
    {
        if (!DockContentInputAvailable || !PanelVisible(DockPanel.RandomizeDroplets)) return;
        AdoptLiveDock(ref randomDockSnapshot, ref liveRandomSnapshot);
        if (!ReferenceEquals(randomDockSnapshot, convertedSnapshot))
        {
            randomDockSnapshot = convertedSnapshot;
            randomDerandomize = Document.DerandomizeFSliderDroplets; randomHardRock = Document.DerandomizeDropletsForHardRock;
            randomNumbers = [Document.RandomizeDropletStrength.ToString(CultureInfo.InvariantCulture), Document.RandomizeDropletSeed.ToString(CultureInfo.InvariantCulture)];
            randomField = -1; randomDragging = false; randomScroll = 0;
            randomBaseline = ReadRandomDraft(); randomError = "";
        }
        randomError = ReadRandomDraft() is null ? L.Get("randomize.invalid") : "";
    }
    private void CommitRandomDock()
    {
        if (!RandomDockEditing || notesLocked || ReadRandomDraft() is not { } draft || draft == randomBaseline) return;
        if (Edit(L.Get("randomize.title"), () => SetRandomDraft(Document, draft)))
        { randomBaseline = draft; liveRandomSnapshot = Document.DeepClone(); RememberDropletSettings(); }
    }
    private void RunRandomDockAction(Action action)
    {
        if (!RandomDockEditing || notesLocked) return;
        CommitRandomDock();
        if (Edit(L.Get("randomize.title"), action))
        {
            randomBaseline = ReadRandomDraft();
            liveRandomSnapshot = Document.DeepClone();
            RememberDropletSettings();
        }
    }
    private void DrawRandomDock(ICanvas c, Rect bounds)
    {
        randomBounds = bounds;
        if (!RandomDockEditing) return;
        randomScroll = Math.Clamp(randomScroll, 0, Math.Max(0, RandomContentHeight - bounds.Height));
        float y = bounds.Y - randomScroll;
        int firstHit = hits.Count; c.Clip(bounds);
        bool editable = !notesLocked;
        c.Text(L.Get("randomize.currentDifficulty"), bounds.X, y, 12, Foreground, bounds.Width, true);
        y += 24;
        Button(c, new(bounds.X, y, bounds.Width, 38), L.Get(randomDerandomize ? "randomize.derandomizeOn" : "randomize.derandomizeOff"), () => RunRandomDockAction(() =>
        {
            randomDerandomize = !randomDerandomize;
            Document.DerandomizeFSliderDroplets = randomDerandomize;
            Document.RandomizeNewSliders = !randomDerandomize;
        }), randomDerandomize, editable, fontSize: 12);
        Button(c, new(bounds.X, y + 44, bounds.Width, 38), L.Get(randomHardRock ? "randomize.derandomizeHrOn" : "randomize.derandomizeHrOff"), () => RunRandomDockAction(() => Document.DerandomizeDropletsForHardRock = randomHardRock = !randomHardRock), randomHardRock, editable, fontSize: 12);
        for (int i = 0; i < 2; i++)
        {
            int field = i; float row = y + 94 + i * 102;
            c.Text(L.Get(i == 0 ? "song.RandomizeDropletStrength" : "song.RandomizeDropletSeed"), bounds.X, row, 12, Foreground, bounds.Width);
            var box = RandomDockValueBounds(i);
            c.Fill(box, Surface, 4); c.Stroke(box, randomField == i ? Accent : Grid, radius: 4);
            DrawInputText(c, new(box.X + 8, box.Y + 7, box.Width - 16, 20), randomNumbers[i], 13, randomField == i, "random:" + i);
            hits.Add(new(box, () => { randomField = field; FocusInput("random:" + field, randomNumbers[field], mouseX); }, editable));
        }
        randomStrengthBounds = new(bounds.X + RandomDockValueBounds(0).Width + 15, y + 122, Math.Max(1, bounds.Width - RandomDockValueBounds(0).Width - 22), 24);
        c.Line(randomStrengthBounds.X, randomStrengthBounds.Y + 12, randomStrengthBounds.Right, randomStrengthBounds.Y + 12, Grid, 4);
        if (double.TryParse(randomNumbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double strength))
            c.Circle(randomStrengthBounds.X + (float)Math.Clamp(strength / 100, 0, 1) * randomStrengthBounds.Width, randomStrengthBounds.Y + 12, 7, Accent);
        Button(c, new(bounds.X, y + 158, bounds.Width, 30), L.Get("randomize.resetStrength"), () => RunRandomDockAction(() => { randomNumbers[0] = "20"; Document.RandomizeDropletStrength = 20; }), enabled: editable, fontSize: 12);
        c.Text(randomError, bounds.X, y + 266, 12, Error, bounds.Width);
        DrawDropletDefaults(c, new(bounds.X, y + 304, bounds.Width, 132));
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--) if (hits[i].Bounds.Y < bounds.Y || hits[i].Bounds.Bottom > bounds.Bottom) hits.RemoveAt(i);
        if (RandomContentHeight > bounds.Height)
        {
            float thumb = Math.Max(20, bounds.Height * bounds.Height / RandomContentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + randomScroll / (RandomContentHeight - bounds.Height) * (bounds.Height - thumb), 3, thumb), Muted, 1);
        }
    }
    private bool RandomDockPointer(float x, float y, int button)
    {
        if (!RandomDockEditing || menu >= 0 || contextItems.Count > 0) return false;
        if (!randomBounds.Contains(x, y)) { randomField = -1; return false; }
        if (button != 0) return true;
        if (!notesLocked && randomStrengthBounds.Contains(x, y)) { randomDragging = true; randomField = -1; MoveRandomStrength(x); return true; }
        for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return true; }
        randomField = -1; return true;
    }
    private void MoveRandomStrength(float x) => randomNumbers[0] = Math.Round(Math.Clamp((x - randomStrengthBounds.X) / randomStrengthBounds.Width, 0, 1) * 100, 1).ToString(CultureInfo.InvariantCulture);
    private void RandomDockText(string text)
    {
        if (!RandomDockFocused) return;
        randomNumbers[randomField] = InsertInput("random:" + randomField, randomNumbers[randomField], new string(text.Where(c => char.IsAsciiDigit(c) || c is '.' or '-' or '+').ToArray()), 16);
    }
}
