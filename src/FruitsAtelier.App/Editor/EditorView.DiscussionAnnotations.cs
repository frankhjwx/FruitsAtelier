using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const uint DiscussionPendingColour = 0xE6AA64, DiscussionResolvedColour = 0xA6D9AE;
    private sealed record DiscussionRegion(double Start, double End, DiscussionAnchor Anchor, uint Colour);
    private sealed record DiscussionSource(double Start, double End, int Number);
    private DiscussionRegion[] discussionRegions = [];
    private double[] discussionRegionEnds = [];
    private object? discussionRegionConversion, discussionRegionAnchors;
    private readonly Dictionary<(long Id, float Width, string Language, Type Canvas), DiscussionLayout> discussionQuoteLayouts = [];
    private readonly Dictionary<long, float> discussionQuoteScrolls = [];
    private readonly List<(Rect Bounds, Action Action)> discussionQuoteActions = [];
    private sealed record DiscussionQuoteView(long Id, Rect Bounds, Rect Viewport, Rect Track, Rect Thumb, float Maximum);
    private readonly List<DiscussionQuoteView> discussionQuoteViews = [];
    private DiscussionQuoteView? discussionQuoteDrag;
    private float discussionQuoteDragOffset;
    internal int DiscussionQuoteLayoutBuilds { get; private set; }
    internal IReadOnlyList<(long Id, float Scroll, float Maximum)> DiscussionQuoteScrollState
        => discussionQuoteViews.Select(v => (v.Id, discussionQuoteScrolls.GetValueOrDefault(v.Id), v.Maximum)).ToArray();
    private readonly List<(DiscussionRegion Region, float Y)> discussionVisibleRegions = [];
    private readonly List<Rect> discussionQuoteBounds = [];
    internal IReadOnlyList<Rect> DiscussionQuoteBounds => discussionQuoteBounds;
    internal IReadOnlyList<(double Start, double End, long Discussion)> DiscussionHighlightRanges
        => discussionRegions.Select(r => (r.Start, r.End, r.Anchor.Discussion)).ToArray();

    private float DiscussionCanvasReserve => DiscussionQuotesVisible && !IsOsuCanvas
        ? Math.Min(190, Math.Max(0, canvas.Width - 198 - MinimumPlayfieldWidth)) : 0;

    private void ClearDiscussionRegions()
    {
        discussionRegionConversion = discussionRegionAnchors = null;
        discussionRegions = []; discussionRegionEnds = [];
        discussionQuoteLayouts.Clear(); discussionVisibleRegions.Clear(); discussionQuoteBounds.Clear();
        discussionQuoteActions.Clear(); discussionQuoteViews.Clear(); discussionQuoteDrag = null;
    }

    private void PrepareDiscussionRegions()
    {
        if (ReferenceEquals(discussionRegionConversion, convertedSnapshot) && ReferenceEquals(discussionRegionAnchors, discussionAnchors)) return;
        if (conversion is null || IsContentDrag || history.HasActiveTransaction) return;
        discussionRegionConversion = convertedSnapshot; discussionRegionAnchors = discussionAnchors;
        discussionQuoteLayouts.Clear(); discussionRegions = []; discussionRegionEnds = [];
        if (discussionAnchors.Length == 0 || discussionSnapshot is null) return;
        // The timeline already owns converted slider end times and combo event numbers.
        // Only a new committed conversion or discussion snapshot rebuilds associations.
        RefreshTimelineSources();
        var sources = timelineMarkers.Select(s => new DiscussionSource(s.Start, s.End,
            timelineEventNumbers.GetValueOrDefault((s.Id, s.EventIndex))))
            .OrderBy(s => s.Start).ToArray();
        var byId = discussionSnapshot.Discussions.ToDictionary(d => d.Id);
        var regions = new List<DiscussionRegion>();
        foreach (var anchor in discussionAnchors)
        {
            double start = anchor.Time, end = anchor.Time;
            int first = LowerSource(anchor.Time - 1), found = -1;
            for (int i = first; i < sources.Length && sources[i].Start <= anchor.Time + 1; i++)
                if (anchor.Numbers.Count == 0 || sources[i].Number == anchor.Numbers[0])
                { if (found >= 0) { found = -1; break; } found = i; }
            if (found >= 0)
            {
                double candidateEnd = sources[found].End; int next = anchor.Numbers.Count > 0 ? 1 : 0;
                for (int i = found + 1; i < sources.Length && next < anchor.Numbers.Count; i++)
                    if (sources[i].Number == anchor.Numbers[next]) { candidateEnd = Math.Max(candidateEnd, sources[i].End); next++; }
                if (next == anchor.Numbers.Count) { start = sources[found].Start; end = candidateEnd; }
            }
            bool positive = byId.TryGetValue(anchor.Discussion, out var thread) && (thread.Resolved || thread.MessageType == "praise");
            regions.Add(new(start, end, anchor, positive ? DiscussionResolvedColour : DiscussionPendingColour));
        }
        discussionRegions = regions.OrderBy(r => r.Start).ThenBy(r => r.Anchor.Discussion).ToArray();
        discussionRegionEnds = new double[discussionRegions.Length]; double maximum = double.NegativeInfinity;
        for (int i = 0; i < discussionRegions.Length; i++) discussionRegionEnds[i] = maximum = Math.Max(maximum, discussionRegions[i].End);

        int LowerSource(double time)
        {
            int lo = 0, hi = sources.Length;
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (sources[mid].Start < time) lo = mid + 1; else hi = mid; }
            return lo;
        }
    }

    private void VisibleDiscussionRegions()
    {
        discussionVisibleRegions.Clear();
        float radius = (float)(CatchSize.FruitRadius(Document.CircleSize) * CanvasObjectWidth / 512);
        double low = PresentationViewStart - radius / pixelsPerMs, high = PresentationViewStart + (plot.Height + radius) / pixelsPerMs;
        int lo = 0, hi = discussionRegionEnds.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (discussionRegionEnds[mid] < low) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < discussionRegions.Length && discussionRegions[i].Start <= high; i++)
        {
            var region = discussionRegions[i]; if (region.End < low) continue;
            discussionVisibleRegions.Add((region, Screen(new(region.Anchor.Time, 512)).Y));
        }
        discussionVisibleRegions.Sort((a, b) => a.Y.CompareTo(b.Y));
    }

    private void DrawDiscussionHighlights(ICanvas c)
    {
        if (!DiscussionAnnotationsVisible || IsOsuCanvas || PatternEditingActive) return;
        VisibleDiscussionRegions();
        float radius = (float)(CatchSize.FruitRadius(Document.CircleSize) * CanvasObjectWidth / 512);
        var field = Playfield;
        c.Clip(CanvasRenderBounds);
        foreach (var item in discussionVisibleRegions)
        {
            var region = item.Region;
            float top = Screen(new(region.End, 0)).Y - radius, bottom = Screen(new(region.Start, 0)).Y + radius;
            var rect = DiscussionIntersection(new(field.X, top, field.Width, Math.Max(radius * 2, bottom - top)),
                new(field.X, plot.Y, field.Width, plot.Height));
            uint colour = region.Colour;
            c.Fill(rect, colour, opacity: discussionFocused == region.Anchor.Discussion ? .2f : .11f);
            c.Line(rect.X, rect.Y, rect.X, rect.Bottom, colour, 2, .7f);
        }
        c.Unclip();
    }

    private double DiscussionViewPadding(double normal)
        => DiscussionAnnotationsVisible && !IsOsuCanvas && !IsLazerEditing
            ? Math.Max(normal, plot.Height * .9 / pixelsPerMs) : normal;

    private bool ScrollDiscussionPreroll(double steps, int surface)
    {
        if (surface != 0 || !DiscussionAnnotationsVisible || AudioPlaying || IsOsuCanvas || IsLazerEditing
            || drag != DragKind.None || playhead != 0) return false;
        double padding = plot.Height * EffectivePlaybackLineFromBottom / pixelsPerMs;
        if (steps > 0 && viewStart >= -padding) return false;
        double next = viewStart + steps * TimingMap.At(Document, 0).BeatLengthMs / divisor;
        viewStart = Math.Clamp(next, -DiscussionViewPadding(padding), steps > 0 ? -padding : double.MaxValue);
        pinPlayhead = false; ClampView(); return true;
    }

    private void DrawDiscussionMarkers(ICanvas c)
    {
        discussionMarkerHits.Clear(); discussionQuoteBounds.Clear(); discussionQuoteActions.Clear(); discussionQuoteViews.Clear();
        if (!DiscussionQuotesVisible || PatternEditingActive || IsOsuCanvas || discussionRegions.Length == 0) return;
        VisibleDiscussionRegions();
        var field = Playfield;
        float right = Math.Min(assistPalette.Width > 0 ? assistPalette.X - 8 : canvas.Right - 126, CanvasRenderBounds.Right - 6);
        float left = field.Right + 8, quoteWidth = Math.Max(0, right - left);
        bool quotes = DiscussionAnnotationsVisible && quoteWidth >= 80;
        var groups = discussionVisibleRegions.GroupBy(item => item.Region.Anchor.Discussion)
            .Select(group =>
            {
                double? time = discussionSnapshot!.Discussions.First(d => d.Id == group.Key).Timestamp;
                return group.FirstOrDefault(item => item.Region.Anchor.Time == time, group.First());
            }).OrderBy(item => item.Y).ToArray();
        float slot = Math.Max(1, (plot.Height - 4) / Math.Max(1, groups.Length) - 6);
        float lastBottom = plot.Y + 2; int groupIndex = 0;
        c.Clip(CanvasRenderBounds);
        foreach (var item in groups)
        {
            var region = item.Region; var anchor = region.Anchor;
            uint colour = region.Colour;
            float anchorY = Math.Clamp(item.Y, plot.Y + 2, plot.Bottom - 2);
            var thread = discussionSnapshot!.Discussions.First(d => d.Id == anchor.Discussion);
            if (!quotes)
            {
                float x = Math.Min(left, right - 38), y = Math.Clamp(anchorY - 10, plot.Y, Math.Max(plot.Y, plot.Bottom - 22));
                if (x < field.Right + 2 || y < lastBottom) continue;
                var marker = new Rect(x, y, Math.Min(54, right - x), 21);
                c.Fill(marker, Surface, 4); c.Stroke(marker, colour, radius: 4);
                DrawDiscussionTypeIcons(c, thread, marker.X + 4, marker.Y + 4);
                discussionMarkerHits.Add((marker, anchor.Discussion)); lastBottom = marker.Bottom + 4; continue;
            }
            var key = (thread.Id, MathF.Floor(quoteWidth), L.Language, c.GetType());
            if (!discussionQuoteLayouts.TryGetValue(key, out var layout))
            {
                layout = new(); BuildDiscussionLayout(c, Math.Max(20, quoteWidth - 26), [thread], layout);
                discussionQuoteLayouts[key] = layout; DiscussionQuoteLayoutBuilds++;
            }
            float h = Math.Min(layout.ContentHeight + 8, Math.Min(420, slot));
            float slotTop = plot.Y + 2 + groupIndex++ * (slot + 6);
            float top = Math.Clamp(anchorY - 18, slotTop, slotTop + slot - h);
            var card = new Rect(left, top, quoteWidth, h);
            var viewport = new Rect(left + 6, top + 4, Math.Max(1, quoteWidth - 16), Math.Max(1, h - 8));
            float maximum = Math.Max(0, layout.ContentHeight - viewport.Height);
            float scroll = Math.Clamp(discussionQuoteScrolls.GetValueOrDefault(thread.Id), 0, maximum);
            discussionQuoteScrolls[thread.Id] = scroll;
            c.Line(field.Right + 1, anchorY, left - 2, top + Math.Min(16, h / 2), colour, 1, .8f);
            c.Fill(card, Panel, 5); c.Stroke(card, colour, radius: 5);
            c.Fill(new(left, top + 4, 3, Math.Max(1, h - 8)), colour, 1);
            c.Clip(viewport);
            DrawDiscussionContent(c, layout, viewport, left + 10, quoteWidth - 26, scroll, canvasQuote: true);
            c.Unclip();
            var track = new Rect(card.Right - 8, viewport.Y, 7, viewport.Height); Rect thumb = default;
            if (maximum > 0)
            {
                float thumbHeight = Math.Min(viewport.Height, Math.Max(18, viewport.Height * viewport.Height / layout.ContentHeight));
                thumb = new(track.X + 1, track.Y + scroll / maximum * (track.Height - thumbHeight), 4, thumbHeight);
                c.Fill(thumb, colour, 2, .8f);
            }
            discussionQuoteViews.Add(new(thread.Id, card, viewport, track, thumb, maximum));
            discussionQuoteBounds.Add(card); discussionMarkerHits.Add((card, thread.Id)); lastBottom = card.Bottom + 6;
        }
        c.Unclip();
    }

    private bool DiscussionQuotePointerDown(float x, float y)
    {
        foreach (var view in discussionQuoteViews)
            if (view.Maximum > 0 && view.Track.Contains(x, y))
            {
                discussionQuoteDrag = view;
                discussionQuoteDragOffset = view.Thumb.Contains(x, y) ? y - view.Thumb.Y : view.Thumb.Height / 2;
                MoveDiscussionQuoteScrollbar(y); return true;
            }
        for (int i = discussionQuoteActions.Count - 1; i >= 0; i--)
            if (discussionQuoteActions[i].Bounds.Contains(x, y)) { discussionQuoteActions[i].Action(); return true; }
        return false;
    }

    private void MoveDiscussionQuoteScrollbar(float y)
    {
        if (discussionQuoteDrag is not { } view) return;
        float travel = view.Track.Height - view.Thumb.Height;
        if (travel > 0) discussionQuoteScrolls[view.Id] = Math.Clamp((y - view.Track.Y - discussionQuoteDragOffset) / travel, 0, 1) * view.Maximum;
    }
}
