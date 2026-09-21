using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action? RequestDistanceSnapPreference { get; set; }
    public bool DistanceSnapDialogVisible { get; private set; }
    private bool distanceSnapFlyout, dsReplace;
    private List<DistanceSnap.Preset> dsDraft = [];
    private int dsField = -1;
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
        dsDraft = LibrarySettings.DistanceSnapPresets.ToList();
        dsField = -1; dsBuffer = dsError = ""; dsScroll = 0;
        DistanceSnapDialogVisible = true;
        hits.Clear(); fields.Clear();
    }

    private void CloseDistanceSnapDialog()
    {
        DistanceSnapDialogVisible = false; dsField = -1;
        hits.Clear(); fields.Clear();
    }

    private bool CommitDistanceSnapField()
    {
        if (dsField < 0) return true;
        int index = dsField / 2;
        if (dsField % 2 == 0) dsDraft[index] = dsDraft[index] with { Name = dsBuffer.Trim() };
        else
        {
            if (!double.TryParse(dsBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio)
                || !double.IsFinite(ratio) || ratio <= 0)
            { dsError = L.Get("ds.invalid"); return false; }
            dsDraft[index] = dsDraft[index] with { Ratio = ratio };
        }
        dsField = -1; dsError = "";
        return true;
    }

    private void EditDistanceSnapField(int field)
    {
        if (!CommitDistanceSnapField()) return;
        dsField = field; dsReplace = true;
        var preset = dsDraft[field / 2];
        dsBuffer = field % 2 == 0 ? preset.Name : preset.Ratio.ToString("G", CultureInfo.InvariantCulture);
    }

    private void DistanceSnapKey(int key, bool ctrl)
    {
        if (key == 27)
        {
            if (dsField >= 0) { dsField = -1; dsError = ""; }
            else CloseDistanceSnapDialog();
        }
        else if (dsField >= 0)
        {
            if (ctrl && key == 65) dsReplace = true;
            else if (key is 8 or 46) { dsBuffer = dsReplace || key == 46 ? "" : dsBuffer[..Math.Max(0, dsBuffer.Length - 1)]; dsReplace = false; }
            else if (key is 13 or 9) CommitDistanceSnapField();
        }
    }

    private void DistanceSnapText(char value)
    {
        if (dsField < 0 || char.IsControl(value)) return;
        if (dsField % 2 == 1 && !char.IsAsciiDigit(value) && value != '.') return;
        if (!dsReplace && dsBuffer.Length >= (dsField % 2 == 0 ? 40 : 16)) return;
        ResetTextCaret();
        dsBuffer = (dsReplace ? "" : dsBuffer) + value; dsReplace = false; dsError = "";
    }

    private void DrawDistanceSnapDialog(ICanvas c)
    {
        if (!DistanceSnapDialogVisible) return;
        hits.Clear(); fields.Clear();
        var r = DistanceSnapDialogBounds;
        c.Fill(new(0, 84, width, Math.Max(0, height - 84)), Background, opacity: .65f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("ds.title"), r.X + 20, r.Y + 18, 16, Foreground, r.Width - 40, true);
        DrawDistanceSnapReference(c, r);
        c.Text(L.Get("ds.zeroHint"), r.X + 20, r.Y + 205, 10, Muted, r.Width - 40);
        c.Text(L.Get("ds.presets", dsDraft.Count, DistanceSnap.MaximumPresets), r.X + 20, r.Y + 236, 12, Foreground, r.Width - 150);
        Button(c, new(r.Right - 116, r.Y + 226, 96, 30), L.Get("ds.add"), () =>
        {
            if (!CommitDistanceSnapField() || dsDraft.Count >= DistanceSnap.MaximumPresets) return;
            dsDraft.Add(new(L.Get("ds.defaultName", dsDraft.Count + 1), Document.DistanceSpacing));
            dsScroll = Math.Max(0, dsDraft.Count * 46 - dsList.Height);
        }, enabled: dsDraft.Count < DistanceSnap.MaximumPresets);
        dsList = new(r.X + 20, r.Y + 266, r.Width - 40, Math.Max(30, r.Height - 342));
        dsScroll = Math.Clamp(dsScroll, 0, Math.Max(0, dsDraft.Count * 46 - dsList.Height));
        c.Fill(dsList, Background, 4);
        if (dsDraft.Count == 0) c.Text(L.Get("ds.empty"), dsList.X + 12, dsList.Y + 16, 12, Muted, dsList.Width - 24);
        c.Clip(dsList);
        for (int i = 0; i < dsDraft.Count; i++)
        {
            int row = i;
            float y = dsList.Y + i * 46 - dsScroll;
            DrawField(new(dsList.X + 8, y + 6, dsList.Width - 186, 32), i * 2, dsDraft[i].Name);
            DrawField(new(dsList.Right - 170, y + 6, 88, 32), i * 2 + 1, L.Get("ds.ratio", dsDraft[i].Ratio));
            var remove = new Rect(dsList.Right - 74, y + 6, 66, 32);
            int before = hits.Count;
            Button(c, remove, L.Get("ds.remove"), () =>
            {
                if (dsField / 2 == row && dsField >= 0) { dsField = -1; dsError = ""; }
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
            LibrarySettings.DistanceSnapPresets = dsDraft.ToList();
            RequestDistanceSnapPreference?.Invoke();
            CloseDistanceSnapDialog();
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

    private void DrawDistanceSnapReference(ICanvas c, Rect r)
    {
        double beat = TimingMap.At(Document, playhead).BeatLengthMs;
        double duration = beat / divisor;
        double unit = duration * DistanceSnap.BaseVelocity(Document, playhead);
        double stand = CatchSize.CatchWidth(Document.CircleSize) / 2;
        // A fresh departure has the full catcher allowance; preceding movement is deliberately excluded.
        double dash = Math.Max(stand, (int)(playhead + duration) - (int)playhead - 1000f / 60f / 4
            + CatchSize.CatcherWidth(Document.CircleSize) / 2);
        double walk = Math.Min(dash, stand + duration * .5);
        double[] limits = [0, stand, walk, dash, Math.Max(512, dash)];
        string[] names = ["movement.stand", "movement.walk", "movement.dash", "movement.hyperdash"];
        uint[] colors = [0xC0C0C0, 0x63B99D, 0xD6B365, 0xCE7683];
        c.Text(L.Get("ds.reference", 60000 / beat, divisor, Document.CircleSize, duration), r.X + 20, r.Y + 48, 11, Muted, r.Width - 40);
        c.Text(L.Get("ds.referenceHint"), r.X + 20, r.Y + 69, 11, Muted, r.Width - 40);
        float segment = (r.Width - 40) / 4;
        for (int i = 0; i < 4; i++)
        {
            float x = r.X + 20 + i * segment;
            c.Fill(new(x, r.Y + 101, segment - 2, 25), colors[i], 3);
            c.Text(L.Get(names[i]), x + 6, r.Y + 107, 11, Background, segment - 12, true);
            string mid = limits[i] >= 512 ? "—" : L.Get("assist.ratio", (limits[i] + Math.Min(512, limits[i + 1])) / 2 / unit);
            c.Text(L.Get("ds.midpoint", mid), x, r.Y + 137, 11, Foreground, segment - 4);
            if (i < 3)
            {
                string boundary = L.Get("assist.ratio", limits[i + 1] / unit);
                float center = x + segment;
                c.Line(center - 1, r.Y + 123, center - 1, r.Y + 132, colors[i]);
                c.Text(boundary, center - c.MeasureText(boundary, 11) / 2, r.Y + 160, 11, Foreground, segment);
            }
        }
        c.Text(L.Get("ds.rangeHint"), r.X + 20, r.Y + 184, 10, Muted, r.Width - 40);
    }
}
