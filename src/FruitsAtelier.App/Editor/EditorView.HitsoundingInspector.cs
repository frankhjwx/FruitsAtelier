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
    private int inspectorBank, inspectorIndex, inspectorVolume, inspectorNoteIndex;
    private bool inspectorTimingDirty, inspectorIndexDirty;
    private Rect hitsoundingInspectorBounds;
    private float hitsoundingInspectorScroll, hitsoundingInspectorScrollMaximum;
    private ConvertedCatchObject? HitsoundingSelectedNote => hitsoundingSelected is { } selected
        ? hitsoundingNoteLookup.GetValueOrDefault((selected.Id, selected.Event)) : null;
    private bool HitsoundingHeadHovered(float x, float y) => HitsoundingPageVisible && hitsoundingBounds.Contains(x, y)
        && Math.Abs(x - (hitsoundingGridBounds.X + hitsoundingGridBounds.Width * hitsoundingHeadRatio)) <= 6;

    private void RefreshHitsoundingInspector()
    {
        var note = HitsoundingSelectedNote;
        double time = note?.TimeMs ?? playhead;
        bool changed = inspectorSelection != hitsoundingSelected || !ReferenceEquals(inspectorConversion, hitsoundingConversion);
        if (!changed && (inspectorTimingDirty || inspectorIndexDirty || inspectorTime == time)) return;
        inspectorSelection = hitsoundingSelected; inspectorConversion = hitsoundingConversion;
        inspectorTime = time; inspectorTimingDirty = inspectorIndexDirty = false;
        var sample = hitsoundingResolver!.Describe(new(Guid.Empty, 0, CatchObjectKind.Fruit, time - 5, 0, 0, 0, 0, true));
        inspectorBank = sample.NormalSet; inspectorIndex = sample.Index; inspectorVolume = sample.Volume;
        inspectorNoteIndex = note is null ? 0 : hitsoundingResolver.Describe(note).Index;
    }
    private void DrawHitsoundingInspector(ICanvas c, Rect r)
    {
        RefreshHitsoundingInspector();
        hitsoundingInspectorBounds = r;
        hitsoundingInspectorScrollMaximum = Math.Max(0, (HitsoundingSelectedNote is { Kind: not CatchObjectKind.Banana } ? 450 : 330) - r.Height);
        hitsoundingInspectorScroll = Math.Clamp(hitsoundingInspectorScroll, 0, hitsoundingInspectorScrollMaximum);
        int firstHit = hits.Count, firstField = fields.Count;
        c.Clip(r);
        float y = r.Y + 8 - hitsoundingInspectorScroll, x = r.X + 10, w = Math.Max(0, r.Width - 20);
        c.Text(L.Get(hitsoundingSelected is null ? "hitsounding.currentTime" : "hitsounding.selectedNote"), x, y, 12, Muted, w); y += 24;
        c.Text(Time(inspectorTime), x, y, 16, Foreground, w, true); y += 30;
        var timing = renderedTiming!.At(inspectorTime);
        c.Text(L.Get("hitsounding.timingInfo", 60000 / timing.BeatLengthMs, timing.SliderVelocityMultiplier), x, y, 11, Muted, w); y += 28;
        var red = LatestHitsoundingPoint(hitsoundingRedPoints, inspectorTime);
        var green = LatestHitsoundingPoint(hitsoundingGreenPoints, inspectorTime);
        c.Text(L.Get("hitsounding.redPoint", red is null ? "—" : Time(red.TimeMs)), x, y, 10, 0xE77785, w); y += 20;
        c.Text(L.Get("hitsounding.greenPoint", green is null || green.TimeMs < (red?.TimeMs ?? double.NegativeInfinity) ? "—" : Time(green.TimeMs)), x, y, 10, 0x72C686, w); y += 26;
        c.Text(L.Get("samples.sampleset"), x, y, 11, Muted, w); y += 18;
        for (int i = 0; i < 3; i++)
        {
            int bank = new[] { 2, 3, 1 }[i];
            Button(c, new(x + i * w / 3, y, w / 3 - 3, 28), BankCode(bank), () => { inspectorBank = bank; inspectorTimingDirty = true; }, inspectorBank == bank);
        }
        y += 38;
        Field(c, x, ref y, w, L.Get("hitsounding.index"), inspectorIndex, v => { inspectorIndex = ValidHsIndex(v); inspectorTimingDirty = true; }, 80);
        Field(c, x, ref y, w, L.Get("hitsounding.volume"), inspectorVolume, v => { inspectorVolume = (int)Math.Clamp(Math.Round(v), 0, 100); inspectorTimingDirty = true; }, 80);
        Button(c, new(x, y + 4, w, 48), L.Get("hitsounding.applyTiming"), ApplyHitsoundingTiming, enabled: inspectorTimingDirty || editField >= 0, fontSize: 10);
        y += 68;
        if (HitsoundingSelectedNote is { Kind: not CatchObjectKind.Banana })
        {
            c.Line(x, y, x + w, y, Grid); y += 14;
            c.Text(L.Get("hitsounding.selectedNote"), x, y, 12, Foreground, w, true); y += 24;
            Field(c, x, ref y, w, L.Get("hitsounding.index"), inspectorNoteIndex, v => { inspectorNoteIndex = ValidHsIndex(v); inspectorIndexDirty = true; }, 80);
            Button(c, new(x, y + 4, w, 30), L.Get("hitsounding.applyIndex"), ApplyHitsoundingIndex, enabled: inspectorIndexDirty || editField >= 0, fontSize: 11);
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
        if (!CommitField() || !inspectorTimingDirty) return;
        var active = overviewTiming.LastOrDefault(p => p.TimeMs <= inspectorTime);
        var state = renderedTiming!.At(inspectorTime);
        var point = new TimingPoint { TimeMs = inspectorTime, Uninherited = false,
            BeatLengthMs = active is { Uninherited: false } ? active.BeatLengthMs : -100 / state.SliderVelocityMultiplier,
            Meter = state.Meter, SampleSet = inspectorBank, SampleIndex = inspectorIndex, Volume = inspectorVolume,
            Effects = active?.Effects ?? 0 };
        if (Edit(L.Get("hitsounding.applyTiming"), () => Document.TimingPoints.Add(point))) inspectorTimingDirty = false;
    }
    private void ApplyHitsoundingIndex()
    {
        if (!CommitField() || !inspectorIndexDirty || HitsoundingSelectedNote is not { } note) return;
        var setting = hitsoundingResolver!.EventSettings(note);
        setting = setting with { Sample = setting.Sample with { Index = inspectorNoteIndex }, InheritedFields = setting.InheritedFields & ~InheritedSampleFields.Index };
        if (Edit(L.Get("hitsounding.applyIndex"), () => SetHitsoundingEvent(setting))) inspectorIndexDirty = false;
    }
    private void SetHitsoundingEvent(EventHitsound setting)
    {
        int index = Document.HitsoundOverrides.FindIndex(s => s.SourceId == setting.SourceId && s.EventIndex == setting.EventIndex);
        if (index >= 0) Document.HitsoundOverrides[index] = setting; else Document.HitsoundOverrides.Add(setting);
    }
    private void SelectHitsoundingNote(ConvertedCatchObject note, bool seek)
    {
        if (!CommitField()) return;
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
    private bool BeginHitsoundingCanvasPointer(float x, float y, int button)
    {
        if (!hitsoundingBounds.Contains(x, y)) return false;
        if (button == 0 && HitsoundingHeadHovered(x, y))
        { hitsoundingHeadBefore = hitsoundingHeadRatio; hitsoundingHeadDrag = true; return true; }
        if (!hitsoundingGridBounds.Contains(x, y)) return false;
        var note = HitsoundingNoteAt(x, y);
        int lane = Math.Clamp((int)((y - hitsoundingGridBounds.Y) / (hitsoundingGridBounds.Height / 7)), 0, 6);
        if (note is not null) SelectHitsoundingNote(note, seek: !hitsoundingDraw && button == 0);
        else if (button == 0 && !hitsoundingDraw) { hitsoundingSelected = null; SelectObjects([]); }
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
        if (hitsoundingHeadDrag)
        { hitsoundingHeadRatio = Math.Clamp((x - hitsoundingGridBounds.X) / Math.Max(1, hitsoundingGridBounds.Width), .05, .95); return true; }
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
        if (hitsoundingHeadDrag) { hitsoundingHeadDrag = false; return true; }
        if (!hitsoundingPainting) return false;
        hitsoundingPainting = false;
        var changes = hitsoundingPaint.Values.ToArray(); hitsoundingPaint.Clear();
        if (changes.Length > 0) Edit(L.Get("assist.soundChange"), () => { foreach (var setting in changes) SetHitsoundingEvent(setting); });
        return true;
    }
}
