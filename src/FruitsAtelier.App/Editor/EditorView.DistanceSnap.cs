using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool DistanceSnapDialogVisible { get; private set; }
    private bool distanceSnapFlyout, dsReplace;
    private List<double> dsDraft = [];
    private int dsField = -1, dsSliderDrag = -1;
    private double dsDragStart;
    private Rect dsDragBounds;
    private double[] dsDragLimits = [];
    private readonly List<Rect> dsSliders = [];
    internal IReadOnlyList<Rect> DistanceSnapSliderBounds => dsSliders;
    private static readonly uint[] DsColors = [0xC0C0C0, 0x63B99D, 0xD6B365, 0xCE7683];
    private string dsBuffer = "", dsError = "";
    private float dsScroll;
    private Rect dsList;
    private Rect DistanceSnapDialogBounds => new((width - Math.Min(640, width - 32)) / 2,
        (height - Math.Min(570, height - 32)) / 2, Math.Min(640, width - 32), Math.Min(570, height - 32));

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
        dsField = -1; dsBuffer = dsError = ""; dsScroll = 0;
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
        if (!double.TryParse(dsBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio)
            || !double.IsFinite(ratio) || ratio <= 0)
        { dsError = L.Get("ds.invalid"); return false; }
        dsDraft[dsField] = ratio;
        dsField = -1; dsError = "";
        return true;
    }

    private void EditDistanceSnapField(int field)
    {
        if (!CommitDistanceSnapField()) return;
        dsField = field; dsReplace = true;
        dsBuffer = dsDraft[field].ToString("G", CultureInfo.InvariantCulture);
    }

    private void DistanceSnapKey(int key, bool ctrl, bool shift)
    {
        if (key == 27)
        {
            if (dsSliderDrag >= 0) CancelDistanceSnapDrag();
            else if (dsField >= 0) { dsField = -1; dsError = ""; }
            else CloseDistanceSnapDialog();
        }
        else if (key == 9 && dsSliderDrag < 0 && dsDraft.Count > 0)
        {
            int next = dsField < 0 ? (shift ? dsDraft.Count - 1 : 0)
                : (dsField + (shift ? dsDraft.Count - 1 : 1)) % dsDraft.Count;
            if (!CommitDistanceSnapField()) return;
            EditDistanceSnapField(next);
            float top = next * 46;
            if (top < dsScroll) dsScroll = top;
            else if (top + 46 > dsScroll + dsList.Height) dsScroll = top + 46 - dsList.Height;
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
        if (!char.IsAsciiDigit(value) && value != '.') return;
        if (!dsReplace && dsBuffer.Length >= 16) return;
        ResetTextCaret();
        dsBuffer = (dsReplace ? "" : dsBuffer) + value; dsReplace = false; dsError = "";
    }

    private void DrawDistanceSnapDialog(ICanvas c)
    {
        if (!DistanceSnapDialogVisible) return;
        hits.Clear(); fields.Clear(); dsSliders.Clear();
        var r = DistanceSnapDialogBounds;
        c.Fill(new(0, 84, width, Math.Max(0, height - 84)), Background, opacity: .65f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("ds.title"), r.X + 20, r.Y + 18, 16, Foreground, r.Width - 40, true);
        DrawDistanceSnapReference(c, r);
        c.Text(L.Get("ds.presets", dsDraft.Count, DistanceSnap.MaximumPresets), r.X + 20, r.Y + 164, 12, Foreground, r.Width - 150);
        Button(c, new(r.Right - 116, r.Y + 154, 96, 30), L.Get("ds.add"), () =>
        {
            if (!CommitDistanceSnapField() || dsDraft.Count >= DistanceSnap.MaximumPresets) return;
            dsDraft.Add(Document.DistanceSpacing);
            dsScroll = Math.Max(0, dsDraft.Count * 46 - dsList.Height);
        }, enabled: dsDraft.Count < DistanceSnap.MaximumPresets);
        dsList = new(r.X + 20, r.Y + 194, r.Width - 40, Math.Max(30, r.Height - 270));
        dsScroll = Math.Clamp(dsScroll, 0, Math.Max(0, dsDraft.Count * 46 - dsList.Height));
        c.Fill(dsList, Background, 4);
        c.Clip(dsList);
        for (int i = 0; i < dsDraft.Count; i++)
        {
            int row = i;
            float y = dsList.Y + i * 46 - dsScroll;
            var slider = new Rect(dsList.X + 14, y + 6, dsList.Width - 198, 32);
            dsSliders.Add(slider);
            DrawDistanceSnapSlider(c, slider, dsDraft[i]);
            int sliderHit = hits.Count;
            hits.Add(new(slider, () =>
            {
                if (!CommitDistanceSnapField()) return;
                dsSliderDrag = row; dsDragStart = dsDraft[row];
                dsDragBounds = slider; dsDragLimits = DistanceSnapLimits();
                UpdateDistanceSnapSlider(mouseX);
            }, true));
            ClipHits(sliderHit);
            DrawField(new(dsList.Right - 170, y + 6, 88, 32), i, L.Get("ds.ratio", dsDraft[i]));
            var remove = new Rect(dsList.Right - 74, y + 6, 66, 32);
            int before = hits.Count;
            Button(c, remove, L.Get("ds.remove"), () =>
            {
                if (dsField == row) { dsField = -1; dsError = ""; }
                else if (!CommitDistanceSnapField()) return;
                dsDraft.RemoveAt(row);
            });
            ClipHits(before);
        }
        c.Unclip();
        if (dsDraft.Count * 46 > dsList.Height)
        {
            float thumb = dsList.Height * dsList.Height / (dsDraft.Count * 46);
            c.Fill(new(dsList.Right - 3, dsList.Y + dsScroll / (dsDraft.Count * 46) * dsList.Height, 3, thumb), Muted, 1);
        }
        if (dsError.Length > 0) c.Text(dsError, r.X + 20, r.Bottom - 67, 11, Error, r.Width - 40);
        Button(c, new(r.Right - 196, r.Bottom - 44, 80, 30), L.Get("mac.cancel"), CloseDistanceSnapDialog);
        Button(c, new(r.Right - 108, r.Bottom - 44, 88, 30), L.Get("library.apply"), () =>
        {
            if (!CommitDistanceSnapField()) return;
            if (Edit(L.Get("ds.title"), () =>
            {
                Document.DistanceSnapRatios.Clear();
                Document.DistanceSnapRatios.AddRange(dsDraft);
            })) CloseDistanceSnapDialog();
        });

        void DrawField(Rect bounds, int field, string value)
        {
            c.Fill(bounds, Surface, 3); c.Stroke(bounds, dsField == field ? Accent : Grid, radius: 3);
            if (dsField == field) DrawInputText(c, new(bounds.X + 6, bounds.Y + 7, bounds.Width - 12, 20), dsBuffer, 12, true, dsReplace);
            else c.Text(value, bounds.X + 6, bounds.Y + 9, 12, Foreground, bounds.Width - 12);
            int before = hits.Count;
            hits.Add(new(bounds, () => EditDistanceSnapField(field), true));
            ClipHits(before);
        }
        void ClipHits(int start)
        {
            for (int j = hits.Count - 1; j >= start; j--)
            {
                var h = hits[j];
                float top = Math.Max(h.Bounds.Y, dsList.Y), bottom = Math.Min(h.Bounds.Bottom, dsList.Bottom);
                if (bottom <= top) hits.RemoveAt(j);
                else hits[j] = h with { Bounds = new(h.Bounds.X, top, h.Bounds.Width, bottom - top) };
            }
        }
    }

    private double[] DistanceSnapLimits()
    {
        double duration = TimingMap.At(Document, playhead).BeatLengthMs / divisor;
        double unit = duration * DistanceSnap.BaseVelocity(Document, playhead);
        double stand = CatchSize.CatchWidth(Document.CircleSize) / 2;
        // A fresh departure has the full catcher allowance; preceding movement is deliberately excluded.
        double dash = Math.Max(stand, (int)(playhead + duration) - (int)playhead - 1000f / 60f / 4
            + CatchSize.CatcherWidth(Document.CircleSize) / 2);
        double walk = Math.Min(dash, stand + duration * .5);
        return [0, stand / unit, walk / unit, dash / unit, Math.Max(512, dash) / unit];
    }

    private void UpdateDistanceSnapSlider(float x)
    {
        double position = Math.Clamp((x - dsDragBounds.X) / dsDragBounds.Width, 0, 1) * 4;
        int segment = Math.Min(3, (int)position);
        double ratio = dsDragLimits[segment] + (dsDragLimits[segment + 1] - dsDragLimits[segment]) * (position - segment);
        dsDraft[dsSliderDrag] = Math.Max(.001, Math.Round(ratio, 3));
    }

    private void CancelDistanceSnapDrag()
    {
        if (dsSliderDrag < 0) return;
        dsDraft[dsSliderDrag] = dsDragStart;
        dsSliderDrag = -1;
    }

    private void DrawDistanceSnapSlider(ICanvas c, Rect slider, double ratio)
    {
        var limits = dsSliderDrag >= 0 ? dsDragLimits : DistanceSnapLimits();
        for (int i = 0; i < 4; i++)
            c.Fill(new(slider.X + slider.Width * i / 4, slider.Y + 12, slider.Width / 4, 8), DsColors[i]);
        double position = 4;
        for (int i = 0; i < 4; i++)
            if (ratio <= limits[i + 1] && limits[i + 1] > limits[i])
            { position = i + Math.Clamp((ratio - limits[i]) / (limits[i + 1] - limits[i]), 0, 1); break; }
        float x = slider.X + (float)(position / 4) * slider.Width;
        c.Circle(x, slider.Y + 16, 7, Foreground);
        c.Circle(x, slider.Y + 16, 7, Panel, filled: false, width: 2);
    }

    private void DrawDistanceSnapReference(ICanvas c, Rect r)
    {
        double[] limits = DistanceSnapLimits();
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
        float segment = (r.Width - 40) / 4;
        for (int i = 0; i < 4; i++)
        {
            float x = r.X + 20 + i * segment;
            c.Fill(new(x, r.Y + 56, segment - 2, 25), DsColors[i], 3);
            c.Text(L.Get(names[i]), x + 6, r.Y + 62, 11, Background, segment - 12, true);
            if (i < 3)
            {
                double maximum = 512 / (TimingMap.At(Document, playhead).BeatLengthMs / divisor * DistanceSnap.BaseVelocity(Document, playhead));
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
