using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LegacyAlignmentTests
{
    public static void ClipboardAndGrid()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.AddRange([
            new() { TimeMs = 1000, X = 100 },
            new() { TimeMs = 1250, X = 160, OriginalLine = "160,192,1250,5,0,0:0:0:0:" },
            new() { TimeMs = 1500, X = 230 }
        ]);
        var ui = new Ui(); ui.LoadDocument(map);
        string? copied = null;
        ui.View.RequestCopyText = text => copied = text;
        ui.ClickMap(1250, 160); ui.ClickMap(1500, 230, ctrl: true);
        Check(ui.View.CopySelection() && copied == "00:01:250 (1,2) - ", "Clipboard uses combo numbers and legacy timestamps");
        ui.View.UpdateTransport(3000, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), "Same-map paste succeeds");
        Check(ui.View.Document.Fruits.Any(f => f.TimeMs == 3000 && f.X == 160)
            && ui.View.Document.Fruits.Any(f => f.TimeMs == 3250 && f.X == 230), "Paste preserves the pattern's relative time and X");
        ui.View.AddDifficulty();
        Check(!ui.View.CanPasteSelection && !ui.View.PasteSelection(), "Other difficulties reject the pattern");
        ui.View.SwitchDifficulty(0);
        Check(ui.View.CanPasteSelection, "Returning to the source difficulty retains the pattern");
        ui.View.LoadDocument(map);
        Check(!ui.View.CanPasteSelection, "Reopening a map does not reuse another session's clipboard");

        ui.LoadDocument(new MapDocument { DurationMs = 10000 });
        ui.Key('T'); ui.Key('F');
        foreach (int spacing in new[] { 4, 8, 16, 32 })
        {
            ui.ClickMap(1000 + spacing * 125, 139);
            var fruit = ui.View.Document.Fruits[^1];
            Check(fruit.X == Math.Round(139d / spacing, MidpointRounding.AwayFromZero) * spacing, "Grid uses osu pixel spacing");
            Check(fruit.TimeMs == 1000 + spacing * 125, "Horizontal grid leaves beat snap independent");
            ui.Key('G');
        }
    }

    public static void TimestampAndReadOnly()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.Fruits.Add(new() { TimeMs = 1000.75, X = 200 });
        var ui = new Ui(); ui.LoadDocument(map); ui.ClickMap(1000.75, 200);
        double ar = ui.View.Document.ApproachRate, cs = ui.View.Document.CircleSize;
        var details = ui.Canvas.Texts.Single(t => t.Value.StartsWith("AR ") && !t.Value.Contains("NM"));
        ui.Click(details.X + 10, details.Y + 3); ui.Type("1"); ui.Key(13);
        Check(ui.View.Document.ApproachRate == ar && ui.View.Document.CircleSize == cs && !ui.View.IsDirty, "Details AR/CS are read-only");
        ui.FocusField(L.Get("ui.timeField")); ui.Key(13);
        Check(ui.View.Document.Fruits[0].TimeMs == 1000.75 && !ui.View.IsDirty, "Opening a timestamp does not quantize stored precision");
        ui.SetField(L.Get("ui.timeField"), "00:02:123");
        Check(ui.View.Document.Fruits[0].TimeMs == 2123, "Timestamp input parses milliseconds");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits[0].TimeMs == 1000.75, "Timestamp edits are undoable");
        ui.View.UpdateTransport(59999.9, 100000, true, false, false, null, "fixture.wav"); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "00:59:999"), "Timestamp truncation does not round into the next minute");
    }

    public static void Samples()
    {
        var map = new MapDocument();
        map.TimingPoints.Add(new() { TimeMs = 0, SampleSet = 1, Volume = 80 });
        map.TimingPoints.Add(new() { TimeMs = 1000, SampleSet = 2, Volume = 40 });
        var slider = new ImportedSlider { TimeMs = 100, OriginalLine = "100,192,100,2,0,L|200:192,1,100,0|0,0:0|0:0,0:0:0:0:" };
        map.ImportedSliders.Add(slider);
        var edge = new ConvertedCatchObject(slider.Id, 0, CatchObjectKind.Fruit, 995, 100, 100, 100, 0, false);
        var resolver = new HitsoundResolver(map, [edge]);
        Check(resolver.Resolve(edge).Single().SampleSet == 2, "Edge sample applies the inclusive 5 ms boundary");
        Check(resolver.Resolve(edge with { TimeMs = 994.999 }).Single().SampleSet == 1, "Boundary does not extend beyond 5 ms");
        var tick = resolver.Resolve(edge with { Kind = CatchObjectKind.Droplet, TimeMs = 1100 }).Single();
        Check(tick.SampleSet == 1 && tick.Volume == .8f, "Ticks retain the slider start bank and volume across timing changes");
        slider.TimeMs = 994;
        resolver = new(map, [edge]);
        Check(resolver.Resolve(edge with { Kind = CatchObjectKind.Droplet }).Single().SampleSet == 2, "Slider body uses 6 ms sample leniency");
    }

    public static int InspectMap(string path)
    {
        var map = OsuBeatmapReader.ReadFile(path);
        var converted = CatchStreamConverter.Convert(map);
        Check(converted.Diagnostics.Count == 0, string.Join("; ", converted.Diagnostics));
        var resolver = new HitsoundResolver(map, converted.Objects);
        int changed = 0, checkedEvents = 0;
        foreach (var item in converted.Objects.Where(o => o.TimeMs < 40000 && o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet))
        {
            double lookup = item.Kind == CatchObjectKind.Droplet
                ? map.ImportedSliders.Single(s => s.Id == item.SourceId).TimeMs + 6 : item.TimeMs + 5;
            var old = map.TimingPoints.LastOrDefault(p => p.TimeMs <= item.TimeMs);
            var expected = map.TimingPoints.LastOrDefault(p => p.TimeMs <= lookup);
            var samples = resolver.Resolve(item);
            if (expected is not null && old is not null && (old.SampleIndex != expected.SampleIndex || old.Volume != expected.Volume))
            {
                changed++;
                Console.WriteLine($"{item.Kind} {item.TimeMs:F6} ms: sample {old.SampleIndex}/{old.Volume}% -> {expected.SampleIndex}/{expected.Volume}%; {string.Join(", ", samples.Select(s => Path.GetFileName(s.FilePath)))}");
            }
            checkedEvents++;
        }
        Console.WriteLine($"First 40 seconds: {checkedEvents} audible objects checked; {changed} sample-boundary corrections; source file unchanged.");
        return 0;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
