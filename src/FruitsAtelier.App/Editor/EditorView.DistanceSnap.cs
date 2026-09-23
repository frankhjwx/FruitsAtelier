using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool DistanceSnapDialogVisible { get; private set; }
    private bool distanceSnapFlyout, dsSliderShift, dsSnapDragging, dsBaseDragging, dsBaseFocused;
    private bool dsGridInitialized, dsGridSnap;
    private int dsGridSize = 4;
    internal (bool Enabled, int Size) EditorGridSettings => (gridSnap, gridSize);
    private bool dsShowValues;
    private List<double> dsDraft = [];
    private int dsSliderDrag = -1, dsSnapDragStart;
    private double dsDragStart;
    private double dsBaseDraft, dsBaseDragStart;
    private string dsBaseText = "", dsBaseDragText = "";
    private Rect dsDragBounds;
    private double[] dsDragLimits = [];
    private readonly List<Rect> dsPointers = [];
    internal IReadOnlyList<Rect> DistanceSnapPointerBounds => dsPointers;
    internal Rect DistanceSnapTrackBounds { get; private set; }
    internal Rect DistanceSnapBaseTrackBounds { get; private set; }
    internal Rect DistanceSnapBaseFieldBounds { get; private set; }
    internal Rect DistanceSnapPreviewBounds { get; private set; }
    internal IReadOnlyList<MapPoint> DistanceSnapPreviewFruits => dsPreviewFruits;
    private readonly List<MapPoint> dsPreviewFruits = [];
    private double dsBpm, dsReferenceTime;
    private int dsSnap;
    private static readonly uint[] DsColors = [0xC0C0C0, 0x63B99D, 0xD6B365, 0xCE7683];
    internal Rect DistanceSnapSubdivisionBounds { get; private set; }
    private float dsDragX;
    private Rect DistanceSnapDialogBounds => new((width - Math.Min(1040, width - 32)) / 2,
        (height - Math.Min(640, height - 32)) / 2, Math.Min(1040, width - 32), Math.Min(640, height - 32));

    private void DrawDistanceSnapFlyout(ICanvas c)
    {
        var button = assistButtons[5];
        float y = Math.Clamp(button.Y, assistPalette.Y, assistPalette.Bottom - 34);
        var flyout = new Rect(button.X - 154, y, 154, 34);
        var area = new Rect(flyout.X, y, flyout.Width + button.Width, Math.Max(34, button.Height));
        distanceSnapFlyout = button.Contains(mouseX, mouseY) && assistPalette.Contains(mouseX, mouseY)
            || distanceSnapFlyout && area.Contains(mouseX, mouseY);
        if (!distanceSnapFlyout) return;
        c.Fill(flyout, Surface, 4);
        Button(c, new(flyout.X, flyout.Y, 152, 32), L.Get("ds.configure"), OpenDistanceSnapDialog);
    }

    internal void OpenDistanceSnapDialog()
    {
        if (LibraryVisible || IsTestplaying || !PrepareFileOperation()) return;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        dsDraft = Document.DistanceSnapRatios.ToList();
        dsBaseDraft = Document.SliderMultiplier * 100;
        dsBaseText = dsBaseDraft.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (!dsGridInitialized) { dsGridSnap = gridSnap; dsGridSize = gridSize; dsGridInitialized = true; }
        dsSliderDrag = -1; dsSnapDragging = dsBaseDragging = dsBaseFocused = false;
        dsBpm = 60000 / TimingMap.At(Document, playhead).BeatLengthMs;
        dsSnap = divisor; dsReferenceTime = playhead; dsPreviewFruits.Clear();
        DistanceSnapDialogVisible = true;
        hits.Clear(); fields.Clear();
    }

    private void CloseDistanceSnapDialog()
    {
        DistanceSnapDialogVisible = false; dsSliderDrag = -1; dsSnapDragging = dsBaseDragging = dsBaseFocused = false;
        hits.Clear(); fields.Clear();
    }

    private void DistanceSnapKey(int key, bool ctrl, bool shift)
    {
        if (dsBaseFocused)
        {
            if (key is 13 or 27) { dsBaseFocused = false; return; }
            string text = dsBaseText;
            if (InputKey("ds:base", ref text, key, ctrl, shift, 16, RequestPasteField)) SetDistanceBaseText(text);
            return;
        }
        if (key != 27) return;
        if (dsSliderDrag >= 0 || dsSnapDragging || dsBaseDragging) CancelDistanceSnapDrag();
        else CloseDistanceSnapDialog();
    }

    private void SetDistanceSnapSubdivision(float x)
    {
        var r = DistanceSnapSubdivisionBounds;
        int index = (int)MathF.Round(Math.Clamp((x - r.X - 7) / (r.Width - 38), 0, 1) * (SnapDivisors.Length - 1));
        dsSnap = SnapDivisors[index];
    }

    private void DrawDistanceSnapDialog(ICanvas c)
    {
        if (!DistanceSnapDialogVisible) return;
        hits.Clear(); fields.Clear(); dsPointers.Clear();
        var r = DistanceSnapDialogBounds;
        c.Fill(new(0, 84, width, Math.Max(0, height - 84)), Background, opacity: .65f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("ds.title"), r.X + 20, r.Y + 18, 16, Foreground, r.Width - 40, true);
        float split = r.X + r.Width * .62f;
        c.Line(split, r.Y + 56, split, r.Bottom - 64, Grid);
        DrawDistanceBase(c, new(r.X + 20, r.Y + 64, split - r.X - 40, 190));
        c.Line(r.X + 20, r.Y + 258, split - 16, r.Y + 258, Grid);
        var left = new Rect(r.X, r.Y + 294, split - r.X - 84, r.Height - 308);
        c.Text(L.Get("ds.presetsTitle"), r.X + 20, r.Y + 269, 12, Foreground, split - r.X - 40, true);
        DrawDistanceSnapReference(c, left);
        Button(c, new(split - 82, left.Y + 54, 72, 30), L.Get("ds.add"), () => AddDistanceSnapPreset(0),
            enabled: dsDraft.Count < DistanceSnap.MaximumPresets);
        c.Text(L.Get("ds.presets", dsDraft.Count, DistanceSnap.MaximumPresets), r.X + 20, r.Y + 448, 12, Foreground);
        float textX = r.X + 20, textY = r.Y + 480;
        foreach (var entry in dsDraft.Select((value, index) => (value, index)).OrderBy(entry => entry.value))
        {
            string label = L.Get("ds.ratio", entry.value);
            float labelWidth = c.MeasureText(label, 14) + 24;
            if (textX + labelWidth > split - 16) { textX = r.X + 20; textY += 40; }
            uint color = DistanceSnapColor(entry.value);
            var badge = new Rect(textX, textY, labelWidth, 32);
            c.Fill(badge, Surface, 4);
            c.Stroke(badge, color, entry.index == dsSliderDrag ? 2 : 1, 4);
            c.Text(label, textX + 12, textY + 7, 14, Foreground);
            textX += labelWidth + 8;
        }
        c.Text(L.Get("ui.snap"), split + 22, r.Y + 69, 11, Muted, 40);
        DistanceSnapSubdivisionBounds = new(split + 62, r.Y + 60, r.Right - split - 246, 29);
        var snapBounds = DistanceSnapSubdivisionBounds;
        float snapStart = snapBounds.X + 7, snapEnd = snapBounds.Right - 31;
        float snapX = snapStart + Array.IndexOf(SnapDivisors, dsSnap) / (float)(SnapDivisors.Length - 1) * (snapEnd - snapStart);
        c.Line(snapStart, snapBounds.Y + 15, snapEnd, snapBounds.Y + 15, Accent, 2);
        c.Circle(snapX, snapBounds.Y + 15, 6, Accent);
        c.Text(L.Get("ui.snapDivisor", dsSnap), snapBounds.Right - 28, snapBounds.Y + 9, 10, Foreground, 40);
        hits.Add(new(snapBounds, () =>
        {
            dsSnapDragStart = dsSnap; dsSnapDragging = true;
            SetDistanceSnapSubdivision(mouseX);
        }, true));
        Button(c, new(r.Right - 80, r.Y + 60, 64, 29), L.Get("ds.reset"), dsPreviewFruits.Clear);
        Button(c, new(r.Right - 176, r.Y + 60, 88, 29), L.Get("ds.showValues"),
            () => dsShowValues = !dsShowValues, dsShowValues);
        DistanceSnapPreviewBounds = new(split + 16, r.Y + 108, r.Right - split - 32, r.Height - 180);
        DrawDistanceSnapPreview(c);
        Button(c, new(r.Right - 196, r.Bottom - 44, 80, 30), L.Get("mac.cancel"), CloseDistanceSnapDialog);
        bool validBase = DistanceBaseValue(dsBaseText) is not null;
        Button(c, new(r.Right - 108, r.Bottom - 44, 88, 30), L.Get("library.apply"), () =>
        {
            if (Edit(L.Get("ds.title"), () =>
            {
                Document.DistanceSnapRatios.Clear();
                Document.DistanceSnapRatios.AddRange(dsDraft.Order());
                Document.SliderMultiplier = dsBaseDraft / 100;
            })) CloseDistanceSnapDialog();
        }, enabled: validBase);

    }

    private static double? DistanceBaseValue(string text)
        => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) && value is >= 1 and <= 512 ? value : null;

    private void SetDistanceBaseText(string text)
    {
        dsBaseText = text;
        if (DistanceBaseValue(text) is { } value) dsBaseDraft = value;
    }

    private void UpdateDistanceBase(float x)
    {
        var track = DistanceSnapBaseTrackBounds;
        double value = 1 + Math.Clamp((x - track.X) / track.Width, 0, 1) * 511;
        value = dsGridSnap ? Math.Clamp(Math.Round(value / dsGridSize, MidpointRounding.AwayFromZero) * dsGridSize, dsGridSize, 512)
            : Math.Round(value, MidpointRounding.AwayFromZero);
        dsBaseDraft = value;
        dsBaseText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void DrawDistanceBase(ICanvas c, Rect r)
    {
        c.Text(L.Get("ds.baseTitle"), r.X + 4, r.Y + 2, 12, Foreground, r.Width - 8, true);
        DistanceSnapBaseTrackBounds = new(r.X + 4, r.Y + 36, r.Width - 8, 24);
        var track = DistanceSnapBaseTrackBounds;
        double[] limits = DistanceSnapLimits();
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
        float Position(double dpb) => track.X + (float)Math.Clamp((dpb - 1) / 511, 0, 1) * track.Width;
        for (int i = 0; i < 4; i++)
        {
            float start = Position(limits[i] * dsBaseDraft);
            float end = i == 3 ? track.Right : Position(limits[i + 1] * dsBaseDraft);
            if (end <= start) continue;
            c.Fill(new(start, track.Y + 3, end - start, 18), DsColors[i], 2);
            if (end - start >= 52) c.Text(L.Get(names[i]), start + 5, track.Y + 6, 10, Background, end - start - 10, true);
        }
        float marker = Position(dsBaseDraft);
        c.Line(marker, track.Y - 4, marker, track.Bottom + 4, Foreground, 2);
        hits.Add(new(track, () =>
        {
            dsBaseFocused = false; dsBaseDragging = true;
            dsBaseDragStart = dsBaseDraft; dsBaseDragText = dsBaseText;
            UpdateDistanceBase(mouseX);
        }, true));
        c.Text(L.Get("ui.dpb"), r.X + 4, r.Y + 87, 12, Foreground, 48);
        DistanceSnapBaseFieldBounds = new(r.X + 60, r.Y + 80, 106, 30);
        var field = DistanceSnapBaseFieldBounds;
        c.Fill(field, Surface, 4);
        c.Stroke(field, DistanceBaseValue(dsBaseText) is null ? Error : dsBaseFocused ? Accent : Grid, radius: 4);
        DrawInputText(c, new(field.X + 9, field.Y + 5, field.Width - 18, 20), dsBaseText, 13, dsBaseFocused, "ds:base");
        hits.Add(new(field, () => { dsBaseFocused = true; FocusInput("ds:base", dsBaseText, mouseX); }, true));
        c.Text("px", field.Right + 8, field.Y + 8, 12, Muted, 26);
        var gridRow = new Rect(r.X + 4, r.Y + 116, r.Width - 8, 32);
        c.Text(L.Get("ui.gridSnap"), gridRow.X, gridRow.Y + 8, 12, Foreground, 126);
        var switchTrack = new Rect(r.X + 136, gridRow.Y + 4, 46, 24);
        c.Fill(switchTrack, dsGridSnap ? 0x417D77u : 0x46515Fu, 12);
        c.Circle(switchTrack.X + (dsGridSnap ? 34 : 12), switchTrack.Y + 12, 9, 0xF0F3F6);
        hits.Add(new(gridRow, () => dsGridSnap = !dsGridSnap, true));
        c.Text(L.Get("ui.gridLevel", L.Get("ui.grid" + dsGridSize)), r.X + 4, r.Y + 161, 12, Foreground, 126);
        for (int i = 0; i < 4; i++)
        {
            int size = 4 << i;
            Button(c, new(r.X + 136 + i * 70, r.Y + 153, 64, 28), $"{size} px", () => dsGridSize = size, size == dsGridSize);
        }
    }

    private void AddDistanceSnapPreset(double ratio)
    {
        if (dsDraft.Count >= DistanceSnap.MaximumPresets) return;
        dsDraft.Add(ratio);
    }

    private void DrawDistanceSnapPreview(ICanvas c)
    {
        var r = DistanceSnapPreviewBounds;
        c.Fill(r, Background, 4);
        c.Clip(r);
        for (int i = 0; i <= 4 * dsSnap; i++)
        {
            int fraction = i % dsSnap, subdivision = dsSnap;
            if (fraction == 0) subdivision = 1;
            else
                for (int d = 2; d <= dsSnap; d++)
                    if (dsSnap % d == 0 && fraction % (dsSnap / d) == 0) { subdivision = d; break; }
            var style = GridStyle(new(0, fraction == 0, false, subdivision, i % (4 * dsSnap) == 0));
            float y = DistanceSnapPreviewY(i / (double)dsSnap);
            c.Line(r.X, y, r.Right, y, style.Color, style.Width, .5f);
        }
        MapPoint? candidate = r.Contains(mouseX, mouseY) && dsSliderDrag < 0 && !dsSnapDragging
            ? DistanceSnapPreviewCandidate(mouseX, mouseY) : null;
        float radius = Math.Clamp((float)(CatchSize.CatchWidth(Document.CircleSize) / 2 / 512 * (r.Width - 20)), 3, 12);
        var hyperdashStarts = DrawDistanceSnapPreviewConnections(c, candidate, out var labels);
        foreach (var fruit in dsPreviewFruits)
            c.Circle(DistanceSnapPreviewX(fruit.X), DistanceSnapPreviewY(fruit.TimeMs), radius, hyperdashStarts.Contains(fruit) ? 0xFF5555 : Accent);
        if (candidate is { } ghost)
        {
            c.Circle(DistanceSnapPreviewX(ghost.X), DistanceSnapPreviewY(ghost.TimeMs), radius,
                hyperdashStarts.Contains(ghost) ? 0xFF5555 : Foreground, false, 1.5f, .7f);
        }
        foreach (var label in labels)
        {
            c.Fill(label.Bounds, 0x000000, 3, .7f);
            c.Stroke(label.Bounds, label.Border, 1, 3);
            c.Text(label.Text, label.Bounds.X + 6, label.Bounds.Y + 3, 11, label.Color);
        }
        c.Unclip();
    }

    private HashSet<MapPoint> DrawDistanceSnapPreviewConnections(ICanvas c, MapPoint? candidate,
        out List<(Rect Bounds, string Text, uint Color, uint Border)> labels)
    {
        labels = [];
        var fruits = dsPreviewFruits.ToList();
        if (candidate is { } ghost && !fruits.Contains(ghost)) fruits.Add(ghost);
        var ordered = fruits.OrderBy(f => f.TimeMs).ToArray();
        var objects = ordered.Select((f, i) => new ConvertedCatchObject(Guid.Empty, i, CatchObjectKind.Fruit,
            f.TimeMs * 60000 / dsBpm, f.X, f.X, f.X, 0)).ToArray();
        var states = HyperDashCalculator.Calculate(objects, Document.CircleSize);
        var hyperdashStarts = ordered.Where((_, i) => states[i].IsHyperDash).ToHashSet();
        var r = DistanceSnapPreviewBounds;
        for (int i = 0; i + 1 < ordered.Length; i++)
        {
            var from = ordered[i]; var to = ordered[i + 1];
            float x1 = DistanceSnapPreviewX(from.X), y1 = DistanceSnapPreviewY(from.TimeMs);
            float x2 = DistanceSnapPreviewX(to.X), y2 = DistanceSnapPreviewY(to.TimeMs);
            c.Line(x1, y1, x2, y2, Muted, 1, .6f);
            if (!dsShowValues) continue;
            double? ratio = DistanceSnap.Ratio(from, to, dsBaseDraft);
            string label = ratio is { } value ? L.Get("ds.previewRatio", value) : L.Get("ds.previewUndefined");
            float labelWidth = c.MeasureText(label, 11) + 12;
            float x = Math.Clamp((x1 + x2 - labelWidth) / 2, r.X + 2, r.Right - labelWidth - 2);
            float y = Math.Clamp((y1 + y2) / 2 - 10, r.Y + 2, r.Bottom - 22);
            var bounds = new Rect(x, y, labelWidth, 20);
            bool available = ratio is not { } actual || Math.Abs(actual) < .000001
                || dsDraft.Any(preset => Math.Abs(actual - preset) < .000001);
            labels.Add((bounds, label, available ? Foreground : 0xFF5555,
                ratio is { } borderRatio ? DistanceSnapColor(borderRatio) : Muted));
        }
        return hyperdashStarts;
    }

    private float DistanceSnapPreviewX(double x) => DistanceSnapPreviewBounds.X + 10 + (float)(x / 512) * (DistanceSnapPreviewBounds.Width - 20);
    private float DistanceSnapPreviewY(double beat) => DistanceSnapPreviewBounds.Bottom - 12 - (float)(beat / 4) * (DistanceSnapPreviewBounds.Height - 24);

    private MapPoint DistanceSnapPreviewCandidate(float x, float y)
    {
        var r = DistanceSnapPreviewBounds;
        double beat = Math.Clamp(Math.Floor((r.Bottom - 12 - y) / (r.Height - 24) * 4 * dsSnap + .5), 0, 4 * dsSnap) / dsSnap;
        double px = Math.Clamp((x - r.X - 10) / (r.Width - 20) * 512, 0, 512);
        var point = new MapPoint(beat, px);
        var previous = dsPreviewFruits.Where(f => f.TimeMs < beat).OrderByDescending(f => f.TimeMs).ThenBy(f => Math.Abs(f.X - px)).ToArray();
        // Preview time is measured in beats, so the matching velocity is distance per beat.
        DistanceSnap.Reference? reference = previous.Length == 0 ? null : new(Guid.Empty, previous[0], previous[0], dsBaseDraft, 0);
        return DistanceSnap.SnapMultiple(point, reference, dsDraft, out _);
    }

    private void DistanceSnapPointerDown(float x, float y, int button)
    {
        if (dsSliderDrag >= 0 || dsSnapDragging || dsBaseDragging) return;
        if (!DistanceSnapBaseFieldBounds.Contains(x, y)) dsBaseFocused = false;
        if (button == 2)
        {
            for (int i = dsPointers.Count - 1; i >= 0; i--)
                if (dsPointers[i].Contains(x, y))
                {
                    dsDraft.RemoveAt(i); dsPointers.Clear();
                    return;
                }
        }
        if (DistanceSnapPreviewBounds.Contains(x, y))
        {
            if (button == 0)
            {
                var fruit = DistanceSnapPreviewCandidate(x, y);
                if (!dsPreviewFruits.Contains(fruit)) dsPreviewFruits.Add(fruit);
            }
            else if (button == 2)
            {
                int index = dsPreviewFruits.FindLastIndex(f => Math.Abs(DistanceSnapPreviewX(f.X) - x) <= 14 && Math.Abs(DistanceSnapPreviewY(f.TimeMs) - y) <= 14);
                if (index >= 0) dsPreviewFruits.RemoveAt(index);
            }
            return;
        }
        if (button == 0)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return; }
        }
    }

    private double[] DistanceSnapLimits()
    {
        double duration = 60000 / dsBpm / dsSnap;
        double unit = dsBaseDraft / dsSnap;
        double stand = CatchSize.CatchWidth(Document.CircleSize) / 2;
        // A fresh departure has the full catcher allowance; preceding movement is deliberately excluded.
        double dash = Math.Max(stand, (int)(dsReferenceTime + duration) - (int)dsReferenceTime - 1000f / 60f / 4
            + CatchSize.CatcherWidth(Document.CircleSize) / 2);
        double walk = Math.Min(dash, stand + duration * .5);
        return [0, stand / unit, walk / unit, dash / unit, Math.Max(512, dash) / unit];
    }

    private void BeginDistanceSnapDrag(int row, Rect bounds)
    {
        dsSliderDrag = row; dsDragStart = dsDraft[row];
        dsDragBounds = bounds; dsDragLimits = DistanceSnapLimits();
        UpdateDistanceSnapSlider(mouseX, dsSliderShift);
    }

    private void UpdateDistanceSnapSlider(float x, bool shift)
    {
        dsDragX = x; dsSliderShift = shift;
        dsDraft[dsSliderDrag] = DistanceSnapRatioAt(x, dsDragBounds, dsDragLimits, shift);
    }

    private static double DistanceSnapRatioAt(float x, Rect bounds, double[] limits, bool shift)
    {
        double position = Math.Clamp((x - bounds.X) / bounds.Width, 0, 1) * 4;
        int segment = Math.Min(3, (int)position);
        double ratio = limits[segment] + (limits[segment + 1] - limits[segment]) * (position - segment);
        return Math.Max(0, Math.Round(ratio, shift ? 2 : 1, MidpointRounding.AwayFromZero));
    }

    private void CancelDistanceSnapDrag()
    {
        if (dsSnapDragging) { dsSnap = dsSnapDragStart; dsSnapDragging = false; }
        if (dsBaseDragging) { dsBaseDraft = dsBaseDragStart; dsBaseText = dsBaseDragText; dsBaseDragging = false; }
        if (dsSliderDrag < 0) return;
        dsDraft[dsSliderDrag] = dsDragStart;
        dsSliderDrag = -1;
    }

    private uint DistanceSnapColor(double ratio)
    {
        var limits = dsSliderDrag >= 0 ? dsDragLimits : DistanceSnapLimits();
        for (int i = 0; i < 3; i++)
            if (ratio <= limits[i + 1]) return DsColors[i];
        return DsColors[3];
    }

    private static float DistanceSnapFraction(double ratio, double[] limits)
    {
        for (int i = 0; i < 4; i++)
            if (ratio <= limits[i + 1] && limits[i + 1] > limits[i])
                return (float)((i + Math.Clamp((ratio - limits[i]) / (limits[i + 1] - limits[i]), 0, 1)) / 4);
        return 1;
    }

    private void DrawDistanceSnapReference(ICanvas c, Rect r)
    {
        double[] limits = dsSliderDrag >= 0 ? dsDragLimits : DistanceSnapLimits();
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
        float segment = (r.Width - 40) / 4;
        DistanceSnapTrackBounds = new(r.X + 20, r.Y + 37, r.Width - 40, 20);
        hits.Add(new(DistanceSnapTrackBounds,
            () => AddDistanceSnapPreset(DistanceSnapRatioAt(mouseX, DistanceSnapTrackBounds, DistanceSnapLimits(), dsSliderShift)),
            dsDraft.Count < DistanceSnap.MaximumPresets));
        var labels = new List<Rect>();
        for (int i = 0; i < dsDraft.Count; i++)
        {
            float pointer = r.X + 20 + DistanceSnapFraction(dsDraft[i], limits) * (r.Width - 40);
            uint color = i == dsSliderDrag ? Accent : Foreground;
            string value = L.Get("ds.ratio", dsDraft[i]);
            float labelWidth = c.MeasureText(value, 11);
            var label = new Rect(Math.Clamp(pointer - labelWidth / 2, r.X + 20, r.Right - 20 - labelWidth), r.Y + 22, labelWidth, 13);
            var candidates = new[] { label.X }.Concat(Enumerable.Range(0, (int)((r.Width - 40) / (labelWidth + 6)))
                .Select(column => r.X + 20 + column * (labelWidth + 6)));
            label = candidates.SelectMany(x => Enumerable.Range(0, 4).Select(row => new Rect(x, r.Y + 22 - row * 14, labelWidth, 13)))
                .First(candidate => !labels.Any(other => candidate.X < other.Right + 6 && candidate.Right + 6 > other.X
                    && candidate.Y < other.Bottom && candidate.Bottom > other.Y));
            labels.Add(label);
            c.Text(value, label.X, label.Y, 11, color);
            c.Line(pointer, r.Y + 41, pointer, r.Y + 51, color, 2);
            c.Line(pointer - 4, r.Y + 48, pointer, r.Y + 53, color, 2);
            c.Line(pointer + 4, r.Y + 48, pointer, r.Y + 53, color, 2);
            int row = i;
            var bounds = new Rect(pointer - 8, r.Y + 37, 16, 20);
            dsPointers.Add(bounds);
            hits.Add(new(bounds, () => BeginDistanceSnapDrag(row, DistanceSnapTrackBounds), true));
        }

        for (int i = 0; i < 4; i++)
        {
            float x = r.X + 20 + i * segment;
            c.Fill(new(x, r.Y + 56, segment - 2, 25), DsColors[i], 3);
            c.Text(L.Get(names[i]), x + 6, r.Y + 62, 11, Background, segment - 12, true);
            if (i < 3)
            {
                double maximum = 512 / (dsBaseDraft / dsSnap);
                string mid = limits[i] >= maximum ? "—" : L.Get("assist.ratio", (limits[i] + Math.Min(maximum, limits[i + 1])) / 2);
                float center = x + segment / 2;
                c.Line(center, r.Y + 81, center, r.Y + 93, DsColors[i]);
                c.Text(mid, center - c.MeasureText(mid, 11) / 2, r.Y + 98, 11, Foreground, segment);
            }
            if (i < 3)
            {
                string boundary = L.Get("assist.ratio", limits[i + 1]);
                float edge = x + segment - 1;
                c.Line(edge, r.Y + 81, edge, r.Y + 119, DsColors[i]);
                c.Text(boundary, edge - c.MeasureText(boundary, 11) / 2, r.Y + 124, 11, Foreground, segment);
            }
        }
    }
}
