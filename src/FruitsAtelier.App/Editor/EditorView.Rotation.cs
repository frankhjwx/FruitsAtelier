using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal bool RotationVisible { get; private set; }
    private string rotationAngle = "0";
    private bool rotationClockwise
    {
        get => LibrarySettings.RotationClockwise;
        set { if (LibrarySettings.RotationClockwise != value) { LibrarySettings.RotationClockwise = value; RequestViewPreference?.Invoke(); } }
    }
    private bool rotationSelectionCentre
    {
        get => LibrarySettings.RotationSelectionCentre;
        set { if (LibrarySettings.RotationSelectionCentre != value) { LibrarySettings.RotationSelectionCentre = value; RequestViewPreference?.Invoke(); } }
    }
    private bool rotationDragging;
    private bool rotationSliderMoved;
    private float rotationSliderPointerStart;
    private Rect rotationSlider;
    private MapDocument? rotationSource, rotationPreview;
    private HashSet<Guid> rotationTargets = [];
    private CatchConversionCache rotationConversionCache = new();
    private CatchConversionResult? rotationValidation;
    private double rotationTimeScale, rotationPlayfieldTime;
    private (double Angle, bool Clockwise, bool SelectionCentre)? rotationInputs;
    private double rotationMinimum = -180, rotationMaximum = 180;
    private bool rotationWindowDragging;
    private (float X, float Y)? rotationWindowPosition;
    private (float X, float Y) rotationWindowPointer, rotationWindowStart;
    internal Rect RotationBounds => new(
        Math.Clamp(rotationWindowPosition?.X ?? (width - Math.Min(520, width - 32)) / 2, 0, Math.Max(0, width - Math.Min(520, width - 32))),
        Math.Clamp(rotationWindowPosition?.Y ?? (height - 322) / 2, 0, Math.Max(0, height - 322)), Math.Min(520, width - 32), 322);

    private void OpenRotation()
    {
        if (!ClipboardInteractionReady || history.HasActiveTransaction || LegacyDropletRefreshBusy || notesLocked || ClipboardSelectedParentIds().Count == 0 || RejectReadOnlySelection()) return;
        rotationTargets = ClipboardSelectedParentIds().ToHashSet();
        rotationSource = rotationPreview = CaptureRotationObjects();
        rotationTimeScale = pixelsPerMs * 512 / Playfield.Width;
        rotationPlayfieldTime = PresentationViewStart + plot.Height / (2 * pixelsPerMs);
        rotationConversionCache = new();
        rotationValidation = null;
        history.Begin(L.Get("rotation.title"));
        menu = -1; contextItems.Clear(); RotationVisible = true;
        rotationAngle = "0"; rotationMinimum = -180; rotationMaximum = 180;
        rotationInputs = (0, rotationClockwise, rotationSelectionCentre);
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
                PresentationViewStart + plot.Height / (2 * pixelsPerMs),
                selectionOrigin: RotationSelectionOrigin(Document, timeScale));
            if (IsOsuCanvas)
                foreach (var slider in Document.ImportedSliders.Where(s => ids.Contains(s.Id))) OsuSyncSliderLine(slider);
        });
    }

    private void ApplyRotation()
    {
        if (!PreviewRotation()) return;
        RotationVisible = rotationDragging = rotationWindowDragging = false;
        rotationSource = rotationPreview = null;
        rotationValidation = null;
        rotationConversionCache = new();
        CommitContentHistory();
    }

    private MapDocument CaptureRotationObjects()
    {
        var objects = new MapDocument();
        objects.Fruits.AddRange(Document.Fruits.Where(f => rotationTargets.Contains(f.Id)));
        objects.Tracks.AddRange(Document.Tracks.Where(t => rotationTargets.Contains(t.Id)));
        objects.ImportedSliders.AddRange(Document.ImportedSliders.Where(s => rotationTargets.Contains(s.Id)));
        return objects.DeepClone();
    }

    private void RestoreRotationObjects(MapDocument source)
    {
        var copy = source.DeepClone();
        var fruits = copy.Fruits.ToDictionary(f => f.Id);
        var tracks = copy.Tracks.ToDictionary(t => t.Id);
        var sliders = copy.ImportedSliders.ToDictionary(s => s.Id);
        for (int i = 0; i < Document.Fruits.Count; i++) if (fruits.TryGetValue(Document.Fruits[i].Id, out var f)) Document.Fruits[i] = f;
        for (int i = 0; i < Document.Tracks.Count; i++) if (tracks.TryGetValue(Document.Tracks[i].Id, out var t)) Document.Tracks[i] = t;
        for (int i = 0; i < Document.ImportedSliders.Count; i++) if (sliders.TryGetValue(Document.ImportedSliders[i].Id, out var s)) Document.ImportedSliders[i] = s;
    }

    private (double X, double Y)? RotationSelectionOrigin(MapDocument source, double timeScale)
    {
        var ids = ClipboardSelectedParentIds();
        Guid primary = selectedTrack != Guid.Empty ? selectedTrack : selection;
        var slider = source.ImportedSliders.Where(s => ids.Contains(s.Id)).OrderBy(s => s.Id == primary ? 0 : 1).ThenBy(s => s.TimeMs).FirstOrDefault();
        if (slider is not null)
        {
            var geometry = slider.RandomizationSource ?? slider;
            return (geometry.X, geometry.Y);
        }
        var track = source.Tracks.Where(t => ids.Contains(t.Id)).OrderBy(t => t.Id == primary ? 0 : 1).ThenBy(t => t.Nodes[0].TimeMs).FirstOrDefault();
        return track is null ? null : (track.Nodes[0].X, -track.Nodes[0].TimeMs * timeScale);
    }

    private bool PreviewRotation()
    {
        if (!RotationVisible || rotationSource is null) return false;
        if (!double.TryParse(rotationAngle, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)
            || !double.IsFinite(angle)) return false;
        bool sameOrigin = rotationInputs is { } previous && previous.Clockwise == rotationClockwise
            && previous.SelectionCentre == rotationSelectionCentre;
        if (!sameOrigin) { rotationMinimum = -180; rotationMaximum = 180; }
        double requested = angle;
        angle = Math.Clamp(angle, rotationMinimum, rotationMaximum);
        if (rotationInputs != (angle, rotationClockwise, rotationSelectionCentre) && !TryRotationAngle(angle))
        {
            double blocked = angle, accepted = sameOrigin ? rotationInputs!.Value.Angle : 0;
            if (!sameOrigin) TryRotationAngle(0);
            // Retain the discovered boundary for repeated pointer input in the same direction and origin.
            while (Math.Abs(blocked - accepted) > 1)
            {
                double middle = Math.Truncate((blocked + accepted) / 2);
                if (middle == accepted) break;
                if (TryRotationAngle(middle)) accepted = middle; else blocked = middle;
            }
            if (angle > accepted) rotationMaximum = accepted; else rotationMinimum = accepted;
            angle = accepted;
        }
        rotationInputs = (angle, rotationClockwise, rotationSelectionCentre);
        if (requested != angle)
        {
            rotationAngle = angle.ToString(CultureInfo.InvariantCulture);
            if (textEditor.HasSelection) SelectInput("rotation", rotationAngle);
            else { textEditor.Focus("rotation", rotationAngle, rotationAngle.Length); ResetTextCaret(); }
        }
        return true;
    }

    private bool TryRotationAngle(double angle)
    {
        RestoreRotationObjects(rotationSource!);
        try
        {
            CatchConversionResult? validation = null;
            if (Math.Abs(angle % 360) >= 1e-10)
            {
                validation = SelectionRotation.Apply(Document, rotationTargets, rotationClockwise ? angle : -angle,
                    IsOsuCanvas, rotationSelectionCentre, rotationTimeScale, rotationPlayfieldTime, rotationConversionCache, compensateTinyDroplets,
                    RotationSelectionOrigin(rotationSource!, rotationTimeScale));
                if (IsOsuCanvas)
                    foreach (var slider in Document.ImportedSliders.Where(s => rotationTargets.Contains(s.Id))) OsuSyncSliderLine(slider);
            }
            rotationValidation = validation;
            rotationPreview = CaptureRotationObjects();
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            RestoreRotationObjects(rotationPreview!);
            return false;
        }
    }

    private void CancelRotation()
    {
        if (!RotationVisible) return;
        history.Cancel();
        RotationVisible = rotationDragging = rotationWindowDragging = false;
        rotationSource = rotationPreview = null;
        rotationValidation = null;
        rotationConversionCache = new();
    }

    private bool RotationPointerDown(float x, float y, int button)
    {
        if (!RotationVisible) return false;
        var r = RotationBounds;
        if (button == 0 && new Rect(r.X, r.Y, r.Width, 44).Contains(x, y))
        {
            rotationWindowDragging = true; rotationWindowPointer = (x, y); rotationWindowStart = (r.X, r.Y);
            return true;
        }
        if (button == 0) for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
        return true;
    }

    private void RotationPointerMove(float x, float y)
    {
        if (rotationWindowDragging)
            rotationWindowPosition = (Math.Clamp(rotationWindowStart.X + x - rotationWindowPointer.X, 0, Math.Max(0, width - RotationBounds.Width)),
                Math.Clamp(rotationWindowStart.Y + y - rotationWindowPointer.Y, 0, Math.Max(0, height - RotationBounds.Height)));
        else if (rotationDragging)
        {
            rotationSliderMoved |= Math.Abs(x - rotationSliderPointerStart) >= 3;
            if (rotationSliderMoved) MoveRotationSlider(x);
        }
    }

    private void EndRotationPointer(float x, float y)
    {
        RotationPointerMove(x, y);
        if (rotationDragging && !rotationSliderMoved)
        {
            double.TryParse(rotationAngle, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle);
            float thumb = rotationSlider.X + (float)Math.Clamp((angle + 180) / 360, 0, 1) * rotationSlider.Width;
            if (Math.Abs(x - thumb) > 7)
            {
                rotationAngle = Math.Clamp(angle + (x > thumb ? 45 : -45), -180, 180).ToString(CultureInfo.InvariantCulture);
                SelectInput("rotation", rotationAngle); PreviewRotation();
            }
        }
        rotationDragging = rotationWindowDragging = false;
    }

    private void RotationKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { CancelRotation(); return; }
        if (key == 13) { ApplyRotation(); return; }
        if (InputKey("rotation", ref rotationAngle, key, ctrl, shift, 16, RequestPasteField)) PreviewRotation();
    }

    private void RotationText(string text)
    {
        string filtered = new(text.Where(c => char.IsAsciiDigit(c) || c is '.' or '-').ToArray());
        rotationAngle = InsertInput("rotation", rotationAngle, filtered, 16); PreviewRotation();
    }

    private void MoveRotationSlider(float x)
    {
        rotationAngle = Math.Round(Math.Clamp((x - rotationSlider.X) / rotationSlider.Width, 0, 1) * 360 - 180)
            .ToString(CultureInfo.InvariantCulture);
        SelectInput("rotation", rotationAngle); PreviewRotation();
    }

    private void DrawRotation(ICanvas c)
    {
        if (!RotationVisible) return;
        hits.Clear();
        var r = RotationBounds;
        float w = r.Width, h = r.Height, x = r.X, y = r.Y;
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Grid, radius: 8);
        c.Text(L.Get("rotation.title"), x + 18, y + 16, 18, Foreground, w - 36, true);
        c.Text(L.Get("rotation.angle"), x + 18, y + 50, 13, Foreground, w - 36);
        rotationSlider = new(x + 24, y + 84, w - 162, 26);
        c.Line(rotationSlider.X, rotationSlider.Y + 13, rotationSlider.Right, rotationSlider.Y + 13, Grid, 4);
        double.TryParse(rotationAngle, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle);
        c.Circle(rotationSlider.X + (float)Math.Clamp((angle + 180) / 360, 0, 1) * rotationSlider.Width, rotationSlider.Y + 13, 7, Accent);
        hits.Add(new(rotationSlider, () => { rotationDragging = true; rotationSliderMoved = false; rotationSliderPointerStart = mouseX; }, true));
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
                () => { rotationClockwise = first; PreviewRotation(); }, rotationClockwise == first);
            Button(c, new(x + 26 + half, y + 150 + i * 34, half, 30), L.Get(first ? "rotation.playfield" : "rotation.selection"),
                () => { rotationSelectionCentre = !first; PreviewRotation(); }, rotationSelectionCentre != first);
        }
        Button(c, new(x + 18, y + h - 52, w - 138, 34), L.Get("mac.ok"), ApplyRotation, true);
        Button(c, new(x + w - 110, y + h - 52, 92, 34), L.Get("mac.cancel"), CancelRotation);
    }
}
