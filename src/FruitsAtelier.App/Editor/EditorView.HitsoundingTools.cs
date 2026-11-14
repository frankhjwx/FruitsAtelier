using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int hitsoundingBrush = 2;
    private bool hitsoundingAllowNotes => !hitsoundingLocked[0];
    private float hitsoundingToolsWidth;
    private readonly Rect[] hitsoundingToolButtons = new Rect[5];
    internal Rect HitsoundingToolBounds(int index) => hitsoundingToolButtons[index];
    internal int HitsoundingBrush => hitsoundingBrush;
    internal int HitsoundingDisplayedBank
    {
        get
        {
            int bank = 0;
            bool selected = false;
            foreach (var key in hitsoundingMultiSelection)
            {
                if (key.Lane is < 1 or > 4 || !hitsoundingNoteLookup.TryGetValue((key.Id, key.Event), out var note)) continue;
                var sample = hitsoundingResolver!.Describe(note);
                int value = key.Lane == 1 ? sample.NormalSet : sample.AdditionSet;
                if (selected && bank != value) return 0;
                bank = value; selected = true;
            }
            if (hitsoundingNotesSelection)
                foreach (var id in objectSelection)
                foreach (var note in hitsoundingParentNotes.GetValueOrDefault(id) ?? [])
                {
                    var sample = hitsoundingResolver!.Describe(note);
                    if (selected && bank != sample.NormalSet || sample.Additions != 0 && sample.NormalSet != sample.AdditionSet) return 0;
                    bank = sample.NormalSet; selected = true;
                }
            return selected ? bank : hitsoundingBrush;
        }
    }
    internal bool HitsoundingDraw => hitsoundingDraw;
    internal bool HitsoundingBrushEnabled => hitsoundingDraw || hitsoundingMultiSelection.Count > 0
        || hitsoundingNotesSelection && objectSelection.Count > 0;
    private void SetHitsoundingTool(bool draw)
    {
        CancelInteraction();
        hitsoundingDraw = draw;
    }
    private void SetHitsoundingBrush(int bank)
    {
        if (!HitsoundingBrushEnabled || hitsoundingPainting || hitsoundingNoteGesture != HitsoundingNoteGesture.None) return;
        hitsoundingBrush = bank;
        if (notesLocked || !CommitField()) return;
        var targets = hitsoundingNotesSelection
            ? hitsoundingNotes.Where(n => objectSelection.Contains(n.SourceId) && (n.Kind != CatchObjectKind.Banana || SpinnerHitsounds.IsEndEvent(n)))
                .Select(n => (Note: n, Normal: true, Addition: true))
            : hitsoundingMultiSelection.Where(k => k.Lane is >= 1 and <= 4)
                .GroupBy(k => (k.Id, k.Event))
                .Where(g => hitsoundingNoteLookup.ContainsKey(g.Key))
                .Select(g => (Note: hitsoundingNoteLookup[g.Key], Normal: g.Any(k => k.Lane == 1), Addition: g.Any(k => k.Lane > 1)));
        var changes = targets.Where(t => CanSelectCollaborationObject(t.Note.SourceId)).Select(t =>
        {
            var setting = hitsoundingResolver!.EventSettings(t.Note);
            var sample = setting.Sample;
            var inherited = setting.InheritedFields;
            if (t.Normal && !t.Addition && sample.Additions != 0 && inherited.HasFlag(InheritedSampleFields.AdditionSet))
            {
                sample = sample with { AdditionSet = hitsoundingResolver.Describe(t.Note).AdditionSet };
                inherited &= ~InheritedSampleFields.AdditionSet;
            }
            if (t.Normal) { sample = sample with { NormalSet = bank }; inherited &= ~InheritedSampleFields.NormalSet; }
            if (t.Addition) { sample = sample with { AdditionSet = bank }; inherited &= ~InheritedSampleFields.AdditionSet; }
            return (Before: setting, After: setting with { Sample = sample, InheritedFields = inherited });
        }).Where(t => t.Before != t.After).Select(t => t.After).ToArray();
        if (changes.Length > 0)
            Edit(L.Get("samples.change"), () => { foreach (var change in changes) SetHitsoundingEvent(change); });
    }
    private void DrawHitsoundingTools(ICanvas c, Rect bounds)
    {
        const float padding = 10;
        float size = Math.Min(bounds.Width - 12, Math.Min(72, Math.Max(18, (bounds.Height - 2 * padding) / 5.72f)));
        float gap = size * .11f, groupGap = size * .39f;
        float total = 5 * size + 3 * gap + groupGap;
        float x = bounds.X + (bounds.Width - size) / 2, y = bounds.Y + Math.Max(padding, (bounds.Height - total) / 2);
        string[] names = ["select", "draw", "soft", "normal", "drum"];
        string[] keys = ["hitsounding.select", "hitsounding.draw", "timing.soft", "timing.normal", "timing.drum"];
        string[] commands = ["hitSelect", "hitDraw", "hitWhistle", "hitNormal", "hitFinish"];
        int[] banks = [0, 0, 2, 1, 3];
        int displayedBank = HitsoundingDisplayedBank;
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            if (i == 2) { y += groupGap - gap; c.Line(x + 8, y - groupGap / 2, x + size - 8, y - groupGap / 2, Grid); }
            var box = new Rect(x, y, size, size - 2);
            hitsoundingToolButtons[i] = box;
            bool enabled = i < 2 || HitsoundingBrushEnabled;
            bool active = enabled && (i < 2 ? hitsoundingDraw == (i == 1) : displayedBank == banks[i]);
            uint color = !enabled ? Muted : i < 2 ? Accent : HitsoundingBankColor(banks[i]);
            c.Fill(box, active ? Surface : Panel, 6);
            c.StrokeOpacity(box, color, active ? 2 : 1, 6, active ? 1 : .35f);
            if (i == 0)
                DrawHitsoundingSelectIcon(c, new(x + size * .28f, y + size * .12f, size * .44f, size * .44f), active ? 1 : .55f);
            else
                c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "hitsounding", names[i] + ".png"),
                    new(x + size * .22f, y + 3, size * .56f, size * .56f), opacity: !enabled ? .18f : active ? 1 : .55f);
            string shortcut = ShortcutKeys(commands[i], ShortcutScope.Hitsounding, first: true);
            float keyFont = Math.Min(10, (size - 10) / Math.Max(1, c.MeasureText(shortcut, 10, true)) * 10);
            if (shortcut.Length > 0) c.Text(shortcut, x + 5, y + 4, keyFont, color, size - 10, true);
            string label = L.Get(keys[i]);
            float font = Math.Clamp(size * .17f, 8, 11);
            c.Text(label, x + Math.Max(2, (size - c.MeasureText(label, font, true)) / 2), y + size * .73f,
                font, active ? Foreground : Muted, size - 4, true);
            if (enabled) hits.Add(new(box, () => { if (index < 2) SetHitsoundingTool(index == 1); else SetHitsoundingBrush(banks[index]); }, true));
            y += size + gap;
        }
    }
    private void DrawHitsoundingSelectIcon(ICanvas c, Rect r, float opacity)
    {
        float width = Math.Max(1, r.Width * .055f);
        void Line(float x1, float y1, float x2, float y2, uint colour) =>
            c.Line(r.X + x1 * r.Width, r.Y + y1 * r.Height, r.X + x2 * r.Width, r.Y + y2 * r.Height, colour, width, opacity);
        for (int i = 0; i < 3; i++)
        {
            float from = i * .32f, to = from + .18f;
            Line(from, 0, to, 0, Accent); Line(0, from, 0, to, Accent);
            if (i < 2) { Line(from, .82f, to, .82f, Accent); Line(.82f, from, .82f, to, Accent); }
        }
        c.Polyline(HitsoundingSelectCursor, r.X, r.Y, r.Width, Foreground, width, opacity);
    }
    private static readonly System.Numerics.Vector2[] HitsoundingSelectCursor =
        [new(.45f, .35f), new(.95f, .73f), new(.72f, .75f), new(.86f, .96f),
         new(.75f, 1), new(.61f, .79f), new(.5f, .94f), new(.45f, .35f)];
    private void DrawHitsoundingToolTooltip(ICanvas c)
    {
        if (menu >= 0 || contextItems.Count > 0 || WantsCapture) return;
        for (int i = 0; i < hitsoundingToolButtons.Length; i++)
        {
            var bounds = hitsoundingToolButtons[i];
            if (!bounds.Contains(mouseX, mouseY)) continue;
            string text = i < 2 ? L.Get(i == 0 ? "hitsounding.hint.select" : "hitsounding.hint.draw")
                : L.Get("hitsounding.hint.brush", L.Get(i == 2 ? "timing.soft" : i == 3 ? "timing.normal" : "timing.drum"));
            string command = i switch { 0 => "hitSelect", 1 => "hitDraw", 2 => "hitWhistle", 3 => "hitNormal", _ => "hitFinish" };
            text = ShortcutHint(text, command, ShortcutScope.Hitsounding);
            if (i >= 2 && ShortcutKeys("cycleSamples", ShortcutScope.Hitsounding).Length > 0)
                text += "\n" + ShortcutHint(L.Get("shortcuts.description.cycleSamples"), "cycleSamples", ShortcutScope.Hitsounding);
            DrawPaletteTooltip(c, text, bounds, left: false, above: false);
        }
    }
}
