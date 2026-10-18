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
