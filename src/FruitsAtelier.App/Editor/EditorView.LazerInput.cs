using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int[] lazerDivisorPresets = [1, 2, 4, 8, 16];
    private float lazerWheelRemainder;
    private Guid lazerTimelinePivot;
    private bool lazerMiddleMarquee;

    private bool HandleLazerKey(int key, bool ctrl, bool shift)
    {
        if (altHeld && !ctrl && !shift && key is 37 or 39)
        {
            if (drag == DragKind.None) SeekBookmark(key == 39);
            return true;
        }
        if (!TimingPageVisible && !ctrl && !altHeld && !shift && key is 114 or 115)
        {
            if (drag == DragKind.None) AdjustLazerScrollSpeed(key == 115);
            return true;
        }
        if (!ctrl && (shift && !altHeld || altHeld && !shift) && key is 81 or 87 or 69 or 82)
        {
            if (drag == DragKind.None && !TimingPageVisible)
                SetSampleBank(altHeld, key switch { 81 => 0, 87 => 1, 69 => 2, _ => 3 });
            return true;
        }
        if (altHeld) return false;
        if (ctrl && key is 37 or 39)
        {
            if (drag != DragKind.None) return true;
            if (shift) { SeekLazerSamplePoint(key == 37 ? -1 : 1); return true; }
            if (!TimingPageVisible && ClipboardSelectedParentIds().Count > 0)
                NudgeSelection(0, key == 37 ? -1 : 1);
            else
            {
                var times = ClipboardParents(Document).Select(p => p.TimeMs);
                double target = key == 37 ? times.Where(t => t < playhead).DefaultIfEmpty(double.NaN).Max()
                    : times.Where(t => t > playhead).DefaultIfEmpty(double.NaN).Min();
                if (!double.IsNaN(target)) SeekLazerSmoothlyTo(target);
            }
            return true;
        }
        // Catch has no vertical-flip operation; this combination must not extend a slider.
        if (ctrl && !shift && key == 74) return true;
        if (ctrl) return false;
        if (shift && key is >= 49 and <= 57)
        {
            ForgetTemporarySnap();
            SetLazerDivisor(key - 48);
            return true;
        }
        if (key is 37 or 39)
        {
            if (drag == DragKind.None || drag == DragKind.Marquee || NavigationDuringDrag)
                SeekLazerStep(key == 37 ? -1 : 1, shift);
            return true;
        }
        if (!shift && key is 38 or 40)
        {
            if (drag == DragKind.None)
            {
                double lenience = AudioPlaying && key == 38 ? 1000 * PlaybackSpeed : 0;
                var times = Document.TimingPoints.Select(p => p.TimeMs).Distinct();
                double target = key == 38 ? times.Where(t => t < playhead - lenience).DefaultIfEmpty(double.NaN).Max()
                    : times.Where(t => t > playhead).DefaultIfEmpty(double.NaN).Min();
                if (!double.IsNaN(target)) SeekTo(target);
            }
            return true;
        }
        if (!shift && key is 90 or 86 or 35)
        {
            if (drag != DragKind.None && drag != DragKind.Marquee && !NavigationDuringDrag) return true;
            double boundary = key == 90 ? ClipboardParents(Document).Select(p => p.TimeMs).DefaultIfEmpty(0).Min() : LazerLastObjectEndMs();
            SeekObjectBoundary(playhead == boundary ? key == 90 ? 0 : TimelineDurationMs : boundary);
            return true;
        }
        if (!shift && key == 84 && !TimingPageVisible)
        {
            if (drag == DragKind.None && draftTrack == Guid.Empty && draftBanana == Guid.Empty) distanceSnap = !distanceSnap;
            return true;
        }
        return false;
    }

    private double LazerLastObjectEndMs()
    {
        EnsureConversion();
        if (playableExport is { PlayableEndTimes.Count: > 0 }) return playableExport.PlayableEndTimes.Values.Max();
        return ClipboardParents(Document).Select(p => p.TimeMs).DefaultIfEmpty(TimelineDurationMs).Max();
    }

    private void SeekLazerSamplePoint(int direction)
    {
        RefreshTimelineSources();
        double current = playhead;
        var containing = timelineSources.LastOrDefault(p => !p.IsBanana && p.Spans > 0 && !timelinePatterns.Contains(p.Id)
            && (direction < 0 ? p.Start < current && p.End >= current : p.Start <= current && p.End > current));
        double target = double.NaN;
        if (containing.Id != Guid.Empty)
        {
            double spanDuration = (containing.End - containing.Start) / containing.Spans;
            var points = Enumerable.Range(0, containing.Spans + 1).Select(i => containing.Start + spanDuration * i)
                .Append(containing.Start + spanDuration / 2).Order();
            target = direction < 0 ? points.Where(t => t < current).DefaultIfEmpty(double.NaN).Max()
                : points.Where(t => t > current).DefaultIfEmpty(double.NaN).Min();
        }
        else
        {
            var parent = direction < 0 ? timelineSources.LastOrDefault(p => p.Start < current)
                : timelineSources.FirstOrDefault(p => p.Start > current);
            if (parent.Id != Guid.Empty) target = direction < 0 && !parent.IsBanana && parent.Spans > 0 ? parent.End : parent.Start;
        }
        if (!double.IsNaN(target)) SeekLazerSmoothlyTo(target);
    }

    private void SetLazerDivisor(int value)
    {
        if (!lazerDivisorPresets.Contains(value))
            lazerDivisorPresets = new[] { 1, 2, 4, 8, 16 }.Contains(value) ? [1, 2, 4, 8, 16]
                : new[] { 1, 3, 6, 12 }.Contains(value) ? [1, 3, 6, 12]
                : Enumerable.Range(1, value).Where(n => value % n == 0).ToArray();
        divisor = value;
    }

    private void AdjustLazerSnapWheel(float delta)
    {
        snapWheelRemainder += delta;
        int steps = (int)Math.Truncate(snapWheelRemainder / 120);
        snapWheelRemainder -= steps * 120;
        if (steps == 0) return;
        ForgetTemporarySnap();
        SetLazerDivisor(divisor);
        int index = Array.IndexOf(lazerDivisorPresets, divisor);
        divisor = lazerDivisorPresets[Math.Clamp(index + steps, 0, lazerDivisorPresets.Length - 1)];
        if (TimingPageVisible) ResetHitsounds();
    }

    private void SeekLazerStep(int direction, bool shift)
    {
        double current = playhead;
        var timing = SnapTiming();
        var point = timing.At(current);
        double amount = shift ? 4 : 1;
        if (AudioPlaying) amount *= divisor * (60000 / point.BeatLengthMs / 120) * (1 + 250 / Math.Max(1, (int)point.BeatLengthMs));
        if (direction < 0 && point.OffsetMs == current) point = timing.At(current - 1);
        double step = point.BeatLengthMs / divisor * amount;
        double target = current + step * direction;
        if (!AudioPlaying && Document.TimingPoints.Any(p => p.Uninherited))
        {
            double index = direction > 0 ? Math.Floor((target - point.OffsetMs) / step) : Math.Ceiling((target - point.OffsetMs) / step);
            target = point.OffsetMs + index * step;
            double next = Document.TimingPoints.Where(p => p.Uninherited && p.TimeMs > point.OffsetMs).Select(p => p.TimeMs).DefaultIfEmpty(double.PositiveInfinity).Min();
            if (target > next) target = next;
            if (Math.Abs(current - target) <= .5) target = point.OffsetMs + (index + direction) * step;
            double first = Document.TimingPoints.Where(p => p.Uninherited).Select(p => p.TimeMs).Min();
            if (target < point.OffsetMs && point.OffsetMs != first) target = point.OffsetMs;
        }
        if (drag == DragKind.Marquee) ScrollBoxTo(target);
        else SeekLazerSmoothlyTo(target);
    }

    private bool HandleLazerWheel(float x, float y, float delta, bool ctrl, bool shift, bool alt, bool timeline, bool canvasArea, bool summary)
    {
        if (!timeline && !canvasArea && !summary) return false;
        if (ctrl && alt) return true;
        if (ctrl) { if (!shift) AdjustLazerSnapWheel(delta); return true; }
        if (alt) return canvasArea;
        lazerWheelRemainder += delta / 120;
        int steps = (int)Math.Truncate(lazerWheelRemainder);
        lazerWheelRemainder -= steps;
        for (int i = 0; i < Math.Abs(steps); i++) SeekLazerStep(steps > 0 ? -1 : 1, shift);
        return true;
    }

    private bool HandleLazerPointerDown(float x, float y, int button, bool shift, bool ctrl)
    {
        if (menu >= 0 || contextItems.Count > 0 || editField >= 0 || !plot.Contains(x, y) && !objectTimeline.Contains(x, y)) return false;
        if (draftTrack != Guid.Empty)
        {
            if (button != 2 || !plot.Contains(x, y)) return false;
            if (LegacyMode && !UpdateLegacyPreview(x, y)) return true;
            FinishCurve();
            return true;
        }
        if (draftBanana != Guid.Empty) return false;
        if (button == 1 || button == 2 && shift)
        {
            if (objectTimeline.Contains(x, y))
            {
                if (timelineObjects.Any(o => o.Bounds.Contains(x, y))) DeleteTimelineObject(x, y);
                else if (button == 1)
                {
                    BeginObjectTimeline(x, y, ctrl);
                    lazerMiddleMarquee = drag == DragKind.Marquee;
                }
            }
            else if ((HitCatchObject(x, y)?.SourceId ?? HitSliderLocation(x, y)?.Id ?? HitBananaRectangle(x, y)?.Id) is { } source)
            {
                if (!objectSelection.Contains(source)) SelectObjects([source]);
                DeleteSelectedObjects();
            }
            else if (button == 1)
            {
                BeginBox(x, y, ctrl, false);
                lazerMiddleMarquee = true;
            }
            return true;
        }
        if (button != 2) return false;
        Guid id = objectTimeline.Contains(x, y) ? timelineObjects.LastOrDefault(o => o.Bounds.Contains(x, y)).Id
            : HitCatchObject(x, y)?.SourceId ?? HitSliderLocation(x, y)?.Id ?? HitBananaRectangle(x, y)?.Id ?? Guid.Empty;
        MapPoint? extensionTarget = null;
        if (plot.Contains(x, y) && SelectedTrack is { } selected && (id == Guid.Empty || id == selected.Id) && !notesLocked)
        {
            var point = MapAt(x, y, true, clampX: false);
            if (point.TimeMs >= CurveMath.EndTimeMs(selected) + CurveMath.MinimumAnchorSpacingMs)
            {
                extensionTarget = point;
                if (id == Guid.Empty) id = selected.Id;
            }
        }
        if (id == Guid.Empty) { SelectObjects([]); return true; }
        if (!objectSelection.Contains(id)) SelectObjects([id]);
        contextItems.Clear();
        contextItems.Add(new(L.Get("editor.command.cut"), () => CutSelection(), Shortcut: "Ctrl+X"));
        contextItems.Add(new(L.Get("editor.command.copy"), () => CopySelection(), Shortcut: "Ctrl+C"));
        contextItems.Add(new(L.Get("editor.command.delete"), DeleteSelectedObjects, Shortcut: "Delete"));
        if (extensionTarget is { } target && SelectedTrack is { } extend)
            contextItems.Add(new(L.Get("editor.command.extendSlider"), () => ExtendSlider(extend.Id, target)));
        if (Document.Tracks.Any(t => t.Id == id) || Document.ImportedSliders.Any(s => s.Id == id))
        {
            RefreshSliderInspector();
            foreach (var action in SliderInspectorActions()) contextItems.Add(new(action.Label, action.Action, action.Enabled, SliderAction: action.Kind));
        }
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 280)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 280, ContextMenuHeight);
        return true;
    }

    private bool HandleLazerTimelineRange(float x, float y, bool shift, bool ctrl)
    {
        var clicked = timelineObjects.LastOrDefault(o => o.Bounds.Contains(x, y));
        if (clicked.Id == Guid.Empty) return false;
        var objects = ClipboardParents(Document).ToArray();
        var pivot = objects.FirstOrDefault(o => o.Id == lazerTimelinePivot);
        if (!shift || pivot.Id == Guid.Empty) { lazerTimelinePivot = clicked.Id; return false; }
        var target = objects.First(o => o.Id == clicked.Id);
        double start = Math.Min(pivot.TimeMs, target.TimeMs), end = Math.Max(LazerParentEnd(pivot.Id, pivot.TimeMs), LazerParentEnd(target.Id, target.TimeMs));
        var ids = objects.Where(o => o.TimeMs >= start && LazerParentEnd(o.Id, o.TimeMs) <= end).Select(o => o.Id);
        SelectObjects(ctrl ? ids.Concat(objectSelection).Distinct().ToArray() : ids.ToArray());
        if (ctrl) lazerTimelinePivot = clicked.Id;
        return true;
    }

    private double LazerParentEnd(Guid id, double fallback) => playableExport?.PlayableEndTimes.GetValueOrDefault(id, fallback) ?? fallback;
}
