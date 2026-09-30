using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int stackNumericField = -1;
    private string stackNumericText = "", stackNumericOpeningText = "", stackNumericError = "";
    internal Rect StackPercentFieldBounds { get; private set; }
    internal Rect StackDistanceFieldBounds { get; private set; }
    private StackPoint SelectedStackPoint => stackSelectedFruit >= 0 && stackSelectedFruit < stackPreview.Count
        ? new(StackFruitProgressAt(stackSelectedFruit), StackFruitDistance(stackSelectedFruit))
        : stackDraft.Points[Math.Clamp(stackSelectedPoint, 0, stackDraft.Points.Count - 1)];
    private bool StackNumericPointerDown(float x, float y)
    {
        if (StackPercentFieldBounds.Contains(x, y) || StackDistanceFieldBounds.Contains(x, y)) return true;
        return CommitStackNumeric();
    }
    private void DrawStackNumeric(ICanvas c, float x, float y)
    {
        StackPercentFieldBounds = new(x + 66, y, 90, 28);
        StackDistanceFieldBounds = new(x + 246, y, 90, 28);
        var selected = SelectedStackPoint;
        c.Text(L.Get("stack.timePercent"), x, y + 7, 11, Muted, 64);
        c.Text(L.Get("stack.distancePx"), x + 180, y + 7, 11, Muted, 64);
        for (int i = 0; i < 2; i++)
        {
            int field = i; var bounds = i == 0 ? StackPercentFieldBounds : StackDistanceFieldBounds;
            string value = stackNumericField == i ? stackNumericText
                : (i == 0 ? selected.Progress * 100 : selected.Distance).ToString(i == 0 ? "0.00" : "0.########", CultureInfo.InvariantCulture);
            bool enabled = i == 1 || selected.Progress is > 0 and < 1;
            c.Fill(bounds, Surface, 4); c.Stroke(bounds, stackNumericField == i ? stackNumericError.Length > 0 ? Error : Accent : Grid, radius: 4);
            DrawInputText(c, new(bounds.X + 7, bounds.Y + 5, bounds.Width - 14, 20), value, 12, stackNumericField == i, "stack:" + i);
            hits.Add(new(bounds, () =>
            {
                if (stackNumericField == field || !CommitStackNumeric()) return;
                var point = SelectedStackPoint;
                stackNumericField = field; stackNumericError = "";
                stackNumericText = stackNumericOpeningText = (field == 0 ? point.Progress * 100 : point.Distance)
                    .ToString(field == 0 ? "0.00" : "0.########", CultureInfo.InvariantCulture);
                SelectInput("stack:" + field, stackNumericText);
            }, enabled));
        }
        if (stackNumericError.Length > 0) c.Text(stackNumericError, x + 352, y + 7, 10, Error, Math.Max(80, width / 2 - 352));
    }
    private void StackNumericText(string text)
    {
        if (stackNumericField < 0) return;
        string filtered = new(text.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        stackNumericText = InsertInput("stack:" + stackNumericField, stackNumericText, filtered, 24); stackNumericError = "";
    }
    private bool StackNumericKey(int key, bool ctrl, bool shift)
    {
        if (stackNumericField < 0) return false;
        if (key == 27) { stackNumericField = -1; stackNumericError = ""; return true; }
        if (key is 13 or 9) { CommitStackNumeric(); return true; }
        InputKey("stack:" + stackNumericField, ref stackNumericText, key, ctrl, shift, 24, RequestPasteField);
        stackNumericError = ""; return true;
    }
    private bool CommitStackNumeric()
    {
        if (stackNumericField < 0) return true;
        if (stackNumericText == stackNumericOpeningText)
        { stackNumericField = -1; stackNumericError = ""; return true; }
        if (!double.TryParse(stackNumericText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value) || value < 0 || value > (stackNumericField == 0 ? 100 : StackMaximumDistance))
        { stackNumericError = L.Get("stack.numericInvalid"); return false; }
        var selected = SelectedStackPoint;
        if (stackNumericField == 0 && Math.Abs(value / 100 - selected.Progress) > 1e-8)
        {
            double progress = value / 100;
            if (stackSelectedFruit >= 0)
            {
                if (progress <= 0 || progress >= 1 || stackDraft.Points.Count >= 64)
                { stackNumericError = L.Get("stack.numericInvalid"); return false; }
                stackDraft.SetAdjustment(selected.Progress, 0);
                int i = stackDraft.Points.FindIndex(p => p.Progress >= progress);
                if (Math.Abs(stackDraft.Points[i].Progress - progress) < 1e-8)
                    stackDraft.Points[i] = new(progress, selected.Distance);
                else stackDraft.Points.Insert(i, new(progress, selected.Distance));
                stackSelectedPoint = i; stackSelectedFruit = -1;
            }
            else
            {
                int i = stackSelectedPoint;
                if (i <= 0 || i >= stackDraft.Points.Count - 1 || progress <= stackDraft.Points[i - 1].Progress
                    || progress >= stackDraft.Points[i + 1].Progress)
                { stackNumericError = L.Get("stack.numericOrder"); return false; }
                stackDraft.Points[i] = selected with { Progress = progress };
            }
        }
        else if (stackNumericField == 1)
        {
            if (stackSelectedFruit >= 0)
            {
                var fruit = stackPreview[stackSelectedFruit]; double centre = CurveMath.PositionAtTime(stackPreviewSource!, fruit.TimeMs);
                double side = Math.Sign(fruit.X - centre);
                if (side == 0) side = ((fruit.EventIndex % 2 == 0) == stackDraft.StartLeft ? -1 : 1);
                double baseX = Math.Clamp(centre + ((fruit.EventIndex % 2 == 0) == stackDraft.StartLeft ? -1 : 1)
                    * stackDraft.DistanceAt(selected.Progress), 0, 512);
                stackDraft.SetAdjustment(selected.Progress, Math.Clamp(centre + side * value, 0, 512) - baseX);
            }
            else stackDraft.Points[stackSelectedPoint] = stackDraft.Points[stackSelectedPoint] with { Distance = value };

        }
        stackNumericField = -1; stackNumericError = ""; RefreshStackPreview(); RecordStackDraft(); return true;
    }
    private void AutoStackEnds()
    {
        if (!CommitStackNumeric()) return;
        var points = stackDraft.Points;
        double head = points.Count > 2 ? points[1].Distance : Math.Max(points[0].Distance, points[^1].Distance);
        double tail = points.Count > 2 ? points[^2].Distance : head;
        double first = points.Count > 2 ? Math.Min(.02, points[1].Progress) : .02;
        double last = points.Count > 2 ? Math.Max(.98, points[^2].Progress) : .98;
        if (points.Count > 62) { points[1] = new(first, head); points[^2] = new(last, tail); }
        else
        {
            if (points.Count == 2 || points[1].Progress > first) points.Insert(1, new(first, head));
            if (points[^2].Progress < last) points.Insert(points.Count - 1, new(last, tail));
        }
        points[0] = new(0, 0); points[^1] = new(1, 0);
        stackDraft.SetAdjustment(0, 0); stackDraft.SetAdjustment(1, 0);
        stackSelectedFruit = -1; stackSelectedPoint = 1; RefreshStackPreview(); RecordStackDraft();
    }
}
