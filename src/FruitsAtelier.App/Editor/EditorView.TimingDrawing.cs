using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static string TimingN(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private void DrawTimingSetup(ICanvas c)
    {
        if (!TimingModal) return;
        hits.Clear(); fields.Clear(); timingFields.Clear();
        var r = TimingSetupBounds;
        c.Fill(new(0, 0, width, height), Background, opacity: .8f); c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        if (timingCommand.Length > 0)
        {
            c.Text(L.Get($"timing.{timingCommand}"), r.X + 24, r.Y + 24, 20, Foreground, r.Width - 48);
            c.Text(L.Get($"timing.{timingCommand}Help"), r.X + 24, r.Y + 86, 14, Foreground, r.Width - 48);
            if (timingCommand == "move") TimingNumber(c, "move", new(r.X + 24, r.Y + 130, 250, 34), timingText, v => timingText = TimingN(v));
            c.Text(timingError, r.X + 24, r.Bottom - 100, 12, Error, r.Width - 48);
            Button(c, new(r.Right - 224, r.Bottom - 48, 92, 32), L.Get("mac.cancel"), CloseTimingSetup);
            Button(c, new(r.Right - 120, r.Bottom - 48, 96, 32), L.Get("song.ok"), ApplyTimingCommand, true);
            return;
        }
        c.Text(L.Get("timing.title"), r.X + 20, r.Y + 16, 19, Foreground, r.Width - 80, true);
        Button(c, new(r.Right - 48, r.Y + 10, 32, 28), "×", CloseTimingSetup);
        float leftWidth = Math.Clamp(r.Width * .39f, 280, 360), listX = r.X + leftWidth + 24;
        string[] tabs = ["timing.timing", "timing.audio", "timing.style"];
        for (int i = 0; i < 3; i++)
        {
            int tab = i;
            Button(c, new(r.X + 16 + i * (leftWidth / 3), r.Y + 52, leftWidth / 3 - 4, 30), L.Get(tabs[i]),
                () => { if (CommitTimingField()) timingTab = tab; }, timingTab == i);
        }
        string[] filters = ["timing.all", "timing.red", "timing.green"];
        float filterWidth = (r.Right - listX - 16) / 3;
        for (int i = 0; i < 3; i++)
        {
            int filter = i;
            Button(c, new(listX + i * filterWidth, r.Y + 52, filterWidth - 3, 30), L.Get(filters[i]),
                () => { if (CommitTimingField()) { timingFilter = filter; timingScroll = 0; timingSelected.IntersectWith(VisibleTimingEntries().Select(e => e.Id)); } }, timingFilter == i);
        }
        var selected = timingEntries.Where(e => timingSelected.Contains(e.Id)).Select(e => e.Point).ToArray();
        var left = new Rect(r.X + 16, r.Y + 98, leftWidth, r.Height - 310);
        c.Fill(left, Background, 4);
        if (selected.Length == 0) c.Text(L.Get("timing.select"), left.X + 12, left.Y + 20, 13, Muted, left.Width - 24);
        else DrawTimingProperties(c, left, selected);
        timingListBounds = new(listX, r.Y + 98, r.Right - listX - 16, r.Height - 348);
        var list = timingListBounds;
        c.Fill(list, Background, 4);
        float[] columns = [0, .28f, .48f, .62f, .79f, .91f];
        string[] headings = ["timing.offset", "timing.bpm", "timing.meter", "timing.sample", "timing.volumeShort", "timing.ki"];
        for (int i = 0; i < columns.Length; i++) c.Text(L.Get(headings[i]), list.X + 9 + list.Width * columns[i], list.Y + 8, 11, Muted, list.Width * (i == 0 ? .27f : .17f) - 8);
        var entries = VisibleTimingEntries();
        int capacity = Math.Max(1, (int)((list.Height - 34) / 28));
        timingScroll = Math.Clamp(timingScroll, 0, Math.Max(0, entries.Length - capacity));
        c.Clip(list);
        for (int i = timingScroll; i < Math.Min(entries.Length, timingScroll + capacity); i++)
        {
            var entry = entries[i]; var p = entry.Point;
            var row = new Rect(list.X + 3, list.Y + 32 + (i - timingScroll) * 28, list.Width - 12, 28);
            c.Fill(row, timingSelected.Contains(entry.Id) ? 0x35495B : i % 2 == 0 ? 0x222832u : Background);
            c.Circle(row.X + 6, row.Y + 14, 3, p.Uninherited ? Error : Accent);
            string[] values = [Time(p.TimeMs), p.Uninherited ? TimingN(60000 / p.BeatLengthMs) : "×" + TimingN(p.BeatLengthMs < 0 ? -100 / p.BeatLengthMs : 1),
                p.Uninherited ? p.Meter + "/4" : "", (p.SampleSet switch { 1 => "N", 2 => "S", 3 => "D", _ => "–" }) + ":C" + p.SampleIndex,
                p.Volume + "%", (p.Effects & 1) != 0 ? "✓" : ""];
            for (int k = 0; k < columns.Length; k++) c.Text(values[k], list.X + (k == 0 ? 15 : 9) + list.Width * columns[k], row.Y + 7, 11, Foreground,
                list.Width * (k + 1 < columns.Length ? columns[k + 1] - columns[k] : .09f) - 12);
            hits.Add(new(row, () => SelectTimingEntry(entry.Id, placementCtrl, timingPointerShift), true));
        }
        c.Unclip();
        if (entries.Length > capacity)
        {
            float h = Math.Max(12, (list.Height - 34) * capacity / entries.Length);
            c.Fill(new(list.Right - 6, list.Y + 34 + (list.Height - 34 - h) * timingScroll / Math.Max(1, entries.Length - capacity), 3, h), Muted, 2);
        }
        float controlsY = list.Bottom + 8;
        TimingCheck(c, new(list.X, controlsY, list.Width - 90, 30), "timing.inherit",
            selected.Length > 0 && selected.All(p => !p.Uninherited), ToggleTimingInheritance, selected.Length > 0);
        Button(c, new(list.Right - 82, controlsY, 36, 30), "+", () => AddTimingPoint(timingFilter != 1), true);
        Button(c, new(list.Right - 40, controlsY, 36, 30), "−", DeleteTimingPoints, enabled: selected.Length > 0);
        float applyY = r.Bottom - 188;
        c.Text(L.Get("timing.whenApplying"), r.X + 20, applyY, 13, Foreground, r.Width - 40);
        TimingCheck(c, new(r.X + 20, applyY + 25, r.Width * .47f, 28), "timing.scale", timingScale, () => timingScale = !timingScale);
        TimingCheck(c, new(r.X + 20, applyY + 58, r.Width * .47f, 28), "timing.snap", timingResnap, () => timingResnap = !timingResnap);
        TimingCheck(c, new(r.X + r.Width * .5f, applyY + 25, r.Width * .47f, 28), "timing.lengths", timingLengths, () => timingLengths = !timingLengths);
        TimingCheck(c, new(r.X + r.Width * .5f, applyY + 58, r.Width * .47f, 28), "timing.bookmarks", timingBookmarks, () => timingBookmarks = !timingBookmarks);
        Button(c, new(r.X + 20, applyY + 91, 235, 26), L.Get("timing.snapDivisor", timingSnap),
            () => timingSnap = SnapDivisors[(Array.IndexOf(SnapDivisors, timingSnap) + 1) % SnapDivisors.Length]);
        c.Text(timingError, r.X + 265, applyY + 95, 11, Error, r.Width - 285);
        Button(c, new(r.X + 20, r.Bottom - 46, r.Width * .65f - 24, 32), L.Get("song.ok"), ApplyTimingSetup, true);
        Button(c, new(r.X + r.Width * .65f, r.Bottom - 46, r.Width * .35f - 20, 32), L.Get("mac.cancel"), CloseTimingSetup);
    }

    private void TimingCheck(ICanvas c, Rect bounds, string key, bool value, Action action, bool enabled = true)
    {
        var box = new Rect(bounds.X + 7, bounds.Y + (bounds.Height - 14) / 2, 14, 14);
        c.Fill(box, value ? Accent : Background, 2); c.Stroke(box, enabled ? Muted : Grid, radius: 2);
        if (value)
        { c.Line(box.X + 3, box.Y + 7, box.X + 6, box.Y + 10, Background, 2); c.Line(box.X + 6, box.Y + 10, box.X + 11, box.Y + 4, Background, 2); }
        c.Text(L.Get(key), bounds.X + 29, bounds.Y + (bounds.Height - 16) / 2, 12, enabled ? Foreground : Muted, bounds.Width - 33);
        hits.Add(new(bounds, () => { if (CommitTimingField()) action(); }, enabled));
    }

    private void DrawTimingProperties(ICanvas c, Rect r, TimingPoint[] points)
    {
        float y = r.Y + 14;
        string Common(Func<TimingPoint, double> get) => points.Select(get).Distinct().Take(2).Count() == 1 ? TimingN(get(points[0])) : "";
        void Number(string key, Func<TimingPoint, double> get)
        {
            c.Text(L.Get($"timing.{key}"), r.X + 12, y + 8, 12, Foreground, r.Width * .48f - 12);
            TimingNumber(c, key, new(r.X + r.Width * .48f, y, r.Width * .52f - 12, 30), Common(get), v => SetTimingValues(key, v), () => get(points[0])); y += timingTab == 1 ? 34 : 45;
        }
        if (timingTab == 0)
        {
            Number("offset", p => p.TimeMs);
            Button(c, new(r.X + 12, y, r.Width - 24, 32), L.Get("timing.useCurrent"), () => SetTimingValues("offset", playhead)); y += 48;
            if (points.All(p => p.Uninherited)) { Number("bpm", p => 60000 / p.BeatLengthMs); Number("meter", p => p.Meter); }
            else if (points.Any(p => p.Uninherited)) c.Text(L.Get("timing.mixedTypes"), r.X + 12, y, 12, Muted, r.Width - 24);
        }
        else if (timingTab == 1)
        {
            string[] banks = ["timing.normal", "timing.soft", "timing.drum"];
            for (int i = 0; i < 3; i++)
            {
                int bank = i + 1; Button(c, new(r.X + 12 + i * (r.Width - 24) / 3, y, (r.Width - 24) / 3 - 4, 30), L.Get(banks[i]), () => SetTimingValues("bank", bank), points.All(p => p.SampleSet == bank));
            }
            y += 34;
            Button(c, new(r.X + 12, y, (r.Width - 28) / 2, 30), L.Get("timing.default"), () => SetTimingValues("index", 0), points.All(p => p.SampleIndex == 0));
            Button(c, new(r.X + r.Width / 2, y, (r.Width - 28) / 2, 30), L.Get("timing.custom1"), () => SetTimingValues("index", 1), points.All(p => p.SampleIndex == 1));
            y += 34; Number("index", p => p.SampleIndex); Number("volume", p => p.Volume);
            var slider = new Rect(r.X + 16, y, r.Width - 32, 20);
            c.Line(slider.X, y + 10, slider.Right, y + 10, Grid, 4);
            c.Circle(slider.X + slider.Width * points[0].Volume / 100, y + 10, 6, Accent);
            hits.Add(new(slider, () => BeginTimingVolumeDrag(slider), true)); y += 24;
            string[] sounds = ["hitnormal", "hitfinish", "hitwhistle", "hitclap"];
            for (int i = 0; i < 4; i++)
            {
                string sound = sounds[i]; Button(c, new(r.X + 12 + i % 2 * (r.Width - 24) / 2, y + i / 2 * 30, (r.Width - 24) / 2 - 4, 28), L.Get($"timing.{sound}"), () => PreviewTimingSample(points[0], sound));
            }
            y += 60;
            Button(c, new(r.X + 12, y, r.Width - 24, 28), L.Get("timing.sampleHelp"), () => RequestTimingSampleHelp?.Invoke());
        }
        else
        {
            bool enabled = points.All(p => !p.Uninherited);
            TimingCheck(c, new(r.X + 12, y, r.Width - 24, 32), "timing.kiai", points.All(p => (p.Effects & 1) != 0),
                () => SetTimingValues("kiai", points.All(p => (p.Effects & 1) != 0) ? 0 : 1), enabled);
            c.Text(L.Get("timing.kiaiHelp"), r.X + 12, y + 50, 13, Muted, r.Width - 24);
        }
    }

    private void PreviewTimingSample(TimingPoint point, string sound)
    {
        if (!CommitTimingField()) return;
        var map = Document.DeepClone(); map.TimingPoints.Clear();
        var previewPoint = TimingEditing.Copy(point); previewPoint.TimeMs = 0; map.TimingPoints.Add(previewPoint);
        var fruit = new Fruit { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1," + (sound switch { "hitwhistle" => 2, "hitfinish" => 4, "hitclap" => 8, _ => 0 }) + ",0:0:0:0:" };
        map.Fruits.Clear(); map.Tracks.Clear(); map.ImportedSliders.Clear(); map.BananaShowers.Clear(); map.Fruits.Add(fruit);
        var objects = CatchStreamConverter.Convert(map).Objects;
        var resolved = new HitsoundResolver(map, objects, HitsoundSkinFolders);
        foreach (var sample in resolved.Resolve(objects[0]).Where(s => s.Name == sound)) RequestHitsound?.Invoke(sample);
    }

    private void DrawTimingPage(ICanvas c)
    {
        timingFields.Clear();
        var r = new Rect(24, 96, width - 48, height - 218);
        c.Fill(r, Panel, 8);
        Button(c, new(r.X + 20, r.Y + 14, 120, 30), L.Get("timing.compose"), () => ShowTimingPage(false));
        c.Text(L.Get("timing.page"), r.X + 158, r.Y + 20, 19, Foreground, 240, true);
        Button(c, new(r.Right - 240, r.Y + 14, 220, 32), L.Get("timing.setup"), OpenTimingSetup, true);
        var state = TimingMap.At(Document, playhead);
        if (timingResetPoint is not null && !ReferenceEquals(timingResetDocument, Document)) timingResetPoint = null;
        float x = r.X + 24, y = r.Y + 78, controlsWidth = Math.Min(500, r.Width * .58f);
        void Number(string key, double value, double step, Action<double> apply)
        {
            c.Text(L.Get($"timing.{key}"), x, y + 9, 13, Foreground, controlsWidth * .45f);
            float inputX = x + controlsWidth * .45f, inputWidth = controlsWidth * .55f - 80;
            TimingNumber(c, "page." + key, new(inputX + 36, y, inputWidth, 34), TimingN(value), apply);
            void Step(int direction)
            {
                if (!CommitTimingField()) return;
                double change = key == "bpm" ? placementCtrl ? .25 : timingPointerShift ? 5 : 1 : key == "offset" ? placementCtrl ? 1 : timingPointerShift ? 10 : 2 : step;
                var current = TimingMap.At(Document, playhead);
                double number = key == "bpm" ? 60000 / (timingResetPoint?.BeatLengthMs ?? current.BeatLengthMs)
                    : key == "offset" ? timingResetPoint?.TimeMs ?? current.OffsetMs : Document.SliderTickRate;
                try { apply(number + direction * change); } catch (ArgumentException ex) { timingError = ex.Message; }
            }
            Button(c, new(inputX, y, 32, 34), "−", () => Step(-1));
            Button(c, new(inputX + inputWidth + 40, y, 32, 34), "+", () => Step(1));
            y += 56;
        }
        Number("bpm", 60000 / (timingResetPoint?.BeatLengthMs ?? state.BeatLengthMs), 1, v => ChangeCurrentRed("bpm", v));
        Number("offset", timingResetPoint?.TimeMs ?? state.OffsetMs, 2, v => ChangeCurrentRed("offset", v));
        TimingCheck(c, new(x, y, controlsWidth, 34), "timing.moveNotes", timingMoveNotes, () => timingMoveNotes = !timingMoveNotes); y += 56;
        Number("tickRate", Document.SliderTickRate, 1, v =>
        {
            if (v < .5 || v > 8) throw new ArgumentException(L.Get("timing.range"));
            Edit(L.Get("timing.edit"), () => Document.SliderTickRate = v);
        });
        float rightX = r.X + controlsWidth + 50, rightWidth = r.Right - rightX - 24;
        int count = Math.Min(16, state.Meter);
        double beat = (playhead - state.OffsetMs) / state.BeatLengthMs;
        int active = ((int)Math.Floor(beat) % state.Meter + state.Meter) % state.Meter;
        for (int i = 0; i < count; i++)
            c.Fill(new(rightX + i * rightWidth / count, r.Y + 78, rightWidth / count - 4, 32),
                AudioPlaying && i == active && beat * (placementCtrl ? divisor : 1) % 1 < .35 ? i == 0 ? Accent : Foreground : Background, 4);
        TimingCheck(c, new(rightX, r.Y + 124, rightWidth, 30), "timing.metronome", metronomeEnabled, () => { metronomeEnabled = !metronomeEnabled; ResetHitsounds(); });
        Button(c, new(rightX, r.Y + 170, rightWidth, 40), L.Get("timing.tap"), TapTiming, true);
        Button(c, new(rightX, r.Y + 224, rightWidth / 2 - 4, 32), L.Get("timing.tapReset"), () => timingTaps.Clear());
        Button(c, new(rightX + rightWidth / 2, r.Y + 224, rightWidth / 2, 32), L.Get("timing.tapApply"), ApplyTappedTiming, enabled: timingTaps.Count >= 2);
        if (timingTaps.Count >= 2) c.Text(L.Get("timing.tapResult", 60000 * (timingTaps.Count - 1) / (timingTaps[^1] - timingTaps[0]), timingTaps.Count), rightX, r.Y + 270, 13, Accent, rightWidth);
        Button(c, new(rightX, r.Y + 308, rightWidth, 30), L.Get("timing.snapDivisor", divisor), () => { divisor = SnapDivisors[(Array.IndexOf(SnapDivisors, divisor) + 1) % SnapDivisors.Length]; ResetHitsounds(); });
        c.Text(L.Get("timing.ctrlTicks"), rightX, r.Y + 350, 12, Muted, rightWidth);
        c.Text(timingError.Length > 0 ? timingError : timingResetPoint is not null ? L.Get("timing.retiming") : "", x, r.Bottom - 32, 12, timingError.Length > 0 ? Error : Gold, r.Width - 48);
    }
}
