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
                double expectedSpeed = objects.Zip(objects.Skip(1), (a, b) => b.TimeMs > a.TimeMs
                    ? Math.Abs(b.X - a.X) / (b.TimeMs - a.TimeMs) : 0).Max();
                foreach (bool lazer in new[] { false, true })
                foreach (bool speedMode in new[] { true, false })
                {
                    ui.View.LibrarySettings.LazerEditing = lazer;
                    ui.View.LibrarySettings.MappingSpeedDistanceSnap = speedMode;
                    ui.View.LibrarySettings.LazerSpeedDistanceSnap = speedMode;
                    ui.Paint();
                    double value = speedMode ? expectedSpeed : expected;
                    Check(Math.Abs(ui.View.InspectorMaxDs!.Value - value) < 1e-9
                        && Math.Abs(ui.View.InspectorPeakRatio!.Value - value) < 1e-9,
                        "Both inspector metrics refresh immediately with the selected preset units in either editor.");
                    string formatted = speedMode ? L.Get("ds.lazerSpeed", value)
                        : value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
                    Check(ui.Canvas.Texts.Any(t => t.Value == formatted)
                        && ui.Canvas.Texts.Count(t => t.Value == L.Get(speedMode ? "inspector.peak" : "inspector.maxDs")) == 1
                        && !ui.Canvas.Texts.Any(t => t.Value == L.Get(speedMode ? "inspector.maxDs" : "inspector.peak")),
                        "Inspector shows only Peak horizontal speed in Speed mode or Max DS in DS mode.");
                    Check(ReferenceEquals(committed, ui.View.Conversion) && ui.View.Document.ContentEquals(map),
                        "Changing inspector units must reuse conversion and preserve map content.");
                }
                ui.View.LibrarySettings.LazerEditing = false;
                ui.View.LibrarySettings.LazerSpeedDistanceSnap = true;
                ui.Paint();
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
                var contentBeforeWheel = ui.View.Document.DeepClone();
                var conversionBeforeWheel = ui.View.Conversion;
                var randomViewport = ui.View.RandomDockViewport;
                float wheelX = leftArrow.X + 2, wheelY = leftArrow.Y + 2;
                ui.View.Wheel(wheelX, wheelY, 12000, false); ui.Paint();
                float firstTabX = ui.Canvas.Texts.Single(t => t.Value == L.Get("dock.preview")).X;
                var previewTab = ui.View.DockTabBounds(EditorView.DockPanel.Preview);
                ui.View.Wheel(previewTab.X + 2, previewTab.Y + 2, -60, false); ui.Paint();
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("dock.preview")).X == firstTabX - 24,
                    "Wheel over a tab scrolls right proportionally for partial wheel deltas");
                ui.View.Wheel(wheelX, wheelY, -12000, false); ui.Paint();
                float lastTabX = ui.Canvas.Texts.Single(t => t.Value == L.Get("dock.preview")).X;
                ui.View.Wheel(wheelX, wheelY, -120, false); ui.Paint();
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("dock.preview")).X == lastTabX,
                    "Wheel over an arrow clamps at the right edge");
                ui.View.Wheel(wheelX, wheelY, 12000, false); ui.Paint();
                Check(ui.Canvas.Texts.Single(t => t.Value == L.Get("dock.preview")).X == firstTabX,
                    "Wheel up returns to the left edge");
                Check(ui.View.RandomDockViewport == randomViewport && ui.View.Document.ContentEquals(contentBeforeWheel)
                    && ReferenceEquals(conversionBeforeWheel, ui.View.Conversion),
                    "Tab scrolling preserves the active panel, map content and conversion");
                var baseline = ui.View.PatternCanvasObjects(0, 10000).Select(o => (o.TimeMs, o.X)).ToArray();
                SetStrength("70"); Wait();
                var preview = ui.View.PatternCanvasObjects(0, 10000).Select(o => (o.TimeMs, o.X)).ToArray();
                Check(!preview.SequenceEqual(baseline) && ui.View.Document.RandomizeDropletStrength == 70 && ui.View.IsDirty,
                    $"Randomization edits immediately update content and canvas ({ui.View.Document.RandomizeDropletStrength})");
                Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply") || t.Value == L.Get("mac.cancel")), "Random panel has no confirmation controls");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Randomization edit undoes in one step");
                ui.Paint(); Wait(); SetStrength("90");
                Check(ui.View.RandomDockValid && ui.View.Document.RandomizeDropletStrength == 90, "Strength slider remains editable after undo");
                RandomActions();
                ui.View.ShowSongDock(); ui.Paint();
                var song = ui.View.SongDockViewport;
                ui.View.Wheel(song.X + 10, song.Y + 40, 12000, false, false); ui.Paint();
                ui.Click(song.X + 3 * ((song.Width - 12) / 5 + 3) + 5, song.Y + 10);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("song.countdownOff")), "Horizontal category buttons select Design");
                void RandomActions()
                {
                    var second = track.DeepClone(); second.Id = Guid.NewGuid();
                    foreach (var node in second.Nodes) { node.Id = Guid.NewGuid(); node.TimeMs += 3500; }
                    second.DropletRandomization = null;
                    var actionMap = map.DeepClone(); actionMap.Tracks[0].DropletRandomization!.Adjustments.Add(new(.5, 5)); actionMap.Tracks.Add(second);
                    ui.Resize(1440, 900); ui.LoadDocument(actionMap); ui.Key('1'); ui.Key('A', ctrl: true);
                    ui.View.ShowSliderInspector(); ui.Paint();
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("inspector.mixed"))
                        && !ui.Canvas.Texts.Any(t => t.Value == L.Get("inspector.controls")), "Inspector shows mixed derandomization without control count");
                    Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("randomize.title") && t.Y >= ui.View.SliderInspectorViewport.Y), "Inspector has no tab-jump action");
                    var inspector = ui.View.SliderInspectorViewport;
                    ui.View.Wheel(inspector.X + 10, inspector.Y + 40, -12000, false, false); ui.Paint();
                    ui.ClickText(L.Get("inspector.resetDroplets", 1)); WaitInspector();
                    Check(ui.View.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 0, "Inspector resets selected corrections immediately");
                    ui.Key('Z', ctrl: true); ui.Paint(); WaitInspector();
                    Check(ui.View.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 1, "Reset undoes in one step");
                    ui.Key('A', ctrl: true); ui.Paint();
                    ChooseMode(L.Get("inspector.mixed"), SliderDropletMode.Randomize);
                    Check(ui.View.Document.Tracks.All(t => t.RandomizesDroplets(ui.View.Document))
                        && ui.View.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 1, "Multi-selection mode retains corrections");
                    ui.Key('Z', ctrl: true); WaitInspector();
                    Check(ui.View.Document.ContentEquals(actionMap), "Multi-selection mode undoes atomically");
                    ui.Key('Y', ctrl: true); WaitInspector();
                    ui.Key('A', ctrl: true); ui.Paint();
                    ChooseMode(L.Get("droplet.mode.Randomize"), SliderDropletMode.FollowMap);
                    ui.View.ShowRandomDock(); ui.Paint(); Wait();
                    Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("randomize.enableSelected")
                        || t.Value == L.Get("randomize.disableAll") || t.Value == L.Get("randomize.resetAdjustments")), "Global panel contains only difficulty settings");
                    ui.ClickText(L.Get("randomize.derandomizeOff")); Wait();
                    Check(ui.View.Document.Tracks.All(t => !t.RandomizesDroplets(ui.View.Document)), "Follow map resolves the global default");
                    ui.View.ShowSliderInspector(); ui.Paint();
                    ChooseMode(L.Get("droplet.mode.FollowMap"), SliderDropletMode.Randomize);
                    Check(ui.View.Document.DerandomizeFSliderDroplets && ui.View.Document.Tracks.All(t => t.RandomizesDroplets(ui.View.Document)),
                        "Random override works while the global default is derandomized");
                    ui.ClickMap(500, 450); ui.SelectTrack(track.Id); ui.View.ShowSliderInspector(); ui.Paint();
                    ChooseMode(L.Get("droplet.mode.Randomize"), SliderDropletMode.Derandomize);
                    Check(!ui.View.Document.Tracks[0].RandomizesDroplets(ui.View.Document)
                        && ui.View.Document.Tracks[1].RandomizesDroplets(ui.View.Document), "Single override retains the other slider");
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("inspector.resetDropletsInactive", 1)), "Inactive corrections remain visible");
                    ChooseMode(L.Get("droplet.mode.Derandomize"), SliderDropletMode.Randomize);
                    Check(ui.View.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 1, "Switching modes retains manual corrections");
                    void ChooseMode(string current, SliderDropletMode mode)
                    {
                        var b = ui.View.SliderInspectorViewport;
                        ui.View.Wheel(b.X + 10, b.Y + 40, -12000, false, false); ui.Paint();
                        string label = L.Get("inspector.dropletMode", current);
                        ui.ClickText(label);
                        ui.ClickText(L.Get("droplet.mode." + mode)); WaitInspector();
                        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("inspector.dropletMode", L.Get("droplet.mode." + mode))), "Mode changes stay in Inspector");
                    }
                    void WaitInspector()
                    {
                        var end = DateTime.UtcNow.AddSeconds(30);
                        do { Thread.Sleep(5); ui.Paint(); } while (ui.View.ConversionRefreshing && DateTime.UtcNow < end);
                        Check(!ui.View.ConversionRefreshing, "Inspector conversion completes");
                    }
                }
                void SetStrength(string value)
                {
                    var b = ui.View.RandomDockViewport; ui.View.Wheel(b.X + 10, b.Y + 40, 12000, false, false); ui.Paint();
                    if (!ui.Canvas.Texts.Any(t => t.Value == L.Get("song.RandomizeDropletStrength")))
                    { var advanced = ui.View.RandomDockAdvancedBounds; ui.Click(advanced.X + 20, advanced.Y + 15); }
                    var field = ui.View.RandomDockValueBounds(0);
                    ui.Click(field.X + 7 + (field.Width - 14) * float.Parse(value, System.Globalization.CultureInfo.InvariantCulture) / 100, field.Y + 16); ui.Paint();
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
