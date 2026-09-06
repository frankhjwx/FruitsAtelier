using FruitsAtelier.Core;

internal static class ClipboardTests
{
    public static void FruitSnapshotAndHistory()
    {
        var ui = new Ui();
        var map = new MapDocument { DurationMs = 5000 };
        var fruit = new Fruit { TimeMs = 1000, X = 160, SourceOrder = 3, OriginalLine = "160,192,1000,5,2,2:3:4:70:clap.wav" };
        map.Fruits.Add(fruit); ui.LoadDocument(map); ui.Paint();
        ui.ClickMap(1000, 160);
        Check(ui.View.CanCopySelection && ui.View.CopySelection(), "Selected fruit could not be copied.");
        Check(!ui.View.IsDirty && ui.View.Document.Fruits.Count == 1, "Copy changed the document.");
        ui.View.Document.Fruits[0].X = 200;
        ui.View.UpdateTransport(2250.25, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), ui.View.StatusMessage);
        var pasted = ui.View.Document.Fruits.Single(f => f.Id != fruit.Id);
        Guid pastedId = pasted.Id;
        Near(2250.25, pasted.TimeMs); Near(160, pasted.X);
        Check(pasted.SourceOrder > fruit.SourceOrder && pasted.OriginalLine == fruit.OriginalLine, "Fruit metadata or new source order was lost.");
        var exported = OsuBeatmapWriter.Serialize(ui.View.Document, false).ReadBack.Fruits.Single(f => f.TimeMs != 1000);
        Near(2250, exported.TimeMs);
        Check(exported.OriginalLine!.EndsWith("5,2,2:3:4:70:clap.wav", StringComparison.Ordinal), "Pasted fruit lost flags or hit samples.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Fruits.Count == 1 && ui.View.Document.Fruits[0].X == 200, "Paste undo altered the source fruit.");
        ui.Key('Y', ctrl: true);
        Check(ui.View.Document.Fruits.Single(f => f.Id != fruit.Id).Id == pastedId, "Redo generated a different pasted identity.");
        Check(ui.View.PasteSelection(), ui.View.StatusMessage);
        Check(ui.View.Document.Fruits.Select(f => f.Id).Distinct().Count() == 3, "Repeated paste reused an object ID.");
    }

    public static void AnchorCopiesAndCutsParent()
    {
        var ui = new Ui();
        var track = new CurveTrack { Name = "Clipboard slider", SpanCount = 2, SourceOrder = 4, CompensateTinyDroplets = false };
        track.Nodes.Add(new() { TimeMs = 1000, X = 100, HandleOut = new(100, 30), OutgoingKind = CurveKind.Bezier });
        track.Nodes.Add(new() { TimeMs = 1500, X = 240, HandleIn = new(-100, -40), HandleOut = new(100, 50), OutgoingKind = CurveKind.Linear });
        track.Nodes.Add(new() { TimeMs = 2000, X = 300, HandleIn = new(-100, -20) });
        var map = new MapDocument { DurationMs = 3200 }; map.Tracks.Add(track);
        ui.LoadDocument(map); ui.Paint(); ui.ClickText("锚点 2   1500");
        Check(ui.View.CopySelection(), "Selecting an anchor did not copy its parent track.");
        ui.View.Document.Tracks[0].Nodes[1].X = 250;
        ui.View.UpdateTransport(2500, 6000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), ui.View.StatusMessage);
        var pasted = ui.View.Document.Tracks.Single(t => t.Id != track.Id);
        Near(2500, pasted.Nodes[0].TimeMs); Near(3000, pasted.Nodes[1].TimeMs); Near(4500, CurveMath.EndTimeMs(pasted));
        Near(4500, ui.View.Document.DurationMs); Near(240, pasted.Nodes[1].X);
        Check(pasted.SpanCount == 2 && pasted.CompensateTinyDroplets == false
            && pasted.Nodes[0].HandleOut == new MapPoint(100, 30)
            && pasted.Nodes[1].OutgoingKind == CurveKind.Linear, "Paste lost repeat, handles, segment kind or tiny policy.");
        Check(!pasted.Nodes.Any(n => track.Nodes.Any(original => original.Id == n.Id)), "Pasted anchors reused source IDs.");
        ui.Key('Z', ctrl: true);
        Near(3200, ui.View.Document.DurationMs);
        Check(ui.View.Document.Tracks.Count == 1, "Paste undo retained the clone.");

        ui.View.UpdateTransport(0, 6000, true, false, false, null, "fixture.wav"); ui.Paint();
        ui.ClickText("锚点 2   1500");
        Check(ui.View.CutSelection(), ui.View.StatusMessage);
        Check(ui.View.Document.Tracks.Count == 0 && ui.View.CanPasteSelection, "Cut deleted only the anchor or lost the clipboard.");
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.Tracks.Single().Id == track.Id && ui.View.Document.Tracks[0].Nodes.Count == 3,
            "Cut undo did not restore the whole slider.");
        ui.Key('Y', ctrl: true);
        Check(ui.View.Document.Tracks.Count == 0, "Cut redo did not remove the whole slider.");
    }

    public static void ImportedAndBananaMetadata()
    {
        var ui = new Ui();
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n3000,250,4,1,0,100,1,0\n[HitObjects]\n160,192,1000,6,2,B|220:250|300:192,2,200,2|4|8,1:2|2:3|3:4,2:3:4:70:clap.wav\n256,192,6000,12,8,6500,2:3:4:60:banana.wav\n");
        var source = map.ImportedSliders.Single(); var shower = map.BananaShowers.Single();
        ui.LoadDocument(map); ui.Paint(); ui.ClickText("Legacy Slider  1000");
        Check(ui.View.CopySelection(), "Imported slider copy failed.");
        ui.View.UpdateTransport(4000.5, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), ui.View.StatusMessage);
        var pasted = ui.View.Document.ImportedSliders.Single(s => s.Id != source.Id);
        Near(4000.5, pasted.TimeMs);
        Check(pasted.SpanCount == 2 && pasted.PathType == source.PathType && pasted.ControlPoints.SequenceEqual(source.ControlPoints)
            && !ReferenceEquals(pasted.ControlPoints, ui.View.Document.ImportedSliders[0].ControlPoints), "Imported paste changed or shared its path.");
        var output = OsuBeatmapWriter.Serialize(ui.View.Document, false).ReadBack.ImportedSliders.Single(s => s.TimeMs > 3000);
        Near(4000.5, output.TimeMs);
        Check(output.OriginalLine!.EndsWith("2|4|8,1:2|2:3|3:4,2:3:4:70:clap.wav", StringComparison.Ordinal), "Imported paste lost edge or hit samples.");

        ui.View.UpdateTransport(0, 10000, true, false, false, null, "fixture.wav"); ui.Paint();
        ui.ClickText("香蕉雨  6000");
        Check(ui.View.CopySelection(), "Banana shower copy failed.");
        ui.View.UpdateTransport(7000.25, 10000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), ui.View.StatusMessage);
        var pastedShower = ui.View.Document.BananaShowers.Single(s => s.Id != shower.Id);
        Near(7000.25, pastedShower.TimeMs); Near(7500.25, pastedShower.EndTimeMs);
        var readBack = OsuBeatmapWriter.Serialize(ui.View.Document, false).ReadBack.BananaShowers.Single(s => s.TimeMs > 6500);
        Near(7500.25, readBack.EndTimeMs);
        Check(readBack.OriginalLine!.EndsWith("2:3:4:60:banana.wav", StringComparison.Ordinal), "Banana paste lost its sample metadata.");
    }

    public static void ClipboardBoundaries()
    {
        var ui = new Ui();
        ui.LoadDocument(new MapDocument { DurationMs = 5000 }); ui.Paint();
        Check(!ui.View.CanPasteSelection && !ui.View.PasteSelection() && !ui.View.CanCopySelection,
            "Empty clipboard or selection was accepted.");
        ui.Key('B'); ui.ClickMap(1000, 100);
        Check(!ui.View.CanCopySelection && !ui.View.CopySelection() && !ui.View.CutSelection(), "An unfinished slider entered the clipboard.");
        ui.Key(27);
        var map = new MapDocument { DurationMs = 5000 };
        var track = new CurveTrack { Kind = CurveKind.Linear, Name = "Overflow slider" };
        track.Nodes.Add(new() { TimeMs = 1000, X = 100 }); track.Nodes.Add(new() { TimeMs = 2000, X = 100 }); map.Tracks.Add(track);
        ui.LoadDocument(map); ui.Paint(); ui.ClickText("Overflow slider");
        Check(ui.View.CopySelection(), "Completed slider could not be copied.");
        var before = ui.View.Document.DeepClone();
        ui.View.UpdateTransport(int.MaxValue - 100, int.MaxValue, true, false, false, null, "fixture.wav");
        Check(!ui.View.PasteSelection() && ui.View.Document.ContentEquals(before), "Overflow paste changed the map or silently shortened the slider.");
        ui.View.UpdateTransport(0, 5000, true, false, false, null, "fixture.wav");
        Check(ui.View.PasteSelection(), "A failed paste destroyed the clipboard.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double expected, double actual)
    { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.000001) throw new Exception($"Expected {expected:R}, got {actual:R}."); }
}
