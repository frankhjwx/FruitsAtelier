using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private IReadOnlyList<ConvertedCatchObject>? lazerGuideMovementSource;
    private double lazerGuideCircleSize;
    private readonly HashSet<Guid> lazerGuideExcluded = [];
    private ConvertedCatchObject[] lazerGuideMovementObjects = [];
    private HyperDashContext?[] lazerGuideMovementContexts = [];

    private void EnsureLazerGuideMovement(ISet<Guid>? excluded)
    {
        if (ReferenceEquals(lazerGuideMovementSource, playableObjects) && lazerGuideCircleSize == Document.CircleSize
            && (excluded is null ? lazerGuideExcluded.Count == 0 : lazerGuideExcluded.SetEquals(excluded))) return;
        lazerGuideMovementSource = playableObjects;
        lazerGuideCircleSize = Document.CircleSize;
        lazerGuideExcluded.Clear();
        if (excluded is not null) lazerGuideExcluded.UnionWith(excluded);
        lazerGuideMovementObjects = playableObjects.Where(item => !lazerGuideExcluded.Contains(item.SourceId)
            && item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet).OrderBy(item => item.TimeMs).ToArray();
        lazerGuideMovementContexts = HyperDashCalculator.CreateContexts(lazerGuideMovementObjects, Document.CircleSize);
    }

    internal CatchMovementMode LazerGuideMovementAt(double time, double x)
    {
        int low = 0, high = lazerGuideMovementObjects.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (lazerGuideMovementObjects[mid].TimeMs < time) low = mid + 1; else high = mid;
        }
        if (low == 0) return CatchMovementMode.Stand;
        var departure = lazerGuideMovementObjects[low - 1];
        var context = lazerGuideMovementContexts[low - 1]!.Value;
        return HyperDashCalculator.Evaluate(departure.TimeMs, departure.X, time, x, context).State.Movement?.Mode
            ?? CatchMovementMode.Stand;
    }

    private void DrawLazerColouredGuide(ICanvas c, MapPoint origin, double velocity, double end)
    {
        double start = Math.Max(origin.TimeMs, Transform.ToMap(Playfield.X, plot.Bottom).TimeMs);
        if (end <= start) return;
        MapPoint Point(double time) => new(time, origin.X + (time - origin.TimeMs) * velocity);
        var a = Screen(Point(start));
        var b = Screen(Point(end));
        double length = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
        int steps = Math.Max(1, (int)Math.Ceiling(length / 3));
        double step = (end - start) / steps;
        double runStart = start;
        uint runColour = 0;
        for (int i = 0; i < steps; i++)
        {
            double first = start + i * step, last = start + (i + 1) * step;
            double middle = (first + last) / 2;
            uint colour = BlendGuideColours(ColourAt(middle - step), ColourAt(middle), ColourAt(middle + step));
            if (i > 0 && colour != runColour)
            {
                DrawRun(first);
                runStart = first;
            }
            runColour = colour;
        }
        DrawRun(end);

        void DrawRun(double until)
        {
            var from = Screen(Point(runStart));
            var to = Screen(Point(until));
            c.Line(from.X, from.Y, to.X, to.Y, runColour, 4 * Playfield.Width / 512, .5f);
        }

        uint ColourAt(double time)
        {
            time = Math.Clamp(time, Math.Min(end, origin.TimeMs + .0001), end);
            return MovementColour(LazerGuideMovementAt(time, Point(time).X));
        }
    }

    // A short screen-space transition softens category boundaries without colouring an entire speed uniformly.
    private static uint BlendGuideColours(uint before, uint current, uint after)
    {
        uint result = 0;
        for (int shift = 0; shift <= 16; shift += 8)
            result |= ((((before >> shift) & 255) + 2 * ((current >> shift) & 255) + ((after >> shift) & 255)) / 4) << shift;
        return result;
    }
}
