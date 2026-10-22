using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderCanvasLockTests
{
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                NoticeAndClone();
                CrossCanvasDeletion();
                var map = new MapDocument { DurationMs = 5000 };
                var legacy = new ImportedSlider { TimeMs = 1000, X = 64, Y = 64, PixelLength = 128, PathType = 'L' };
                legacy.ControlPoints.AddRange([new(64, 64), new(192, 64)]);
                map.ImportedSliders.Add(legacy);
                var track = new CurveTrack();
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 320 }, new() { TimeMs = 1500, X = 448 }]);
                map.Tracks.Add(track);
                var ui = new Ui(false); ui.LoadDocument(map); ui.Key('1');
                string path = Path.GetFullPath($"artifacts/tests/slider-canvas-lock-{locale}.json");
                int saves = 0;
                ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
                Check(!ui.View.LibrarySettings.LockSlidersToCanvas, "Canvas selection lock must start disabled.");
                Toggle();
                var restored = LibrarySettings.Load(path);
                Check(restored.LockSlidersToCanvas && saves == 1, "Canvas selection lock was not saved globally.");
                SelectBoth();
                Check(ui.View.SelectedObjectIds.SequenceEqual(new[] { track.Id }), "Catch must select only FSliders when locked.");
                ui.View.ToggleOsuCanvas(); ui.View.UpdateTransport(900, 5000, true, false, false, null, null); ui.Paint();
                SelectBoth();
                Check(ui.View.SelectedObjectIds.SequenceEqual(new[] { legacy.Id }), "osu must select only Legacy Sliders when locked.");
                var head = ui.View.OsuCanvasScreen(320, 192);
                Check(ui.View.OsuCanvasHit(head.X, head.Y) is null, "Locked FSlider remained pickable in osu.");
                head = ui.View.OsuCanvasScreen(64, 64);
                Check(ui.View.OsuCanvasHit(head.X, head.Y) == legacy.Id, "Native Legacy Slider became unpickable.");
                Toggle(); SelectBoth();
                Check(ui.View.SelectedObjectIds.Count == 2 && !LibrarySettings.Load(path).LockSlidersToCanvas,
                    $"Disabling the canvas lock did not restore cross-canvas selection or persistence: selected={ui.View.SelectedObjectIds.Count}, live={ui.View.LibrarySettings.LockSlidersToCanvas}, saved={LibrarySettings.Load(path).LockSlidersToCanvas}, saves={saves}.");
                Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Canvas selection preferences changed content.");

                void Toggle()
                {
                    var button = ui.View.AssistButtonBounds[6];
                    ui.View.PointerMove(button.X + 5, button.Y + 5, false, false); ui.Paint();
                    ui.ClickText(L.Get("assist.lockSlidersToCanvas"));
                }
                void SelectBoth() => typeof(EditorView).GetMethod("SelectObjects",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(ui.View, [new[] { legacy.Id, track.Id }, legacy.Id]);
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void CrossCanvasDeletion()
    {
        foreach (bool osu in new[] { false, true })
        foreach (bool context in new[] { false, true })
        foreach (bool mixed in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 5000 };
            var legacy = new ImportedSlider { TimeMs = 1000, X = 64, Y = 64, PixelLength = 128, PathType = 'L' };
            legacy.ControlPoints.AddRange([new(64, 64), new(192, 64)]);
            map.ImportedSliders.Add(legacy);
            var track = new CurveTrack();
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 320 }, new() { TimeMs = 1500, X = 448 }]);
            map.Tracks.Add(track);
            map.Fruits.Add(new() { TimeMs = 3000, X = 256 });
            var ui = new Ui(false); ui.LoadDocument(map); ui.Key('1');
            if (osu) ui.View.ToggleOsuCanvas();
            ui.View.UpdateTransport(1000, 5000, true, false, false, null, null); ui.Paint();
            Guid id = osu ? track.Id : legacy.Id;
            var before = ui.View.Document.DeepClone();
            if (context)
            {
                var point = osu ? ui.View.OsuCanvasScreen(320, 192) : ui.ScreenAt(1000, 64);
                ui.View.PointerDown(point.X, point.Y, 2, false, false);
                ui.View.PointerUp(point.X, point.Y, 2); ui.Paint();
                if (osu) ui.ClickText(L.Get("editor.command.deleteObjects"));
            }
            else
            {
                var ids = mixed ? new[] { legacy.Id, track.Id } : new[] { id };
                typeof(EditorView).GetMethod("SelectObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(ui.View, [ids, id]);
                ui.Key(46);
            }
            int remaining = !context && mixed ? 0 : 1;
            Check(ui.View.Document.ImportedSliders.Count + ui.View.Document.Tracks.Count == remaining
                && ui.View.Document.Fruits.Count == 1 && ui.View.SelectedObjectIds.Count == 0 && ui.View.IsDirty,
                "Cross-canvas deletion must remove exactly the selected slider parents.");
            var after = ui.View.Document.DeepClone();
            Check(OsuBeatmapWriter.Serialize(after).ObjectSequenceMatches, "Cross-canvas deletion changed exported objects.");
            ui.Key('Z', ctrl: true);
            Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Cross-canvas deletion must undo in one step.");
            ui.Key('Y', ctrl: true);
            Check(after.ContentEquals(ui.View.Document), "Cross-canvas deletion redo changed content.");
        }
    }

    private static void NoticeAndClone()
    {
        foreach (bool osu in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 5000 };
            if (osu)
            {
                var track = new CurveTrack();
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 64 }, new() { TimeMs = 1500, X = 192 }]);
                map.Tracks.Add(track);
            }
            else
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 64, Y = 64, PixelLength = 128, PathType = 'L' };
                slider.ControlPoints.AddRange([new(64, 64), new(192, 64)]);
                map.ImportedSliders.Add(slider);
            }
            var clock = new Clock();
            var ui = new Ui(false, clock); ui.LoadDocument(map); ui.Key('1');
            if (osu)
            {
                ui.View.ToggleOsuCanvas();
                ui.View.UpdateTransport(1000, 5000, true, false, false, null, null); ui.Paint();
                var point = ui.View.OsuCanvasScreen(64, 192);
                ui.Click(point.X, point.Y);
            }
            else ui.ClickMap(1000, 64);
            ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("osu.readOnlySlider")) && ui.View.OsuLinkNoticeOpacity == 1,
                "Cross-canvas selection must show the canvas-switch notice.");
            clock.Milliseconds = 3000; ui.Paint();
            Check(ui.View.OsuLinkNoticeOpacity == 1, "Canvas notice must remain opaque for three seconds.");
            clock.Milliseconds = 3500; ui.Paint();
            Check(!ui.View.OsuLinkNoticeNeedsRedraw, "Canvas notice must disappear after fading.");
            ui.Key('H', ctrl: true);
            Check(ui.View.OsuLinkNoticeOpacity == 1 && ui.View.Document.ContentEquals(map) && !ui.View.IsDirty,
                "A blocked edit must restart the notice without changing content or history.");
        }

        foreach (bool derandomize in new[] { false, true })
        foreach (double strength in new[] { 20d, 8d })
        foreach (bool osu in new[] { false, true })
        {
            const string source = "osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n64,64,1000,2,0,L|192:64,1,128\n";
            var map = OsuBeatmapReader.Read(source);
            Configure(map);
            Check(SliderRepresentationEditing.RefreshLegacyDroplets(map).Success, "The Legacy fixture must apply its map settings.");
            var ui = new Ui(false); ui.LoadDocument(map);
            ui.Key('1');
            if (osu)
            {
                ui.View.ToggleOsuCanvas();
                ui.View.UpdateTransport(1000, 5000, true, false, false, null, null); ui.Paint();
                var point = ui.View.OsuCanvasScreen(64, 64); ui.Click(point.X, point.Y);
            }
            else ui.ClickMap(1000, 64);
            ui.Key('D', ctrl: true); Wait(ui);
            Check(ui.View.Document.ImportedSliders.Count == 2 && ui.View.Document.Tracks.Count == 0
                && ui.View.Document.ImportedSliders.Max(s => s.TimeMs) == 3000,
                "Ctrl+D must retain the original and create a Legacy Slider copy one measure later.");
            var original = ui.View.Document.ImportedSliders.Single(s => s.Id == map.ImportedSliders.Single().Id);
            var before = map.ImportedSliders.Single();
            Check((original.TimeMs, original.X, original.Y, original.PathType, original.PixelLength, original.SpanCount)
                == (before.TimeMs, before.X, before.Y, before.PathType, before.PixelLength, before.SpanCount)
                && original.ControlPoints.SequenceEqual(before.ControlPoints)
                && ui.View.Document.DerandomizeFSliderDroplets == map.DerandomizeFSliderDroplets
                && ui.View.Document.RandomizeDropletStrength == map.RandomizeDropletStrength
                && ui.View.Document.RandomizeDropletSeed == map.RandomizeDropletSeed,
                "Cloning must preserve the original slider geometry and map settings.");
            var reference = OsuBeatmapReader.Read(source + "64,64,3000,2,0,L|192:64,1,128\n");
            Configure(reference);
            Check(SliderRepresentationEditing.RefreshLegacyDroplets(reference).Success, "Reference Legacy processing must succeed.");
            var expected = OsuBeatmapWriter.Serialize(reference);
            var actual = OsuBeatmapWriter.Serialize(ui.View.Document);
            Compare(expected.PlayableObjects, actual.PlayableObjects);
            Compare(expected.PlayableHardRockObjects, actual.PlayableHardRockObjects);
            Check(actual.ObjectSequenceMatches, "The Legacy copy must preserve its playable sequence through export.");
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document)).ContentEquals(ui.View.Document),
                "Legacy copy baselines must survive project serialization.");
            var copied = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(map), "Undo must restore the original slider after cloning.");
            ui.Key('Y', ctrl: true);
            Check(ui.View.Document.ContentEquals(copied), "Redo must restore the prepared Legacy copy.");
            if (osu)
            {
                ui.View.UpdateTransport(3000, 5000, true, false, false, null, null); ui.Paint();
                var point = ui.View.OsuCanvasScreen(64, 64); ui.Click(point.X, point.Y);
            }
            else ui.ClickMap(3000, 64);
            Check(ui.View.CopySelection(), "The processed Legacy copy must remain copyable.");
            ui.View.UpdateTransport(6000, 10000, true, false, false, null, null);
            Check(ui.View.PasteSelection(), "Pasting a processed Legacy copy must succeed."); Wait(ui);
            Check(ui.View.Document.ImportedSliders.Count == 3 && ui.View.Document.Tracks.Count == 0
                && ui.View.Document.ImportedSliders.All(s => (s.RandomizationSource is null || s.RandomizationSource.Id == s.Id)
                    && (s.RandomizationTrack is null || s.RandomizationTrack.Id == s.Id)),
                "Every pasted Legacy effect baseline must belong to its new parent.");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(copied), "Pasting a processed Legacy copy must remain one undo step.");

            void Configure(MapDocument document)
            {
                document.DurationMs = 5000;
                document.DerandomizeFSliderDroplets = document.DerandomizeDropletsForHardRock = derandomize;
                document.RandomizeDropletStrength = strength; document.RandomizeDropletSeed = strength == 20 ? 1337 : 99;
            }
            void Compare(IReadOnlyList<ConvertedCatchObject> expectedObjects, IReadOnlyList<ConvertedCatchObject> actualObjects)
            {
                Check(actualObjects.Count == expectedObjects.Count && actualObjects.Zip(expectedObjects).All(pair =>
                    pair.First.Kind == pair.Second.Kind && Math.Abs(pair.First.TimeMs - pair.Second.TimeMs) < .001
                    && Math.Abs(pair.First.X - pair.Second.X) < 1),
                    "Legacy copies must follow the difficulty's droplet mode, strength and seed in NM and HR.");
            }
        }

        var mixed = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n64,64,1000,2,0,L|192:64,1,128\n256,64,1500,2,0,L|384:64,1,128\n400,192,2000,1,0\n");
        mixed.DurationMs = 10000;
        var batch = new Ui(false); batch.LoadDocument(mixed); batch.Key('1'); batch.ClickMap(2000, 400);
        Check(batch.View.CopySelection(), "The clipboard fixture must contain the standalone fruit.");
        batch.Key('A', ctrl: true); batch.Key('D', ctrl: true); Wait(batch);
        Check(batch.View.Document.ImportedSliders.Count == 4 && batch.View.Document.Tracks.Count == 0
            && batch.View.Document.Fruits.Count == 2 && batch.View.SelectedObjectIds.Count == 3,
            "A mixed clone must retain Legacy Slider types and select the complete copied group.");
        batch.Key('Z', ctrl: true);
        Check(batch.View.Document.ContentEquals(mixed), "One undo must remove the entire mixed clone.");
        batch.View.UpdateTransport(8000, 10000, true, false, false, null, null);
        Check(batch.View.PasteSelection() && batch.View.Document.Fruits.Count == 2
            && batch.View.Document.Tracks.Count == 0 && batch.View.Document.ImportedSliders.Count == 2,
            "Cloning must retain the user's clipboard and ordinary paste behavior.");
    }

    private static void Wait(Ui ui)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (ui.View.LegacyDropletRefreshBusy && timeout.Elapsed.TotalSeconds < 30)
        { Thread.Sleep(1); ui.Paint(); }
        Check(!ui.View.LegacyDropletRefreshBusy, "Legacy copy validation must finish.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
