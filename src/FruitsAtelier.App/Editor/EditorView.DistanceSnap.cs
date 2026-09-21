using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool DistanceSnapDialogVisible { get; private set; }
    private bool distanceSnapFlyout, dsReplace, dsSliderShift;
    private List<double> dsDraft = [];
    private int dsField = -1, dsSliderDrag = -1;
    private double dsDragStart;
    private Rect dsDragBounds;
    private double[] dsDragLimits = [];
    private readonly List<Rect> dsPointers = [];
    internal IReadOnlyList<Rect> DistanceSnapPointerBounds => dsPointers;
    internal Rect DistanceSnapTrackBounds { get; private set; }
    internal Rect DistanceSnapPreviewBounds { get; private set; }
    internal IReadOnlyList<MapPoint> DistanceSnapPreviewFruits => dsPreviewFruits;
    private readonly List<MapPoint> dsPreviewFruits = [];
    private double dsBpm, dsReferenceTime;
    private int dsSnap;
    private static readonly uint[] DsColors = [0xC0C0C0, 0x63B99D, 0xD6B365, 0xCE7683];
    private string dsBuffer = "", dsError = "";
    private float dsDragX;
    private Rect DistanceSnapDialogBounds => new((width - Math.Min(1040, width - 32)) / 2,
        (height - Math.Min(570, height - 32)) / 2, Math.Min(1040, width - 32), Math.Min(570, height - 32));

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
        dsField = dsSliderDrag = -1; dsBuffer = dsError = "";
        dsBpm = 60000 / TimingMap.At(Document, playhead).BeatLengthMs;
        dsSnap = divisor; dsReferenceTime = playhead; dsPreviewFruits.Clear();
        DistanceSnapDialogVisible = true;
        hits.Clear(); fields.Clear();
    }

    private void CloseDistanceSnapDialog()
    {
        DistanceSnapDialogVisible = false; dsField = dsSliderDrag = -1;
        hits.Clear(); fields.Clear();
    }

    private bool CommitDistanceSnapField()
    {
        if (dsField < 0) return true;
        string input = dsField == 1 && dsBuffer.StartsWith("1/", StringComparison.Ordinal) ? dsBuffer[2..] : dsBuffer;
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value) || (dsField == 0 ? value < 1 || value > 1000 : value != Math.Truncate(value) || !SnapDivisors.Contains((int)value)))
        { dsError = L.Get(dsField == 0 ? "ds.invalidBpm" : "ds.invalidSnap"); return false; }
        if (dsField == 0) dsBpm = value; else dsSnap = (int)value;
        dsField = -1; dsError = "";
        return true;
    }

    private void EditDistanceSnapField(int field)
    {
        if (!CommitDistanceSnapField()) return;
        dsField = field; dsReplace = true;
        dsBuffer = field == 0 ? dsBpm.ToString("0.##", CultureInfo.InvariantCulture) : dsSnap.ToString(CultureInfo.InvariantCulture);
    }

    private void DistanceSnapKey(int key, bool ctrl, bool shift)
    {
        if (key == 27)
        {
            if (dsSliderDrag >= 0) CancelDistanceSnapDrag();
            else if (dsField >= 0) { dsField = -1; dsError = ""; }
            else CloseDistanceSnapDialog();
        }
        else if (key == 9 && dsSliderDrag < 0)
        {
            int next = dsField < 0 ? (shift ? 1 : 0) : 1 - dsField;
            if (!CommitDistanceSnapField()) return;
            EditDistanceSnapField(next);
        }
        else if (dsField >= 0)
        {
            if (ctrl && key == 65) dsReplace = true;
            else if (key is 8 or 46) { dsBuffer = dsReplace || key == 46 ? "" : dsBuffer[..Math.Max(0, dsBuffer.Length - 1)]; dsReplace = false; }
            else if (key == 13) CommitDistanceSnapField();
        }
    }

    private void DistanceSnapText(char value)
    {
        if (dsField < 0 || char.IsControl(value)) return;
        if (!char.IsAsciiDigit(value) && value != '.' && !(dsField == 1 && value == '/')) return;
        if (!dsReplace && dsBuffer.Length >= 16) return;
        string current = dsReplace ? "" : dsBuffer;
        int dot = current.IndexOf('.');
        if (value == '.' && dot >= 0 || char.IsAsciiDigit(value) && dot >= 0 && current.Length - dot > 2) return;
        ResetTextCaret();
        dsBuffer = current + value; dsReplace = false; dsError = "";
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
        var left = new Rect(r.X, r.Y + 12, split - r.X - 84, r.Height - 76);
        DrawDistanceSnapReference(c, left);
        Button(c, new(split - 82, left.Y + 54, 72, 30), L.Get("ds.add"), () =>
        {
            if (!CommitDistanceSnapField() || dsDraft.Count >= DistanceSnap.MaximumPresets) return;
            var limits = DistanceSnapLimits();
            dsDraft.Add(Math.Max(.01, Math.Round((limits[1] + limits[2]) / 2, 2, MidpointRounding.AwayFromZero)));
        }, enabled: dsDraft.Count < DistanceSnap.MaximumPresets);
        c.Text(L.Get("ds.presets", dsDraft.Count, DistanceSnap.MaximumPresets), r.X + 20, r.Y + 182, 12, Foreground);
        float textX = r.X + 20, textY = r.Y + 218;
        foreach (var entry in dsDraft.Select((value, index) => (value, index)).OrderBy(entry => entry.value))
        {
            string label = L.Get("ds.ratio", entry.value);
            float labelWidth = c.MeasureText(label, 14) + 24;
            if (textX + labelWidth > split - 16) { textX = r.X + 20; textY += 30; }
            c.Text(label, textX, textY, 14, entry.index == dsSliderDrag ? Accent : Foreground);
            textX += labelWidth;
        }
        float controlWidth = (r.Right - split - 44) / 2;
        DrawPreviewField(new(split + 16, r.Y + 60, controlWidth, 30), 0, L.Get("ds.bpm", dsBpm));
        DrawPreviewField(new(split + 28 + controlWidth, r.Y + 60, controlWidth, 30), 1, L.Get("ds.snap", dsSnap));
        DistanceSnapPreviewBounds = new(split + 16, r.Y + 108, r.Right - split - 32, r.Height - 180);
        DrawDistanceSnapPreview(c);
        if (dsError.Length > 0) c.Text(dsError, r.X + 20, r.Bottom - 65, 11, Error, r.Width - 40);
        Button(c, new(r.Right - 196, r.Bottom - 44, 80, 30), L.Get("mac.cancel"), CloseDistanceSnapDialog);
        Button(c, new(r.Right - 108, r.Bottom - 44, 88, 30), L.Get("library.apply"), () =>
        {
            if (!CommitDistanceSnapField()) return;
            if (Edit(L.Get("ds.title"), () =>
            {
                Document.DistanceSnapRatios.Clear();
                Document.DistanceSnapRatios.AddRange(dsDraft.Order());
            })) CloseDistanceSnapDialog();
        });

        void DrawPreviewField(Rect bounds, int field, string value)
        {
            if (dsField == field)
            {
                c.Fill(bounds, Surface, 3); c.Stroke(bounds, Accent, radius: 3);
                DrawInputText(c, new(bounds.X + 6, bounds.Y + 7, bounds.Width - 12, 20), dsBuffer, 12, true, dsReplace);
            }
            else c.Text(value, bounds.X + 6, bounds.Y + 9, 12, Foreground, bounds.Width - 12);
            hits.Add(new(bounds, () => EditDistanceSnapField(field), true));
        }
    }

    private void DrawDistanceSnapPreview(ICanvas c)
    {
        var r = DistanceSnapPreviewBounds;
        c.Fill(r, Background, 4);
        c.Clip(r);
        for (int i = 0; i < 4 * dsSnap; i++)
        {
            int fraction = i % dsSnap, subdivision = dsSnap;
            if (fraction == 0) subdivision = 1;
            else
                for (int d = 2; d <= dsSnap; d++)
                    if (dsSnap % d == 0 && fraction % (dsSnap / d) == 0) { subdivision = d; break; }
            var style = GridStyle(new(0, fraction == 0, false, subdivision, i == 0));
            float y = DistanceSnapPreviewY(i / (double)dsSnap);
            c.Line(r.X, y, r.Right, y, style.Color, style.Width, .5f);
        }
        float radius = Math.Clamp((float)(CatchSize.CatchWidth(Document.CircleSize) / 2 / 512 * (r.Width - 20)), 3, 12);
        foreach (var fruit in dsPreviewFruits)
            c.Circle(DistanceSnapPreviewX(fruit.X), DistanceSnapPreviewY(fruit.TimeMs), radius, Accent);
        if (r.Contains(mouseX, mouseY) && dsSliderDrag < 0 && dsField < 0)
        {
            var candidate = DistanceSnapPreviewCandidate(mouseX, mouseY);
            c.Circle(DistanceSnapPreviewX(candidate.X), DistanceSnapPreviewY(candidate.TimeMs), radius, Foreground, false, 1.5f, .7f);
        }
        c.Unclip();
    }

    private float DistanceSnapPreviewX(double x) => DistanceSnapPreviewBounds.X + 10 + (float)(x / 512) * (DistanceSnapPreviewBounds.Width - 20);
    private float DistanceSnapPreviewY(double beat) => DistanceSnapPreviewBounds.Bottom - 12 - (float)(beat / 4) * (DistanceSnapPreviewBounds.Height - 24);

    private MapPoint DistanceSnapPreviewCandidate(float x, float y)
    {
        var r = DistanceSnapPreviewBounds;
        double beat = Math.Clamp(Math.Floor((r.Bottom - 12 - y) / (r.Height - 24) * 4 * dsSnap + .5), 0, 4 * dsSnap - 1) / dsSnap;
        double px = Math.Clamp((x - r.X - 10) / (r.Width - 20) * 512, 0, 512);
        var point = new MapPoint(beat, px);
        var previous = dsPreviewFruits.Where(f => f.TimeMs < beat).OrderByDescending(f => f.TimeMs).ThenBy(f => Math.Abs(f.X - px)).ToArray();
        // Preview time is measured in beats, so the matching velocity is distance per beat.
        DistanceSnap.Reference? reference = previous.Length == 0 ? null : new(Guid.Empty, previous[0], previous[0], 100 * Document.SliderMultiplier, 0);
        return DistanceSnap.SnapMultiple(point, reference, dsDraft, Document.DistanceSpacing, out _);
    }

    private void DistanceSnapPointerDown(float x, float y, int button)
    {
        if (dsSliderDrag >= 0) return;
        if (button == 2)
        {
            for (int i = dsPointers.Count - 1; i >= 0; i--)
                if (dsPointers[i].Contains(x, y))
                {
                    if (CommitDistanceSnapField()) { dsDraft.RemoveAt(i); dsPointers.Clear(); }
                    return;
                }
        }
        if (DistanceSnapPreviewBounds.Contains(x, y))
        {
            if (!CommitDistanceSnapField()) return;
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
            CommitDistanceSnapField();
        }
    }

    private double[] DistanceSnapLimits()
    {
        double duration = 60000 / dsBpm / dsSnap;
        double unit = 100 * Document.SliderMultiplier / dsSnap;
        double stand = CatchSize.CatchWidth(Document.CircleSize) / 2;
        // A fresh departure has the full catcher allowance; preceding movement is deliberately excluded.
        double dash = Math.Max(stand, (int)(dsReferenceTime + duration) - (int)dsReferenceTime - 1000f / 60f / 4
            + CatchSize.CatcherWidth(Document.CircleSize) / 2);
        double walk = Math.Min(dash, stand + duration * .5);
        return [0, stand / unit, walk / unit, dash / unit, Math.Max(512, dash) / unit];
    }

    private void BeginDistanceSnapDrag(int row, Rect bounds)
    {
        if (!CommitDistanceSnapField()) return;
        dsSliderDrag = row; dsDragStart = dsDraft[row];
        dsDragBounds = bounds; dsDragLimits = DistanceSnapLimits();
        UpdateDistanceSnapSlider(mouseX, dsSliderShift);
    }

    private void UpdateDistanceSnapSlider(float x, bool shift)
    {
        double position = Math.Clamp((x - dsDragBounds.X) / dsDragBounds.Width, 0, 1) * 4;
        int segment = Math.Min(3, (int)position);
        double ratio = dsDragLimits[segment] + (dsDragLimits[segment + 1] - dsDragLimits[segment]) * (position - segment);
        dsDragX = x; dsSliderShift = shift;
        dsDraft[dsSliderDrag] = Math.Max(shift ? .01 : .1, Math.Round(ratio, shift ? 2 : 1, MidpointRounding.AwayFromZero));
    }

    private void CancelDistanceSnapDrag()
    {
        if (dsSliderDrag < 0) return;
        dsDraft[dsSliderDrag] = dsDragStart;
        dsSliderDrag = -1;
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
        for (int i = 0; i < dsDraft.Count; i++)
        {
            float pointer = r.X + 20 + DistanceSnapFraction(dsDraft[i], limits) * (r.Width - 40);
            uint color = i == dsSliderDrag ? Accent : Foreground;
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
                double maximum = 512 / (100 * Document.SliderMultiplier / dsSnap);
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
