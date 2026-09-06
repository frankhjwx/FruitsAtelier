using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class ClipboardMultiTests
{
    public static void MixedBatchPreservesSnapshotAndOrder()
    {
        var ui = new Ui();
        var map = MixedMap();
        ui.LoadDocument(map);
        var originals = Parents(ui.View.Document).OrderBy(p => p.Time).ThenBy(p => p.Order).ToArray();
        var oldIds = originals.Select(p => p.Id).ToHashSet();
        Select(ui, originals.Reverse().Select(p => p.Id));
        Check(ui.View.CopySelection() && !ui.View.IsDirty, "Batch copy changed the document or rejected a mixed selection.");
        ui.View.Document.Fruits.Single(f => f.TimeMs == 800).X = 444;
        ui.View.Document.Tracks[0].Nodes[1].X = 250;
        var before = ui.View.Document.DeepClone();
        ui.View.UpdateTransport(3000.25, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), ui.View.StatusMessage);
        var pasted = Parents(ui.View.Document).Where(p => !oldIds.Contains(p.Id)).OrderBy(p => p.Order).ToArray();
        Check(pasted.Length == originals.Length && Selected(ui).SetEquals(pasted.Select(p => p.Id)), "Paste did not select the complete new batch.");
        for (int i = 0; i < originals.Length; i++)
        {
            Near(3000.25 + originals[i].Time - 800, pasted[i].Time);
            Check(originals[i].Kind == pasted[i].Kind, "Same-time parent source order was lost between object kinds.");
            Check(pasted[i].Order < int.MaxValue && (i == 0 || pasted[i].Order == pasted[i - 1].Order + 1),
                "Pasted source order was not assigned consistently across the batch.");
        }
        var fruit = ui.View.Document.Fruits.Single(f => !oldIds.Contains(f.Id) && f.TimeMs == 3000.25);
        Near(64, fruit.X);
        var track = ui.View.Document.Tracks.Single(t => !oldIds.Contains(t.Id));
        Near(3200.25, track.Nodes[0].TimeMs); Near(3700.25, track.Nodes[1].TimeMs);
        Near(4200.25, CurveMath.EndTimeMs(track)); Near(200, track.Nodes[1].X);
        Check(track.SpanCount == 2 && track.CompensateTinyDroplets == false
            && track.Nodes[0].OutgoingKind == CurveKind.Bezier && track.Nodes[0].HandleOut == new MapPoint(100, 20)
            && !track.Nodes.Any(n => map.Tracks[0].Nodes.Any(old => old.Id == n.Id)), "Track geometry, repeat policy or node identities changed.");
        var slider = ui.View.Document.ImportedSliders.Single(s => !oldIds.Contains(s.Id));
        var shower = ui.View.Document.BananaShowers.Single(s => !oldIds.Contains(s.Id));
        Check(slider.SpanCount == 2 && slider.ControlPoints.SequenceEqual(map.ImportedSliders[0].ControlPoints)
            && !ReferenceEquals(slider.ControlPoints, ui.View.Document.ImportedSliders[0].ControlPoints)
            && slider.OriginalLine!.Contains(",3200.25,6,2,", StringComparison.Ordinal)
            && slider.OriginalLine.EndsWith("2|4|8,1:2|2:3|3:4,2:3:4:70:clap.wav", StringComparison.Ordinal),
            "Imported path or sample metadata did not survive the batch paste.");
        Near(3500.25, shower.EndTimeMs);
        Check(shower.OriginalLine!.Contains(",3200.25,12,8,3500.25,", StringComparison.Ordinal)
            && shower.OriginalLine.EndsWith("2:3:4:60:banana.wav", StringComparison.Ordinal), "Banana range or sample metadata was lost.");
        var after = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "One undo did not remove the whole batch without altering its originals.");
        ui.Key('Y', ctrl: true);
        Check(ui.View.Document.ContentEquals(after), "Redo changed pasted IDs or restored only part of the batch.");
        Check(ui.View.PasteSelection(), ui.View.StatusMessage);
        Check(Parents(ui.View.Document).Select(p => p.Id).Distinct().Count() == originals.Length * 3,
            "A repeated batch paste reused parent identities.");
    }

    public static void MixedCutIsOneTransaction()
    {
        var ui = new Ui();
        ui.LoadDocument(MixedMap());
        var before = ui.View.Document.DeepClone();
        Guid untouched = ui.View.Document.Fruits.Single(f => f.TimeMs == 800).Id;
        var ids = Parents(ui.View.Document).Select(p => p.Id).Where(id => id != untouched).ToArray();
        Select(ui, ids);
        Check(ui.View.CutSelection(), ui.View.StatusMessage);
        Check(Parents(ui.View.Document).Single().Id == untouched && Selected(ui).Count == 0 && ui.View.CanPasteSelection,
            "Cut did not remove every selected parent or also removed the unselected fruit.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "One undo did not restore every cut object and its identity.");
        ui.Key('Y', ctrl: true);
        Check(Parents(ui.View.Document).Single().Id == untouched, "Redo did not cut the complete batch.");
        ui.View.UpdateTransport(4000, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection() && Parents(ui.View.Document).Count() == ids.Length + 1,
            "Cut did not retain all objects in the clipboard.");
    }

    public static void OverflowPasteRollsBackBatch()
    {
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 50 });
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 2 };
        track.Nodes.Add(new() { TimeMs = 1100, X = 100 });
        track.Nodes.Add(new() { TimeMs = 2100, X = 200 });
        map.Tracks.Add(track);
        ui.LoadDocument(map);
        Select(ui, Parents(map).Select(p => p.Id));
        Check(ui.View.CopySelection(), "Overflow fixture copy failed.");
        var before = ui.View.Document.DeepClone();
        var selectedBefore = Selected(ui).ToHashSet();
        ui.View.UpdateTransport(int.MaxValue - 200, int.MaxValue, true, false, false, null, "fixture.wav");
        Check(!ui.View.PasteSelection() && ui.View.Document.ContentEquals(before) && !ui.View.IsDirty
            && Selected(ui).SetEquals(selectedBefore), "A later overflowing parent caused a partial paste, history change or selection loss.");
        ui.View.UpdateTransport(4000, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), "Failed paste destroyed the batch clipboard.");
        Near(6100, ui.View.Document.DurationMs);
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "One undo after a rejected paste did not restore the map and duration.");
    }

    public static void InvalidMemberRejectsWholeCopyAndCut()
    {
        var ui = new Ui();
        ui.LoadDocument(MixedMap());
        Select(ui, Parents(ui.View.Document).Select(p => p.Id));
        Check(ui.View.CopySelection(), "Valid initial batch was rejected.");
        ui.View.Document.ImportedSliders[0].SpanCount = 0;
        var invalid = ui.View.Document.DeepClone();
        Check(!ui.View.CopySelection() && !ui.View.CutSelection() && ui.View.Document.ContentEquals(invalid),
            "Invalid member allowed a partial copy or cut of the valid members.");
        ui.View.Document.ImportedSliders[0].SpanCount = 2;
        ui.View.UpdateTransport(4000, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection() && Parents(ui.View.Document).Count() == 10,
            "A rejected copy replaced the previously valid clipboard snapshot.");
    }

    private static MapDocument MixedMap()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n256,192,1000,12,8,1300,2:3:4:60:banana.wav\n160,192,1000,6,2,B|220:250|300:192,2,200,2|4|8,1:2|2:3|3:4,2:3:4:70:clap.wav\n320,192,1000,5,2,2:3:4:70:fruit.wav\n64,192,800,1,0,0:0:0:0:\n");
        var track = new CurveTrack { Name = "Mixed clipboard", SpanCount = 2, SourceOrder = int.MaxValue, CompensateTinyDroplets = false };
        track.Nodes.Add(new() { TimeMs = 1000, X = 100, HandleOut = new(100, 20), OutgoingKind = CurveKind.Bezier });
        track.Nodes.Add(new() { TimeMs = 1500, X = 200, HandleIn = new(-100, -20) });
        map.Tracks.Add(track);
        return map;
    }

    private static IEnumerable<(Guid Id, double Time, int Order, string Kind)> Parents(MapDocument map)
        => map.Fruits.Select(f => (f.Id, f.TimeMs, f.SourceOrder, "Fruit"))
            .Concat(map.Tracks.Select(t => (t.Id, t.Nodes[0].TimeMs, t.SourceOrder, "Track")))
            .Concat(map.ImportedSliders.Select(s => (s.Id, s.TimeMs, s.SourceOrder, "Imported")))
            .Concat(map.BananaShowers.Select(s => (s.Id, s.TimeMs, s.SourceOrder, "Banana")));

    private static void Select(Ui ui, IEnumerable<Guid> ids)
    {
        var selected = ids.ToArray();
        var method = typeof(EditorView).GetMethod("SelectObjects", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SelectObjects is unavailable.");
        method.Invoke(ui.View, [selected, selected[0]]);
    }

    private static HashSet<Guid> Selected(Ui ui)
        => (HashSet<Guid>)(typeof(EditorView).GetField("objectSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ui.View) ?? throw new InvalidOperationException("Object selection is unavailable."));

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double expected, double actual)
    { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.000001) throw new Exception($"Expected {expected:R}, got {actual:R}."); }
}
