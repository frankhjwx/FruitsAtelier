using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool stackMode;
    private TimingMap.Lookup? stackTiming;
    private const double StackMaximumDistance = 32;
    private StackEnvelope stackDraft = new();
    private CurveTrack? stackPreviewSource;
    private IReadOnlyList<ConvertedCatchObject> stackPreview = [];
    private Rect stackGraph;
    public Rect StackPreviewBounds { get; private set; }
    private float stackPreviewBottom, stackPreviewRadius;
    private double stackPreviewScrollMs;
    private float StackPreviewContentWidth => (float)Math.Max(1,
        (StackPreviewBounds.Width - 12) / (1 + 2 * CatchSize.FruitRadius(Document.CircleSize) / 512));
    private double StackPreviewScale => CatchScrollTiming.PixelsPerMs(Document.ApproachRate, StackPreviewContentWidth);
    private double StackPreviewScrollMaximum => stackPreviewSource is null ? 0 : Math.Max(0,
        CurveMath.EndTimeMs(stackPreviewSource) - stackPreviewSource.Nodes[0].TimeMs
        - Math.Max(0, StackPreviewBounds.Height - 2 * (stackPreviewRadius + 2)) / StackPreviewScale);
    private int FirstConversionPreviewFruit(double time)
    {
        int low = 0, high = stackPreview.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (stackPreview[mid].TimeMs < time) low = mid + 1; else high = mid;
        }
        return low;
    }
    private void ScrollConversionPreview(float delta)
    {
        if (stackPointDragging >= 0 || stackFruitDragging >= 0 || streamSnapDragging) return;
        stackPreviewScrollMs = Math.Clamp(stackPreviewScrollMs + delta / 120 * 64 / StackPreviewScale, 0, StackPreviewScrollMaximum);
    }
    private int stackFruitDragging = -1, stackSelectedFruit = -1;
    private bool stackFruitGraphDragging;
    private readonly List<int> stackManualFruitIndices = [];
    private float stackFruitPointerStart;
    private double stackFruitStartX, stackFruitBaseX, stackFruitProgress;
    private float StackPreviewX(double x) => StackPreviewBounds.X + 4 + stackPreviewRadius + (float)(x / 512) * StackPreviewContentWidth;
    private float StackFruitX(ConvertedCatchObject fruit) => StackPreviewX(fruit.X);
    private float StackFruitY(ConvertedCatchObject fruit) => stackPreviewBottom - (float)((fruit.TimeMs - stackPreviewSource!.Nodes[0].TimeMs
        - stackPreviewScrollMs) * StackPreviewScale);
    private double StackFruitProgressAt(int index) => (stackPreview[index].TimeMs - stackPreviewSource!.Nodes[0].TimeMs)
        / Math.Max(.001, CurveMath.EndTimeMs(stackPreviewSource) - stackPreviewSource.Nodes[0].TimeMs);
    private double StackFruitDistance(int index) => Math.Abs(stackPreview[index].X
        - CurveMath.PositionAtTime(stackPreviewSource!, stackPreview[index].TimeMs));
    private double StackGraphDistanceAt(double progress)
    {
        if (stackPreview.Count == 0) return stackDraft.DistanceAt(progress);
        int low = 0, high = stackPreview.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (StackFruitProgressAt(mid) < progress) low = mid + 1; else high = mid;
        }
        int right = Math.Min(low, stackPreview.Count - 1), left = Math.Max(0, right - 1);
        double a = StackFruitProgressAt(left), b = StackFruitProgressAt(right);
        double Correction(int index, double position) => Math.Abs(stackDraft.AdjustmentAt(position)) < 1e-8 ? 0
            : StackFruitDistance(index) - stackDraft.DistanceAt(position);
        double u = b > a ? Math.Clamp((progress - a) / (b - a), 0, 1) : 0;
        u = u * u * (3 - 2 * u);
        return Math.Max(0, stackDraft.DistanceAt(progress) + Correction(left, a) * (1 - u) + Correction(right, b) * u);
    }
    private void BeginStackFruitDrag(int index, float x, bool graph)
    {
        stackDragStart = stackDraft.DeepClone();
        stackFruitDragging = stackSelectedFruit = index; stackFruitGraphDragging = graph;
        var selected = stackPreview[index];
        stackFruitPointerStart = x; stackFruitStartX = selected.X;
        stackFruitProgress = StackFruitProgressAt(index);
        double side = ((selected.EventIndex % 2 == 0) == stackDraft.StartLeft ? -1 : 1);
        stackFruitBaseX = Math.Clamp(CurveMath.PositionAtTime(stackPreviewSource!, selected.TimeMs)
            + side * stackDraft.DistanceAt(stackFruitProgress), 0, 512);
    }
    private bool StackFruitPointerDown(float x, float y, int button)
    {
        if (!StackPreviewBounds.Contains(x, y)) return false;
        if (button != 0) return true;
        int nearest = -1;
        double best = Math.Pow(stackPreviewRadius + 3, 2);
        void CheckFruit(int index)
        {
            var fruit = stackPreview[index];
            double distance = Math.Pow(x - StackFruitX(fruit), 2) + Math.Pow(y - StackFruitY(fruit), 2);
            if (distance < best) { best = distance; nearest = index; }
        }
        double start = stackPreviewSource!.Nodes[0].TimeMs + stackPreviewScrollMs;
        for (int i = FirstConversionPreviewFruit(start - (stackPreviewRadius + 3) / StackPreviewScale);
            i < stackPreview.Count && StackFruitY(stackPreview[i]) >= StackPreviewBounds.Y - stackPreviewRadius - 3; i++) CheckFruit(i);
        if (nearest < 0) { stackSelectedFruit = -1; return true; }
        BeginStackFruitDrag(nearest, x, false);
        return true;
    }
    private void MoveStackFruit(float x, float y)
    {
        if (stackFruitDragging < 0) return;
        double desired;
        if (stackFruitGraphDragging)
        {
            double centre = CurveMath.PositionAtTime(stackPreviewSource!, stackPreview[stackFruitDragging].TimeMs);
            double side = Math.Sign(stackFruitStartX - centre);
            if (side == 0) side = ((stackFruitDragging % 2 == 0) == stackDraft.StartLeft ? -1 : 1);
            double distance = Math.Round(Math.Clamp((stackGraph.Bottom - y) / stackGraph.Height, 0, 1) * StackMaximumDistance);
            desired = Math.Clamp(centre + side * distance, 0, 512);
        }
        else desired = Math.Clamp(stackFruitStartX + (x - stackFruitPointerStart) / StackPreviewContentWidth * 512, 0, 512);
        double center = CurveMath.PositionAtTime(stackPreviewSource!, stackPreview[stackFruitDragging].TimeMs);
        double direction = Math.Sign(desired - center);
        desired = Math.Clamp(center + direction * Math.Round(Math.Min(StackMaximumDistance, Math.Abs(desired - center))), 0, 512);
        stackDraft.SetAdjustment(stackFruitProgress, !stackFruitGraphDragging && Math.Abs(x - stackFruitPointerStart) < .001
            ? stackDragStart!.AdjustmentAt(stackFruitProgress) : desired - stackFruitBaseX);
        RefreshStackPreview();
    }
    public Rect StackGraphBounds => stackGraph;
    private StackEnvelope? stackDragStart;
    private int stackPointDragging = -1, stackSelectedPoint;
    private float stackPointStartX, stackPointStartY;
    private void RefreshStackPreview()
    {
        if (stackPreviewSource is null) return;
        stackPreviewSource.StreamSnapDivisor = StreamSnapDivisor;
        stackPreviewSource.Stack = stackMode ? stackDraft : null;
        try
        {
            stackPreview = SliderFruitStream.Convert(Document, stackPreviewSource, stackTiming!); streamError = "";
            stackManualFruitIndices.Clear();
            for (int i = 0; i < stackPreview.Count; i++)
                if (Math.Abs(stackDraft.AdjustmentAt(StackFruitProgressAt(i))) > 1e-8)
                {
                    stackManualFruitIndices.Add(i);

                }
        }
        catch (Exception error) { stackPreview = []; streamError = error.Message; }
    }
    private float StackPointX(StackPoint p) => stackGraph.X + (float)p.Progress * stackGraph.Width;
    private float StackPointY(StackPoint p) => stackGraph.Bottom - (float)(p.Distance / StackMaximumDistance) * stackGraph.Height;
    private bool StackPointerDown(float x, float y, int button)
    {
        if (StackFruitPointerDown(x, y, button)) return true;
        if (!new Rect(stackGraph.X - 8, stackGraph.Y - 8, stackGraph.Width + 16, stackGraph.Height + 16).Contains(x, y)) return false;
        int manual = -1;
        double manualBest = 144;
        foreach (int index in stackManualFruitIndices)
        {
            var point = new StackPoint(StackFruitProgressAt(index), StackFruitDistance(index));
            double distance = Math.Pow(x - StackPointX(point), 2) + Math.Pow(y - StackPointY(point), 2);
            if (distance < manualBest) { manualBest = distance; manual = index; }
        }
        if (manual >= 0 && button == 2)
        {
            stackDraft.SetAdjustment(StackFruitProgressAt(manual), 0); stackSelectedFruit = -1;
            RefreshStackPreview(); RecordStackDraft(); return true;
        }
        if (manual >= 0 && button == 0)
        { BeginStackFruitDrag(manual, x, true); return true; }
        int nearest = -1;
        double best = 144;
        for (int i = 0; i < stackDraft.Points.Count; i++)
        {
            var source = stackDraft.Points[i];
            var p = source with { Distance = StackGraphDistanceAt(source.Progress) };
            double distance = Math.Pow(x - StackPointX(p), 2) + Math.Pow(y - StackPointY(p), 2);
            if (distance < best) { best = distance; nearest = i; }
        }
        if (button == 2)
        {
            if (nearest > 0 && nearest < stackDraft.Points.Count - 1)
            { stackDraft.Points.RemoveAt(nearest); stackSelectedPoint = 0; stackSelectedFruit = -1; RefreshStackPreview(); RecordStackDraft(); }
            return true;
        }
        if (button != 0) return true;
        stackDragStart = stackDraft.DeepClone();
        if (nearest < 0)
        {
            if (!stackGraph.Contains(x, y)) return false;
            if (stackDraft.Points.Count >= 64) return true;
            double progress = Math.Clamp((x - stackGraph.X) / stackGraph.Width, 0.001, 0.999);
            nearest = stackDraft.Points.FindIndex(p => p.Progress > progress);
            if (progress - stackDraft.Points[nearest - 1].Progress < 0.001 || stackDraft.Points[nearest].Progress - progress < 0.001) return true;
            stackDraft.Points.Insert(nearest, new(progress, Math.Round(Math.Clamp((stackGraph.Bottom - y) / stackGraph.Height, 0, 1) * StackMaximumDistance)));
        }
        stackSelectedFruit = -1;
        stackPointStartX = x; stackPointStartY = y;
        stackPointDragging = stackSelectedPoint = nearest;
        MoveStackPoint(x, y); RefreshStackPreview();
        return true;
    }
    private void CancelStackDrag()
    {
        if ((stackPointDragging >= 0 || stackFruitDragging >= 0) && stackDragStart is not null)
        { stackDraft = stackDragStart; RefreshStackPreview(); }
        stackPointDragging = stackFruitDragging = -1; stackDragStart = null;
    }
    private void MoveStackPoint(float x, float y)
    {
        if (Math.Abs(x - stackPointStartX) < .01 && Math.Abs(y - stackPointStartY) < .01) return;
        int i = stackPointDragging;
        if (i < 0 || i >= stackDraft.Points.Count) return;
        double progress = i == 0 ? 0 : i == stackDraft.Points.Count - 1 ? 1
            : Math.Clamp((x - stackGraph.X) / stackGraph.Width, stackDraft.Points[i - 1].Progress + 0.0001, stackDraft.Points[i + 1].Progress - 0.0001);
        double distance = Math.Round(Math.Clamp((stackGraph.Bottom - y) / stackGraph.Height, 0, 1) * StackMaximumDistance);
        stackDraft.Points[i] = new(progress, distance);
        RefreshStackPreview();
    }
    private void DrawStackDialog(ICanvas c)
    {
        c.Clip(plot);
        int low = 0, high = stackPreview.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (stackPreview[mid].TimeMs < viewStart) low = mid + 1; else high = mid;
        }
        double endTime = viewStart + plot.Height / pixelsPerMs;
        for (int i = low; i < stackPreview.Count && stackPreview[i].TimeMs <= endTime; i++)
        {
            var fruit = stackPreview[i];
            var p = Screen(new(fruit.TimeMs, fruit.X));
            if (!plot.Contains(p.X, p.Y)) continue;
            DrawCatchObject(c, fruit, p.X, p.Y, Playfield.Width, .65f);
        }
        c.Unclip();
        hits.Clear();
        float w = Math.Min(700, width - 32), h = Math.Min(510, height - 32);
        float x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Grid, radius: 8);
        c.Text(L.Get(changingStreamSnap ? "conversion.editTitle" : "conversion.title"), x + 24, y + 20, 16, Foreground, w - 48, true);
        Button(c, new(x + 24, y + 58, 96, 28), L.Get("conversion.streamTab"), () => SetConversionMode(false), active: !stackMode);
        Button(c, new(x + 128, y + 58, 96, 28), L.Get("conversion.stackTab"), () => SetConversionMode(true), active: stackMode);
        float columnWidth = (w - 68) / 2;
        float leftX = x + 24, rightX = leftX + columnWidth + 20;
        StreamSnapBounds = new(leftX + 42, y + 110, columnWidth - 42, 32);
        float left = StreamSnapBounds.X + 7, right = StreamSnapBounds.Right - 55;
        c.Text(L.Get("ui.snap"), leftX, y + 120, 11, Muted, 40);
        c.Line(left, y + 126, right, y + 126, Accent, 2);
        c.Circle(left + Array.IndexOf(SnapDivisors, StreamSnapDivisor) / (float)(SnapDivisors.Length - 1) * (right - left), y + 126, 6, Accent);
        c.Text(L.Get("ui.snapDivisor", StreamSnapDivisor), right + 16, y + 120, 10, Foreground, 36);
        ToggleSwitch(c, new(leftX, y + 158, columnWidth, 32), L.Get("stream.breakFruits"), StreamBreakIntoFruits,
            () => StreamBreakIntoFruits = !StreamBreakIntoFruits);
        if (stackMode)
        {
            ToggleSwitch(c, new(leftX, y + 206, columnWidth, 32), L.Get("stack.left"), stackDraft.StartLeft,
                () => { stackDraft.StartLeft = !stackDraft.StartLeft; RefreshStackPreview(); RecordStackDraft(); });
            stackGraph = new(leftX, y + 260, columnWidth, Math.Max(60, h - 363));
            c.Fill(stackGraph, Surface); c.Stroke(stackGraph, Grid);
            for (int i = 0; i <= 32; i++)
            {
                float gy = stackGraph.Bottom - i * stackGraph.Height / 32;
                c.Line(stackGraph.X, gy, stackGraph.Right, gy, Grid, opacity: i % 8 == 0 ? 1 : .35f);
                if (i % 8 == 0) c.Text(i.ToString(), stackGraph.X + 3, gy - 12, 9, Muted, 32);
            }
            for (int i = 1; i <= 128; i++)
            {
                double a = (i - 1) / 128d, b = i / 128d;
                c.Line(StackPointX(new(a, 0)), StackPointY(new(a, StackGraphDistanceAt(a))),
                    StackPointX(new(b, 0)), StackPointY(new(b, StackGraphDistanceAt(b))), Accent, 2);
            }
            for (int i = 0; i < stackDraft.Points.Count; i++)
            { var p = stackDraft.Points[i] with { Distance = StackGraphDistanceAt(stackDraft.Points[i].Progress) }; c.Circle(StackPointX(p), StackPointY(p), i == stackSelectedPoint ? 6 : 4, Accent); }
            foreach (int index in stackManualFruitIndices)
            {
                var point = new StackPoint(StackFruitProgressAt(index), StackFruitDistance(index));
                c.Circle(StackPointX(point), StackPointY(point), index == stackSelectedFruit ? 6 : 4, Foreground);
            }
            DrawStackNumeric(c, leftX, y + h - 88, columnWidth);
        }
        else
        {
            stackGraph = default; StackPercentFieldBounds = StackDistanceFieldBounds = default;
        }
        Rect preview = StackPreviewBounds = new(rightX, y + 110, columnWidth, Math.Max(60, h - 213));
        c.Fill(preview, Surface); c.Stroke(preview, Grid);
        if (stackPreviewSource is { } track)
        {
            double start = track.Nodes[0].TimeMs, duration = Math.Max(0.001, CurveMath.EndTimeMs(track) - start);
            float fruitRadius = stackPreviewRadius = (float)(CatchSize.FruitRadius(Document.CircleSize) / 512 * StackPreviewContentWidth);
            float padding = fruitRadius + 2;
            float previewBottom = stackPreviewBottom = preview.Bottom - padding;
            stackPreviewScrollMs = Math.Clamp(stackPreviewScrollMs, 0, StackPreviewScrollMaximum);
            double visibleStart = start + stackPreviewScrollMs;
            double visibleEnd = Math.Min(start + duration, visibleStart + (preview.Height - padding * 2) / StackPreviewScale);
            c.Clip(preview);
            for (int i = 1; i <= 100; i++)
            {
                double ta = visibleStart + (visibleEnd - visibleStart) * (i - 1) / 100, tb = visibleStart + (visibleEnd - visibleStart) * i / 100;
                c.Line(StackPreviewX(Math.Clamp(CurveMath.PositionAtTime(track, ta), 0, 512)),
                    previewBottom - (float)((ta - visibleStart) * StackPreviewScale),
                    StackPreviewX(Math.Clamp(CurveMath.PositionAtTime(track, tb), 0, 512)),
                    previewBottom - (float)((tb - visibleStart) * StackPreviewScale), Muted);
            }
            int first = FirstConversionPreviewFruit(visibleStart - padding / StackPreviewScale);
            for (int i = first; i < stackPreview.Count && stackPreview[i].TimeMs <= visibleEnd + padding / StackPreviewScale; i++)
            {
                var fruit = stackPreview[i];
                c.Circle(StackFruitX(fruit), StackFruitY(fruit), fruitRadius,
                    i == stackSelectedFruit ? Foreground : Accent, false, i == stackSelectedFruit ? 2.5f : 1.5f);
            }
            c.Unclip();
            if (StackPreviewScrollMaximum > 0)
            {
                float thumbHeight = Math.Max(12, preview.Height * (float)((visibleEnd - visibleStart) / duration));
                float thumbY = preview.Bottom - thumbHeight - (float)(stackPreviewScrollMs / StackPreviewScrollMaximum) * (preview.Height - thumbHeight);
                c.Fill(new(preview.Right - 4, thumbY, 3, thumbHeight), Muted, 1);
                c.Text(L.Get("conversion.scroll"), preview.X, preview.Bottom + 7, 10, Muted, preview.Width);
            }
        }
        if (streamError.Length > 0) c.Text(streamError, x + 24, y + h - 111, 10, Error, w - 48);
        Button(c, new(x + w - 200, y + h - 44, 80, 30), L.Get("mac.cancel"), () => { StreamDialogVisible = false; stackPointDragging = stackFruitDragging = -1; });
        Button(c, new(x + w - 112, y + h - 44, 88, 30), L.Get("stream.confirm"), ApplyStream, true);
    }
}
