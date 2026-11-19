using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class WindowPreferenceTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/window-preferences", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        var defaults = LibrarySettings.Load(path);
        Check(defaults.ShowSliderPaths && defaults.ShowSelectionRect && defaults.WindowsWindow is null && defaults.MacWindow is null,
            "Older settings must retain the existing startup defaults.");
        string language = L.Language;
        try
        {
            L.SetLanguage("en");
            Snaps(root);
            Panels(root);
            ApplyPreservesPreferences(root);
            var ui = new Ui();
            ui.View.LibrarySettings.Workspace = root;
            ui.View.RequestViewPreference = () => ui.View.LibrarySettings.Save(path);
            var before = ui.View.Document.DeepClone();
            bool dirty = ui.View.IsDirty;
            ui.ClickText(L.Get("ui.sliderPathCurves"));
            ui.ClickText(L.Get("ui.selectionRect"));
            var saved = LibrarySettings.Load(path);
            Check(!saved.ShowSliderPaths && !saved.ShowSelectionRect, "Canvas switches did not persist immediately.");
            ui.View.LoadDocument(before.DeepClone()); ui.Paint();
            Check(!ui.View.LibrarySettings.ShowSliderPaths && !ui.View.LibrarySettings.ShowSelectionRect,
                "Document replacement reset global canvas preferences.");
            ui.ClickText(L.Get("ui.view"));
            var menuPath = ui.Canvas.Texts.Last(t => t.Value == L.Get("ui.sliderPathCurves"));
            ui.Click(menuPath.X + 4, menuPath.Y + 5);
            Check(LibrarySettings.Load(path).ShowSliderPaths, "View menu did not persist the path switch.");
            Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Canvas preferences changed beatmap content.");
            saved.WindowsWindow = new() { Width = 1234, Height = 789, Maximized = true };
            saved.MacWindow = new() { Width = 1100, Height = 700 };
            saved.Save(path);
            var restored = LibrarySettings.Load(path);
            Check(restored.WindowsWindow is { Width: 1234, Height: 789, Maximized: true }
                && restored.MacWindow is { Width: 1100, Height: 700, Maximized: false }, "Window settings did not round-trip.");
            Check(!new EditorWindowPreferences { Width = double.NaN, Height = 900 }.HasValidSize()
                && !new EditorWindowPreferences { Width = 1200, Height = -1 }.HasValidSize(), "Invalid window dimensions were accepted.");
        }
        finally { L.SetLanguage(language); }
    }

    private static void ApplyPreservesPreferences(string root)
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        foreach (bool legacySongs in new[] { false, true })
        {
            L.SetLanguage(locale);
            var ui = new Ui(false);
            var settings = ui.View.LibrarySettings;
            settings.Workspace = Path.Combine(root, "apply-workspace");
            settings.OsuRoot = "";
            if (legacySongs)
            {
                settings.Songs = Path.Combine(root, "legacy-maps");
                Directory.CreateDirectory(settings.Songs);
            }
            settings.ShowSliderPaths = false;
            settings.ShowSelectionRect = false;
            settings.LockDropletSelection = false;
            settings.LockSlidersToCanvas = true;
            settings.OsuSliderDefaultSv = 2.5;
            settings.DiscussionWebMode = true;
            settings.ShowCanvasDiscussionAnnotations = false;
            settings.CanvasControlsOffsetX = 35;
            settings.CanvasControlsOffsetY = -20;
            settings.CanvasControlsScale = .5f;
            settings.BookmarkToolbarOffsetX = 40;
            settings.WindowsWindow = new() { Width = 1234, Height = 789, Maximized = true };
            settings.MacWindow = new() { Width = 1100, Height = 700 };
            settings.DockLayouts = ui.View.CaptureDockLayouts();
            settings.DockLayouts.Mapping!.Right!.Width = 345;
            ui.View.RestoreDockLayouts();
            var document = ui.View.Document.DeepClone();
            var conversion = ui.View.Conversion;
            string baseline = System.Text.Json.JsonSerializer.Serialize(settings);
            string path = Path.Combine(root, $"apply-{locale}-{legacySongs}.json");
            ui.View.OpenSettings();
            ui.Paint(); ui.ClickText(L.Get("settings.romanisedOn"));
            ui.View.ApplySettings(root);
            AssertSettings(ui.View.LibrarySettings, baseline, "Failed save");
            ui.View.ApplySettings(path);
            var expected = System.Text.Json.Nodes.JsonNode.Parse(baseline)!;
            expected[nameof(LibrarySettings.RomanisedMetadata)] = false;
            baseline = expected.ToJsonString();
            AssertSettings(ui.View.LibrarySettings, baseline, "Apply");
            AssertSettings(LibrarySettings.Load(path), baseline, "Reload");
            ui.Key(27);
            ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.romanisedOff")); ui.Key(27);
            AssertSettings(ui.View.LibrarySettings, baseline, "Cancel");
            Check(document.ContentEquals(ui.View.Document) && !ui.View.IsDirty && ReferenceEquals(conversion, ui.View.Conversion),
                "Applying preferences changed content or rebuilt conversion.");
        }
        L.SetLanguage("en");

        static void AssertSettings(LibrarySettings actual, string expected, string operation)
        {
            var before = System.Text.Json.Nodes.JsonNode.Parse(expected)!.AsObject();
            var after = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(actual))!.AsObject();
            foreach (var property in before)
                Check(System.Text.Json.Nodes.JsonNode.DeepEquals(property.Value, after[property.Key]),
                    $"{operation} changed preference {property.Key}.");
        }
    }

    private static void Panels(string root)
    {
        string path = Path.Combine(root, "panels.json");
        var project = BeatmapProject.FromDocuments([new MapDocument { IsDemo = false }, new MapDocument { IsDemo = false }]);
        string content = ProjectSerializer.Serialize(project);
        var ui = new Ui(false); ui.View.LibrarySettings.Workspace = root;
        ui.View.RequestViewPreference = () => ui.View.LibrarySettings.Save(path);
        ui.View.LoadProject(project); ui.Paint();
        ui.View.ShowHitsoundingPage(); ui.Paint();
        Check(LibrarySettings.Load(path).MapEditingPreferences[project.Difficulties[0].Id].ActivePanel == "Hitsounding", "Panel selection must save immediately in user settings");
        ClickLane(ui, mute: false, 0);
        ClickLane(ui, mute: false, 4);
        ClickLane(ui, mute: true, 2);
        ui.Key('2');
        ui.ClickText(L.Get("hitsounding.precise"));
        ui.ClickText(L.Get("hitsounding.showText"));
        ui.Key(187);
        var lane = ui.View.HitsoundingNotesLaneBounds;
        float oldHead = PlayLineX(ui), newHead = lane.X + lane.Width * .55f;
        ui.View.PointerDown(oldHead, 137, 0, false, false);
        ui.View.PointerMove(newHead, 137, false, false);
        ui.View.PointerUp(newHead, 137, 0); ui.Paint();
        var first = LibrarySettings.Load(path).MapEditingPreferences[project.Difficulties[0].Id];
        Check(first.HitsoundingMutedLanes == 1 << 2 && first.HitsoundingLockedLanes == 1 << 4
            && first.HitsoundingDraw && first.HitsoundingPrecise == true && first.HitsoundingShowText == false
            && first.HitsoundingSpan == 3200 && Math.Abs(first.HitsoundingHeadRatio - .55) < .001,
            "Hitsounding controls save immediately to local difficulty preferences");
        ui.View.SwitchDifficulty(1); ui.Paint();
        Check(ui.View.HitsoundingPageVisible, "Difficulties in a mapset share the active panel");
        AssertTracks(ui, muteWhistle: false, lockClap: false, draw: false);
        ClickLane(ui, mute: true, 3);
        ui.ClickText(L.Get("hitsounding.panel") + " ▾"); ui.ClickText(L.Get("timing.detailsPanel"));
        ui.ClickText(L.Get("timing.detailsPanel") + " ▾"); ui.ClickText(L.Get("timing.panel"));
        Check(ui.View.TimingPageVisible, "Second map opens Timing");
        ui.View.SwitchDifficulty(0); ui.Paint();
        Check(ui.View.TimingPageVisible, "Switching back retains the shared panel");
        ui.View.ShowHitsoundingPage(); ui.Paint();
        AssertTracks(ui, muteWhistle: true, lockClap: true, draw: true);
        Check(!ui.View.IsDirty && ProjectSerializer.Serialize(ui.View.CaptureProject()) == content, "Panel preferences do not modify the project");
        ui.Key(27); Check(ui.View.LibraryVisible, "Escape returns idle Hitsounding to Library");
        Check(LibrarySettings.Load(path).MapEditingPreferences[project.Difficulties[0].Id].ActivePanel == "Hitsounding", "Leaving the editor retains its panel preference");
        var reopened = new Ui(false);
        string sounds = Path.Combine(root, "HitsoundLibrary");
        Directory.CreateDirectory(Path.Combine(sounds, "restart-folder"));
        File.WriteAllBytes(Path.Combine(sounds, "restart-sound.wav"), HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        reopened.View.InitializeLibrary(false, LibrarySettings.Load(path));
        reopened.View.LoadProject(ProjectSerializer.ReadProject(content)); reopened.Paint();
        Check(reopened.View.HitsoundingPageVisible, "Hitsounding panel survives app restart");
        AssertTracks(reopened, muteWhistle: true, lockClap: true, draw: true);
        var reopenedLane = reopened.View.HitsoundingNotesLaneBounds;
        Check(Math.Abs(PlayLineX(reopened) - (reopenedLane.X + reopenedLane.Width * .55f)) < 1,
            "Play-line position survives app restart");
        Check(reopened.Canvas.Texts.Any(t => t.Value == "restart-folder") && reopened.Canvas.Texts.Any(t => t.Value == "restart-sound.wav"), "Restored Hitsounding library loads folders and samples without pointer input");
        reopened.View.SwitchDifficulty(1); reopened.Paint();
        Check(reopened.View.HitsoundingPageVisible, "Shared panel preference survives app restart across difficulties");
        AssertTracks(reopened, muteWhistle: false, lockClap: false, draw: false);
        Check(IsMuted(reopened, 3), "The second difficulty restores its own muted lane on restart");
        reopened.Key(27); Check(reopened.View.LibraryVisible, "Escape returns idle Timing to Library");
        Check(new MapEditingPreferences { ActivePanel = "Unknown" }.ActivePanel == "Mapping", "Unknown panel names safely use Mapping");

        static void ClickLane(Ui ui, bool mute, int lane)
        {
            var button = mute ? ui.View.HitsoundingMuteBounds(lane) : ui.View.HitsoundingLockBounds(lane);
            ui.Click(button.X + button.Width / 2, button.Y + button.Height / 2);
        }
        static bool IsMuted(Ui ui, int lane)
        {
            var button = ui.View.HitsoundingMuteBounds(lane);
            return ui.Canvas.PaintCalls.Any(call => call.Opacity == .65f && call.FillBounds is { } bounds
                && bounds.X > button.Right && bounds.Width > 300 && bounds.Y <= button.Y && bounds.Bottom >= button.Bottom);
        }
        static float PlayLineX(Ui ui) => ui.Canvas.Lines.Single(line => line.Color is 0xE9CA64 or 0xFFF0A6 && line.Y1 == 130).X1;
        static void AssertTracks(Ui ui, bool muteWhistle, bool lockClap, bool draw)
        {
            var notes = ui.View.HitsoundingNotesLaneBounds;
            bool IsLocked(int lane) => ui.Canvas.Fills.Any(fill => fill.Color == 0xE89A9A
                && fill.Bounds.Contains(notes.X + 60, notes.Y + (lane + .5f) * notes.Height));
            Check(IsMuted(ui, 2) == muteWhistle && IsLocked(4) == lockClap
                && IsLocked(0) != draw && ui.View.HitsoundingDraw == draw, "Difficulty restores its own lock, mute and tool state");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class ManualTime : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ticks;
    }

    private static void Snaps(string root)
    {
        string path = Path.Combine(root, "snaps.json");
        var map = new MapDocument { DurationMs = 4000, BeatLengthMs = 1000, IsDemo = false };
        var fruit = new Fruit { TimeMs = 1167, X = 150 }; map.Fruits.Add(fruit);
        var project = BeatmapProject.FromDocuments([map, map.DeepClone()]);
        string before = ProjectSerializer.Serialize(project);
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        ui.View.InitializeLibrary(false, new LibrarySettings { Workspace = root });
        ui.View.LoadProject(project); ui.Paint();
        int saves = 0;
        ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
        var slider = ui.View.SnapSliderBounds;
        ui.View.PointerDown(slider.X + 20, slider.Y + 8, 0, false, false);
        ui.View.PointerMove(slider.Right - 5, slider.Y + 8, false, false);
        Check(saves == 0, "Snap dragging wrote preferences during pointer movement.");
        ui.View.PointerUp(slider.Right - 5, slider.Y + 8, 0); ui.Paint();
        Check(saves == 1, "Snap drag did not save at release.");
        ui.SetSnapDivisor(4);
        ui.Key('T'); ui.Key('Y'); ui.Key('3', ctrl: true);
        ui.ClickText(L.Get("ui.anchorSnap"));
        Check(ui.View.AnchorSnapEnabled, "Anchor snap did not activate.");
        ui.View.SwitchDifficulty(1); ui.Paint();
        Check(ui.View.SnapDivisor == 4 && !ui.View.AnchorSnapEnabled && ui.View.EditorGridSettings == (false, 4)
            && !ui.View.DistanceSnapEnabled, "Snap settings leaked across difficulties.");
        ui.SetSnapDivisor(8);
        ui.View.SwitchDifficulty(0); ui.Paint();
        Check(ui.View.SnapDivisor == 4 && ui.View.AnchorSnapEnabled && ui.View.EditorGridSettings == (true, 16)
            && ui.View.DistanceSnapEnabled, "Difficulty snap settings were not restored.");
        ui.DownMap(fruit.TimeMs, fruit.X); clock.Ticks += 300; ui.Paint();
        Check(ui.View.SnapDivisor == 6, "Note inspection did not use a temporary snap.");
        Check(LibrarySettings.Load(path).MapEditingPreferences[project.Difficulties[0].Id].BeatSnapDivisor == 4,
            "Temporary note snap overwrote the remembered divisor.");
        ui.UpMap(fruit.TimeMs, fruit.X);
        var reopened = new Ui();
        reopened.View.InitializeLibrary(false, LibrarySettings.Load(path));
        reopened.View.LoadProject(ProjectSerializer.ReadProject(before)); reopened.Paint();
        Check(reopened.View.SnapDivisor == 4 && reopened.View.AnchorSnapEnabled
            && reopened.View.EditorGridSettings == (true, 16) && reopened.View.DistanceSnapEnabled,
            "First difficulty snap settings did not survive restart.");
        reopened.View.SwitchDifficulty(1); reopened.Paint();
        Check(reopened.View.SnapDivisor == 8 && !reopened.View.AnchorSnapEnabled
            && reopened.View.EditorGridSettings == (false, 4) && !reopened.View.DistanceSnapEnabled,
            "Second difficulty snap settings did not survive restart.");
        Check(!ui.View.IsDirty && ProjectSerializer.Serialize(ui.View.CaptureProject()) == before,
            "Remembering snaps changed beatmap content.");
    }
}
