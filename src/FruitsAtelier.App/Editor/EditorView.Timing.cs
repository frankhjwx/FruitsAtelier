using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool TimingPageVisible { get; private set; }
    public bool TimingSetupVisible { get; private set; }
    public Action? RequestPasteTiming { get; set; }
    public Action? RequestTimingSampleHelp { get; set; }
    public int TimingInputSession { get; private set; }
    private sealed record TimingEntry(int Id, TimingPoint? Original, TimingPoint Point);
    private readonly List<TimingEntry> timingEntries = [];
    private readonly HashSet<int> timingSelected = [];
    private readonly Stack<TimingEntry[]> timingUndo = [], timingRedo = [];
    private int timingTab, timingFilter, timingScroll, timingAnchor, timingNextId, timingSnap = 4;
    private bool timingScale, timingResnap, timingLengths, timingBookmarks, timingMoveNotes, metronomeEnabled = true;
    private string timingField = "", timingText = "", timingError = "", timingCommand = "";
    private Action<double>? timingFieldApply;
    private readonly List<(string Key, Rect Bounds, string Value, Action<double> Apply)> timingFields = [];
    private Rect timingListBounds;
    private Rect timingVolumeTrack;
    private TimingEntry[]? timingVolumeStart;
    internal IReadOnlyList<(string Key, Rect Bounds, string Value, Action<double> Apply)> TimingFields => timingFields;
    private readonly List<double> timingTaps = [];
    private bool timingTapHeld;
    private TimingPoint? timingResetPoint;
    private MapDocument? timingResetDocument;
    private bool timingPointerShift;
    private bool panelMenuOpen, timingSnapDragging;
    private Rect panelMenuBounds, timingSnapBounds;
    private bool TimingModal => TimingSetupVisible || timingCommand.Length > 0;
    internal Rect TimingSetupBounds => new((width - Math.Min(1000, width - 24)) / 2,
        (height - Math.Min(680, height - 24)) / 2, Math.Min(1000, width - 24), Math.Min(680, height - 24));

    internal void OpenTimingSetup()
    {
        if (!HasEditorProject || LibraryVisible || IsTestplaying || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        TimingSetupVisible = true; timingCommand = timingField = timingError = "";
        timingEntries.Clear(); timingSelected.Clear(); timingUndo.Clear(); timingRedo.Clear();
        timingNextId = 0; timingTab = timingFilter = timingScroll = 0;
        foreach (var point in Document.TimingPoints) timingEntries.Add(new(timingNextId++, TimingEditing.Copy(point), TimingEditing.Copy(point)));
        var active = TimingEditing.Current(Document, playhead);
        int index = active is null ? -1 : Document.TimingPoints.IndexOf(active);
        if (index >= 0) { timingSelected.Add(index); timingAnchor = index; }
        timingScroll = Math.Max(0, Array.FindIndex(VisibleTimingEntries(), e => e.Id == index) - 3);
        timingSnap = divisor; timingScale = timingResnap = timingLengths = timingBookmarks = false;
        TimingInputSession++; hits.Clear(); fields.Clear();
    }

    private void CloseTimingSetup()
    {
        if (TimingModal) ResetHitsounds();
        timingVolumeStart = null;
        TimingSetupVisible = false; timingCommand = timingField = timingError = "";
        timingFieldApply = null; timingFields.Clear(); hits.Clear(); fields.Clear(); TimingInputSession++;
    }

    private void ShowTimingPage(bool visible)
    {
        if (!PrepareFileOperation()) return;
        TimingPageVisible = visible; panelMenuOpen = false; timingField = timingError = ""; timingTaps.Clear();
        ResetHitsounds(); timingTapHeld = false;
    }

    private TimingEntry[] TimingSnapshot() => timingEntries.Select(e => e with { Point = TimingEditing.Copy(e.Point) }).ToArray();
    private void ChangeTimingDraft(Action action)
    {
        var before = TimingSnapshot();
        try { action(); timingUndo.Push(before); timingRedo.Clear(); timingError = ""; }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        { timingEntries.Clear(); timingEntries.AddRange(before); timingError = ex.Message; }
    }

    private TimingEntry[] VisibleTimingEntries() => timingEntries.Where(e => timingFilter == 0 || e.Point.Uninherited == (timingFilter == 1))
        .OrderBy(e => e.Point.TimeMs).ThenBy(e => e.Point.SourceOrder).ThenBy(e => e.Id).ToArray();

    private void TimingDraftUndo(bool redo)
    {
        var from = redo ? timingRedo : timingUndo; var to = redo ? timingUndo : timingRedo;
        if (!from.TryPop(out var snapshot)) return;
        to.Push(TimingSnapshot()); timingEntries.Clear(); timingEntries.AddRange(snapshot);
        timingSelected.RemoveWhere(id => !timingEntries.Any(e => e.Id == id));
    }

    private void SelectTimingEntry(int id, bool ctrl, bool shift)
    {
        if (!CommitTimingField()) return;
        var visible = VisibleTimingEntries();
        if (shift)
        {
            int first = Array.FindIndex(visible, e => e.Id == timingAnchor), last = Array.FindIndex(visible, e => e.Id == id);
            if (!ctrl) timingSelected.Clear();
            if (first < 0) first = last;
            for (int i = Math.Min(first, last); i <= Math.Max(first, last); i++) timingSelected.Add(visible[i].Id);
        }
        else
        {
            if (!ctrl) timingSelected.Clear();
            if (!timingSelected.Add(id) && ctrl) timingSelected.Remove(id);
            timingAnchor = id;
        }
        TimingInputSession++;
    }

    private void AddTimingPoint(bool inherited)
    {
        if (!TimingSetupVisible) OpenTimingSetup();
        if (!TimingSetupVisible || !CommitTimingField()) return;
        ChangeTimingDraft(() =>
        {
            var map = Document.DeepClone(); map.TimingPoints.Clear(); map.TimingPoints.AddRange(timingEntries.Select(e => e.Point));
            var point = TimingEditing.Create(map, playhead, inherited);
            point.SourceOrder = timingEntries.Select(e => e.Point.SourceOrder).Where(v => v < int.MaxValue).DefaultIfEmpty(-1).Max() + 1;
            int id = timingNextId++;
            timingEntries.Add(new(id, null, point)); timingSelected.Clear(); timingSelected.Add(id); timingAnchor = id;
            timingFilter = 0;
            timingScroll = Math.Max(0, Array.FindIndex(VisibleTimingEntries(), e => e.Id == id) - 3);
        });
    }

    private void DeleteTimingPoints()
    {
        if (!CommitTimingField()) return;
        var first = timingEntries.Where(e => e.Point.Uninherited).OrderBy(e => e.Point.TimeMs).ThenBy(e => e.Point.SourceOrder).FirstOrDefault();
        ChangeTimingDraft(() => timingEntries.RemoveAll(e => timingSelected.Contains(e.Id) && e != first));
        timingSelected.RemoveWhere(id => !timingEntries.Any(e => e.Id == id));
    }

    private void DeleteCurrentTiming()
    {
        var point = TimingEditing.Current(Document, playhead);
        var first = Document.TimingPoints.Where(p => p.Uninherited).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).FirstOrDefault();
        if (point is null || point == first) { StatusMessage = L.Get("timing.keepFirst"); return; }
        Edit(L.Get("timing.delete"), () => Document.TimingPoints.Remove(point));
    }

    private void ApplyTimingSetup()
    {
        if (!CommitTimingField()) return;
        var pairs = timingEntries.Where(e => e.Original is not null).Select(e => (e.Original!, e.Point)).ToArray();
        if (!Edit(L.Get("timing.edit"), () => TimingEditing.Apply(Document, timingEntries.Select(e => e.Point).ToArray(), pairs,
            new(timingScale, timingResnap, timingLengths, timingBookmarks, timingSnap))))
        { timingError = StatusMessage; return; }
        CloseTimingSetup(); ResetHitsounds();
    }

    private void SetTimingValues(string key, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException(L.Get("editor.error.finiteNumberRequired"));
        if (key == "offset" && Math.Abs(value) > int.MaxValue || key == "bpm" && (value < 1 || value > 10000)
            || key == "meter" && (value < 1 || value > 64 || value != Math.Truncate(value))
            || key == "volume" && (value < 0 || value > 100)
            || key == "index" && (value < 0 || value > 10000 || value != Math.Truncate(value)))
            throw new ArgumentException(L.Get("timing.range"));
        ChangeTimingDraft(() =>
        {
            foreach (var entry in timingEntries.Where(e => timingSelected.Contains(e.Id)))
            {
                var p = entry.Point;
                switch (key)
                {
                    case "offset": p.TimeMs = p.Uninherited ? value : Math.Truncate(value); break;
                    case "bpm" when p.Uninherited: p.BeatLengthMs = 60000 / value; break;
                    case "meter" when p.Uninherited: p.Meter = (int)value; break;
                    case "volume": p.Volume = (int)Math.Round(value); break;
                    case "index": p.SampleIndex = (int)value; break;
                    case "bank": p.SampleSet = (int)value; break;
                    case "kiai" when !p.Uninherited: p.Effects = (p.Effects & ~1) | (int)value; break;
                }
            }
        });
    }

    private void ToggleTimingInheritance()
    {
        if (!CommitTimingField()) return;
        var selected = timingEntries.Where(e => timingSelected.Contains(e.Id)).ToArray();
        bool inherited = selected.Any(e => e.Point.Uninherited);
        var first = timingEntries.Where(e => e.Point.Uninherited).OrderBy(e => e.Point.TimeMs).FirstOrDefault();
        ChangeTimingDraft(() =>
        {
            var map = Document.DeepClone(); map.TimingPoints.Clear(); map.TimingPoints.AddRange(timingEntries.Select(e => TimingEditing.Copy(e.Point)));
            foreach (var entry in selected)
            {
                if (inherited && entry == first) continue;
                var state = TimingMap.At(map, entry.Point.TimeMs);
                entry.Point.Uninherited = !inherited;
                if (inherited) entry.Point.TimeMs = Math.Truncate(entry.Point.TimeMs);
                entry.Point.BeatLengthMs = inherited ? -100 / state.SliderVelocityMultiplier : state.BeatLengthMs;
            }
        });
    }

    private void TimingNumber(ICanvas c, string key, Rect box, string value, Action<double> apply, Func<double>? read = null, bool enabled = true)
    {
        var full = box;
        bool stepper = !key.StartsWith("page.", StringComparison.Ordinal) && key is not ("move" or "volume" or "index");
        if (stepper) box = new(box.X + 26, box.Y, box.Width - 52, box.Height);
        if (enabled) timingFields.Add((key, box, value, apply));
        if (!key.StartsWith("page.", StringComparison.Ordinal)) c.Fill(box, Surface, 4);
        if (!key.StartsWith("page.", StringComparison.Ordinal) || timingField == key) c.Stroke(box, timingField == key ? Accent : Grid, radius: 4);
        DrawInputText(c, new(box.X + 8, box.Y + (box.Height - 20) / 2, box.Width - 16, 20), timingField == key ? timingText : value, 13, enabled && timingField == key, "timing:" + key, centered: stepper || key.StartsWith("page.", StringComparison.Ordinal));
        if (!enabled) { c.Fill(full, Background, opacity: .55f); return; }
        hits.Add(new(box, () =>
        {
            if (!CommitTimingField()) return;
            timingField = key; timingText = value; timingFieldApply = apply;
            SelectInput("timing:" + key, timingText); TimingInputSession++;
        }, true));
        if (stepper)
        {
            void Step(int direction)
            {
                string current = timingField == key ? timingText : value;
                if (!CommitTimingField()) return;
                double number;
                if (read is not null) number = read();
                else if (!double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return;
                double step = key == "bpm" ? placementCtrl ? .25 : timingPointerShift ? 5 : 1 : key == "offset" ? placementCtrl ? 1 : timingPointerShift ? 10 : 2 : 1;
                try { apply(number + step * direction); } catch (ArgumentException ex) { timingError = ex.Message; }
            }
            TimingButton(c, new(full.X, full.Y, 24, full.Height), "‹", () => Step(-1), flatArrow: true);
            TimingButton(c, new(full.Right - 24, full.Y, 24, full.Height), "›", () => Step(1), flatArrow: true);
        }
    }

    private bool CommitTimingField()
    {
        if (timingField.Length == 0) return true;
        if (!double.TryParse(timingText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        { timingError = L.Get("editor.error.finiteNumberRequired"); return false; }
        try { timingFieldApply?.Invoke(value); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { timingError = ex.Message; return false; }
        timingField = ""; timingFieldApply = null; timingError = ""; TimingInputSession++; return true;
    }

    public void PasteTimingText(string text, int session)
    {
        if (session != TimingInputSession || !TimingModal && !TimingPageVisible) return;
        if (timingField.Length > 0)
        {
            timingText = InsertInput("timing:" + timingField, timingText,
                new string(text.Where(c => char.IsAsciiDigit(c) || c is '.' or '-' or '+' or 'e' or 'E').ToArray()), 30); return;
        }
        if (!TimingSetupVisible) return;
        try
        {
            var points = TimingEditing.Parse(text);
            foreach (var point in points.Where(p => !p.Uninherited)) point.TimeMs = Math.Truncate(point.TimeMs);
            ChangeTimingDraft(() =>
            {
                timingSelected.Clear();
                int order = timingEntries.Select(e => e.Point.SourceOrder).Where(v => v < int.MaxValue).DefaultIfEmpty(-1).Max() + 1;
                foreach (var point in points)
                {
                    point.SourceOrder = order++; int id = timingNextId++;
                    timingEntries.Add(new(id, null, point)); timingSelected.Add(id); timingAnchor = id;
                }
                timingFilter = 0;
            });
        }
        catch (ArgumentException ex) { timingError = ex.Message; }
    }

    private bool TimingKey(int key, bool ctrl, bool shift)
    {
        if (!TimingModal && !TimingPageVisible) return false;
        if (timingField.Length > 0)
        {
            if (key == 27) { timingField = timingError = ""; TimingInputSession++; return true; }
            if (key is 13 or 9)
            {
                int index = timingFields.FindIndex(f => f.Key == timingField);
                if (CommitTimingField() && key == 9 && timingFields.Count > 0)
                {
                    var next = timingFields[(index + (shift ? timingFields.Count - 1 : 1)) % timingFields.Count];
                    timingField = next.Key; timingText = next.Value; timingFieldApply = next.Apply; SelectInput("timing:" + timingField, timingText);
                }
                return true;
            }
            InputKey("timing:" + timingField, ref timingText, key, ctrl, shift, 30, RequestPasteTiming); return true;
        }
        if (TimingSetupVisible)
        {
            if (ctrl && key == 65) { timingSelected.Clear(); foreach (var entry in VisibleTimingEntries()) timingSelected.Add(entry.Id); }
            else if (ctrl && key == 67) RequestCopyText?.Invoke(TimingEditing.Serialize(VisibleTimingEntries().Where(e => timingSelected.Contains(e.Id)).Select(e => e.Point)));
            else if (ctrl && key == 88)
            { RequestCopyText?.Invoke(TimingEditing.Serialize(VisibleTimingEntries().Where(e => timingSelected.Contains(e.Id)).Select(e => e.Point))); DeleteTimingPoints(); }
            else if (ctrl && key == 86) RequestPasteTiming?.Invoke();
            else if (ctrl && key == 90) TimingDraftUndo(shift);
            else if (ctrl && key == 89) TimingDraftUndo(true);
            else if (ctrl && key == 80) AddTimingPoint(shift);
            else if (key == 46 || ctrl && key == 73) DeleteTimingPoints();
            else if (key == 27) CloseTimingSetup();
            else if (key == 13) ApplyTimingSetup();
            else if (key is 38 or 40 or 33 or 34 or 36 or 35)
            {
                var entries = VisibleTimingEntries(); if (entries.Length == 0) return true;
                int index = Array.FindIndex(entries, e => e.Id == timingAnchor);
                index = key switch { 36 => 0, 35 => entries.Length - 1, _ => Math.Clamp(index + (key is 38 or 33 ? -1 : 1) * (key is 33 or 34 ? 8 : 1), 0, entries.Length - 1) };
                SelectTimingEntry(entries[index].Id, ctrl, shift); timingScroll = Math.Max(0, index - 3);
            }
            else if (key == 9 && timingFields.Count > 0)
            {
                var next = timingFields[0]; timingField = next.Key; timingText = next.Value; timingFieldApply = next.Apply; SelectInput("timing:" + timingField, timingText);
            }
            return true;
        }
        if (timingCommand.Length > 0)
        { if (key == 27) CloseTimingSetup(); else if (key == 13) ApplyTimingCommand(); return true; }
        if (key == 84 && !ctrl && !shift) { if (!timingTapHeld) TapTiming(); timingTapHeld = true; return true; }
        if (key == 27 && panelMenuOpen) { panelMenuOpen = false; return true; }
        if (key == 27 && menu >= 0) { menu = -1; return true; }
        if (key == 27 || key == 112) { ShowTimingPage(false); return true; }
        if (ctrl && shift && key == 73) return true;
        if (ctrl) return key is not (17 or 80 or 73 or 90 or 89 or 83 or 9 or 79 or 66 or 37 or 39 or 38 or 40 or 77);
        return key is not (17 or 32 or 67 or 88 or 90 or 86 or 35 or 36 or 37 or 38 or 39 or 40 or 114 or 115 or 116 or 117)
            && !(shift && key is >= 49 and <= 57);
    }

    private void TimingPointerDown(float x, float y, int button)
    {
        if (button != 0) return;
        if (!timingFields.Any(f => f.Key == timingField && f.Bounds.Contains(x, y)) && !CommitTimingField()) return;
        for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Bounds.Contains(x, y))
        { if (hits[i].Enabled) hits[i].Action(); return; }
    }

    private void BeginTimingVolumeDrag(Rect track)
    {
        if (!CommitTimingField()) return;
        timingVolumeTrack = track; timingVolumeStart = TimingSnapshot(); UpdateTimingVolume(mouseX);
    }

    private void UpdateTimingVolume(float x)
    {
        int volume = (int)Math.Clamp(Math.Round((x - timingVolumeTrack.X) / timingVolumeTrack.Width * 100), 0, 100);
        foreach (var entry in timingEntries.Where(e => timingSelected.Contains(e.Id))) entry.Point.Volume = volume;
    }

    private void EndTimingVolume(bool cancel)
    {
        if (timingVolumeStart is not { } before) return;
        if (cancel) { timingEntries.Clear(); timingEntries.AddRange(before); }
        else { timingUndo.Push(before); timingRedo.Clear(); }
        timingVolumeStart = null;
    }

    private void ChangeCurrentRed(string key, double value)
    {
        bool retiming = timingResetPoint is not null && ReferenceEquals(timingResetDocument, Document);
        var before = retiming ? timingResetPoint! : TimingEditing.Current(Document, playhead, true) ?? new TimingPoint { TimeMs = Document.TimingOffsetMs, BeatLengthMs = Document.BeatLengthMs };
        var after = TimingEditing.Copy(before);
        if (key == "bpm") { if (value < 1 || value > 10000) throw new ArgumentException(L.Get("timing.range")); after.BeatLengthMs = 60000 / value; }
        else if (key == "meter") after.Meter = (int)value;
        else { if (Math.Abs(value) > int.MaxValue) throw new ArgumentException(L.Get("timing.range")); after.TimeMs = value; }
        var points = Document.TimingPoints.Select(p => ReferenceEquals(p, before) ? after : p).ToList();
        if (!Document.TimingPoints.Contains(before)) points.Add(after);
        if (!Edit(L.Get("timing.edit"), () => TimingEditing.Apply(Document, points, [(before, after)], new(Scale: timingMoveNotes))))
            throw new ArgumentException(StatusMessage);
        timingResetPoint = null;
        ResetHitsounds();
    }

    private void TapTiming()
    {
        double time = transportSamplePosition + Math.Clamp(TestplayRealtime - transportSampleAt, 0, 250) * PlaybackSpeed;
        if (!AudioPlaying) { timingError = L.Get("timing.tapPlaying"); return; }
        if (timingTaps.Count > 0 && (time <= timingTaps[^1] || time - timingTaps[^1] > 3000)) timingTaps.Clear();
        timingTaps.Add(time); if (timingTaps.Count > 32) timingTaps.RemoveAt(0);
    }

    private void ApplyTappedTiming()
    {
        if (timingTaps.Count < 2) return;
        double beatLength = (timingTaps[^1] - timingTaps[0]) / (timingTaps.Count - 1);
        bool retiming = timingResetPoint is not null && ReferenceEquals(timingResetDocument, Document);
        var before = retiming ? null : TimingEditing.Current(Document, playhead, true);
        var point = retiming ? TimingEditing.Copy(timingResetPoint!) : before is null ? TimingEditing.Create(Document, timingTaps[0], false) : TimingEditing.Copy(before);
        point.TimeMs = timingTaps[0]; point.BeatLengthMs = beatLength;
        var points = Document.TimingPoints.Select(p => p == before ? point : p).ToList(); if (before is null) points.Add(point);
        if (Edit(L.Get("timing.tap"), () => TimingEditing.Apply(Document, points, before is null ? [] : [(before, point)], new(Scale: timingMoveNotes)))) timingResetPoint = null;
        ResetHitsounds();
    }

    private void OpenTimingCommand(string command)
    {
        if (!PrepareFileOperation()) return;
        timingCommand = command; timingField = timingError = ""; timingText = "0"; TimingInputSession++;
    }

    private void ApplyTimingCommand()
    {
        if (!CommitTimingField()) return;
        bool success = Edit(L.Get($"timing.{timingCommand}"), () =>
        {
            if (timingCommand == "move")
            {
                if (!double.TryParse(timingText, CultureInfo.InvariantCulture, out double amount) || !double.IsFinite(amount)) throw new ArgumentException(L.Get("timing.range"));
                TimingEditing.TransformObjects(Document, t => (t + amount, 1)); TimingEditing.Validate(Document);
            }
            else if (timingCommand == "deleteAll")
            { Document.TimingPoints.Clear(); Document.BeatLengthMs = 500; Document.TimingOffsetMs = 0; }
            else if (timingCommand == "reset")
            {
                var point = TimingEditing.Current(Document, playhead, true);
                timingResetPoint = point is null ? TimingEditing.Create(Document, playhead, false) : TimingEditing.Copy(point);
                if (point is not null) Document.TimingPoints.Remove(point);
                timingResetDocument = Document;
                TimingPageVisible = true;
                timingTaps.Clear();
            }
        });
        if (success) { CloseTimingSetup(); ResetHitsounds(); } else timingError = StatusMessage;
    }

    private void ResnapTimingSection(bool all)
    {
        var red = TimingEditing.Current(Document, playhead, true);
        double start = all ? double.NegativeInfinity : red?.TimeMs ?? double.NegativeInfinity;
        double end = all ? double.PositiveInfinity : Document.TimingPoints.Where(p => p.Uninherited && p.TimeMs > start).Select(p => p.TimeMs).DefaultIfEmpty(double.PositiveInfinity).Min();
        Edit(L.Get("timing.resnap"), () => TimingEditing.Resnap(Document, divisor, start, end));
    }
}
