using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal bool RotationVisible { get; private set; }
    private string rotationAngle = "0", rotationError = "";
    private bool rotationClockwise = true, rotationSelectionCentre;
    private bool rotationDragging;
    private Rect rotationSlider;

    private void OpenRotation()
    {
        if (!ClipboardInteractionReady || notesLocked || ClipboardSelectedParentIds().Count == 0 || RejectReadOnlySelection()) return;
        menu = -1; contextItems.Clear(); RotationVisible = true;
        rotationAngle = "0"; rotationError = "";
        SelectInput("rotation", rotationAngle);
    }

    private bool RotateSelection(double degrees, bool selectionCentre)
    {
        if (notesLocked || !ClipboardInteractionReady || RejectReadOnlySelection()) return false;
        var ids = ClipboardSelectedParentIds().ToHashSet();
        if (ids.Count == 0) return false;
        if (Math.Abs(degrees % 360) < 1e-10) return true;
        return Edit(L.Get("rotation.title"), () =>
        {
            double timeScale = pixelsPerMs * 512 / Playfield.Width;
            SelectionRotation.Apply(Document, ids, degrees, IsOsuCanvas, selectionCentre, timeScale,
                PresentationViewStart + plot.Height / (2 * pixelsPerMs));
            if (IsOsuCanvas)
                foreach (var slider in Document.ImportedSliders.Where(s => ids.Contains(s.Id))) OsuSyncSliderLine(slider);
        });
    }

    private void ApplyRotation()
    {
        if (!double.TryParse(rotationAngle, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)
            || !double.IsFinite(angle) || angle < 0 || angle > 360)
        { rotationError = L.Get("rotation.angleRange"); return; }
        if (RotateSelection(rotationClockwise ? angle : -angle, rotationSelectionCentre)) RotationVisible = false;
        else rotationError = StatusMessage;
    }

    private void RotationKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { RotationVisible = rotationDragging = false; return; }
        if (key == 13) { ApplyRotation(); return; }
        InputKey("rotation", ref rotationAngle, key, ctrl, shift, 16, RequestPasteField);
    }

    private void RotationText(string text)
    {
        string filtered = new(text.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        rotationAngle = InsertInput("rotation", rotationAngle, filtered, 16); rotationError = "";
    }

    private void MoveRotationSlider(float x)
    {
        rotationAngle = Math.Round(Math.Clamp((x - rotationSlider.X) / rotationSlider.Width, 0, 1) * 360)
            .ToString(CultureInfo.InvariantCulture);
        SelectInput("rotation", rotationAngle); rotationError = "";
    }

    private void DrawRotation(ICanvas c)
    {
        if (!RotationVisible) return;
        hits.Clear();
        float w = Math.Min(520, width - 32), h = 322, x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Grid, radius: 8);
        c.Text(L.Get("rotation.title"), x + 18, y + 16, 18, Foreground, w - 36, true);
        c.Text(L.Get("rotation.angle"), x + 18, y + 50, 13, Foreground, w - 36);
        rotationSlider = new(x + 24, y + 84, w - 162, 26);
        c.Line(rotationSlider.X, rotationSlider.Y + 13, rotationSlider.Right, rotationSlider.Y + 13, Grid, 4);
        double.TryParse(rotationAngle, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle);
        c.Circle(rotationSlider.X + (float)Math.Clamp(angle / 360, 0, 1) * rotationSlider.Width, rotationSlider.Y + 13, 7, Accent);
        hits.Add(new(rotationSlider, () => { rotationDragging = true; MoveRotationSlider(mouseX); }, true));
        var input = new Rect(x + w - 112, y + 82, 78, 30);
        c.Fill(input, Surface, 3); c.Stroke(input, Accent, radius: 3);
        DrawInputText(c, new(input.X + 8, input.Y + 7, input.Width - 16, 18), rotationAngle, 13, true, "rotation");
        hits.Add(new(input, () => FocusInput("rotation", rotationAngle, mouseX), true));
        c.Text("°", input.Right + 5, input.Y + 7, 13, Foreground, 18);
        float half = (w - 44) / 2;
        c.Text(L.Get("rotation.direction"), x + 18, y + 128, 13, Foreground, half);
        c.Text(L.Get("rotation.origin"), x + 26 + half, y + 128, 13, Foreground, half);
        for (int i = 0; i < 2; i++)
        {
            bool first = i == 0;
            Button(c, new(x + 18, y + 150 + i * 34, half, 30), L.Get(first ? "rotation.clockwise" : "rotation.anticlockwise"),
                () => rotationClockwise = first, rotationClockwise == first);
            Button(c, new(x + 26 + half, y + 150 + i * 34, half, 30), L.Get(first ? "rotation.playfield" : "rotation.selection"),
                () => rotationSelectionCentre = !first, rotationSelectionCentre != first);
        }
        if (rotationError.Length > 0) c.Text(rotationError, x + 18, y + 220, 11, Error, w - 36);
        Button(c, new(x + 18, y + h - 52, w - 138, 34), L.Get("mac.ok"), ApplyRotation, true);
        Button(c, new(x + w - 110, y + h - 52, 92, 34), L.Get("mac.cancel"), () => RotationVisible = false);
    }
}
