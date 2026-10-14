using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class InspectorPanelsTests
{
    public static void Run()
    {
        string locale = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(language);
                var map = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.92, BeatLengthMs = 500,
                    DerandomizeFSliderDroplets = false, RandomizeDropletStrength = 20 };
                var track = new CurveTrack { Kind = CurveKind.Linear, DropletRandomization = new() { Enabled = true } };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 3000, X = 300 }]);
                map.Tracks.Add(track);
                var ui = new Ui(false); ui.Resize(1440, 900); ui.LoadDocument(map); ui.SelectTrack(track.Id);
                ui.View.ShowSliderInspector(); ui.Paint();
                var exported = OsuBeatmapWriter.Serialize(map);
                var objects = exported.PlayableObjects.Where(o => o.SourceId == track.Id).ToArray();
                Check(objects.Any(o => o.Kind == CatchObjectKind.TinyDroplet), "Inspector fixture includes tiny droplets");
                double expected = objects.Zip(objects.Skip(1), (a, b) => DistanceSnap.Ratio(new(a.TimeMs, a.X), new(b.TimeMs, b.X), 192d / 500)).Max() ?? 0;
                Check(Math.Abs(ui.View.InspectorMaxDs!.Value - expected) < 1e-9 && Math.Abs(ui.View.InspectorPeakRatio!.Value - expected) < 1e-9,
                    "Peak horizontal speed and max DS include exported tiny droplets and use base DPB");
                Check(ui.Canvas.Texts.Any(t => t.Value == "00:01:000") && ui.Canvas.Texts.Any(t => t.Value == "00:03:000"), "Inspector timestamps use osu formatting");
                var startLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("inspector.start"));
                var endLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("inspector.end"));
                Check(startLabel.Y == endLabel.Y && startLabel.X < endLabel.X, "Start and end share a horizontal row");
                var viewport = ui.View.SliderInspectorViewport;
                ui.View.Wheel(viewport.X + 10, viewport.Y + 20, -1200, false, true); ui.Paint();
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("inspector.end")).X == endLabel.X && ui.View.Document.ContentEquals(map), "Inspector fits the panel without horizontal scrolling");
                var committed = ui.View.Conversion;
                for (int i = 0; i < 10; i++) ui.Paint();
                Check(ReferenceEquals(committed, ui.View.Conversion) && ui.View.Document.ContentEquals(map), "Inspector repaint preserves conversion and content");
                var firstObject = new ConvertedCatchObject(track.Id, 0, CatchObjectKind.Fruit, 0, 0, 0, 0, 0);
                var to = firstObject with { TimeMs = 500, X = 96 };
                Check(EditorView.HorizontalSpeedRatio(map, new(map), [], firstObject, to) == .5, "96 DPB is 0.5 times a 192 DPB base");
                var timingMap = map.DeepClone(); timingMap.TimingPoints.Add(new() { TimeMs = 250, BeatLengthMs = 250, Uninherited = true });
                timingMap.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
                Check(EditorView.HorizontalSpeedRatio(timingMap, new(timingMap), [0, 250], firstObject, to with { X = 144 }) == .5,
                    "Peak normalization integrates the base speed across BPM boundaries");
                ui.View.ShowRandomDock(); ui.Paint(); Wait();
                var resize = ui.View.DockResizeBounds(false);
                ui.View.PointerDown(resize.X + 4, resize.Y + 60, 0, false, false);
                ui.View.PointerMove(ui.Width - 180, resize.Y + 60, false, false);
                ui.View.PointerUp(ui.Width - 180, resize.Y + 60, 0); ui.Paint();
                var leftArrow = ui.Canvas.Texts.First(t => t.Value == "‹" && t.X > ui.Width / 2 && t.Y < ui.View.RandomDockViewport.Y);
                ui.Click(leftArrow.X + 2, leftArrow.Y + 2);
                for (int i = 0; i < 6 && ui.View.DockTabBounds(EditorView.DockPanel.Preview).Width == 0; i++)
                { ui.Click(leftArrow.X + 2, leftArrow.Y + 2); }
                Check(ui.View.DockTabBounds(EditorView.DockPanel.Preview).Width > 0, "Dock arrow reveals overflowing tabs");
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("inspector.title")), "Dock titles retain full text");
                var baseline = ui.View.PatternCanvasObjects(0, 10000).Select(o => (o.TimeMs, o.X)).ToArray();
                SetStrength("70"); Wait();
                var preview = ui.View.PatternCanvasObjects(0, 10000).Select(o => (o.TimeMs, o.X)).ToArray();
                Check(!preview.SequenceEqual(baseline) && ui.View.Document.RandomizeDropletStrength == 70 && ui.View.IsDirty,
                    "Randomization edits immediately update content and canvas");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply") || t.Value == L.Get("mac.cancel")), "Random panel has no confirmation controls");
                ui.Key(27); ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Randomization edit undoes in one step");
                ui.Paint(); Wait(); SetStrength("101");
                Check(!ui.View.RandomDockValid && ui.View.Document.ContentEquals(map), "Invalid strength cannot change content");
                ui.View.ShowSongDock(); ui.Paint();
                var song = ui.View.SongDockViewport;
                ui.Click(song.X + 3 * ((song.Width - 9) / 4 + 3) + 5, song.Y + 10);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("song.countdownOff")), "Horizontal category buttons select Design");
                void SetStrength(string value)
                {
                    var b = ui.View.RandomDockViewport; ui.View.Wheel(b.X + 10, b.Y + 40, 12000, false, false); ui.Paint();
                    var field = ui.View.RandomDockValueBounds(0); ui.Click(field.X + 10, field.Y + 12); ui.Key('A', ctrl: true); ui.View.PasteFieldText(value); ui.Paint();
                }
                void Wait()
                {
                    var end = DateTime.UtcNow.AddSeconds(30);
                    while (!ui.View.RandomDockValid && DateTime.UtcNow < end) { Thread.Sleep(5); ui.Paint(); }
                    Check(ui.View.RandomDockValid, "Randomization validation completes");
                }
            }
        }
        finally { L.SetLanguage(locale); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
