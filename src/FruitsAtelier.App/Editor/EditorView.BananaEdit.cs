using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly Dictionary<Guid, IReadOnlyList<BananaShower>> bananaSegments = [];
    private void RefreshBananaSegments()
    {
        bananaSegments.Clear();
        var timing = renderedTiming ?? new TimingMap.Lookup(Document);
        foreach (var shower in Document.BananaShowers.Where(s => s.Pattern is not null))
            try { bananaSegments[shower.Id] = BananaShowerPattern.Segments(shower, timing); }
            catch (InvalidDataException) { }
    }
    public bool BananaEditVisible { get; private set; }
    internal bool BananaPreviewReady => bananaEditReady == bananaEditPattern && bananaEditJob is null;
    internal Rect BananaPreviewBounds { get; private set; }
    internal Rect[] BananaNumeratorBounds { get; } = new Rect[2];
    internal IReadOnlyList<ConvertedCatchObject> BananaEditPreview => bananaEditPreview;
    private Guid[] bananaEditTargets = [];
    private BananaShowerPattern bananaEditPattern = new();
    private string[] bananaEditNumbers = ["1", "3"];
    private int bananaEditField = -1, bananaEditDropdown = -1;
    private string bananaEditError = "", bananaNumericError = "";
    private double bananaEditScroll, bananaPreviewZoom = 1;
    private IReadOnlyList<ConvertedCatchObject> bananaEditPreview = [];
    private IReadOnlyList<BananaShower> bananaEditSegments = [];
    private bool bananaEditExactFit;
    private bool bananaSplit;
    private Task<(OsuWriteResult? Output, string Error)>? bananaEditJob;
    private OsuWriteCache bananaEditCache = new();
    private MapDocument? bananaEditSnapshot;
    private BananaShowerPattern? bananaEditRequested, bananaEditReady;
    private bool CanEditBananas => ClipboardInteractionReady && ClipboardSelectedParentIds() is { Count: > 0 } ids
        && ids.All(id => Document.BananaShowers.Any(s => s.Id == id));

    private void CloseBananaEdit()
    {
        BananaEditVisible = false; bananaEditJob = null; bananaEditSnapshot = null;
        bananaEditPreview = []; bananaEditSegments = []; bananaEditCache = new();
        bananaEditField = bananaEditDropdown = -1;
    }

    private void OpenBananaEdit()
    {
        if (!CanEditBananas || notesLocked) return;
        bananaEditTargets = ClipboardSelectedParentIds().ToArray();
        bananaEditPattern = Document.BananaShowers.First(s => s.Id == bananaEditTargets[0]).Pattern ?? new();
        bananaEditNumbers = [bananaEditPattern.LengthNumerator.ToString(CultureInfo.InvariantCulture), bananaEditPattern.GapNumerator.ToString(CultureInfo.InvariantCulture)];
        bananaEditField = bananaEditDropdown = -1; bananaEditScroll = 0; bananaPreviewZoom = 1; bananaEditError = bananaNumericError = "";
        bananaEditPreview = []; bananaEditSegments = []; bananaEditReady = null;
        bananaSplit = false;
        bananaEditJob = null; bananaEditCache = new();
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        BananaEditVisible = true;
        PumpBananaPreview();
    }

    private void PumpBananaPreview()
    {
        if (!BananaEditVisible && patternDockKind != 2) return;
        if (bananaEditJob is { IsCompleted: false }) return;
        if (bananaEditJob is { } job)
        {
            var result = job.GetAwaiter().GetResult(); bananaEditJob = null;
            if (bananaEditRequested == bananaEditPattern && bananaEditSnapshot!.ContentEquals(Document))
            {
                bananaEditReady = bananaEditRequested;
                bananaEditError = result.Error;
                patternCanvasPreview = result.Output?.PlayableObjects ?? [];
                bananaEditPreview = result.Output?.PlayableObjects.Where(o => o.SourceId == bananaEditTargets[0]).ToArray() ?? [];
            }
        }
        if (bananaEditReady == bananaEditPattern) return;
        bananaEditReady = null; bananaEditError = ""; bananaEditPreview = [];
        bananaEditSnapshot = Document.DeepClone();
        var candidate = bananaEditSnapshot.DeepClone();
        foreach (var shower in candidate.BananaShowers.Where(s => bananaEditTargets.Contains(s.Id))) shower.Pattern = bananaEditPattern;
        bananaEditRequested = bananaEditPattern;
        var cache = bananaEditCache;
        bool compensate = compensateTinyDroplets;
        try
        {
            var timing = new TimingMap.Lookup(candidate);
            (bananaEditSegments, bananaEditExactFit) = BananaShowerPattern.Layout(candidate.BananaShowers.First(s => s.Id == bananaEditTargets[0]), timing);
            if (candidate.BananaShowers.Where(s => bananaEditTargets.Contains(s.Id))
                .Any(s => s.Id == bananaEditTargets[0] ? bananaEditSegments.Count == 0 : BananaShowerPattern.Segments(s, timing).Count == 0))
                throw new InvalidDataException(L.Get("bananaEdit.tooShort"));
        }
        catch (InvalidDataException error) { bananaEditError = error.Message; bananaEditReady = bananaEditPattern; return; }
        bananaEditJob = Task.Run(() =>
        {
            try
            {
                var output = OsuBeatmapWriter.Serialize(candidate, compensate, cache);
                return output.ObjectSequenceMatches ? (output, "") : ((OsuWriteResult?)null, string.Join("\n", output.Diagnostics));
            }
            catch (Exception error) { return ((OsuWriteResult?)null, error.Message); }
        });
    }

    private bool CommitBananaNumber()
    {
        if (!int.TryParse(bananaEditNumbers[0], out int length) || length < 1
            || !int.TryParse(bananaEditNumbers[1], out int gap) || gap < 0)
        { bananaNumericError = L.Get("bananaEdit.invalid"); return false; }
        bananaEditPattern = bananaEditPattern with { LengthNumerator = length, GapNumerator = gap };
        bananaEditField = -1; bananaNumericError = ""; PumpBananaPreview(); return true;
    }
    private void BananaEditText(string text)
    {
        if (bananaEditField < 0) return;
        string digits = new(text.Where(char.IsAsciiDigit).ToArray());
        bananaEditNumbers[bananaEditField] = InsertInput("banana:" + bananaEditField, bananaEditNumbers[bananaEditField], digits, 10);
    }
    private void BananaEditKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { CloseBananaEdit(); return; }
        if (key == 9)
        {
            int next = (bananaEditField + 1) % 2;
            if (CommitBananaNumber()) { bananaEditField = next; SelectInput("banana:" + next, bananaEditNumbers[next]); }
            return;
        }
        if (key == 13) { if (bananaEditField >= 0) CommitBananaNumber(); else ApplyBananaEdit(); return; }
        if (bananaEditField >= 0) InputKey("banana:" + bananaEditField, ref bananaEditNumbers[bananaEditField], key, ctrl, shift, 10, RequestPasteField);
    }
    private void ApplyBananaEdit()
    {
        if (!CommitBananaNumber() || bananaEditReady != bananaEditPattern || bananaEditError.Length > 0) return;
        if (bananaEditSnapshot?.ContentEquals(Document) != true) { bananaEditReady = null; PumpBananaPreview(); return; }
        Guid[] selected = bananaEditTargets;
        if (Edit(L.Get("bananaEdit.title"), () =>
        {
            foreach (var shower in Document.BananaShowers.Where(s => bananaEditTargets.Contains(s.Id))) shower.Pattern = bananaEditPattern;
            if (bananaSplit) selected = ObjectStructureEditing.BreakBananaShowers(Document, bananaEditTargets);
        })) { CloseBananaEdit(); SelectObjects(selected); tool = Tool.Select; }
    }
    internal double BananaPreviewScale => bananaPreviewZoom * .35 * BananaPreviewBounds.Width / 300;
    internal double BananaPreviewVisibleStart => (bananaEditSegments.FirstOrDefault()?.TimeMs ?? 0) + bananaEditScroll;
    private void ZoomBananaPreview(float delta, float y)
    {
        double offset = Math.Clamp(BananaPreviewBounds.Bottom - 16 - y, 0, BananaPreviewBounds.Height);
        double time = bananaEditScroll + offset / BananaPreviewScale;
        bananaPreviewZoom = Math.Clamp(bananaPreviewZoom * Math.Pow(1.2, delta / 120), .125, 8);
        bananaEditScroll = Math.Clamp(time - offset / BananaPreviewScale, 0, BananaPreviewScrollMaximum);
    }
    private double BananaPreviewScrollMaximum => bananaEditSegments.Count == 0 ? 0 : Math.Max(0,
        bananaEditSegments[^1].EndTimeMs - bananaEditSegments[0].TimeMs - (BananaPreviewBounds.Height - 32) / BananaPreviewScale);
    private void DrawBananaEdit(ICanvas c, Rect? dock = null)
    {
        if (dock is null && !BananaEditVisible) return;
        PumpBananaPreview(); if (dock is null) hits.Clear();
        float w = Math.Min(760, width - 32), h = Math.Min(530, height - 32), x = (width - w) / 2, y = (height - h) / 2;
        if (dock is { } area) { x = area.X - 24; y = area.Y; w = area.Width + 48; h = Math.Max(490, area.Height); }
        float col = dock is null ? (w - 68) / 2 : w - 48, left = x + 24, right = left + col + 20;
        bool narrowDock = dock is not null && col < 260;
        float extra = narrowDock ? 86 : 0;
        if (dock is null)
        {
            c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Grid, radius: 8);
            c.Text(L.Get("bananaEdit.title"), left, y + 20, 16, Foreground, w - 48, true);
        }
        c.Text(L.Get("bananaEdit.beats"), left, y + 60, 12, Muted, col);
        for (int i = 0; i < 2; i++)
        {
            int field = i; float row = y + 104 + (narrowDock ? i * 86 : 0), groupWidth = narrowDock ? col : (col - 20) / 2, gx = left + (narrowDock ? 0 : i * (groupWidth + 20));
            c.Text(L.Get(i == 0 ? "bananaEdit.length" : "bananaEdit.gap"), gx, row, 12, Foreground, groupWidth);
            var bounds = BananaNumeratorBounds[i] = new(gx, row + 26, (groupWidth - 20) / 2, 32);
            c.Fill(bounds, Surface, 4); c.Stroke(bounds, bananaEditField == i ? Accent : Grid, radius: 4);
            DrawInputText(c, new(bounds.X + 8, bounds.Y + 6, bounds.Width - 16, 20), bananaEditNumbers[i], 13, bananaEditField == i, "banana:" + i);
            hits.Add(new(bounds, () =>
            {
                if (bananaEditField != field && !CommitBananaNumber()) return;
                bananaEditField = field; bananaEditDropdown = -1; SelectInput("banana:" + field, bananaEditNumbers[field]);
            }, true));
            c.Text("/", bounds.Right + 7, bounds.Y + 7, 14, Muted, 13);
            Button(c, new(bounds.Right + 20, bounds.Y, bounds.Width, 32),
                (i == 0 ? bananaEditPattern.LengthDivisor : bananaEditPattern.GapDivisor) + " ▾", () =>
                { if (CommitBananaNumber()) bananaEditDropdown = bananaEditDropdown == field ? -1 : field; });
        }
        long cycleNumerator = (long)bananaEditPattern.LengthNumerator * bananaEditPattern.GapDivisor
            + (long)bananaEditPattern.GapNumerator * bananaEditPattern.LengthDivisor;
        long cycleDivisor = bananaEditPattern.LengthDivisor * bananaEditPattern.GapDivisor;
        long a = cycleNumerator, b = cycleDivisor;
        while (b != 0) (a, b) = (b, a % b);
        c.Text(L.Get("bananaEdit.cycle", cycleNumerator / a, cycleDivisor / a), left, y + extra + 194, 12, Foreground, col);
        c.Text(L.Get("bananaEdit.count", bananaEditSegments.Count), left, y + extra + 226, 12, Muted, col);
        c.Text(L.Get("bananaEdit.arrangement", bananaEditSegments.Count, Math.Max(0, bananaEditSegments.Count - 1)), left, y + extra + 252, 11, Muted, col);
        if (bananaEditExactFit) c.Text(L.Get("bananaEdit.exactFit"), left, y + extra + 286, 13, Accent, col, true);
        ToggleSwitch(c, new(left, y + extra + 326, col, 32), L.Get("bananaEdit.split"), bananaSplit, () => bananaSplit = !bananaSplit);
        if (dock is null)
        {
            BananaPreviewBounds = new(right, y + 90, col, h - 170);
            c.Text(L.Get("ui.preview"), right, y + 60, 12, Foreground, col);
            c.Fill(BananaPreviewBounds, Surface); c.Stroke(BananaPreviewBounds, Grid);
            bananaEditScroll = Math.Clamp(bananaEditScroll, 0, BananaPreviewScrollMaximum);
            if (bananaEditSegments.Count > 0 && bananaEditReady == bananaEditPattern)
            {
                double start = bananaEditSegments[0].TimeMs + bananaEditScroll;
                float Y(double time) => BananaPreviewBounds.Bottom - 16 - (float)((time - start) * BananaPreviewScale);
                c.Clip(BananaPreviewBounds);
                foreach (var segment in bananaEditSegments)
                {
                    float top = Y(segment.EndTimeMs), bottom = Y(segment.TimeMs);
                    if (bottom < BananaPreviewBounds.Y || top > BananaPreviewBounds.Bottom) continue;
                    c.Fill(new(right + 1, top, col - 2, Math.Max(1, bottom - top)), Gold, opacity: .08f);
                    c.Line(right, top, right + col, top, Gold, opacity: .5f);
                    c.Line(right, bottom, right + col, bottom, Gold, opacity: .5f);
                }
                foreach (var banana in bananaEditPreview)
                {
                    float by = Y(banana.TimeMs);
                    if (by < BananaPreviewBounds.Y - 20 || by > BananaPreviewBounds.Bottom + 20) continue;
                    DrawCatchObject(c, banana, right + 12 + (float)(banana.X / 512) * (col - 24), by, col - 24, 1);
                }
                c.Unclip();
            }
            if (bananaEditJob is not null) c.Text(L.Get("bananaEdit.calculating"), right + 10, y + 105, 12, Muted, col - 20);
            c.Text(L.Get("conversion.scroll"), right, BananaPreviewBounds.Bottom + 7, 10, Muted, col);
        }
        else BananaPreviewBounds = default;
        if (bananaEditError.Length > 0 || bananaNumericError.Length > 0) c.Text(bananaNumericError.Length > 0 ? bananaNumericError : bananaEditError, left, y + h - 74, 11, Error, w - 48);
        Button(c, new(dock is null ? x + w - 200 : left, y + h - 44, dock is null ? 80 : (col - 8) / 2, 30), L.Get("mac.cancel"), () => { if (dock is null) CloseBananaEdit(); else ResetPatternDock(); });
        Button(c, new(dock is null ? x + w - 112 : left + (col + 8) / 2, y + h - 44, dock is null ? 88 : (col - 8) / 2, 30), L.Get(dock is null ? "stream.confirm" : "library.apply"), () => { ApplyBananaEdit(); if (dock is not null && bananaEditSnapshot is null) ResetPatternDock(); },
            enabled: bananaEditReady == bananaEditPattern && bananaEditError.Length == 0);
        if (bananaEditDropdown >= 0)
        {
            int field = bananaEditDropdown;
            var bounds = BananaNumeratorBounds[field]; float px = bounds.Right + 20, py = bounds.Bottom + 3;
            float listHeight = SnapDivisors.Length * 23;
            py = Math.Min(py, y + h - 52 - listHeight);
            c.Fill(new(px, py, bounds.Width, listHeight), Panel, 3); c.Stroke(new(px, py, bounds.Width, listHeight), Grid);
            for (int i = 0; i < SnapDivisors.Length; i++)
            {
                int value = SnapDivisors[i];
                Button(c, new(px, py + i * 23, bounds.Width, 23), value.ToString(), () =>
                {
                    bananaEditPattern = field == 0 ? bananaEditPattern with { LengthDivisor = value } : bananaEditPattern with { GapDivisor = value };
                    bananaEditDropdown = -1; PumpBananaPreview();
                });
            }
        }
    }
}
