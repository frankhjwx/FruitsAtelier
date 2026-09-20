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

    private double BaseDistanceVelocity(double time) => 100 * Document.SliderMultiplier / renderedTiming!.At(time).BeatLengthMs;
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
        PreviousDistanceFieldBounds = editable ? panel : null;
        if (DistanceEditing)
        {
            var slider = new Rect(panel.X + 12, panel.Y + 44, panel.Width - 112, 22);
            var input = new Rect(panel.Right - 88, panel.Y + 44, 76, 22);
            DistanceSliderBounds = slider;
            PreviousDistanceFieldBounds = input;
            float y = slider.Y + 11;
            c.Line(slider.X, y, slider.Right, y, Grid, 4);
            float x = slider.X + (float)(distanceMaximum > 0 ? distanceValue / distanceMaximum : 0) * slider.Width;
            c.Line(slider.X, y, x, y, Accent, 4);
            c.Fill(new(x - 5, y - 7, 10, 14), Accent, 5);
            c.Fill(input, Panel, 3);
            c.Stroke(input, fieldError.Length > 0 ? Error : Accent, 1, 3);
            DrawInputText(c, new(input.X + 5, input.Y + 3, input.Width - 10, 16), editBuffer, 11, true, replaceText);
        }
        else
        {
            c.Text(L.Get("assist.previous", DistanceReadout.Previous is { } p ? L.Get("assist.ratio", p) : "—"), panel.X + 12, panel.Y + 47, 11, Foreground, panel.Width / 2 - 16);
            c.Text(L.Get("assist.next", DistanceReadout.Next is { } n ? L.Get("assist.ratio", n) : "—"), panel.X + panel.Width / 2 + 4, panel.Y + 47, 11, Muted, panel.Width / 2 - 16);
        }
        if (DistanceEditing && fieldError.Length > 0)
        {
            c.Fill(new(panel.X, panel.Y - 30, panel.Width, 28), Panel, 4);
            c.Text(fieldError, panel.X + 6, panel.Y - 24, 11, Error, panel.Width - 12);
        }
    }

    private bool DistancePointerDown(float x, float y, int button)
    {
        if (DistanceEditing)
        {
            if (MovementOverlayBounds is { } panel && panel.Contains(x, y))
            {
                if (button == 0 && DistanceSliderBounds is { } slider && slider.Contains(x, y))
                { distanceDragging = true; UpdateDistanceSlider(x); }
                else if (button == 0) { replaceText = true; ResetTextCaret(); }
                return true;
            }
            if (!FinishDistanceEdit(false)) return true;
        }
        if (button != 0 || PreviousDistanceFieldBounds is not { } bounds || !bounds.Contains(x, y)) return false;
        if (editField >= 0 && !CommitField()) return true;
        distanceEditTarget = SelectedDistanceObject();
        distanceEditReference = DistanceNeighbours(distanceEditTarget!).Previous;
        distanceValue = DistanceReadout.Previous!.Value;
        double unit = (distanceEditTarget!.TimeMs - distanceEditReference!.TimeMs) * BaseDistanceVelocity(distanceEditReference.TimeMs);
        int direction = Math.Sign(distanceEditTarget.X - distanceEditReference.X);
        distanceMaximum = direction == 0 ? 0 : (direction > 0 ? 512 - distanceEditReference.X : distanceEditReference.X) / unit;
        editBuffer = distanceValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        replaceText = true; fieldError = "";
        history.Begin(L.Get("editor.command.changeField", L.Get("distance.previousField")));
        return true;
    }

    private void PreviewDistance(double value)
    {
        // Rebuild each preview from the original geometry and direction, including after passing through zero.
        history.Cancel();
        history.Begin(L.Get("editor.command.changeField", L.Get("distance.previousField")));
        try
        {
            ApplyDistanceRatio(distanceEditTarget!, distanceEditReference!, false, value);
            distanceValue = value; fieldError = "";
        }
        catch (ArgumentException error)
        {
            history.Cancel();
            history.Begin(L.Get("editor.command.changeField", L.Get("distance.previousField")));
            ApplyDistanceRatio(distanceEditTarget!, distanceEditReference!, false, distanceValue);
            fieldError = error.Message;
        }
    }

    private void UpdateDistanceSlider(float x)
    {
        if (DistanceSliderBounds is not { } slider) return;
        double value = Math.Min(Math.Floor(distanceMaximum * 10) / 10,
            Math.Round(Math.Clamp((x - slider.X) / slider.Width, 0, 1) * distanceMaximum, 1, MidpointRounding.AwayFromZero));
        PreviewDistance(value);
        editBuffer = distanceValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        replaceText = true;
    }

    private void PreviewDistanceText()
    {
        ResetTextCaret();
        if (double.TryParse(editBuffer, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
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
        distanceDragging = false; fieldError = "";
        return true;
    }

    private bool DistanceKeyDown(int key, bool ctrl)
    {
        if (!DistanceEditing) return false;
        if (key == 27) FinishDistanceEdit(true);
        else if (key is 13 or 9) FinishDistanceEdit(false);
        else if (ctrl && key == 65) replaceText = true;
        else if (key is 8 or 46)
        {
            editBuffer = replaceText || key == 46 ? "" : editBuffer.Length > 0 ? editBuffer[..^1] : "";
            replaceText = false; PreviewDistanceText();
        }
        return true;
    }

    private void DistanceTextInput(char value)
    {
        if (!(char.IsAsciiDigit(value) || value is '.' or '-')) return;
        string next = replaceText ? value.ToString() : editBuffer + value;
        int dot = next.IndexOf('.');
        if (dot >= 0 && next.Length - dot - 1 > 2) return;
        editBuffer = next; replaceText = false; PreviewDistanceText();
    }

    private void ApplyDistanceRatio(ConvertedCatchObject target, ConvertedCatchObject reference, bool next, double value)
    {
        try { DistanceSpacingEditing.Apply(Document, target, reference, next, value, compensateTinyDroplets); }
        catch (InvalidOperationException error) { throw new ArgumentException(error.Message, error); }
        EnsureConversion();
        var updated = conversion!.Objects.FirstOrDefault(o => o.SourceId == target.SourceId && o.Kind == target.Kind && Math.Abs(o.TimeMs - target.TimeMs) < .001);
        if (updated is not null) distanceObject = (updated.SourceId, updated.EventIndex);
    }

    private void DrawMovementDistanceLabels(ICanvas c)
    {
        if (!movementAnalysis) return;
        var objects = placementMovementObjects ?? conversion!.Objects;
        EnsureMovementStates(objects);
        double endTime = viewStart + plot.Height / pixelsPerMs;
        double margin = Math.Max(200, CatchSize.FruitRadius(Document.CircleSize) * Playfield.Width / 512 * 4 / pixelsPerMs);
        int low = 0, high = objects.Count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (objects[mid].TimeMs < viewStart - margin) low = mid + 1; else high = mid;
        }
        var occupied = new List<Rect>();
        for (int i = low; i < objects.Count && objects[i].TimeMs <= endTime + margin; i++)
        {
            var bounds = CatchHitBounds(objects[i]);
            if (Intersects(bounds, plot)) occupied.Add(bounds);
        }
        if (PlacementGhostPoint() is not null || FlagTargets().Length == 1)
        {
            float panelWidth = Math.Min(340, plot.Width - 12);
            occupied.Add(new(Math.Clamp(Playfield.X + Playfield.Width / 2 - panelWidth / 2, plot.X + 6, plot.Right - panelWidth - 6),
                plot.Bottom - 78, panelWidth, 70));
        }
        foreach (var shower in Document.BananaShowers) occupied.Add(BananaRectangle(shower));
        for (int i = FirstVisibleMovement(objects); i < movementIndices.Length; i++)
        {
            var from = objects[movementIndices[i - 1]];
            var to = objects[movementIndices[i]];
            if (from.TimeMs > endTime) break;
            if (to.TimeMs < viewStart || from.TimeMs >= to.TimeMs
                || Document.BananaShowers.Any(s => s.TimeMs <= to.TimeMs && s.EndTimeMs >= from.TimeMs)) continue;
            if (BaseDistanceRatio(from, to) is not { } ratio) continue;
            string label = L.Get("assist.ratio", ratio);
            float width = c.MeasureText(label, 11) + 8;
            var p = Screen(new((from.TimeMs + to.TimeMs) / 2, (from.X + to.X) / 2));
            var bounds = new Rect(p.X + 8, p.Y - 9, width, 18);
            if (bounds.X < plot.X || bounds.Right > plot.Right || bounds.Y < plot.Y || bounds.Bottom > plot.Bottom
                || occupied.Any(r => Intersects(r, bounds))) continue;
            c.Fill(bounds, Background, 3, .8f);
            c.Text(label, bounds.X + 4, bounds.Y + 2, 11, Foreground, width - 8);
            occupied.Add(new(bounds.X - 3, bounds.Y - 3, bounds.Width + 6, bounds.Height + 6));
            distanceLabelBounds.Add(bounds);
        }
    }
}
