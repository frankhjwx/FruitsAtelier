using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool difficultyCurveVisible;
    private const float DifficultyCurveWidth = 280;
    private IReadOnlyList<CatchStrainSample>? cachedCurveSamples;
    private double[] curveRows = [];
    private bool[] curveHighlight = [];
    private double curveMaximum, cachedCurveDuration;
    private int cachedCurveHeight;
    public bool DifficultyCurveVisible => difficultyCurveVisible;
    public Rect DifficultyCurvePanelBounds => leftPanel;
    public Rect DifficultyCurveToggleBounds { get; private set; }
    public Rect DifficultyCurveGraphBounds { get; private set; }

    private void DrawDifficultySidebar(ICanvas c)
    {
        DifficultyCurveToggleBounds = new(leftPanel.Right - 22, leftPanel.Y + leftPanel.Height / 2 - 22, 22, 44);
        DifficultyCurveGraphBounds = default;
        if (difficultyCurveVisible && leftPanel.Width > 0)
        {
            c.Fill(leftPanel, Panel);
            c.Line(leftPanel.X, leftPanel.Y, leftPanel.X, leftPanel.Bottom, Grid);
            c.Line(leftPanel.X, leftPanel.Y + 38, leftPanel.Right, leftPanel.Y + 38, Grid);
            c.Text(L.Get("difficulty.curve.title"), leftPanel.X + 16, leftPanel.Y + 12, 13, Foreground,
                Math.Max(0, leftPanel.Width - 32), true);
            var graph = new Rect(leftPanel.X + 18, leftPanel.Y + 58,
                Math.Max(0, leftPanel.Width - 40), Math.Max(0, leftPanel.Height - 78));
            DifficultyCurveGraphBounds = graph;
            DrawDifficultyCurve(c, graph);
            c.Fill(new(leftPanel.Right - 1, leftPanel.Y + 38, 2, leftPanel.Height - 38), Grid);
        }
        c.Fill(DifficultyCurveToggleBounds, Surface, 5);
        Button(c, DifficultyCurveToggleBounds, difficultyCurveVisible ? "‹" : "›",
            () => difficultyCurveVisible = !difficultyCurveVisible);
    }

    private void DrawDifficultyCurve(ICanvas c, Rect graph)
    {
        if (graph.Width < 10 || graph.Height < 10) return;
        _ = DifficultyRating(activeDifficulty);
        var samples = difficulties[activeDifficulty].StrainSamples;
        c.Fill(graph, 0x151A22, 3);
        c.Stroke(graph, Grid, 1, 3);
        for (int guide = 1; guide < 4; guide++)
        {
            float guideY = graph.Y + graph.Height * guide / 4;
            c.Line(graph.X + 1, guideY, graph.Right - 1, guideY, Grid, 1, .35f);
        }
        if (samples is null)
        {
            c.Text(L.Get("difficulty.curve.noData"), graph.X + 8, graph.Y + 12, 11, Muted, graph.Width - 16);
        }
        else if (samples.Count > 0)
        {
            PrepareCurveRows(samples, graph.Height);
            float baseline = graph.X + 2;
            float usable = Math.Max(1, graph.Width - 8);
            float lastX = baseline;
            int rows = curveRows.Length - 1;
            c.Clip(graph);
            for (int row = 0; row <= rows; row++)
            {
                double strain = curveRows[row];
                float x = baseline + (float)(curveMaximum > 0 ? Math.Clamp(strain / curveMaximum, 0, 1) * usable : 0);
                float y = graph.Y + row * graph.Height / rows;
                bool highlight = curveHighlight[row];
                uint colour = highlight ? Gold : Accent;
                if (row > 0 && x > baseline)
                {
                    c.Fill(new(baseline, y, x - baseline, 1.1f), Accent, opacity: .16f);
                    float brightStart = baseline + (x - baseline) * .65f;
                    c.Fill(new(brightStart, y, x - brightStart, 1.1f), highlight ? Gold : Accent,
                        opacity: highlight ? .24f : .10f);
                }
                if (row > 0) c.Line(lastX, graph.Y + (row - 1) * graph.Height / rows, x, y, colour, 1.5f);
                lastX = x;
            }
            c.Unclip();
        }
        else c.Text(L.Get("difficulty.curve.noData"), graph.X + 8, graph.Y + 12, 11, Muted, graph.Width - 16);

        float markerY = graph.Bottom - (float)(Math.Clamp(playhead / Math.Max(1, TimelineDurationMs), 0, 1) * graph.Height);
        c.Line(graph.X, markerY, graph.Right, markerY, Gold, 1.5f);
        c.Circle(graph.X + 1, markerY, 5, Gold);
        if (graph.Contains(mouseX, mouseY) && menu < 0)
        {
            double time = GraphTime(mouseY);
            double strain = samples is null ? 0 : StrainAt(samples, time);
            string label = L.Get("difficulty.curve.tooltip", Time(time), Number(strain));
            float tipWidth = Math.Min(graph.Width - 8, c.MeasureText(label, 11) + 12);
            float tipY = Math.Clamp(mouseY + 14, graph.Y + 4, graph.Bottom - 25);
            c.Fill(new(graph.X + 4, tipY, tipWidth, 21), Surface, 3);
            c.Text(label, graph.X + 10, tipY + 4, 11, Foreground, tipWidth - 10);
        }
    }

    private void PrepareCurveRows(IReadOnlyList<CatchStrainSample> samples, float graphHeight)
    {
        int rows = Math.Max(1, (int)graphHeight);
        double duration = TimelineDurationMs;
        if (ReferenceEquals(samples, cachedCurveSamples) && rows == cachedCurveHeight && duration == cachedCurveDuration) return;
        cachedCurveSamples = samples;
        cachedCurveHeight = rows;
        cachedCurveDuration = duration;
        curveRows = new double[rows + 1];
        curveHighlight = new bool[rows + 1];
        curveMaximum = 0;
        for (int row = 0; row <= rows; row++)
            curveRows[row] = StrainAt(samples, (1 - row / (double)rows) * duration);
        foreach (var sample in samples)
        {
            curveMaximum = Math.Max(curveMaximum, sample.After);
            int row = (int)Math.Clamp(Math.Round((1 - sample.TimeMs / Math.Max(1, duration)) * rows), 0, rows);
            curveRows[row] = Math.Max(curveRows[row], sample.After);
        }
        var raw = curveRows;
        int radius = Math.Clamp(rows / 70, 3, 10);
        curveRows = new double[rows + 1];
        for (int row = 0; row <= rows; row++)
        {
            double weighted = 0, total = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                int neighbor = row + offset;
                if (neighbor < 0 || neighbor > rows) continue;
                int weight = radius + 1 - Math.Abs(offset);
                weighted += raw[neighbor] * weight;
                total += weight;
                if (raw[neighbor] >= curveMaximum * .8 && curveMaximum > 0) curveHighlight[row] = true;
            }
            curveRows[row] = weighted / total;
        }
    }

    private static double StrainAt(IReadOnlyList<CatchStrainSample> samples, double time)
    {
        int low = 0, high = samples.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (samples[middle].TimeMs <= time) low = middle + 1;
            else high = middle;
        }
        if (low == 0) return 0;
        var sample = samples[low - 1];
        return sample.After * Math.Pow(.2, Math.Max(0, time - sample.TimeMs) / 1000);
    }

    private double GraphTime(float y) => (1 - Math.Clamp((y - DifficultyCurveGraphBounds.Y)
        / Math.Max(1, DifficultyCurveGraphBounds.Height), 0, 1)) * TimelineDurationMs;
    private void SeekDifficultyCurve(float y) => SeekTo(GraphTime(y));
}
