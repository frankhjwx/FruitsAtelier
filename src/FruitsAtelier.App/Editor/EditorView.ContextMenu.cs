using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly List<ContextItem> contextItems = [];
    private Rect contextBounds;
    private sealed record ContextItem(string Label, Action Action, bool Enabled = true, string Shortcut = "");
    private sealed record SliderLocation(Guid Id, double FirstSpanTimeMs);

    private void OpenContextMenu(float x, float y)
    {
        contextItems.Clear();
        menu = -1;
        if (!plot.Contains(x, y) && !listBounds.Contains(x, y)) return;
        var extendTrack = draftTrack == Guid.Empty && objectSelection.Count <= 1 ? SelectedTrack : null;
        var extendPoint = MapAt(x, y, true);
        bool canExtend = extendTrack is not null && plot.Contains(x, y)
            && extendPoint.TimeMs >= CurveMath.EndTimeMs(extendTrack) + CurveMath.MinimumAnchorSpacingMs;
        bool anchors = tool == Tool.Slider && SelectedTrack is not null && !listBounds.Contains(x, y);
        var previousAnchors = anchorSelection.ToArray();
        Guid previousTrack = selectedTrack;
        if (draftTrack != Guid.Empty || !anchors) FinishForSelection();
        if (anchors && Document.Tracks.FirstOrDefault(t => t.Id == previousTrack) is { } editing)
        { tool = Tool.Slider; SelectAnchors(editing, previousAnchors); }
        SliderLocation? location = null;
        bool found = false;
        bool anchorHit = false;
        if (listBounds.Contains(x, y))
        {
            foreach (var row in rows)
                if (row.Bounds.Contains(x, y)) { PickObject(row.Track != Guid.Empty ? row.Track : row.Id, false); found = true; break; }
        }
        else
        {
            if (showTargets)
            {
                var point = Document.Tracks.OrderByDescending(t => t.Id == selectedTrack)
                    .SelectMany(t => t.Nodes.Select(n => (Track: t, Node: n)))
                    .FirstOrDefault(p => Near(Point(p.Node), x, y, 7));
                if (anchors && point.Node is not null && point.Track.Id == selectedTrack)
                { PickAnchor(point.Track, point.Node, false); found = true; anchorHit = true; }
                location = HitSliderLocation(x, y);
            }
            if (!anchors && !found && HitCatchObject(x, y) is { } hit)
            { PickObject(hit.SourceId, false); found = true; }
            if (!anchors && !found && location is not null)
            { PickObject(location.Id, false); found = true; }
            if (!anchors && !found && HitBananaRectangle(x, y) is { } shower)
            { PickObject(shower.Id, false); found = true; }
        }
        if (!anchors && !found && objectSelection.Count <= 1 && !canExtend) Select(Guid.Empty);
        if (canExtend && !found && location is null)
            contextItems.Add(new(L.Get("editor.command.extendSlider"), () => ExtendSlider(extendTrack!.Id, extendPoint)));
        if (SelectedTrack is { } repeatTrack && draftTrack == Guid.Empty)
        {
            contextItems.Add(new(L.Get("editor.command.addReverse"), () => ChangeReverseCount(repeatTrack.Id, 1), repeatTrack.SpanCount < 9000));
            contextItems.Add(new(L.Get("editor.command.removeReverse"), () => ChangeReverseCount(repeatTrack.Id, -1), repeatTrack.SpanCount > 1));
        }
        if (anchorHit && anchorSelection.Count > 0 && SelectedAnchor is { } node && SelectedTrack is { } track)
        {
            bool curved = CurvePointEditing.IsCurved(track, node.Id);
            if (anchorSelection.Count == 1)
                contextItems.Add(new(curved ? L.Get("editor.command.pointToCorner") : L.Get("editor.command.pointToCurve"), () => SetSelectedPointCurved(!curved)));
            contextItems.Add(new(L.Get("editor.command.deletePoint"), DeleteSelectedAnchors, Shortcut: L.Get("editor.shortcut.delete")));
        }
        else if (objectSelection.Count <= 1 && location is not null && (SelectedTrack?.Id ?? SelectedImportedSlider?.Id) == location.Id)
        {
            var target = location;
            contextItems.Add(new(L.Get("editor.command.insertPoint"), () => InsertControlPoint(target)));
        }
        if (objectSelection.Count <= 1 && SelectedImportedSlider is not null)
            contextItems.Add(new(L.Get("editor.command.editSlider"), EditImportedSlider));
        if (!anchors && objectSelection.Count == 1 && SelectedTrack is not null)
            contextItems.Add(new(L.Get("editor.command.editAnchors"), () => ChangeTool(Tool.Slider)));
        if (CanCopySelection && anchorSelection.Count <= 1)
        {
            contextItems.Add(new(L.Get("editor.command.delete"), DeleteSelectedObject, Shortcut: SelectedAnchor is null ? L.Get("editor.shortcut.delete") : ""));
            contextItems.Add(new(L.Get("editor.command.cut"), () => CutSelection(), Shortcut: L.Get("editor.shortcut.cut")));
            contextItems.Add(new(L.Get("editor.command.copy"), () => CopySelection(), Shortcut: L.Get("editor.shortcut.copy")));
        }
        contextItems.Add(new(L.Get("editor.command.paste"), () => PasteSelection(), CanPasteSelection, L.Get("editor.shortcut.paste")));
        float popupWidth = Math.Min(270, width - 8), popupHeight = contextItems.Count * 32 + 12;
        contextBounds = new(Math.Clamp(x, 4, Math.Max(4, width - popupWidth - 4)),
            Math.Clamp(y, 4, Math.Max(4, height - popupHeight - 4)), popupWidth, popupHeight);
    }

    private void DrawContextMenu(ICanvas c)
    {
        if (contextItems.Count == 0) return;
        c.Fill(new(contextBounds.X + 3, contextBounds.Y + 4, contextBounds.Width, contextBounds.Height), 0x11151B, 5);
        c.Fill(contextBounds, Surface, 5);
        c.Stroke(contextBounds, Grid, 1, 5);
        for (int i = 0; i < contextItems.Count; i++)
        {
            var item = contextItems[i];
            var rect = ContextItemBounds(i);
            if (item.Enabled && rect.Contains(mouseX, mouseY)) c.Fill(rect, 0x343E4D, 4);
            c.Text(item.Label, rect.X + 9, rect.Y + 7, 12, item.Enabled ? Foreground : 0x5B6777, rect.Width - (item.Shortcut.Length > 0 ? 90 : 18));
            string shortcut = item.Shortcut;
            if (shortcut.Length > 0) c.Text(shortcut, rect.Right - 72, rect.Y + 7, 11, item.Enabled ? Muted : 0x5B6777, 66);
        }
    }

    private Rect ContextItemBounds(int index) => new(contextBounds.X + 6, contextBounds.Y + 6 + index * 32, contextBounds.Width - 12, 30);

    private void ActivateContextMenu(float x, float y)
    {
        Action? action = null;
        for (int i = 0; i < contextItems.Count; i++)
            if (ContextItemBounds(i).Contains(x, y) && contextItems[i].Enabled) { action = contextItems[i].Action; break; }
        contextItems.Clear();
        action?.Invoke();
    }

    private void DeleteSelectedObject()
    {
        DeleteSelectedObjects();
    }

    private void SetSelectedPointCurved(bool curved)
    {
        if (SelectedTrack is not { } track || SelectedAnchor is not { } node || draftTrack != Guid.Empty) return;
        if (Edit(L.Get("editor.command.convertPoint"), () => CurvePointEditing.SetCurved(track, node.Id, curved)))
            StatusMessage = curved ? L.Get("editor.status.pointToCurve") : L.Get("editor.status.pointToCorner");
    }

    private void InsertControlPoint(SliderLocation location)
    {
        Anchor? inserted = null;
        if (!Edit(L.Get("editor.command.insertPoint"), () =>
        {
            var track = Document.Tracks.FirstOrDefault(t => t.Id == location.Id)
                ?? ImportedSliderEditing.ConvertToTrack(Document, location.Id).Track;
            int segment = track.Nodes.FindIndex(n => n.TimeMs > location.FirstSpanTimeMs) - 1;
            if (segment < 0 || segment >= track.Nodes.Count - 1) throw new ArgumentException(L.Get("editor.error.insertBetweenPoints"));
            double low = 0, high = 1;
            for (int i = 0; i < 50; i++)
            {
                double middle = (low + high) / 2;
                if (CurveMath.Evaluate(track, segment, middle).TimeMs < location.FirstSpanTimeMs) low = middle;
                else high = middle;
            }
            inserted = CurvePointEditing.InsertCorner(track, segment, (low + high) / 2);
        })) return;
        Select(inserted!.Id, location.Id);
        tool = Tool.Slider;
        StatusMessage = L.Get("editor.status.pointInserted");
    }

    private SliderLocation? HitSliderLocation(float x, float y)
    {
        double best = 9;
        SliderLocation? result = null;
        foreach (var track in Document.Tracks.Where(t => t.Nodes.Count >= 2))
        {
            double duration = track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs;
            for (int span = 0; span < track.SpanCount; span++)
            {
                double start = track.Nodes[0].TimeMs + span * duration;
                if (start > viewStart + plot.Height / pixelsPerMs || start + duration < viewStart) continue;
                double DisplayTime(double time) => start + (span % 2 == 0 ? time - track.Nodes[0].TimeMs : track.Nodes[^1].TimeMs - time);
                for (int segment = 0; segment < track.Nodes.Count - 1; segment++)
                {
                    var previous = CurveMath.Evaluate(track, segment, 0);
                    for (int n = 1; n <= 64; n++)
                    {
                        var next = CurveMath.Evaluate(track, segment, n / 64.0);
                        Consider(track.Id, new(DisplayTime(previous.TimeMs), previous.X), new(DisplayTime(next.TimeMs), next.X), previous.TimeMs, next.TimeMs);
                        previous = next;
                    }
                }
            }
        }
        EnsureConversion();
        foreach (var slider in conversion!.Sliders.Where(s => s.IsImported && s.Path.Count >= 2))
        {
            double duration = slider.DurationMs / slider.SpanCount;
            double[] distances = new double[slider.Path.Count];
            for (int i = 1; i < distances.Length; i++)
            {
                var a = slider.Path[i - 1]; var b = slider.Path[i];
                distances[i] = distances[i - 1] + Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.GeometryY - a.GeometryY, 2));
            }
            if (distances[^1] <= 0) continue;
            for (int span = 0; span < slider.SpanCount; span++)
            {
                double start = slider.StartTimeMs + span * duration;
                if (start > viewStart + plot.Height / pixelsPerMs || start + duration < viewStart) continue;
                for (int i = 1; i < distances.Length; i++)
                {
                    double a = distances[i - 1] / distances[^1] * duration, b = distances[i] / distances[^1] * duration;
                    Consider(slider.SourceId, new(start + (span % 2 == 0 ? a : duration - a), slider.Path[i - 1].X),
                        new(start + (span % 2 == 0 ? b : duration - b), slider.Path[i].X), slider.StartTimeMs + a, slider.StartTimeMs + b);
                }
            }
        }
        return result;

        void Consider(Guid id, MapPoint a, MapPoint b, double sourceA, double sourceB)
        {
            var p = Screen(a); var q = Screen(b);
            double dx = q.X - p.X, dy = q.Y - p.Y, length = dx * dx + dy * dy;
            double u = length == 0 ? 0 : Math.Clamp(((x - p.X) * dx + (y - p.Y) * dy) / length, 0, 1);
            double distance = Math.Sqrt(Math.Pow(x - p.X - u * dx, 2) + Math.Pow(y - p.Y - u * dy, 2));
            if (distance >= best) return;
            best = distance;
            result = new(id, sourceA + (sourceB - sourceA) * u);
        }
    }
}
