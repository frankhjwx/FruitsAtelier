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
        float leftWidth = Math.Clamp(r.Width * .39f, 280, 360), listX = r.X + leftWidth + 40;
        c.Stroke(new(r.X + 10, r.Y + 46, leftWidth + 12, r.Height - 252), Grid, radius: 5);
        c.Stroke(new(listX - 6, r.Y + 46, r.Right - listX - 4, r.Height - 252), Grid, radius: 5);
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
        float[] columns = [0, .22f, .35f, .49f, .69f, .84f];
        string[] headings = ["timing.offsetShort", "timing.bpm", "timing.meter", "timing.sample", "timing.volumeShort", "timing.kiai"];
        for (int i = 0; i < columns.Length; i++) c.Text(L.Get(headings[i]), list.X + 9 + list.Width * columns[i], list.Y + 8, 11, Muted, list.Width * (i + 1 < columns.Length ? columns[i + 1] - columns[i] : 1 - columns[i]) - 8);
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
            string[] values = [Time(p.TimeMs), p.Uninherited ? TimingN(60000 / p.BeatLengthMs) : "",
                p.Uninherited ? p.Meter + "/4" : "", (p.SampleSet switch { 1 => "N", 2 => "S", 3 => "D", _ => "–" }) + (p.SampleIndex == 0 ? "" : ":C" + p.SampleIndex),
                p.Volume + "%", (p.Effects & 1) != 0 ? "✓" : ""];
            for (int k = 0; k < columns.Length; k++) c.Text(values[k], list.X + (k == 0 ? 15 : 9) + list.Width * columns[k], row.Y + 7, 11, Foreground,
                list.Width * (k + 1 < columns.Length ? columns[k + 1] - columns[k] : 1 - columns[k]) - 12);
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
        DrawTimingSnap(c, new(r.X + 20, applyY + 91, 300, 38));
        c.Text(timingError, r.X + 340, applyY + 95, 11, Error, r.Width - 360);
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
            c.Text(L.Get($"timing.{key}"), r.X + 12, y + 8, 12, Foreground, 90);
            TimingNumber(c, key, new(r.X + 106, y, r.Width - 118, 30), Common(get), v => SetTimingValues(key, v), () => get(points[0])); y += timingTab == 1 ? 34 : 45;
        }
        if (timingTab == 0)
        {
            Number("offset", p => p.TimeMs);
            TimingButton(c, new(r.X + 12, y, r.Width - 24, 32), L.Get("timing.useCurrent"), () => SetTimingValues("offset", playhead)); y += 48;
            if (points.All(p => p.Uninherited)) { Number("bpm", p => 60000 / p.BeatLengthMs); Number("meter", p => p.Meter); }
            else if (points.Any(p => p.Uninherited)) c.Text(L.Get("timing.mixedTypes"), r.X + 12, y, 12, Muted, r.Width - 24);
        }
        else if (timingTab == 1)
        {
            float bankWidth = (r.Width - 36) * .47f, choiceX = r.X + bankWidth + 24, choiceWidth = r.Right - choiceX - 12;
            c.Stroke(new(r.X + 12, y, bankWidth, 96), Grid);
            string[] banks = ["timing.normal", "timing.soft", "timing.drum"];
            for (int i = 0; i < 3; i++)
            {
                int bank = i + 1;
                Button(c, new(r.X + 14, y + 2 + i * 30, bankWidth - 4, 30), L.Get(banks[i]), () => SetTimingValues("bank", bank), points.All(p => p.SampleSet == bank));
            }
            void Choice(string key, int index, float cy)
            {
                bool active = index == 2 ? points.All(p => p.SampleIndex >= 2) : points.All(p => p.SampleIndex == index);
                var bounds = new Rect(choiceX, cy, choiceWidth, 28);
                c.Circle(choiceX + 8, cy + 14, 7, Muted);
                c.Circle(choiceX + 8, cy + 14, 5, Background);
                if (active) c.Circle(choiceX + 8, cy + 14, 3, Accent);
                c.Text(L.Get(key), choiceX + 22, cy + 7, 12, Foreground, choiceWidth - 22);
                hits.Add(new(bounds, () => SetTimingValues("index", index == 2 ? Math.Max(2, points[0].SampleIndex) : index), true));
            }
            Choice("timing.default", 0, y); Choice("timing.custom1", 1, y + 30); Choice("timing.custom", 2, y + 60);
            TimingNumber(c, "index", new(choiceX + 22, y + 92, choiceWidth - 22, 28), Common(p => p.SampleIndex), v => SetTimingValues("index", v), enabled: points.All(p => p.SampleIndex >= 2));
            y += 132;
            c.Text(L.Get("timing.volume"), r.X + 12, y + 7, 12, Foreground, 64);
            var slider = new Rect(r.X + 78, y + 3, r.Width - 166, 24);
            c.Line(slider.X, slider.Y + 12, slider.Right, slider.Y + 12, Grid, 4);
            c.Circle(slider.X + slider.Width * points[0].Volume / 100, slider.Y + 12, 6, Accent);
            hits.Add(new(slider, () => BeginTimingVolumeDrag(slider), true));
            TimingNumber(c, "volume", new(r.Right - 78, y, 50, 28), Common(p => p.Volume), v => SetTimingValues("volume", v));
            c.Text("%", r.Right - 24, y + 7, 12, Foreground, 20); y += 38;
            string[] sounds = ["hitnormal", "hitfinish", "hitwhistle", "hitclap"];
            for (int i = 0; i < 4; i++)
            {
                string sound = sounds[i]; TimingButton(c, new(r.X + 12 + i % 2 * (r.Width - 24) / 2, y + i / 2 * 32, (r.Width - 24) / 2 - 6, 28), L.Get($"timing.{sound}"), () => PreviewTimingSample(points[0], sound));
            }
            y += 66;
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
        foreach (var sample in resolved.Resolve(objects[0]).Where(s => s.Name == sound)) (RequestAuditionHitsound ?? RequestHitsound)?.Invoke(sample);
    }

    private void TimingButton(ICanvas c, Rect r, string text, Action action, int sprite = -1, bool enabled = true, bool flatArrow = false)
    {
        bool arrow = text is "‹" or "›";
        if (arrow && !flatArrow)
        {
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "ui", "timing", "arrows.png"), r,
                source: new Rect(text == "‹" ? 62 : 674, 368, 520, 492), opacity: enabled ? 1 : .35f);
        }
        else if (arrow)
        {
            c.Fill(r, Surface, 3);
            c.Stroke(r, Grid, radius: 3);
            float direction = text == "‹" ? -1 : 1, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            uint color = enabled ? Foreground : Muted;
            c.Line(cx - direction * 3, cy - 5, cx + direction * 3, cy, color, 2);
            c.Line(cx + direction * 3, cy, cx - direction * 3, cy + 5, color, 2);
        }
        else if (sprite >= 0)
        {
            var source = new Rect(80, new[] { 80, 374, 666, 962 }[sprite], 1096, 192);
            if (!c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "ui", "timing", "controls.png"), r, source: source, opacity: enabled ? 1 : .35f))
                c.Fill(r, sprite == 2 ? 0xC98512u : 0x2977B8u, 4);
        }
        else { c.Fill(r, Surface, 4); c.Stroke(r, enabled ? Muted : Grid, radius: 4); }
        if (enabled && r.Contains(mouseX, mouseY)) c.Fill(r, Foreground, 4, .12f);
        string label = text.Split("  ")[0];
        float size = 12, tw = Math.Min(r.Width - 8, c.MeasureText(label, size));
        if (!arrow) c.Text(label, r.X + (r.Width - tw) / 2, r.Y + (r.Height - 16) / 2, size, enabled ? Foreground : Muted, tw + 1);
        hits.Add(new(r, () => { if (CommitTimingField()) action(); }, enabled));
    }

    private void DrawPanelMenu(ICanvas c)
    {
        if (!panelMenuOpen) return;
        panelMenuBounds = new(rightPanel.X + 8, rightPanel.Y + 36, 180, 76);
        c.Fill(panelMenuBounds, Surface, 4); c.Stroke(panelMenuBounds, Muted, radius: 4);
        Button(c, new(panelMenuBounds.X + 4, panelMenuBounds.Y + 4, 172, 32), L.Get("timing.detailsPanel"), () => ShowTimingPage(false), !TimingPageVisible);
        Button(c, new(panelMenuBounds.X + 4, panelMenuBounds.Y + 40, 172, 32), L.Get("timing.panel"), () => ShowTimingPage(true), TimingPageVisible);
    }

    private void PanelPointerDown(float x, float y)
    {
        if (panelMenuBounds.Contains(x, y))
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { hits[i].Action(); return; }
        }
        panelMenuOpen = false;
    }

    private void DrawTimingSnap(ICanvas c, Rect r)
    {
        timingSnapBounds = r;
        int value = TimingSetupVisible ? timingSnap : divisor;
        c.Text(L.Get("timing.snapDivisor", value), r.X, r.Y, 12, Foreground, r.Width);
        float left = r.X + 8, length = r.Width - 16, cy = r.Y + 28;
        c.Line(left, cy, left + length, cy, Muted, 3);
        for (int i = 0; i < SnapDivisors.Length; i++) c.Line(left + length * i / (SnapDivisors.Length - 1), cy - 4, left + length * i / (SnapDivisors.Length - 1), cy + 4, Grid);
        c.Circle(left + length * Math.Max(0, Array.IndexOf(SnapDivisors, value)) / (SnapDivisors.Length - 1), cy, 6, Accent);
        hits.Add(new(r, () => { timingSnapDragging = true; SetTimingSnap(mouseX); }, true));
    }

    private void SetTimingSnap(float x)
    {
        int index = (int)Math.Round(Math.Clamp((x - timingSnapBounds.X - 8) / (timingSnapBounds.Width - 16), 0, 1) * (SnapDivisors.Length - 1));
        if (TimingSetupVisible) timingSnap = SnapDivisors[index];
        else { divisor = SnapDivisors[index]; ResetHitsounds(); }
    }

    private void DrawTimingPage(ICanvas c)
    {
        timingFields.Clear();
        var r = new Rect(rightPanel.X + 14, rightPanel.Y + 46, rightPanel.Width - 28, rightPanel.Height - 52);
        bool compact = r.Height < 540;
        float y = r.Y, gap = compact ? 5 : 12, row = compact ? 28 : 34;
        var state = TimingMap.At(Document, playhead);
        if (timingResetPoint is not null && !ReferenceEquals(timingResetDocument, Document)) timingResetPoint = null;
        int count = Math.Min(16, state.Meter);
        double beat = (playhead - state.OffsetMs) / state.BeatLengthMs;
        int active = ((int)Math.Floor(beat) % state.Meter + state.Meter) % state.Meter;
        float lampX = r.X + 82, lampWidth = (r.Width - 82) / count;
        for (int i = 0; i < count; i++)
        {
            var lamp = new Rect(lampX + i * lampWidth, y, lampWidth - 4, row);
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "ui", "timing", "controls.png"), lamp, source: new Rect(80, 962, 1096, 192));
            if (AudioPlaying && i == active && beat * MetronomeSubdivision % 1 < .35) c.Fill(lamp, i == 0 ? Gold : Accent, 4, .8f);
        }
        TimingButton(c, new(r.X, y, 74, row * 2 + gap), L.Get("timing.tapReset"), () => timingTaps.Clear(), 2);
        y += row + gap;
        TimingButton(c, new(lampX, y, r.Width - 82, row), L.Get("timing.tap"), TapTiming, 1); y += row + gap;
        TimingButton(c, new(r.X, y, r.Width, row), timingTaps.Count >= 2 ? L.Get("timing.tapResult", 60000 * (timingTaps.Count - 1) / (timingTaps[^1] - timingTaps[0]), timingTaps.Count) : L.Get("timing.tapApply"), ApplyTappedTiming, enabled: timingTaps.Count >= 2); y += row + gap;
        void Number(string key, double value, Action<double> apply)
        {
            c.Text(L.Get($"timing.{key}"), r.X, y + 7, 12, Foreground, 95);
            var input = new Rect(r.X + 100, y, r.Width - 100, row);
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "ui", "timing", "controls.png"), input, source: new Rect(80, 80, 1096, 192));
            TimingNumber(c, "page." + key, new(input.X + 26, y, input.Width - 52, row), TimingN(TimingPageValue(key, value)), apply);
            void Step(int direction)
            {
                var current = TimingMap.At(Document, playhead);
                double number = key == "bpm" ? 60000 / (timingResetPoint?.BeatLengthMs ?? current.BeatLengthMs) : key == "offset" ? timingResetPoint?.TimeMs ?? current.OffsetMs : Document.SliderTickRate;
                double step = key == "bpm" ? placementCtrl ? .25 : timingPointerShift ? 5 : 1 : key == "offset" ? placementCtrl ? 1 : timingPointerShift ? 10 : 2 : 1;
                try { apply(TimingPageValue(key, number) + direction * step); } catch (ArgumentException ex) { timingError = ex.Message; }
            }
            TimingButton(c, new(input.X, y, 24, row), "‹", () => Step(-1));
            TimingButton(c, new(input.Right - 24, y, 24, row), "›", () => Step(1));
            y += row + gap;
        }
        Number("bpm", 60000 / (timingResetPoint?.BeatLengthMs ?? state.BeatLengthMs), v => ChangeCurrentRed("bpm", v));
        Number("offset", timingResetPoint?.TimeMs ?? state.OffsetMs, v => ChangeCurrentRed("offset", v));
        TimingCheck(c, new(r.X, y, r.Width, row), "timing.moveNotes", timingMoveNotes, () => timingMoveNotes = !timingMoveNotes); y += row + gap;
        Number("tickRate", Document.SliderTickRate, v => { if (v < .5 || v > 8) throw new ArgumentException(L.Get("timing.range")); Edit(L.Get("timing.edit"), () => Document.SliderTickRate = v); });
        TimingCheck(c, new(r.X, y, r.Width, row), "timing.metronome", metronomeEnabled, () => { metronomeEnabled = !metronomeEnabled; ResetHitsounds(); }); y += row + gap;
        TimingButton(c, new(r.X, y, r.Width, row), L.Get("timing.setup"), OpenTimingSetup); y += row + gap;
        c.Text(timingError, r.X, y, 11, Error, r.Width);
    }
}
