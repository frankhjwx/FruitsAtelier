using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ObjectStructureTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance() => ticks += 1000;
    }

    public static void StreamsAndAnchors()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var clock = new Clock(); var ui = new Ui(timeProvider: clock);
                var map = new MapDocument();
                var track = Line(1000, 1500, 100, 240);
                track.Nodes.Insert(1, new Anchor { TimeMs = 1250, X = 180 });
                map.Tracks.Add(track);
                ui.LoadDocument(map); ui.SelectTrack(track.Id);
                ui.Key('F', ctrl: true, shift: true);
                Check(!ui.View.StreamBreakIntoFruits, "initial conversion defaults to a managed stream");
                ui.ClickText(L.Get("stream.breakFruits")); ui.Key(13);
                Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.Fruits.Count == 5, "initial conversion can create separate fruits");
                Check(ui.View.SelectedObjectIds.Count == 5, "all broken fruits are selected");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "breaking during conversion has one undo step");
                ui.SelectTrack(track.Id); ui.Key('F', ctrl: true, shift: true);
                Check(!ui.View.StreamBreakIntoFruits, "toggle resets on every opening");
                ui.Key(13);
                var stream = ui.View.Document.DeepClone();
                var expected = OsuBeatmapWriter.Serialize(stream).Text;
                ui.HoldMap(1000, 100, clock.Advance);
                var change = ui.Canvas.Texts.Single(t => t.Value == L.Get("conversion.title"));
                var breakLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("stream.breakFruits"));
                Check(breakLabel.Y > change.Y && breakLabel.Y - change.Y < 40, "break is immediately below Convert to Stream/Stack");
                ui.ClickText(L.Get("stream.breakFruits"));
                Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.Fruits.Count == 5, "long press breaks an existing stream");
                Check(OsuBeatmapWriter.Serialize(ui.View.Document).Text == expected, "breaking retains exported positions, samples and flags");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(stream), "undo restores stream geometry");
                ui.SelectTrack(track.Id); ui.Key('F', ctrl: true, shift: true);
                ui.ClickText(L.Get("stream.breakFruits")); ui.Key(13);
                Check(ui.View.Document.Tracks.Count == 0, "Change snapping also supports breaking");

                var second = Line(2000, 2500, 300, 400);
                second.Nodes.Insert(1, new Anchor { TimeMs = 2250, X = 330 });
                map.Tracks.Add(second); ui.LoadDocument(map); ui.Key('A', ctrl: true);
                ui.HoldMap(1000, 100, clock.Advance);
                Check(ui.View.SelectedObjectIds.Count == 2, "holding a selected slider retains the batch");
                ui.ClickText(L.Get("slider.clearInternal"));
                Check(ui.View.Document.Tracks.All(t => t.Nodes.Count == 2), "menu clears the internal anchors of both sliders");
                Check(ui.View.Document.Tracks.All(t => t.Nodes[0].Id == map.Tracks.First(s => s.Id == t.Id).Nodes[0].Id
                    && t.Nodes[^1].Id == map.Tracks.First(s => s.Id == t.Id).Nodes[^1].Id), "endpoint identities are retained");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "batch clear has one undo step");
                ui.Key('A', ctrl: true); ui.Key('A', ctrl: true, shift: true);
                Check(ui.View.Document.Tracks.All(t => t.Nodes.Count == 2), "clear shortcut applies to all selected sliders");
            }
        }
        finally { L.SetLanguage(language); }
    }

    public static void MergeUi()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var clock = new Clock(); var ui = new Ui(timeProvider: clock);
                var map = new MapDocument();
                map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 1500, X = 280 }, new Fruit { TimeMs = 2000, X = 150 }]);
                ui.LoadDocument(map); ui.Key('A', ctrl: true);
                ui.HoldMap(1500, 280, clock.Advance);
                Check(ui.View.SelectedObjectIds.Count == 3 && ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.title")), "multi-circle hold offers merging without losing selection");
                ui.ClickText(L.Get("merge.title"));
                Check(ui.View.MergeDialogVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.linear"))
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.curved"))
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.saveWarning")), "dialog offers both paths and the save warning");
                ui.Key(46); ui.Key('Z', ctrl: true); ui.Key(116); ui.View.TextInput('1'); ui.View.Wheel(0, 0, 120, false, false);
                Check(ui.View.MergeDialogVisible && ui.View.Document.ContentEquals(map) && !ui.View.IsTestplaying, "modal blocks editing and testplay");
                ui.Key(27); Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "cancelling merge leaves content unchanged");
                ui.Key('M', ctrl: true, shift: true); ui.ClickText(L.Get("merge.curved")); ui.Key(13);
                var merged = ui.View.Document.Tracks.Single();
                Check(ui.View.Document.Fruits.Count == 0 && merged.Nodes.Count == 3
                    && CurveMath.SegmentKind(merged, 0) == CurveKind.Bezier, "curve merge passes through selected circles");
                var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
                Check(saved.Fruits.Count == 0 && saved.Tracks.Count == 1, "saved merge retains only the resulting slider");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "merge undo restores originals");
                ui.Key('A', ctrl: true); ui.Key('M', ctrl: true, shift: true); ui.Key(13);
                Check(ui.View.Document.Tracks.Single().Nodes.All(n => n.HandleIn == default && n.HandleOut == default), "straight choice uses linear segments");
                ui.LoadDocument(map); ui.ClickFruit(map.Fruits[0].Id); ui.Key('M', ctrl: true, shift: true);
                Check(!ui.View.MergeDialogVisible, "one circle cannot merge");
                ui.ClickMap(2000, 150, ctrl: true); ui.HoldMap(1000, 100, clock.Advance);
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.title")), "a skipped circle hides the merge action");
                ui.Key('M', ctrl: true, shift: true);
                Check(!ui.View.MergeDialogVisible && ui.View.StatusMessage == L.Get("merge.notConsecutive"), "shortcut explains nonconsecutive selection");
                map.BananaShowers.Add(new BananaShower { TimeMs = 3000, EndTimeMs = 3500 });
                ui.LoadDocument(map); ui.Key('A', ctrl: true); ui.HoldMap(1000, 100, clock.Advance);
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.title")), "selected banana hides merge");
                ui.Key('M', ctrl: true, shift: true);
                Check(!ui.View.MergeDialogVisible && ui.View.StatusMessage == L.Get("merge.bananaSelected"), "selected banana blocks shortcut");
                map.BananaShowers.Clear(); map.Fruits[1].TimeMs = 1000;
                ui.LoadDocument(map); ui.Key('A', ctrl: true); ui.Key('M', ctrl: true, shift: true); ui.Key(13);
                Check(ui.View.MergeDialogVisible && ui.View.Document.ContentEquals(map) && !ui.View.IsDirty
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.failed", L.Get("merge.sameTime"))), "failed merge explains the cause and rolls back");
            }
        }
        finally { L.SetLanguage(language); }
    }

    public static void MixedPaths()
    {
        var map = new MapDocument();
        var track = Line(1500, 2000, 200, 350);
        track.Nodes[0].OutgoingKind = CurveKind.Bezier;
        track.Nodes[0].HandleOut = new(160, 10); track.Nodes[1].HandleIn = new(-160, -20);
        track.SpanCount = 2;
        map.Tracks.Add(track);
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 100 }, new Fruit { TimeMs = 3000, X = 250 }]);
        var ids = map.Fruits.Select(f => f.Id).Append(track.Id).ToArray();
        Check(!ObjectStructureEditing.MergeSelection(map, ids).AllowsLinear, "curved paths disable straight merge");
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('A', ctrl: true); ui.Key('M', ctrl: true, shift: true);
        Check(ui.View.MergeDialogVisible && !ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.linear")), "curve selection only offers curved merge");
        var before = map.DeepClone();
        var merged = ObjectStructureEditing.Merge(map, ids, true);
        for (double at = 1500; at <= 2500; at += 11)
            Check(Math.Abs(CurveMath.PositionAtTime(merged, at) - CurveMath.PositionAtTime(track, at)) < 1e-6, "merging preserves each repeated curved traversal");
        Check(merged.SpanCount == 1 && map.Fruits.Count == 0 && map.Tracks.Count == 1, "repeats unfold into one path");
        Check(CatchStreamConverter.Convert(map).Success, "merged repeated path converts");
        OsuBeatmapWriter.Serialize(map);

        var overlap = before.DeepClone(); overlap.Fruits[^1].TimeMs = 1750;
        Reject(overlap, overlap.Fruits.Select(f => f.Id).Append(track.Id), "merge.overlap");

        var imported = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n200,192,1500,2,0,L|300:192,1,100\n350,192,2500,1,0,0:0:0:0:\n");
        var importedIds = imported.Fruits.Select(f => f.Id).Concat(imported.ImportedSliders.Select(s => s.Id)).ToArray();
        var legacyMerged = ObjectStructureEditing.Merge(imported, importedIds, false);
        Check(imported.ImportedSliders.Count == 0 && imported.Fruits.Count == 0 && legacyMerged.Nodes.Count >= 4, "circles and imported sliders merge together");
        OsuBeatmapWriter.Serialize(imported);

        var streamMap = new MapDocument { BeatLengthMs = 501 };
        var streamTrack = Line(1000.4, 2000.4, 100.4, 300.7);
        streamTrack.StreamSnapDivisor = 3;
        streamTrack.OriginalLine = "100,192,1000,38,6,L|300:192,1,200,6|2,2:3|1:2,2:3:4:80:custom.wav";
        streamMap.Tracks.Add(streamTrack);
        streamMap.Fruits.Add(new Fruit { TimeMs = 1000.4, X = 400 });
        streamMap.BananaShowers.Add(new BananaShower { TimeMs = 3000, EndTimeMs = 3500 });
        var streamExport = OsuBeatmapWriter.Serialize(streamMap).Text;
        ObjectStructureEditing.BreakStreams(streamMap, [streamTrack.Id]);
        Check(OsuBeatmapWriter.Serialize(streamMap).Text == streamExport, "breaking preserves fractional export, tied fruit order, samples and downstream bananas");

        var exact = before.DeepClone(); var source = exact.Tracks.Single(); source.SpanCount = 1;
        var curve = source.Nodes[0].OutgoingCurve = new ControlCurve { Kind = ControlCurveKind.Bezier };
        curve.Controls.AddRange([new CurveControl { Offset = new(150, 40) }, new CurveControl { Offset = new(350, 110) }]);
        var original = source.DeepClone();
        var exactMerged = ObjectStructureEditing.Merge(exact, exact.Fruits.Select(f => f.Id).Append(source.Id), true);
        for (double at = 1500; at <= 2000; at += 13)
            Check(Math.Abs(CurveMath.PositionAtTime(exactMerged, at) - CurveMath.PositionAtTime(original, at)) < 1e-7, "exact controls survive merging");

        source = original.DeepClone(); source.Nodes.Insert(1, new Anchor { TimeMs = 1750, X = 260 }); source.Nodes[0].OutgoingCurve = null;
        ObjectStructureEditing.ClearInternalAnchors(source);
        Check(source.Nodes.Count == 2, "clearing removes all internal anchors");
    }

    private static CurveTrack Line(double start, double end, double x, double tail)
    {
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new Anchor { TimeMs = start, X = x }, new Anchor { TimeMs = end, X = tail }]);
        return track;
    }
    private static void Reject(MapDocument map, IEnumerable<Guid> ids, string key)
    {
        var before = map.DeepClone();
        try { ObjectStructureEditing.Merge(map, ids, true); throw new Exception("merge should fail"); }
        catch (InvalidOperationException ex) { Check(ex.Message == L.Get(key), "failure has a specific reason"); }
        Check(map.ContentEquals(before), "failed core merge leaves the document untouched");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
