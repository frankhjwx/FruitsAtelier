using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private (Guid Source, int Event)? distanceObject;
    private readonly List<Rect> distanceLabelBounds = [];
    public IReadOnlyList<Rect> DistanceLabelBounds => distanceLabelBounds;
    public Rect? PreviousDistanceFieldBounds { get; private set; }
    public Rect? NextDistanceFieldBounds { get; private set; }
    public Rect? XCoordinateFieldBounds { get; private set; }
    private Rect? XCoordinateRowBounds => MovementOverlayBounds is { } panel
        ? new(panel.X, panel.Y + 56, panel.Width, panel.Height - 56) : null;

    private double BaseDistanceVelocity(double time) => DistanceSnap.BaseVelocity(Document, time, renderedTiming);
    private double? BaseDistanceRatio(ConvertedCatchObject from, ConvertedCatchObject to)
        => DistanceSnap.Ratio(new(from.TimeMs, from.X), new(to.TimeMs, to.X), BaseDistanceVelocity(from.TimeMs));

    private ConvertedCatchObject? SelectedDistanceObject()
    {
        if (distanceObject is not { } selected || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return null;
        var ids = FlagTargets();
        if (ids.Length != 1 || ids[0] != selected.Source) return null;
        return conversion!.Objects.FirstOrDefault(o => o.SourceId == selected.Source && o.EventIndex == selected.Event);
    }

    private (ConvertedCatchObject? Previous, ConvertedCatchObject? Next) DistanceNeighbours(ConvertedCatchObject target, IReadOnlyList<ConvertedCatchObject>? objects = null)
    {
        objects ??= conversion!.Objects;
        EnsureMovementStates(objects);
        int index = Array.FindIndex(movementIndices, i => objects[i].SourceId == target.SourceId && objects[i].EventIndex == target.EventIndex);
        if (index < 0) return (null, null);
        return (index > 0 ? objects[movementIndices[index - 1]] : null,
            index + 1 < movementIndices.Length ? objects[movementIndices[index + 1]] : null);
    }

    private ConvertedCatchObject? distanceEditTarget, distanceEditReference;
    private bool distanceDragging;
    private bool editingXCoordinate;
    private string DistanceEditLabel => L.Get(editingXCoordinate ? "coordinate.x" : "distance.previousField");
    private double distanceValue, distanceMaximum;
    public Rect? DistanceSliderBounds { get; private set; }
    private bool DistanceEditing => distanceEditTarget is not null;

    private void DrawDistanceFields(ICanvas c, Rect panel)
    {
        DistanceSliderBounds = null;
        var target = PlacementGhostPoint() is null ? SelectedDistanceObject() : null;
        var previous = target is not null ? DistanceNeighbours(target).Previous : null;
        bool editable = target is not null && previous is not null && DistanceReadout.Previous.HasValue && !notesLocked && drag == DragKind.None;
        NextDistanceFieldBounds = null;
        PreviousDistanceFieldBounds = editable ? new(panel.X, panel.Y, panel.Width, 56) : null;
        var coordinateInput = new Rect(panel.Right - 74, panel.Y + 58, 64, 20);
        XCoordinateFieldBounds = target is not null && !notesLocked && drag == DragKind.None
            ? editingXCoordinate ? coordinateInput : XCoordinateRowBounds : null;
        if (DistanceEditing && editingXCoordinate)
        {
            c.Text(L.Get("coordinate.x"), panel.X + 10, panel.Y + 60, MovementPanelFontSize, Muted, panel.Width - 94);
            c.Fill(coordinateInput, Panel, 3);
            c.Stroke(coordinateInput, fieldError.Length > 0 ? Error : Accent, 1, 3);
            DrawInputText(c, new(coordinateInput.X + 5, coordinateInput.Y + 2, coordinateInput.Width - 10, 16), editBuffer, MovementPanelFontSize, true, "distance");
        }
        else
        {
            double? coordinate = PlacementGhostPoint()?.X ?? target?.X;
            c.Text(L.Get("coordinate.readout", coordinate?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "—"),
                panel.X + 10, panel.Y + 60, MovementPanelFontSize, Muted, panel.Width - 20);
        }
        if (DistanceEditing && !editingXCoordinate)
        {
            var slider = new Rect(panel.X + 10, panel.Y + 36, panel.Width - 94, 20);
            var input = new Rect(panel.Right - 74, panel.Y + 36, 64, 20);
            DistanceSliderBounds = slider;
            PreviousDistanceFieldBounds = input;
            float y = slider.Y + 10;
            c.Line(slider.X, y, slider.Right, y, Grid, 4);
            float x = slider.X + (float)(distanceMaximum > 0 ? distanceValue / distanceMaximum : 0) * slider.Width;
            c.Line(slider.X, y, x, y, Accent, 4);
            c.Fill(new(x - 5, y - 7, 10, 14), Accent, 5);
            c.Fill(input, Panel, 3);
            c.Stroke(input, fieldError.Length > 0 ? Error : Accent, 1, 3);
            DrawInputText(c, new(input.X + 5, input.Y + 2, input.Width - 10, 16), editBuffer, MovementPanelFontSize, true, "distance");
        }
        else
        {
            c.Text(L.Get("assist.previous", DistanceReadout.Previous is { } p ? L.Get("assist.ratio", p) : "—"), panel.X + 10, panel.Y + 38, MovementPanelFontSize, Muted, panel.Width / 2 - 14);
            string next = L.Get("assist.next", DistanceReadout.Next is { } n ? L.Get("assist.ratio", n) : "—");
            float nextWidth = Math.Min(c.MeasureText(next, MovementPanelFontSize), panel.Width / 2 - 14);
            c.Text(next, panel.Right - 10 - nextWidth, panel.Y + 38, MovementPanelFontSize, Muted, nextWidth);
        }
        if (DistanceEditing && fieldError.Length > 0)
        {
            c.Fill(new(panel.X, panel.Y - 30, panel.Width, 28), Panel, 4);
            c.Text(fieldError, panel.X + 6, panel.Y - 24, 11, Error, panel.Width - 12);
        }
    }

    private bool DistancePointerDown(float x, float y, int button, bool shift)
    {
        bool coordinateRowHit = XCoordinateRowBounds is { } row && row.Contains(x, y);
        bool coordinateHit = coordinateRowHit && XCoordinateFieldBounds.HasValue;
        if (DistanceEditing)
        {
            if (MovementOverlayBounds is { } panel && panel.Contains(x, y)
                && (editingXCoordinate ? coordinateRowHit : !coordinateRowHit))
            {
                if (button == 0 && DistanceSliderBounds is { } slider && slider.Contains(x, y))
                { distanceDragging = true; UpdateDistanceSlider(x, shift); }
                else if (button == 0 && textLayouts.ContainsKey("distance"))
                    FocusInput("distance", editBuffer, x);
                return true;
            }
            if (!FinishDistanceEdit(false)) return true;
        }
        if (button != 0 || !coordinateHit && (PreviousDistanceFieldBounds is not { } bounds || !bounds.Contains(x, y))) return coordinateRowHit;
        if (editField >= 0 && !CommitField()) return true;
        distanceEditTarget = SelectedDistanceObject();
        editingXCoordinate = coordinateHit;
        if (editingXCoordinate)
        {
            distanceEditReference = null;
            distanceValue = distanceEditTarget!.X;
        }
        else
        {
            distanceEditReference = DistanceNeighbours(distanceEditTarget!).Previous;
            distanceValue = DistanceReadout.Previous!.Value;
            double unit = (distanceEditTarget!.TimeMs - distanceEditReference!.TimeMs) * BaseDistanceVelocity(distanceEditReference.TimeMs);
            int direction = Math.Sign(distanceEditTarget.X - distanceEditReference.X);
            distanceMaximum = direction == 0 ? 0 : (direction > 0 ? 512 - distanceEditReference.X : distanceEditReference.X) / unit;
        }
        editBuffer = distanceValue.ToString(editingXCoordinate ? "0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
        replaceText = false; fieldError = "";
        textEditor.Field = "";
        textEditor.Focus("distance", editBuffer, editBuffer.Length);
        history.Begin(L.Get("editor.command.changeField", DistanceEditLabel));
        return true;
    }

    private void PreviewDistance(double value)
    {
        // Rebuild each preview from the original geometry and direction, including after passing through zero.
        history.Cancel();
        history.Begin(L.Get("editor.command.changeField", DistanceEditLabel));
        try
        {
            ApplyPositionPreview(value);
            distanceValue = value; fieldError = "";
        }
        catch (ArgumentException error)
        {
            history.Cancel();
            history.Begin(L.Get("editor.command.changeField", DistanceEditLabel));
            ApplyPositionPreview(distanceValue);
            fieldError = error.Message;
        }
    }

    private void UpdateDistanceSlider(float x, bool fine)
    {
        if (DistanceSliderBounds is not { } slider) return;
        int decimals = fine ? 2 : 1;
        double scale = fine ? 100 : 10;
        double value = Math.Min(Math.Floor(distanceMaximum * scale) / scale,
            Math.Round(Math.Clamp((x - slider.X) / slider.Width, 0, 1) * distanceMaximum, decimals, MidpointRounding.AwayFromZero));
        PreviewDistance(value);
        editBuffer = distanceValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        replaceText = true;
        textEditor.Focus("distance", editBuffer, editBuffer.Length);
    }

    private void PreviewDistanceText()
    {
        ResetTextCaret();
        var style = editingXCoordinate ? System.Globalization.NumberStyles.Integer : System.Globalization.NumberStyles.Float;
        if (double.TryParse(editBuffer, style, System.Globalization.CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            PreviewDistance(value);
        else fieldError = L.Get("editor.error.finiteNumberRequired");
    }

    private bool FinishDistanceEdit(bool cancel)
    {
        if (!DistanceEditing) return true;
        if (!cancel && fieldError.Length > 0) return false;
        if (cancel)
        {
            history.Cancel();
            distanceObject = (distanceEditTarget!.SourceId, distanceEditTarget.EventIndex);
        }
        else history.Commit();
        distanceEditTarget = distanceEditReference = null;
        editingXCoordinate = false;
        distanceDragging = false; fieldError = "";
        return true;
    }

    private bool DistanceKeyDown(int key, bool ctrl, bool shift)
    {
        if (!DistanceEditing) return false;
        if (ctrl && key == 90 && !shiftHeld)
        {
            bool changed = SelectedDistanceObject() is { } current && Math.Abs(current.X - distanceEditTarget!.X) > .00001;
            // Keep the last valid preview redoable; an untouched field must not undo earlier edits.
            fieldError = "";
            FinishDistanceEdit(!changed);
            if (changed) Undo();
        }
        else if (key == 27) FinishDistanceEdit(true);
        else if (key is 13 or 9) FinishDistanceEdit(false);
        else
        {
            if (InputKey("distance", ref editBuffer, key, ctrl, shift, 30, RequestPasteField))
            { replaceText = false; PreviewDistanceText(); }
        }
        return true;
    }

    private void DistanceTextInput(char value)
    {
        if (!(char.IsAsciiDigit(value) || value == '-' || !editingXCoordinate && value == '.')) return;
        string next = InsertInput("distance", editBuffer, value.ToString(), 30);
        int dot = next.IndexOf('.');
        if (dot >= 0 && next.Length - dot - 1 > 2) return;
        editBuffer = next; replaceText = false; PreviewDistanceText();
    }

    private void ApplyPositionPreview(double value)
    {
        var target = distanceEditTarget!;
        try
        {
            if (editingXCoordinate) DistanceSpacingEditing.ApplyX(Document, target, value, compensateTinyDroplets);
            else DistanceSpacingEditing.Apply(Document, target, distanceEditReference!, false, value, compensateTinyDroplets);
        }
        catch (InvalidOperationException error) { throw new ArgumentException(error.Message, error); }
        EnsureConversion();
        var updated = conversion!.Objects.FirstOrDefault(o => o.SourceId == target.SourceId && o.Kind == target.Kind && Math.Abs(o.TimeMs - target.TimeMs) < .001);
        if (updated is not null) distanceObject = (updated.SourceId, updated.EventIndex);
    }

    private void DrawSelectedDistanceTick(ICanvas c)
    {
        if (SelectedDistanceObject() is not { IsStandalone: false, Kind: CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet } tick) return;
        var p = Screen(new(tick.TimeMs, tick.X));
        float radius = Math.Max(6, ObjectRadius(tick.Kind) * Playfield.Width / 512);
        c.Circle(p.X, p.Y, radius + 3, Accent, false, 2);
        c.Circle(p.X, p.Y, radius + 7, Foreground, false, 2.5f);
    }

    private sealed record DistanceLabel(double Time, float X, float Width, string Text);
    private readonly List<DistanceLabel> distanceLayout = [];
    private IReadOnlyList<ConvertedCatchObject>? distanceLayoutSource;
    private TimingMap.Lookup? distanceLayoutTiming;
    private double distanceLayoutScale, distanceLayoutWidth, distanceLayoutDpb;
    private string distanceLayoutLanguage = "";

    private void EnsureDistanceLabelLayout(ICanvas c, IReadOnlyList<ConvertedCatchObject> objects)
    {
        if (ReferenceEquals(distanceLayoutSource, objects) && ReferenceEquals(distanceLayoutTiming, renderedTiming)
            && distanceLayoutScale == pixelsPerMs && distanceLayoutWidth == Playfield.Width
            && distanceLayoutDpb == Document.DistancePerBeat && distanceLayoutLanguage == L.Language) return;
        distanceLayoutSource = objects; distanceLayoutTiming = renderedTiming;
        distanceLayoutScale = pixelsPerMs; distanceLayoutWidth = Playfield.Width;
        distanceLayoutDpb = Document.DistancePerBeat; distanceLayoutLanguage = L.Language;
        distanceLayout.Clear();
        var nearby = new List<DistanceLabel>();
        // Choose labels in map order, including offscreen predecessors, so scrolling cannot change priority.
        for (int i = 1; i < movementIndices.Length; i++)
        {
            var from = objects[movementIndices[i - 1]];
            var to = objects[movementIndices[i]];
            if (to.TimeMs - from.TimeMs <= 37.5
                || Document.BananaShowers.Any(s => s.TimeMs <= to.TimeMs && s.EndTimeMs >= from.TimeMs)
                || KiaiOverlaps(from.TimeMs, to.TimeMs)
                || breakPeriods.Any(period => period.StartMs <= to.TimeMs && period.EndMs >= from.TimeMs)) continue;
            if (BaseDistanceRatio(from, to) is not { } ratio) continue;
            string text = L.Get("assist.ratio", ratio);
            var label = new DistanceLabel((from.TimeMs + to.TimeMs) / 2,
                (float)((from.X + to.X) / 2 / 512 * Playfield.Width) + 8, c.MeasureText(text, 11) + 8, text);
            nearby.RemoveAll(other => (label.Time - other.Time) * pixelsPerMs > 21);
            if (nearby.Any(other => label.X <= other.X + other.Width + 3 && label.X + label.Width >= other.X - 3)) continue;
            nearby.Add(label);
            distanceLayout.Add(label);
        }
    }

    private void DrawMovementDistanceLabels(ICanvas c)
    {
        if (!movementAnalysis) return;
        var objects = placementMovementObjects ?? conversion!.Objects;
        EnsureMovementStates(objects);
        EnsureDistanceLabelLayout(c, objects);
        double endTime = viewStart + plot.Height / pixelsPerMs;
        var occupied = new List<Rect>();
        if (PlacementGhostPoint() is not null || FlagTargets().Length == 1)
        {
            float panelWidth = Math.Min(MovementPanelWidth, plot.Width - 12);
            occupied.Add(new(Math.Clamp(Playfield.X + Playfield.Width / 2 - panelWidth / 2, plot.X + 6, plot.Right - panelWidth - 6),
                plot.Bottom - MovementPanelHeight - 8, panelWidth, MovementPanelHeight));
        }
        foreach (var shower in Document.BananaShowers) occupied.Add(BananaRectangle(shower));
        int low = 0, high = distanceLayout.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (distanceLayout[mid].Time < viewStart) low = mid + 1; else high = mid;
        }
        for (int i = low; i < distanceLayout.Count; i++)
        {
            var label = distanceLayout[i];
            if (label.Time > endTime) break;
            if (breakPeriods.Any(period => label.Time >= period.StartMs && label.Time <= period.EndMs)) continue;
            var bounds = new Rect(Playfield.X + label.X,
                plot.Bottom - (float)((label.Time - viewStart) * pixelsPerMs) - 9, label.Width, 18);
            if (bounds.X < plot.X || bounds.Right > plot.Right || bounds.Y < plot.Y || bounds.Bottom > plot.Bottom
                || occupied.Any(r => Intersects(r, bounds))) continue;
            c.Fill(bounds, Background, 3, .8f);
            c.Text(label.Text, bounds.X + 4, bounds.Y + 2, 11, Foreground, label.Width - 8);
            distanceLabelBounds.Add(bounds);
        }
    }
}
