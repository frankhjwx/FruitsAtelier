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
    private void DrawStackNumeric(ICanvas c, float x, float y, float columnWidth)
    {
        float labelWidth = Math.Min(58, columnWidth / 5);
        float fieldWidth = (columnWidth - labelWidth * 2 - 14) / 2;
        float secondX = x + labelWidth + fieldWidth + 14;
        StackPercentFieldBounds = new(x + labelWidth, y, fieldWidth, 28);
        StackDistanceFieldBounds = new(secondX + labelWidth, y, fieldWidth, 28);
        var selected = SelectedStackPoint;
        c.Text(L.Get("stack.timePercent"), x, y + 7, 11, Muted, labelWidth - 2);
        c.Text(L.Get("stack.distancePx"), secondX, y + 7, 11, Muted, labelWidth - 2);
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
        if (stackNumericError.Length > 0) c.Text(stackNumericError, x, y + 31, 10, Error, columnWidth);
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
}
