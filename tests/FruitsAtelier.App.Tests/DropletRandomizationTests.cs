using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DropletRandomizationTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string lang in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(lang);
                var map = new MapDocument { DurationMs = 8000, IsDemo = false };
                foreach (int start in new[] { 1000, 4000 })
                {
                    var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true };
                    track.Nodes.AddRange([new Anchor { TimeMs = start, X = 256 }, new Anchor { TimeMs = start + 2000, X = 256 }]);
                    map.Tracks.Add(track);
                }
                var stream = map.Tracks[0].DeepClone(); stream.Id = Guid.NewGuid(); stream.StreamSnapDivisor = 4;
                foreach (var node in stream.Nodes) { node.Id = Guid.NewGuid(); node.TimeMs += 6000; }
                map.DurationMs = 12000; map.Tracks.Add(stream);
                var ui = new Ui(); ui.LoadDocument(map);
                ui.View.LoadProject(BeatmapProject.FromDocuments([map, map.DeepClone()])); ui.Paint();
                var before = ui.View.Document.DeepClone();
                ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("randomize.title"));
                ui.ClickText(L.Get("randomize.enableAll"));
                Check(ui.View.Document.ContentEquals(before), "setup batch remains a cancellable draft");
                ui.Key(27); Check(ui.View.Document.ContentEquals(before), "cancel preserves switches");
                ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("randomize.title"));
                Set(ui, "RandomizeDropletStrength", "12"); Set(ui, "RandomizeDropletSeed", "-123");
                ui.ClickText(L.Get("randomize.enableAll")); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.Document.RandomizeDropletStrength == 12 && ui.View.Document.RandomizeDropletSeed == -123, "parameters committed");
                Check(ui.View.Document.Tracks.Take(2).All(t => t.DropletRandomization is { Enabled: true })
                    && ui.View.Document.Tracks[2].DropletRandomization is null, "batch affects ordinary FSliders only");
                Check(ui.View.CaptureProject().Difficulties[1].Document.ContentEquals(map), "batch and parameters stay on current diff");
                var enabled = ui.View.Document.DeepClone();
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "setup undoes atomically");
                ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(enabled), "setup redo");
                ui.ClickMap(1000, 256); ui.View.ToggleSelectedDropletRandomization(); ui.Paint();
                Check(ui.View.Document.Tracks[0].DropletRandomization is { Enabled: false }
                    && ui.View.Document.Tracks[1].DropletRandomization is { Enabled: true }, "individual switch is independent");
                ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("randomize.title")); ui.ClickText(L.Get("randomize.enableAll"));
                Check(ui.View.Document.Tracks.Take(2).All(t => t.DropletRandomization is { Enabled: true }), "Edit batch enable entry");
                ui.ClickText(L.Get("ui.edit")); ui.ClickText(L.Get("randomize.title")); ui.ClickText(L.Get("randomize.disableAll"));
                Check(ui.View.Document.Tracks.Take(2).All(t => t.DropletRandomization is { Enabled: false }), "Edit batch disable entry");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.Tracks.Take(2).All(t => t.DropletRandomization is { Enabled: true }), "batch has one undo step");
                ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("randomize.title"));
                Set(ui, "RandomizeDropletSeed", "1.5"); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.SongSetupVisible && ui.View.Document.RandomizeDropletSeed == -123, "invalid seed cannot apply partially"); ui.Key(27);
                foreach (var size in new[] { (980, 620), (760, 580) })
                {
                    ui.Resize(size.Item1, size.Item2); ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("randomize.title"));
                    foreach (var bounds in ui.View.SongSetupFieldBounds.Values)
                        Check(bounds.X >= 0 && bounds.Right <= ui.Width && bounds.Y >= 0 && bounds.Bottom <= ui.Height, "fields fit window");
                    ui.Key(27);
                }
                foreach (var mode in Enum.GetValues<FruitsAtelier.App.Editor.SliderEditingMode>())
                {
                    var dragMap = map.DeepClone(); dragMap.Tracks.RemoveRange(1, 2);
                    dragMap.Tracks[0].DropletRandomization = new() { Enabled = true };
                    var dragUi = new Ui(); dragUi.LoadDocument(dragMap); dragUi.View.SetSliderEditingMode(mode); dragUi.Paint();
                    var target = OsuBeatmapWriter.Serialize(dragMap).PlayableObjects.First(o => o.Kind == CatchObjectKind.TinyDroplet);
                    dragUi.ClickMap(target.TimeMs, target.X);
                    dragUi.DownMap(target.TimeMs, target.X); dragUi.MoveMap(target.TimeMs, target.X + 4);
                    dragUi.UpMap(target.TimeMs, target.X + 4);
                    Check(dragUi.View.Document.Tracks[0].Nodes.Count == 2
                        && dragUi.View.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 1, "pointer drag stores an FX correction in both editing modes");
                    var moved = OsuBeatmapWriter.Serialize(dragUi.View.Document).PlayableObjects.Single(o => o.EventIndex == target.EventIndex);
                    Check(Math.Abs(moved.X - target.X - 4) < 1, "pointer target matches playable export");
                    dragUi.Key('Z', ctrl: true); Check(dragUi.View.Document.ContentEquals(dragMap), "pointer gesture undoes in one step");
                }
            }
        }
        finally { L.SetLanguage(language); }
        PlacementSequence();
    }
    private static void PlacementSequence()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false, RandomizeDropletStrength = 12, RandomizeDropletSeed = -123 };
        var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true,
            DropletRandomization = new() { Enabled = true } };
        track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 3000, X = 256 }]);
        map.Tracks.Add(track);
        var ui = new Ui(); ui.LoadDocument(map); ui.Key('F'); ui.MoveMap(500, 200);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = ui.View.GetType();
        var ghost = (ConvertedCatchObject)type.GetField("placementGhost", flags)!.GetValue(ui.View)!;
        var preview = (IReadOnlyList<ConvertedCatchObject>)type.GetField("placementMovementObjects", flags)!.GetValue(ui.View)!;
        var candidate = map.DeepClone(); candidate.Fruits.Add(new Fruit { TimeMs = ghost.TimeMs, X = ghost.X });
        double[] Targets(IEnumerable<ConvertedCatchObject> objects) => objects
            .Where(o => o.SourceId == track.Id && o.Kind == CatchObjectKind.TinyDroplet).Select(o => o.X).ToArray();
        var expected = Targets(OsuBeatmapWriter.Serialize(candidate).PlayableObjects);
        Check(Targets(preview).SequenceEqual(expected), "fruit hover uses the full diff counter and configured FX parameters");
        Check(!expected.SequenceEqual(Targets(OsuBeatmapWriter.Serialize(map).PlayableObjects)), "earlier fruit preview advances downstream FX");
        Check(ui.View.Document.ContentEquals(map), "hover preserves beatmap content");
    }
    private static void Set(Ui ui, string key, string value)
    {
        var box = ui.View.SongSetupFieldBounds[key]; ui.Click(box.X + 5, box.Y + 5); ui.Key('A', ctrl: true);
        ui.View.PasteSongSetupText(value, ui.View.SongSetupInputSession); ui.Paint();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
