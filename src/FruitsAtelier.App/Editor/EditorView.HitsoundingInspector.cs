using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private (Guid Id, int Event)? hitsoundingSelected;
    private readonly List<(Rect Bounds, ConvertedCatchObject Note, int Lane)> hitsoundingSoundHits = [];
    private readonly Dictionary<(Guid, int), EventHitsound> hitsoundingPaint = [];
    private bool hitsoundingDraw, hitsoundingPainting, hitsoundingErase, hitsoundingHeadDrag;
    private float hitsoundingPaintX;
    private double hitsoundingHeadBefore;
    private int hitsoundingPaintLane;
    private (Guid Id, int Event)? inspectorSelection;
    private CatchConversionResult? inspectorConversion;
    private double inspectorTime;
    private TimingPoint? hitsoundingSelectedTiming, inspectorTimingSelection;
    private sealed class TimingHelperPoint(TimingPoint source)
    {
        public TimingPoint Source = source;
        public double Time { get => Source.TimeMs; set => Source.TimeMs = value; }
        public double BeatLength { get => Source.BeatLengthMs; set => Source.BeatLengthMs = value; }
        public int Bank { get => Source.SampleSet; set => Source.SampleSet = value; }
        public int Index { get => Source.SampleIndex; set => Source.SampleIndex = value; }
        public int Volume { get => Source.Volume; set => Source.Volume = value; }
        public int Meter { get => Source.Meter; set => Source.Meter = value; }
        public int Effects { get => Source.Effects; set => Source.Effects = value; }
    }
    private TimingHelperPoint[] timingHelperPoints = [];
    private TimingHelperPoint? timingHelperVolumeDrag;
    private Rect timingHelperVolumeTrack;
    private void MoveTimingHelperVolume(float x)
    {
        if (timingHelperVolumeDrag is not { } entry) return;
        int volume = (int)Math.Round(Math.Clamp((x - timingHelperVolumeTrack.X) / Math.Max(1, timingHelperVolumeTrack.Width), 0, 1) * 100);
        if (entry.Volume == volume) return;
        entry.Volume = volume;
        RequestStopHitsounds?.Invoke(); scheduledThrough = null;
    }
    private void FinishTimingHelperVolume(bool cancel = false)
    {
        if (timingHelperVolumeDrag is null) return;
        timingHelperVolumeDrag = null;
        if (cancel) history.Cancel(); else CommitContentHistory();
        hitsoundConversion = null; hitsoundingConversion = null;
        RequestStopHitsounds?.Invoke(); scheduledThrough = null;
        inspectorConversion = null;
    }
    private int inspectorBank, inspectorIndex, inspectorVolume;
    private Rect hitsoundingInspectorBounds;
    private float hitsoundingInspectorScroll, hitsoundingInspectorScrollMaximum;
    private ConvertedCatchObject? HitsoundingSelectedNote => hitsoundingSelected is { } selected
        ? hitsoundingNoteLookup.GetValueOrDefault((selected.Id, selected.Event)) : null;
    private bool HitsoundingHeadHovered(float x, float y) => HitsoundingPageVisible && hitsoundingBounds.Contains(x, y)
        && Math.Abs(x - (hitsoundingGridBounds.X + hitsoundingGridBounds.Width * hitsoundingHeadRatio)) <= 6;

    private void RefreshHitsoundingInspector()
    {
        var note = HitsoundingSelectedNote;
        if (!ReferenceEquals(inspectorConversion, hitsoundingConversion) && hitsoundingSelectedTiming is not null && !Document.TimingPoints.Contains(hitsoundingSelectedTiming)) hitsoundingSelectedTiming = null;
        double time = hitsoundingSelectedTiming?.TimeMs ?? note?.TimeMs ?? playhead;
        bool changed = inspectorTimingSelection != hitsoundingSelectedTiming || inspectorSelection != hitsoundingSelected || !ReferenceEquals(inspectorConversion, hitsoundingConversion);
        if (!changed && inspectorTime == time) return;
        if (inspectorTimingSelection != hitsoundingSelectedTiming || inspectorSelection != hitsoundingSelected) hitsoundingInspectorScroll = 0;
        inspectorSelection = hitsoundingSelected; inspectorConversion = hitsoundingConversion;
        inspectorTimingSelection = hitsoundingSelectedTiming;
        timingHelperPoints = (hitsoundingSelectedTiming is { } selectedPoint ? new[] { selectedPoint }
            : TimingHelperPointsAt(time)).Select(p => new TimingHelperPoint(p)).ToArray();
        inspectorTime = time;
        var sample = hitsoundingResolver!.Describe(new(Guid.Empty, 0, CatchObjectKind.Fruit, time - 5, 0, 0, 0, 0, true));
        inspectorBank = sample.NormalSet; inspectorIndex = sample.Index; inspectorVolume = sample.Volume;
    }
    private IEnumerable<TimingPoint> TimingHelperPointsAt(double time)
    {
        // Match the millisecond precision shown by Time(), including fractional snap positions.
        double timestamp = Math.Truncate(time);
        int lo = 0, hi = overviewTiming.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (Math.Truncate(overviewTiming[mid].TimeMs) < timestamp) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < overviewTiming.Length && Math.Truncate(overviewTiming[i].TimeMs) == timestamp; i++) yield return overviewTiming[i];
    }
    private void DrawHitsoundingInspector(ICanvas c, Rect r)
    {
        RefreshHitsoundingInspector();
        hitsoundingInspectorBounds = r;
        var nearby = TimingHelperNearbyRange();
        float contentHeight = timingHelperPoints.Length > 0 ? 122 + timingHelperPoints.Sum(d => d.Source.Uninherited ? 410 : 336) : 418;
        float nearbyHeight = nearby.Count > 0 ? 30 + nearby.Count * 20 : 0;
        float editorHeight = Math.Max(0, r.Height - nearbyHeight - 8);
        var nearbyBounds = TimingHelperNearbyBounds = new Rect(r.X + 10, r.Y + editorHeight + 8, Math.Max(0, r.Width - 20), nearbyHeight);
        r = new(r.X, r.Y, r.Width, editorHeight);
        hitsoundingInspectorScrollMaximum = Math.Max(0, contentHeight - r.Height);
        hitsoundingInspectorScroll = Math.Clamp(hitsoundingInspectorScroll, 0, hitsoundingInspectorScrollMaximum);
        int firstHit = hits.Count, firstField = fields.Count;
        c.Clip(r);
        float y = r.Y + 8 - hitsoundingInspectorScroll, x = r.X + 10, w = Math.Max(0, r.Width - 20);
        DrawTimingHelperHeader(c, new(x, y, w, 148)); y += 158;
        if (timingHelperPoints.Length > 0)
        {
            foreach (var entry in timingHelperPoints)
            {
                if (entry != timingHelperPoints[0])
                {
                    c.Text(L.Get(entry.Source.Uninherited ? "hitsounding.redPoint" : "hitsounding.greenPoint", Time(entry.Time)), x, y, 12,
                        entry.Source.Uninherited ? 0xE77785u : 0x72C686u, w, true); y += 28;
                }
                DrawTimingHelperPoint(c, x, ref y, w, entry);
                if (entry != timingHelperPoints[^1]) { c.Line(x, y, x + w, y, Grid); y += 16; }
            }
        }
        else
        {
            c.Text(L.Get("samples.sampleset"), x, y, 11, Muted, w); y += 18;
            for (int i = 0; i < 3; i++)
            {
                int bank = new[] { 2, 1, 3 }[i];
                Button(c, new(x + i * w / 3, y, w / 3 - 3, 28), L.Get(bank == 2 ? "timing.soft" : bank == 1 ? "timing.normal" : "timing.drum"), () => { inspectorBank = bank; }, inspectorBank == bank);
            }
            y += 38;
            Field(c, x, ref y, w, L.Get("hitsounding.index"), inspectorIndex, v => { inspectorIndex = ValidHsIndex(v); }, 80);
            Field(c, x, ref y, w, L.Get("hitsounding.volume"), inspectorVolume, v => { inspectorVolume = (int)Math.Clamp(Math.Round(v), 0, 100); }, 80);
            Button(c, new(x, y + 4, w, 48), L.Get("hitsounding.applyTiming"), ApplyHitsoundingTiming, fontSize: 10);
        }
        c.Unclip();
        for (int i = firstHit; i < hits.Count; i++)
        {
            var b = hits[i].Bounds; float top = Math.Max(r.Y, b.Y), bottom = Math.Min(r.Bottom, b.Bottom);
            hits[i] = hits[i] with { Bounds = bottom > top ? new(b.X, top, b.Width, bottom - top) : default };
        }
        for (int i = firstField; i < fields.Count; i++)
            if (fields[i].Bounds.Y < r.Y || fields[i].Bounds.Bottom > r.Bottom) fields[i] = fields[i] with { Bounds = default };
        if (hitsoundingInspectorScrollMaximum > 0)
            c.Fill(new(r.Right - 3, r.Y + hitsoundingInspectorScroll / hitsoundingInspectorScrollMaximum * (r.Height - 40), 3, 40), Muted, 1.5f);
        DrawTimingHelperNearby(c, nearbyBounds, nearby);
    }
    private void DrawTimingHelperHeader(ICanvas c, Rect r)
    {
        c.Fill(r, Surface, 6);
        float x = r.X + 10, w = Math.Max(0, r.Width - 20);
        c.Text(L.Get(hitsoundingSelectedTiming is not null ? "hitsounding.selectedTiming" : hitsoundingSelected is null ? "hitsounding.currentTime" : "hitsounding.selectedNote"), x, r.Y + 8, 11, Muted, w);
        c.Text(Time(inspectorTime), x, r.Y + 27, 20, Foreground, Math.Max(0, w - 60), true);
        Button(c, new(r.Right - 64, r.Y + 28, 54, 28), L.Get("timeJump.go"), () =>
        {
            if (CommitField()) SeekTo(Math.Max(0, hitsoundingSelectedTiming?.TimeMs ?? inspectorTime));
        });
        var timing = renderedTiming!.At(inspectorTime);
        c.Text(L.Get("timing.bpm") + " " + Number(60000 / timing.BeatLengthMs), x, r.Y + 54, 10, Muted, w);
        c.Line(x, r.Y + 73, x + w, r.Y + 73, Grid);
        var red = LatestHitsoundingPoint(hitsoundingRedPoints, inspectorTime);
        var green = LatestHitsoundingPoint(hitsoundingGreenPoints, inspectorTime);
        c.Text(L.Get("hitsounding.affectingTiming"), x, r.Y + 80, 11, Foreground, w, true);
        if (green is not null && green.TimeMs < (red?.TimeMs ?? double.NegativeInfinity)) green = null;
        DrawAffectingRow(red, r.Y + 102, 0xE77785);
        DrawAffectingRow(green, r.Y + 122, 0x72C686);
        void DrawAffectingRow(TimingPoint? point, float y, uint color)
        {
            if (point is null) c.Text("—", x + 4, y + 3, 10, color, w);
            else DrawTimingHelperRow(c, new(x, y, w, 19), point, ReferenceEquals(point, hitsoundingSelectedTiming));
        }
    }
    private void DrawTimingHelperPoint(ICanvas c, float x, ref float y, float w, TimingHelperPoint entry)
    {
        bool red = entry.Source.Uninherited;
        Field(c, x, ref y, w, L.Get("timing.offset"), entry.Time, v => { if (!double.IsFinite(v)) throw new ArgumentException(L.Get("timing.range")); entry.Time = v; }, 100);
        if (red) Field(c, x, ref y, w, L.Get("timing.bpm"), 60000 / entry.BeatLength,
            v => { if (!double.IsFinite(v) || v <= 0 || v > 10000) throw new ArgumentException(L.Get("timing.range")); entry.BeatLength = 60000 / v; }, 100);
        if (red) Field(c, x, ref y, w, L.Get("timing.meter"), entry.Meter, v => { if (v < 1 || v > 32 || v != Math.Truncate(v)) throw new ArgumentException(L.Get("timing.range")); entry.Meter = (int)v; }, 100);
        c.Text(L.Get("samples.sampleset"), x, y, 11, Muted, w); y += 20;
        int displayedBank = entry.Bank > 0 ? entry.Bank : hitsoundingResolver!.Describe(new(Guid.Empty, 0, CatchObjectKind.Fruit, entry.Time - 5, 0, 0, 0, 0, true)).NormalSet;
        float bankWidth = w * .44f, choiceX = x + bankWidth + 8, choiceWidth = w - bankWidth - 8;
        float customLabelWidth = Math.Min(choiceWidth - 40, c.MeasureText(L.Get("timing.custom"), 11) + 26);
        for (int i = 0; i < 3; i++)
        {
            int bank = i + 1;
            Button(c, new(x, y + i * 30, bankWidth, 28), L.Get(bank == 2 ? "timing.soft" : bank == 1 ? "timing.normal" : "timing.drum"),
                () => EditTimingHelperPoint(() => entry.Bank = bank), displayedBank == bank);
            int index = i;
            var choice = new Rect(choiceX, y + i * 30, index == 2 && entry.Index >= 2 ? customLabelWidth : choiceWidth, 28);
            bool active = index == 2 ? entry.Index >= 2 : entry.Index == index;
            c.Circle(choice.X + 7, choice.Y + 14, 6, Muted, false, 2);
            if (active) c.Circle(choice.X + 7, choice.Y + 14, 3, Accent);
            c.Text(L.Get(index == 0 ? "timing.default" : index == 1 ? "timing.custom1" : "timing.custom"), choice.X + 20, choice.Y + 7, 11, Foreground, choice.Width - 20);
            hits.Add(new(choice, () => EditTimingHelperPoint(() => entry.Index = index == 2 ? Math.Max(2, entry.Index) : index), true));
        }
        if (entry.Index >= 2)
        {
            float customY = y + 60;
            Field(c, choiceX + customLabelWidth, ref customY, choiceWidth - customLabelWidth, L.Get("hitsounding.index"), entry.Index, v => { entry.Index = Math.Max(2, ValidHsIndex(v)); }, 0);
        }
        y += 94;
        float volumeY = y;
        float labelWidth = Math.Min(64, c.MeasureText(L.Get("volume.title"), 11) + 8);
        c.Text(L.Get("volume.title"), x, volumeY + 8, 11, Muted, labelWidth);
        Field(c, x + w - 60, ref y, 46, L.Get("hitsounding.volume"), entry.Volume, v => { entry.Volume = (int)Math.Clamp(Math.Round(v), 0, 100); }, 0);
        c.Text("%", x + w - 10, volumeY + 8, 11, Muted, 10);
        var slider = new Rect(x + labelWidth + 4, volumeY + 3, Math.Max(1, w - labelWidth - 74), 24);
        c.Line(slider.X, slider.Y + 12, slider.Right, slider.Y + 12, Grid, 4);
        c.Circle(slider.X + slider.Width * entry.Volume / 100, slider.Y + 12, 6, Accent);
        hits.Add(new(slider, () =>
        {
            if (!CommitField()) return;
            EnsureConversion(); GetHitsoundConversion();
            history.Begin(L.Get("timing.edit"));
            timingHelperVolumeDrag = entry; timingHelperVolumeTrack = slider; MoveTimingHelperVolume(mouseX);
        }, true));
        string[] sounds = ["hitnormal", "hitfinish", "hitwhistle", "hitclap"];
        for (int i = 0; i < sounds.Length; i++)
        {
            string sound = sounds[i];
            Button(c, new(x + i % 2 * w / 2, y + i / 2 * 32, w / 2 - 4, 28), L.Get("timing." + sound), () =>
            {
                if (!CommitField()) return;
                var point = TimingEditing.Copy(entry.Source);
                point.SampleSet = entry.Bank; point.SampleIndex = entry.Index; point.Volume = entry.Volume;
                PreviewTimingSample(point, sound);
            });
        }
        y += 66;
        ToggleSwitch(c, new(x, y, w, 32), L.Get("timing.kiai"), (entry.Effects & 1) != 0,
            () => EditTimingHelperPoint(() => entry.Effects ^= 1)); y += 38;
    }
    private void EditTimingHelperPoint(Action change)
    {
        if (!CommitField()) return;
        Edit(L.Get("timing.edit"), change);
        inspectorConversion = null;
    }
    private static TimingPoint? LatestHitsoundingPoint(TimingPoint[] points, double time)
    {
        int lo = 0, hi = points.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (points[mid].TimeMs <= time) lo = mid + 1; else hi = mid; }
        return lo > 0 ? points[lo - 1] : null;
    }
    private static int ValidHsIndex(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 10000 || value != Math.Truncate(value)) throw new ArgumentException(L.Get("timing.range"));
        return (int)value;
    }
    private void ApplyHitsoundingTiming()
    {
        if (!CommitField()) return;
        var active = overviewTiming.LastOrDefault(p => p.TimeMs <= inspectorTime);
        var state = renderedTiming!.At(inspectorTime);
        var point = new TimingPoint { TimeMs = inspectorTime, Uninherited = false,
            BeatLengthMs = active is { Uninherited: false } ? active.BeatLengthMs : -100 / state.SliderVelocityMultiplier,
            Meter = state.Meter, SampleSet = inspectorBank, SampleIndex = inspectorIndex, Volume = inspectorVolume,
            Effects = active?.Effects ?? 0 };
        Edit(L.Get("hitsounding.applyTiming"), () => Document.TimingPoints.Add(point));
    }
    private void SetHitsoundingEvent(EventHitsound setting)
    {
        int index = Document.HitsoundOverrides.FindIndex(s => s.SourceId == setting.SourceId && s.EventIndex == setting.EventIndex);
        if (index >= 0) Document.HitsoundOverrides[index] = setting; else Document.HitsoundOverrides.Add(setting);
    }
    private void SelectHitsoundingNote(ConvertedCatchObject note, bool seek)
    {
        if (!CommitField()) return;
        hitsoundingSelectedTiming = null;
        PickObject(note.SourceId, false); PickSoundEdge(note);
        hitsoundingSelected = (note.SourceId, note.EventIndex);
        if (seek) SeekTo(note.TimeMs);
    }
    private ConvertedCatchObject? HitsoundingNoteAt(float x, float y)
    {
        var hit = hitsoundingSoundHits.LastOrDefault(h => h.Bounds.Contains(x, y));
        if (hit.Note is not null) return hit.Note;
        float notesBottom = hitsoundingGridBounds.Y + hitsoundingGridBounds.Height / 7;
        if (y < hitsoundingGridBounds.Y || y >= notesBottom) return null;
        var marker = timelineObjects.LastOrDefault(m => m.Bounds.Contains(x, y));
        if (marker.Id == Guid.Empty) return null;
        double time = HitsoundingStart + (x - hitsoundingGridBounds.X) / hitsoundingGridBounds.Width * hitsoundingSpan;
        return hitsoundingNotes.Where(n => n.SourceId == marker.Id).MinBy(n => Math.Abs(n.TimeMs - time));
    }
    private ConvertedCatchObject? HitsoundingPlacementNoteAt(float x, float y)
    {
        if (!hitsoundingGridBounds.Contains(x, y)) return null;
        double time = HitsoundingStart + (x - hitsoundingGridBounds.X) / hitsoundingGridBounds.Width * hitsoundingSpan;
        double tolerance = 14 / hitsoundingGridBounds.Width * hitsoundingSpan;
        int first = FirstAfter(hitsoundingNotes, time - tolerance - .001);
        ConvertedCatchObject? best = null;
        for (int i = first; i < hitsoundingNotes.Length && hitsoundingNotes[i].TimeMs <= time + tolerance; i++)
            if (best is null || Math.Abs(hitsoundingNotes[i].TimeMs - time) < Math.Abs(best.TimeMs - time)) best = hitsoundingNotes[i];
        return best;
    }
    private bool BeginHitsoundingCanvasPointer(float x, float y, int button, bool ctrl, bool shift)
    {
        if (!hitsoundingBounds.Contains(x, y)) return false;
        if (button == 0 && !ctrl && HitsoundingPrecise && hitsoundingPreciseAuditionButton is { } audition && audition.Bounds.Contains(x, y))
        { ToggleHitsoundingLibraryAudition(audition.Sound); return true; }
        if (BeginHitsoundingTimingPointer(x, y, button, ctrl, shift)) return true;
        if (button == 0 && !ctrl && HitsoundingHeadHovered(x, y))
        { hitsoundingHeadBefore = hitsoundingHeadRatio; hitsoundingHeadDrag = true; return true; }
        if (button == 0)
        {
            var pointHit = hitsoundingTimingBoxes.LastOrDefault(t => t.Bounds.Contains(x, y)
                || Math.Abs(t.Anchor - x) <= 4 && y >= t.Bounds.Bottom && y < hitsoundingGridBounds.Y);
            if (pointHit.Point is not null)
            {
                if (!CommitField()) return true;
                SelectHitsoundingTimingPoint(pointHit.Point); return true;
            }
        }
        if (!hitsoundingGridBounds.Contains(x, y)) return false;
        hitsoundingTimingSelection.Clear();
        if (button == 2 && !hitsoundingDraw) { OpenHitsoundingSelectionMenu(x, y); return true; }
        if (BeginHitsoundingStoryboardPointer(x, y, button, ctrl)) return true;
        var note = HitsoundingNoteAt(x, y);
        int lane = Math.Clamp((int)((y - hitsoundingGridBounds.Y) / (hitsoundingGridBounds.Height / 7)), 0, 6);
        var pressedSound = hitsoundingSoundHits.LastOrDefault(h => h.Bounds.Contains(x, y));
        if (button == 0 && !hitsoundingDraw && ctrl)
        {
            if (!CommitField()) return true;
            if (pressedSound.Note is { } soundNote)
            {
                var key = (soundNote.SourceId, soundNote.EventIndex, pressedSound.Lane);
                if (!hitsoundingMultiSelection.Add(key)) hitsoundingMultiSelection.Remove(key);
            }
            else if (note is not null)
            {
                var keys = hitsoundingSoundHits.Where(h => h.Note.SourceId == note.SourceId && h.Note.EventIndex == note.EventIndex && h.Lane > 0)
                    .Select(h => (h.Note.SourceId, h.Note.EventIndex, h.Lane)).Distinct().ToArray();
                if (keys.All(hitsoundingMultiSelection.Contains)) hitsoundingMultiSelection.ExceptWith(keys);
                else hitsoundingMultiSelection.UnionWith(keys);
            }
            if (note is not null) SyncHitsoundingNoteSelection(note);
            return true;
        }
        bool canMove = !hitsoundingDraw && button == 0 && pressedSound.Note is { Kind: CatchObjectKind.Fruit } && pressedSound.Lane is >= 2 and <= 4;
        if (canMove)
        {
            hitsoundingMoveSource = (pressedSound.Note!.SourceId, pressedSound.Note.EventIndex, pressedSound.Lane);
            hitsoundingMoveStart = (x, y); hitsoundingMoveTarget = null; hitsoundingMoveActive = false;
        }
        if (note is not null) SelectHitsoundingNote(note, seek: false);
        else if (button == 0 && !hitsoundingDraw) { hitsoundingSelectedTiming = null; hitsoundingSelected = null; SelectObjects([]); }
        if (button == 0 && !hitsoundingDraw)
        {
            hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
            var soundHit = hitsoundingSoundHits.LastOrDefault(h => h.Bounds.Contains(x, y));
            if (soundHit.Note is not null && soundHit.Lane > 0) hitsoundingMultiSelection.Add((soundHit.Note.SourceId, soundHit.Note.EventIndex, soundHit.Lane));
            else if (note is not null)
                foreach (var hit in hitsoundingSoundHits.Where(h => h.Note.SourceId == note.SourceId && h.Note.EventIndex == note.EventIndex && h.Lane > 0))
                    hitsoundingMultiSelection.Add((hit.Note.SourceId, hit.Note.EventIndex, hit.Lane));
            if (note is null)
            {
                hitsoundingMarquee = true; hitsoundingMarqueeMoved = false;
                hitsoundingMarqueeStartTime = HitsoundingStart + (x - hitsoundingGridBounds.X) / hitsoundingGridBounds.Width * hitsoundingSpan;
                hitsoundingMarqueeStart = hitsoundingMarqueeEnd = (x, y);
            }
            else SyncHitsoundingNoteSelection(note);
        }
        if (lane is >= 2 and <= 4 && (button == 2 || hitsoundingDraw && button == 0))
        {
            hitsoundingPainting = true; hitsoundingErase = button == 2; hitsoundingPaintLane = lane;
            hitsoundingPaintX = x; hitsoundingPaint.Clear();
            var target = note ?? (button == 0 ? HitsoundingPlacementNoteAt(x, y) : null);
            if (target is { Kind: CatchObjectKind.Fruit }) PaintHitsoundingNote(target);
        }
        return true;
    }
    private void PaintHitsoundingNote(ConvertedCatchObject note)
    {
        var key = (note.SourceId, note.EventIndex);
        if (hitsoundingPaint.ContainsKey(key)) return;
        int flag = 1 << (hitsoundingPaintLane - 1);
        var setting = hitsoundingResolver!.EventSettings(note);
        int additions = hitsoundingErase ? setting.Sample.Additions & ~flag : setting.Sample.Additions | flag;
        if (!hitsoundingErase && nextAdditionBank > 0)
            setting = setting with { Sample = setting.Sample with { AdditionSet = nextAdditionBank }, InheritedFields = setting.InheritedFields & ~InheritedSampleFields.AdditionSet };
        if (!hitsoundingErase && nextNormalBank > 0)
            setting = setting with { Sample = setting.Sample with { NormalSet = nextNormalBank }, InheritedFields = setting.InheritedFields & ~InheritedSampleFields.NormalSet };
        if (additions != setting.Sample.Additions) hitsoundingPaint[key] = setting with { Sample = setting.Sample with { Additions = additions } };
    }
    private void ToggleHitsoundingAddition(int flag)
    {
        if (HitsoundingSelectedNote is not { Kind: CatchObjectKind.Fruit } note) return;
        var setting = hitsoundingResolver!.EventSettings(note);
        setting = setting with { Sample = setting.Sample with { Additions = setting.Sample.Additions ^ flag } };
        Edit(L.Get("assist.soundChange"), () => SetHitsoundingEvent(setting));
    }
    private bool MoveHitsoundingCanvasPointer(float x, float y)
    {
        if (MoveHitsoundingTimingPointer(x)) return true;
        if (hitsoundingHeadDrag)
        { hitsoundingHeadRatio = Math.Clamp((x - hitsoundingGridBounds.X) / Math.Max(1, hitsoundingGridBounds.Width), .05, .95); return true; }
        if (MoveHitsoundingStoryboard(x, y)) return true;
        if (MoveHitsoundingSound(x, y)) return true;
        if (hitsoundingMarquee)
        {
            hitsoundingMarqueeEnd = (Math.Clamp(x, hitsoundingGridBounds.X, hitsoundingGridBounds.Right), Math.Clamp(y, hitsoundingGridBounds.Y, hitsoundingGridBounds.Bottom));
            hitsoundingMarqueeMoved |= Math.Abs(x - hitsoundingMarqueeStart.X) >= ContentDragThreshold || Math.Abs(y - hitsoundingMarqueeStart.Y) >= ContentDragThreshold;
            UpdateHitsoundingMarquee(); return true;
        }
        if (!hitsoundingPainting) return false;
        double from = HitsoundingStart + (Math.Min(x, hitsoundingPaintX) - hitsoundingGridBounds.X) / hitsoundingGridBounds.Width * hitsoundingSpan;
        double to = HitsoundingStart + (Math.Max(x, hitsoundingPaintX) - hitsoundingGridBounds.X) / hitsoundingGridBounds.Width * hitsoundingSpan;
        int first = FirstAfter(hitsoundingNotes, from - .001);
        if (hitsoundingGridBounds.Contains(x, y))
            for (int i = first; i < hitsoundingNotes.Length && hitsoundingNotes[i].TimeMs <= to; i++)
                if (hitsoundingNotes[i].Kind == CatchObjectKind.Fruit) PaintHitsoundingNote(hitsoundingNotes[i]);
        hitsoundingPaintX = x; return true;
    }
    private bool EndHitsoundingCanvasPointer()
    {
        if (EndHitsoundingTimingPointer()) return true;
        if (hitsoundingHeadDrag) { hitsoundingHeadDrag = false; return true; }
        if (EndHitsoundingStoryboardMove()) return true;
        if (EndHitsoundingSoundMove()) return true;
        if (hitsoundingMarquee)
        {
            hitsoundingMarquee = false;
            return true;
        }
        if (!hitsoundingPainting) return false;
        hitsoundingPainting = false;
        var changes = hitsoundingPaint.Values.ToArray(); hitsoundingPaint.Clear();
        if (changes.Length > 0) Edit(L.Get("assist.soundChange"), () => { foreach (var setting in changes) SetHitsoundingEvent(setting); });
        return true;
    }
}
