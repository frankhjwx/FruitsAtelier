using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed class OsuCanvasObject
    {
        public required Guid Id;
        public double Start, End, X, Y;
        public int Order, Flags, Number, Colour, Stack, Spans = 1;
        public string NumberText = "";
        public bool Spinner;
        public SliderPathPoint[] Path = [];
        public System.Numerics.Vector2[] StrokePath = [];
        public double[] Distances = [];
        public double TickDistance, TickEndDistance;
    }

    private object? osuCanvasSource;
    private double osuCanvasAr = double.NaN, osuCanvasCs;
    private OsuCanvasObject[] osuCanvasObjects = [];
    private double[] osuCanvasMaximumEnds = [];
    private readonly List<int> osuCanvasVisible = [];
    private ImportedSlider? osuDraftPathSource;
    private int osuDraftPathCount;
    private char osuDraftPathType;
    private (double X, double Y)? osuDraftPathPointer;
    private IReadOnlyList<SliderPathPoint> osuDraftPath = [];
    private System.Numerics.Vector2[] osuDraftStrokePath = [];
    private double osuCanvasZoom = 1, osuCanvasPanX, osuCanvasPanY;
    internal Rect OsuCanvasBounds
    {
        get
        {
            float scale = Math.Max(.01f, Math.Min((plot.Width - 32) / 512, (plot.Height - 32) / 384)) * (float)osuCanvasZoom;
            return new(plot.X + (plot.Width - 512 * scale) / 2 + (float)osuCanvasPanX, plot.Y + (plot.Height - 384 * scale) / 2 + (float)osuCanvasPanY, 512 * scale, 384 * scale);
        }
    }
    internal void ZoomOsuCanvasAt(float x, float y, double factor)
    {
        var point = OsuCanvasPoint(x, y);
        osuCanvasZoom = Math.Clamp(osuCanvasZoom * factor, .25, 4);
        var next = OsuCanvasScreen(point.X, point.Y);
        osuCanvasPanX += x - next.X; osuCanvasPanY += y - next.Y;
    }
    internal (double X, double Y) OsuCanvasPoint(float x, float y)
    {
        var r = OsuCanvasBounds;
        return ((x - r.X) * 512 / r.Width, (y - r.Y) * 384 / r.Height);
    }
    internal (float X, float Y) OsuCanvasScreen(double x, double y)
    {
        var r = OsuCanvasBounds;
        return (r.X + (float)x * r.Width / 512, r.Y + (float)y * r.Height / 384);
    }
    private double OsuCanvasPreempt => Document.ApproachRate <= 5 ? 1800 - Document.ApproachRate * 120 : 1200 - (Document.ApproachRate - 5) * 150;
    private double OsuCanvasRadius => Math.Max(1, 54.4 - 4.48 * Document.CircleSize);

    private void EnsureOsuCanvasObjects()
    {
        object? source = (object?)playableExport ?? conversion;
        if (ReferenceEquals(source, osuCanvasSource) && osuCanvasAr == Document.ApproachRate && osuCanvasCs == Document.CircleSize) return;
        osuCanvasSource = source; osuCanvasAr = Document.ApproachRate; osuCanvasCs = Document.CircleSize;
        var map = playableExport?.ReadBack ?? Document;
        var sliders = conversion?.Sliders.ToDictionary(s => s.SourceId) ?? [];
        var items = new List<OsuCanvasObject>();
        foreach (var f in map.Fruits)
        {
            Guid id = playableExport is { } output && f.SourceOrder >= 0 && f.SourceOrder < output.ObjectSources.Count ? output.ObjectSources[f.SourceOrder] : f.Id;
            items.Add(new() { Id = id, Start = f.TimeMs, End = f.TimeMs, X = f.X, Y = f.Y, Order = f.SourceOrder, Flags = Flags(f.OriginalLine) });
        }
        var timing = new TimingMap.Lookup(map);
        foreach (var s in map.ImportedSliders)
        {
            sliders.TryGetValue(s.Id, out var converted);
            var path = ImportedSliderConverter.Path(s).ToArray();
            var state = timing.At(s.TimeMs);
            double velocity = 100 * map.EffectiveSliderMultiplier * state.SliderVelocityMultiplier / state.BeatLengthMs;
            double length = s.PixelLength;
            double duration = length / Math.Max(.000001, velocity) * s.SpanCount;
            double tick = converted?.TickDistance ?? 100 * map.EffectiveSliderMultiplier * state.SliderVelocityMultiplier / (map.SliderTickRate > 0 ? map.SliderTickRate : 1);
            if (!state.GenerateTicks) tick = 0;
            items.Add(Slider(s.Id, s.TimeMs, duration, s.SourceOrder, Flags(s.OriginalLine), s.SpanCount, path, tick));
        }
        if (playableExport is null)
            foreach (var t in map.Tracks)
                if (sliders.TryGetValue(t.Id, out var s))
                    items.Add(Slider(t.Id, s.StartTimeMs, s.DurationMs, t.SourceOrder, Flags(t.OriginalLine), s.SpanCount, s.Path.ToArray(), s.TickDistance));
        foreach (var s in map.BananaShowers)
            items.Add(new() { Id = s.Id, Start = s.TimeMs, End = s.EndTimeMs, X = 256, Y = 192, Spinner = true, Order = s.SourceOrder, Flags = Flags(s.OriginalLine) });
        osuCanvasObjects = items.OrderBy(i => i.Start).ThenBy(i => i.Order).ToArray();
        int number = 0, colour = -1;
        bool afterSpinner = false;
        foreach (var o in osuCanvasObjects)
        {
            if (!o.Spinner && (number == 0 || afterSpinner || (o.Flags & 4) != 0)) { number = 0; colour += 1 + ((o.Flags >> 4) & 7); }
            o.Number = o.Spinner ? 0 : ++number; o.NumberText = o.Number.ToString(CultureInfo.InvariantCulture);
            o.Colour = Math.Max(0, colour); afterSpinner = o.Spinner;
        }
        ApplyOsuCanvasStacking();
        osuCanvasMaximumEnds = new double[osuCanvasObjects.Length];
        double maximum = double.NegativeInfinity;
        for (int i = 0; i < osuCanvasObjects.Length; i++) osuCanvasMaximumEnds[i] = maximum = Math.Max(maximum, osuCanvasObjects[i].End);

        static int Flags(string? line) => line?.Split(',') is { Length: > 3 } p && int.TryParse(p[3], out int flags) ? flags : 0;
        static OsuCanvasObject Slider(Guid id, double start, double duration, int order, int flags, int spans, SliderPathPoint[] path, double tick)
        {
            var distances = new double[path.Length];
            for (int i = 1; i < path.Length; i++) distances[i] = distances[i - 1] + Math.Sqrt(Math.Pow(path[i].X - path[i - 1].X, 2) + Math.Pow(path[i].GeometryY - path[i - 1].GeometryY, 2));
            double tickEnd = distances.Length > 0 && duration > 0 ? distances[^1] * (1 - 10 * spans / duration) : 0;
            return new() { Id = id, Start = start, End = start + duration, Order = order, Flags = flags, Spans = Math.Max(1, spans), Path = path, StrokePath = path.Select(p => new System.Numerics.Vector2((float)p.X, (float)p.GeometryY)).ToArray(), Distances = distances, TickDistance = tick, TickEndDistance = tickEnd, X = path.Length > 0 ? path[0].X : 256, Y = path.Length > 0 ? path[0].GeometryY : 192 };
        }
    }

    private void ApplyOsuCanvasStacking()
    {
        double leniency = .7;
        foreach (var section in Document.OriginalSections)
            if (section.Name.Equals("General", StringComparison.OrdinalIgnoreCase))
                foreach (var line in section.Lines)
                    if (line.Split(':', 2) is { Length: 2 } p && p[0].Trim() == "StackLeniency" && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) leniency = v;
        float threshold = (int)OsuCanvasPreempt * (float)leniency;
        for (int i = osuCanvasObjects.Length - 1; i > 0; i--)
        {
            var current = osuCanvasObjects[i];
            if (current.Spinner || current.Stack != 0) continue;
            bool sliderBranch = current.Path.Length > 0;
            for (int n = i - 1; n >= 0; n--)
            {
                var previous = osuCanvasObjects[n];
                if (previous.Spinner) continue;
                if (sliderBranch ? current.Start - previous.Start > threshold : (int)current.Start - (int)previous.End > threshold) break;
                var tail = OsuPathPosition(previous, previous.Spans % 2 == 0 ? 0 : 1);
                if (sliderBranch)
                {
                    if (Distance(tail.X, tail.Y, current.X, current.Y) < 3) { previous.Stack = current.Stack + 1; current = previous; }
                    continue;
                }
                if (previous.Path.Length > 0 && Distance(tail.X, tail.Y, current.X, current.Y) < 3)
                {
                    int offset = current.Stack - previous.Stack + 1;
                    for (int j = n + 1; j <= i; j++)
                        if (Distance(tail.X, tail.Y, osuCanvasObjects[j].X, osuCanvasObjects[j].Y) < 3) osuCanvasObjects[j].Stack -= offset;
                    break;
                }
                if (Distance(previous.X, previous.Y, current.X, current.Y) < 3) { previous.Stack = current.Stack + 1; current = previous; }
            }
        }
    }
    private static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    private static (double X, double Y) OsuPathPosition(OsuCanvasObject o, double progress)
    {
        if (o.Path.Length == 0) return (o.X, o.Y);
        double distance = Math.Clamp(progress, 0, 1) * o.Distances[^1];
        int index = Array.BinarySearch(o.Distances, distance);
        if (index < 0) index = ~index;
        if (index <= 0) return (o.Path[0].X, o.Path[0].GeometryY);
        if (index >= o.Path.Length) return (o.Path[^1].X, o.Path[^1].GeometryY);
        double amount = (distance - o.Distances[index - 1]) / Math.Max(.00001, o.Distances[index] - o.Distances[index - 1]);
        return (o.Path[index - 1].X + (o.Path[index].X - o.Path[index - 1].X) * amount, o.Path[index - 1].GeometryY + (o.Path[index].GeometryY - o.Path[index - 1].GeometryY) * amount);
    }
    private void RefreshOsuCanvasVisible()
    {
        EnsureOsuCanvasObjects();
        osuCanvasVisible.Clear();
        double now = LazerPresentationTime;
        int low = 0, high = osuCanvasMaximumEnds.Length;
        while (low < high) { int mid = (low + high) / 2; if (osuCanvasMaximumEnds[mid] < now - 700) low = mid + 1; else high = mid; }
        for (int i = low; i < osuCanvasObjects.Length && osuCanvasObjects[i].Start <= now + OsuCanvasPreempt; i++)
            if (osuCanvasObjects[i].End + 700 > now) osuCanvasVisible.Add(i);
    }
    internal Guid? OsuCanvasHit(float x, float y)
    {
        RefreshOsuCanvasVisible();
        var point = OsuCanvasPoint(x, y);
        foreach (int index in osuCanvasVisible)
        {
            var o = osuCanvasObjects[index];
            double now = LazerPresentationTime;
            if (o.Spinner) { if (now < o.End + 240 && Distance(point.X, point.Y, 256, 192) < 160) return o.Id; continue; }
            double shift = -o.Stack * OsuCanvasRadius / 10;
            if (now < o.Start + 700 && Distance(point.X, point.Y, o.X + shift, o.Y + shift) <= OsuCanvasRadius) return o.Id;
            var tail = OsuPathPosition(o, o.Spans % 2 == 0 ? 0 : 1);
            if (o.Path.Length > 0 && Distance(point.X, point.Y, tail.X + shift, tail.Y + shift) <= OsuCanvasRadius) return o.Id;
            if (now >= o.End + 240) continue;
            for (int i = 1; i < o.Path.Length; i++)
            {
                var a = o.Path[i - 1]; var b = o.Path[i];
                double dx = b.X - a.X, dy = b.GeometryY - a.GeometryY;
                double p = Math.Clamp(((point.X - shift - a.X) * dx + (point.Y - shift - a.GeometryY) * dy) / Math.Max(.00001, dx * dx + dy * dy), 0, 1);
                if (Distance(point.X - shift, point.Y - shift, a.X + p * dx, a.GeometryY + p * dy) <= OsuCanvasRadius) return o.Id;
            }
        }
        return null;
    }

    private void DrawOsuCanvas(ICanvas c)
    {
        RefreshOsuCanvasVisible();
        var field = OsuCanvasBounds;
        float scale = field.Width / 512, radius = (float)OsuCanvasRadius * scale;
        c.Clip(CanvasRenderBounds);
        DrawOsuCanvasGrid(c);
        c.Stroke(field, 0xFFFFFF, 1);
        double now = LazerPresentationTime;
        for (int n = osuCanvasVisible.Count - 1; n >= 0; n--)
        {
            var o = osuCanvasObjects[osuCanvasVisible[n]];
            float opacity = (float)Math.Clamp((now - (o.Start - OsuCanvasPreempt)) / Math.Min(400, OsuCanvasPreempt), 0, 1);
            if (o.Spinner) { DrawOsuSpinner(c, o, opacity * FadeAfter(o.End, 240), now); continue; }
            double shift = -o.Stack * OsuCanvasRadius / 10;
            (float X, float Y) Point(double x, double y) => OsuCanvasScreen(x + shift, y + shift);
            var head = Point(o.X, o.Y);
            var palette = beatmapColours.Length > 0 ? beatmapColours : (skin ?? defaultSkin)?.ComboColours ?? CatchSkin.DefaultComboColours;
            uint colour = palette[o.Colour % palette.Count];
            if (o.Path.Length > 0)
            {
                float bodyOpacity = opacity * FadeAfter(o.End, 240);
                if (bodyOpacity > 0)
                {
                    var origin = Point(0, 0);
                    c.SliderBody(o.StrokePath, origin.X, origin.Y, scale, (float)OsuCanvasRadius * 2,
                        (skin ?? defaultSkin)?.SliderBorderColour ?? 0xFFFFFF, (skin ?? defaultSkin)?.SliderTrackColour ?? colour, bodyOpacity);
                }
                double spanDuration = (o.End - o.Start) / o.Spans;
                double elapsed = Math.Clamp(now - o.Start, 0, Math.Max(0, o.End - o.Start));
                int span = Math.Min(o.Spans - 1, (int)(elapsed / Math.Max(1, spanDuration)));
                double progress = elapsed / Math.Max(1, spanDuration) - span;
                if (span % 2 != 0) progress = 1 - progress;
                if (o.TickDistance > 0 && o.Distances[^1] > 0)
                    for (double d = o.TickDistance; d < o.TickEndDistance; d += o.TickDistance)
                    {
                        double fraction = d / o.Distances[^1];
                        if (now >= o.Start && (span % 2 == 0 ? fraction <= progress : fraction >= progress)) continue;
                        var p = OsuPathPosition(o, fraction); var at = Point(p.X, p.Y);
                        if (!((skin ?? defaultSkin)?.DrawOsuTexture(c, "sliderscorepoint", at.X, at.Y, 16 * scale, opacity: opacity) ?? false)) c.Circle(at.X, at.Y, 3 * scale, 0xFFFFFF, opacity: opacity);
                    }
                var tail = OsuPathPosition(o, 1); var tailScreen = Point(tail.X, tail.Y);
                var finalTail = OsuPathPosition(o, o.Spans % 2 == 0 ? 0 : 1); var finalTailScreen = Point(finalTail.X, finalTail.Y);
                DrawOsuCircle(c, finalTailScreen.X, finalTailScreen.Y, radius * 2, now >= o.End ? 0xFFFFFF : colour,
                    prefix: "sliderendcircle", opacity: opacity * FadeAfter(o.End, 700));
                int firstRepeat = Math.Max(1, (int)Math.Floor((now - 700 - o.Start) / Math.Max(1, spanDuration)) + 1);
                int lastRepeat = Math.Min(o.Spans - 1, Math.Max(1, span + 1));
                for (int repeat = firstRepeat; repeat <= lastRepeat; repeat++)
                {
                    double repeatTime = o.Start + repeat * spanDuration;
                    var repeatAt = repeat % 2 == 1 ? tailScreen : head;
                    DrawOsuCircle(c, repeatAt.X, repeatAt.Y, radius * 2, now >= repeatTime ? 0xFFFFFF : colour,
                        prefix: "sliderendcircle", opacity: opacity * FadeAfter(repeatTime, 700));
                }
                if (span < o.Spans - 1)
                {
                    var reverse = span % 2 == 0 ? tailScreen : head;
                    int from = span % 2 == 0 ? o.Path.Length - 1 : 0;
                    int to = span % 2 == 0 ? Math.Max(0, from - 1) : Math.Min(o.Path.Length - 1, 1);
                    float arrowAngle = (float)(Math.Atan2(o.Path[to].GeometryY - o.Path[from].GeometryY, o.Path[to].X - o.Path[from].X) * 180 / Math.PI);
                    if (!((skin ?? defaultSkin)?.DrawOsuTexture(c, "reversearrow", reverse.X, reverse.Y, radius * 1.5f, opacity: opacity, rotation: arrowAngle) ?? false))
                    {
                        float angle = arrowAngle * MathF.PI / 180;
                        float dx = MathF.Cos(angle) * radius * .3f, dy = MathF.Sin(angle) * radius * .3f;
                        c.Line(reverse.X - dx - dy, reverse.Y - dy + dx, reverse.X + dx, reverse.Y + dy, 0xFFFFFF, 3 * scale, opacity);
                        c.Line(reverse.X + dx, reverse.Y + dy, reverse.X - dx + dy, reverse.Y - dy - dx, 0xFFFFFF, 3 * scale, opacity);
                    }
                }
                if (now >= o.Start && now <= o.End)
                {
                    var ball = OsuPathPosition(o, progress); var at = Point(ball.X, ball.Y);
                    if (!((skin ?? defaultSkin)?.DrawOsuTexture(c, "sliderfollowcircle", at.X, at.Y, radius * 4, opacity: opacity) ?? false)) c.Circle(at.X, at.Y, radius * 1.8f, 0xFFFFFF, false, 2 * scale, opacity * .6f);
                    var beforeBall = OsuPathPosition(o, Math.Max(0, progress - .001));
                    var afterBall = OsuPathPosition(o, Math.Min(1, progress + .001));
                    float ballRotation = (float)(Math.Atan2(afterBall.Y - beforeBall.Y, afterBall.X - beforeBall.X) * 180 / Math.PI);
                    double velocity = o.Distances[^1] / Math.Max(.000001, spanDuration);
                    if (!((skin ?? defaultSkin)?.DrawSliderBall(c, at.X, at.Y, radius * 2, colour, now - o.Start, velocity, opacity, ballRotation) ?? false))
                        c.Circle(at.X, at.Y, radius * .85f, 0xFFFFFF, false, 4 * scale, opacity);
                }
            }
            float headOpacity = opacity * FadeAfter(o.Start, 700);
            if (headOpacity > 0)
                DrawOsuCircle(c, head.X, head.Y, radius * 2, now >= o.Start ? 0xFFFFFF : colour, o.Number, o.Path.Length > 0 ? "sliderstartcircle" : "hitcircle", headOpacity, o.NumberText);
            if (now < o.Start)
            {
                float approach = radius * (1 + 3 * (float)Math.Clamp((o.Start - now) / OsuCanvasPreempt, 0, 1));
                if (!((skin ?? defaultSkin)?.DrawOsuTexture(c, "approachcircle", head.X, head.Y, approach * 2 * 128 / 118, colour, opacity) ?? false)) c.Circle(head.X, head.Y, approach, colour, false, 2 * scale, opacity);
            }
            if (IsObjectSelected(o.Id)) c.Circle(head.X, head.Y, radius + 3, 0x59D3C3, false, 2);
            float FadeAfter(double eventTime, double duration) => (float)Math.Clamp(1 - (now - eventTime) / duration, 0, 1);
        }
        DrawOsuCanvasControls(c);
        c.Unclip();
        DrawOsuDistanceSpacing(c);
    }

    private void DrawOsuCanvasControls(ICanvas c)
    {
        if (tool == Tool.Fruit && plot.Contains(mouseX, mouseY) && drag == DragKind.None)
        {
            var point = OsuInputPoint(mouseX, mouseY, useDistance: true);
            var at = OsuCanvasScreen(point.X, point.Y);
            DrawOsuCircle(c, at.X, at.Y,
                (float)OsuCanvasRadius * OsuCanvasBounds.Width / 256, Accent, opacity: .45f);
        }
        foreach (var slider in Document.ImportedSliders)
        {
            if (!IsObjectSelected(slider.Id)) continue;
            for (int i = 0; i < slider.ControlPoints.Count; i++)
            {
                var p = slider.ControlPoints[i]; var at = OsuCanvasScreen(p.X, p.GeometryY);
                if (i > 0) { var a = slider.ControlPoints[i - 1]; var before = OsuCanvasScreen(a.X, a.GeometryY); c.Line(before.X, before.Y, at.X, at.Y, 0xFFFFFF, .5f, .9f); }
                bool active = osuControlSlider == slider.Id && osuSelectedControl == i;
                bool separator = i > 0 && slider.ControlPoints[i] == slider.ControlPoints[i - 1]
                    || i + 1 < slider.ControlPoints.Count && slider.ControlPoints[i] == slider.ControlPoints[i + 1];
                DrawOsuControlPoint(c, at.X, at.Y, separator, active);
            }
        }
        if (osuDraftSlider is { } draft)
        {
            if (!ReferenceEquals(osuDraftPathSource, draft) || osuDraftPathCount != draft.ControlPoints.Count
                || osuDraftPathPointer != osuDraftPointer || osuDraftPathType != draft.PathType)
            {
                osuDraftPathSource = draft; osuDraftPathCount = draft.ControlPoints.Count;
                osuDraftPathPointer = osuDraftPointer; osuDraftPathType = draft.PathType;
                var candidate = new ImportedSlider { X = draft.X, Y = draft.Y, PathType = draft.PathType };
                candidate.ControlPoints.AddRange(draft.ControlPoints);
                if (osuDraftPointer is { } p && (candidate.ControlPoints.Count == 0 || candidate.ControlPoints[^1] != new SliderPathPoint(p.X, p.Y)))
                    candidate.ControlPoints.Add(new(p.X, p.Y));
                osuDraftPath = candidate.ControlPoints.Count >= 2 ? ImportedSliderConverter.Path(candidate) : [];
                osuDraftStrokePath = osuDraftPath.Select(p => new System.Numerics.Vector2((float)p.X, (float)p.GeometryY)).ToArray();
            }
            float radius = (float)OsuCanvasRadius * OsuCanvasBounds.Width / 512;
            var origin = OsuCanvasScreen(0, 0);
            c.Polyline(osuDraftStrokePath, origin.X, origin.Y, OsuCanvasBounds.Width / 512, Accent, radius * 2, .4f);
            (float X, float Y)? previous = null;
            for (int i = 0; i < draft.ControlPoints.Count; i++)
            {
                var p = draft.ControlPoints[i];
                var at = OsuCanvasScreen(p.X, p.GeometryY);
                if (previous is { } before) c.Line(before.X, before.Y, at.X, at.Y, 0xFFFFFF, .5f, .9f);
                bool separator = i > 0 && p == draft.ControlPoints[i - 1]
                    || i + 1 < draft.ControlPoints.Count && p == draft.ControlPoints[i + 1];
                DrawOsuControlPoint(c, at.X, at.Y, separator, false);
                previous = at;
            }
            if (previous is { } last && osuDraftPointer is { } pointer)
            {
                var at = OsuCanvasScreen(pointer.X, pointer.Y);
                c.Line(last.X, last.Y, at.X, at.Y, 0xFFFFFF, .5f, .9f);
                if (draft.ControlPoints.Count == 0 || draft.ControlPoints[^1] != new SliderPathPoint(pointer.X, pointer.Y))
                    DrawOsuControlPoint(c, at.X, at.Y, false, false);
            }
        }
    }
    private void DrawOsuCanvasGrid(ICanvas c)
    {
        if (gridSnap == shiftHeld) return;
        var field = OsuCanvasBounds; var bounds = CanvasRenderBounds;
        float spacing = gridSize * field.Width / 512;
        if (spacing < 1) return;
        int firstX = (int)Math.Ceiling((bounds.X - field.X) / spacing), lastX = (int)Math.Floor((bounds.Right - field.X) / spacing);
        int firstY = (int)Math.Ceiling((bounds.Y - field.Y) / spacing), lastY = (int)Math.Floor((bounds.Bottom - field.Y) / spacing);
        for (int i = firstX; i <= lastX; i++)
        {
            float x = field.X + i * spacing;
            c.Line(x, bounds.Y, x, bounds.Bottom, 0xFFFFFF, .5f, .15f);
        }
        for (int i = firstY; i <= lastY; i++)
        {
            float y = field.Y + i * spacing;
            c.Line(bounds.X, y, bounds.Right, y, 0xFFFFFF, .5f, .15f);
        }
    }
    private static void DrawOsuControlPoint(ICanvas c, float x, float y, bool separator, bool active)
    {
        var bounds = new Rect(x - 2, y - 2, 4, 4);
        c.Fill(bounds, separator ? 0xE00000u : active ? 0xFFFFFFu : 0xB8B8B8u);
        c.Stroke(bounds, 0x303030, .5f);
    }
    private void DrawOsuCircle(ICanvas c, float x, float y, float diameter, uint colour,
        int? number = null, string prefix = "hitcircle", float opacity = 1, string? label = null)
    {
        if (opacity <= 0) return;
        if ((skin ?? defaultSkin) is { } provider)
        {
            CatchSkin.DrawTimelineCircle(c, provider, x, y, diameter, colour, number, prefix, opacity);
            return;
        }
        c.Circle(x, y, diameter / 2, colour, opacity: opacity);
        c.Circle(x, y, diameter / 2, 0xFFFFFF, false, 1.5f, opacity);
        if (number is not null && label is not null)
        {
            float size = 13 * diameter / 38;
            c.TextOpacity(label, x - c.MeasureText(label, size) / 2, y - size * .6f, size, 0xFFFFFF, diameter, false, opacity);
        }
    }
    private void DrawOsuSpinner(ICanvas c, OsuCanvasObject o, float opacity, double now)
    {
        if (opacity <= 0) return;
        var field = OsuCanvasBounds; var at = OsuCanvasScreen(256, 192); float scale = field.Width / 512;
        if ((skin ?? defaultSkin)?.DrawSpinner(c, at.X, at.Y, scale, now - o.Start, o.End - o.Start, opacity) == true) return;
        float progress = (float)Math.Clamp((now - o.Start) / Math.Max(1, o.End - o.Start), 0, 1);
        uint channel = (uint)Math.Clamp(Math.Round(255 * (1 - progress)), 0, 255);
        c.Circle(at.X, at.Y, 12 * scale, 0xFF0000 | channel << 8 | channel, opacity: opacity);
        c.Circle(at.X, at.Y, 195 * (1.1625f - 1.1f * progress) * scale, 0xFFFFFF, false, 2 * scale, opacity);
    }
}
