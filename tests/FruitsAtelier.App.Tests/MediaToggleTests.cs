using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class MediaToggleTests
{
    private static readonly string DirectoryPath = Path.GetFullPath("artifacts/tests/media-toggle/fixtures");
    public static int Run()
    {
        Directory.CreateDirectory(DirectoryPath);
        Defaults();
        foreach (string language in new[] { "en", "zh-CN" }) MenuAndSettings(language);
        Lifecycle();
        SampleLayers();
        return 0;
    }

    private static void Defaults()
    {
        string settings = Path.Combine(DirectoryPath, "settings.json");
        File.WriteAllText(settings, "{}");
        Check(new LibrarySettings().EnableVideoStoryboard && LibrarySettings.Load(settings).EnableVideoStoryboard,
            "New and legacy settings must enable video/storyboard by default.");
        var preferences = LibrarySettings.Load(settings); preferences.Workspace = Path.Combine(DirectoryPath, "workspace");
        preferences.EnableVideoStoryboard = false; preferences.Save(settings);
        Check(!LibrarySettings.Load(settings).EnableVideoStoryboard, "Disabled preference did not survive settings reload.");
        preferences.EnableVideoStoryboard = true; preferences.Save(settings);
        Check(LibrarySettings.Load(settings).EnableVideoStoryboard, "Reenabled preference did not survive settings reload.");
        Console.WriteLine("PASS media preference defaults and persisted restart state");
    }

    private static void MenuAndSettings(string language)
    {
        string previous = L.Language; L.SetLanguage(language);
        var ui = new Ui(false); ui.View.LibrarySettings.Workspace = Path.Combine(DirectoryPath, language);
        try
        {
            var original = ui.View.Document.DeepClone(); bool dirty = ui.View.IsDirty;
            int saved = 0; ui.View.RequestViewPreference = () => saved++;
            ui.ClickText(L.Get("ui.view"));
            var toggle = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.enableVideoStoryboard"));
            var dim = ui.Canvas.Texts.Single(t => t.Value.EndsWith(L.Get("settings.forceBackgroundDim"), StringComparison.Ordinal));
            Check(toggle.Y > dim.Y && toggle.Y - dim.Y < 40, "View media checkbox is not immediately below Dim Background.");
            Check(!toggle.Value.StartsWith('✓'), "Checkbox label contains a separate prefix tick.");
            ui.ClickText(toggle.Value);
            Check(!ui.View.LibrarySettings.EnableVideoStoryboard && saved == 1, "View checkbox did not immediately disable and persist.");
            ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.testplay"));
            ui.ClickText(L.Get("settings.enableVideoStoryboard"));
            Check(!ui.View.LibrarySettings.EnableVideoStoryboard, "Settings draft applied before Apply.");
            ui.Click(ui.View.SettingsBounds.Right - 32, ui.View.SettingsBounds.Y + 24);
            ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.testplay"));
            string path = Path.Combine(DirectoryPath, language + "-settings.json");
            ui.View.ApplySettings(path, force: true);
            Check(!LibrarySettings.Load(path).EnableVideoStoryboard, "Cancel retained an enabled draft.");
            ui.ClickText(L.Get("settings.enableVideoStoryboard")); ui.View.ApplySettings(path); ui.Key(27);
            Check(ui.View.LibrarySettings.EnableVideoStoryboard && LibrarySettings.Load(path).EnableVideoStoryboard,
                "Settings Apply did not update and persist the shared preference.");
            ui.Click(ui.View.SettingsBounds.Right - 32, ui.View.SettingsBounds.Y + 24);
            var canvasToggle = ui.View.CanvasMediaToggleBounds;
            ui.Click(canvasToggle.X + 15, canvasToggle.Y + 15);
            Check(!ui.View.LibrarySettings.EnableVideoStoryboard, "Canvas checkbox did not immediately disable the shared preference.");
            ui.ClickText(L.Get("ui.view")); ui.ClickText(L.Get("settings.enableVideoStoryboard"));
            Check(ui.View.LibrarySettings.EnableVideoStoryboard, "View checkbox did not follow the Canvas checkbox.");
            ui.Resize(980, 620); ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.testplay"));
            var setting = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.enableVideoStoryboard"));
            Check(setting.Y > ui.View.SettingsBounds.Y && setting.Y < ui.View.SettingsBounds.Bottom - 50,
                "Minimum window media checkbox overlaps the Settings footer.");
            ui.ClickText(setting.Value); ui.View.ApplySettings(path); ui.Key(27);
            Check(!ui.View.LibrarySettings.EnableVideoStoryboard, "Minimum window checkbox did not apply.");
            Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Media preference changed document or undo state.");
            Console.WriteLine($"PASS {language} View checkbox immediate state and Settings Apply/Cancel share one preference");
        }
        finally { ui.View.StopFileMonitoring(); ui.View.CompleteLibraryCacheWrites(); L.SetLanguage(previous); }
    }

    private static MapDocument Map(string name)
    {
        var map = new MapDocument { SourcePath = Path.Combine(DirectoryPath, name + ".osu"), DurationMs = 10000 };
        map.Fruits.AddRange([new() { TimeMs = 5000, X = 256 }, new() { TimeMs = 9000, X = 256 }]);
        map.OriginalSections.Add(new() { Name = "Events", Lines = {
            "0,0,\"background.png\",0,0", "Video,0,\"video.mp4\"",
            $"Sprite,Foreground,Centre,\"{name}.png\",320,240", " F,0,0,10000,1,1",
            "Sample,500,Foreground,\"sample.wav\",25" } });
        return map;
    }

    private static void Lifecycle()
    {
        var view = new EditorView(false); var canvas = new ProbeCanvas();
        view.LibrarySettings.TestplayStartupDelaySeconds = 0;
        view.LibrarySettings.BackgroundDim = view.LibrarySettings.MappingCanvasBackgroundDim = 0;
        view.LibrarySettings.EnableVideoStoryboard = false;
        view.LoadDocument(Map("first")); view.CloseLibrary();
        Settle();
        Check(canvas.LoadRequests == 0 && canvas.Sprites.Count == 0 && canvas.VideoRequests == 0,
            "Cold disabled opening requested sprite/video loading.");
        var samplesOnly = Field<StoryboardScene>(view, "storyboardScene");
        Check(samplesOnly is { SpriteCount: 0 } && samplesOnly.ImagePaths.Count == 0 && samplesOnly.Videos.Count == 0 && samplesOnly.Samples.Count == 1,
            "Disabled parse retained visual media or lost ordinary Sample metadata.");
        var snapshot = view.Document.DeepClone(); bool dirty = view.IsDirty;
        Toggle(); Settle();
        view.UpdateTransport(1000, 10000, true, false, false, null, null); Paint();
        Check(canvas.Sprites.Any(p => p.EndsWith("first.png", StringComparison.Ordinal)) && canvas.VideoRequests > 0,
            "Enabled mapping did not restore current storyboard/video.");
        var preview = view.PreviewToggleBounds;
        view.PointerDown(preview.X + 5, preview.Y + 5, 0, false, false); view.PointerUp(preview.X + 5, preview.Y + 5, 0); Paint();
        Check(canvas.Sprites.Count == 2, "Mapping and preview do not share enabled visuals.");
        int releases = canvas.Releases; Toggle(); Settle();
        int requests = canvas.LoadRequests, videos = canvas.VideoRequests;
        for (int i = 0; i < 10; i++) { view.UpdateTransport(i * 800, 10000, true, false, false, null, null); Paint(); }
        Check(canvas.Releases > releases && canvas.Sprites.Count == 0 && canvas.LoadRequests == requests && canvas.VideoRequests == videos,
            "Disabling failed to unload or continued visual loader requests.");
        Check(canvas.Backgrounds > 0, "Disabling video/storyboard removed the ordinary background.");
        view.UpdateTransport(1000, 10000, false, false, false, null, null); view.StartTestplay(); Paint();
        Check(view.IsTestplaying && canvas.Sprites.Count == 0 && canvas.LoadRequests == requests && canvas.VideoRequests == videos,
            "Disabled testplay leaked visual loading.");
        view.StopTestplay(); Paint();
        Check(snapshot.ContentEquals(view.Document) && dirty == view.IsDirty, "Toggling changed map content or dirty state.");

        canvas.Ready = false; Toggle(); Paint();
        Toggle(); Settle();
        canvas.Ready = true; for (int i = 0; i < 20; i++) { Paint(); Thread.Sleep(1); }
        Check(canvas.Sprites.Count == 0, "An asynchronous ready result resurrected disabled media.");
        for (int i = 0; i < 12; i++)
        {
            Toggle(); view.LoadDocument(Map(i % 2 == 0 ? "first" : "second")); Paint(); Toggle(); Paint();
        }
        Settle(); Check(!view.LibrarySettings.EnableVideoStoryboard && canvas.Sprites.Count == 0, "Rapid reopen resurrected retired media.");
        view.LoadDocument(Map("second")); Toggle(); Settle();
        Check(canvas.Sprites.Count > 0 && canvas.Sprites.All(p => p.EndsWith("second.png", StringComparison.Ordinal)),
            "Reenabling published media from an old difficulty.");
        view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
        Console.WriteLine("PASS disabled zero visual loader requests, mapping/preview/testplay, release, ready race and rapid reopen");
        return;
        void Toggle() => typeof(EditorView).GetMethod("ToggleVideoStoryboard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);
        void Paint() { canvas.Sprites.Clear(); view.Render(canvas, 1440, 900); }
        void Settle()
        {
            var timeout = Stopwatch.StartNew();
            do { Paint(); Thread.Sleep(2); } while ((view.LibraryOpening || Field<Task>(view, "storyboardLoad") is not null || Field<Task>(view, "storyboardGameplayLoad") is not null) && timeout.Elapsed.TotalSeconds < 10);
            Check(!view.LibraryOpening && Field<Task>(view, "storyboardLoad") is null, "Media preference preparation timed out.");
        }
    }

    private static void SampleLayers()
    {
        foreach (bool hit in new[] { true, false })
        foreach (bool toggleDuringPlayback in new[] { false, true })
        {
            var clock = new Clock(); var view = new EditorView(false, timeProvider: clock); var canvas = new ProbeCanvas();
            var map = Map("layers"); map.Fruits.Clear();
            for (int i = 0; i < 12; i++) map.Fruits.Add(new() { TimeMs = 1000, X = 0 });
            map.Fruits.Add(new() { TimeMs = 9000, X = 0 });
            map.OriginalSections.Single(s => s.Name == "Events").Lines.AddRange([
                "Sample,1200,Pass,\"pass.wav\",25", "Sample,1200,Fail,\"fail.wav\",75", "Sample,1400,Foreground,\"later.wav\",50"]);
            view.LibrarySettings.EnableVideoStoryboard = toggleDuringPlayback;
            view.LibrarySettings.TestplayStartupDelaySeconds = 0;
            view.LoadDocument(map); view.CloseLibrary();
            void Paint() => view.Render(canvas, 1440, 900);
            var warm = Stopwatch.StartNew();
            do { Paint(); Thread.Sleep(2); } while ((view.LibraryOpening || Field<Task>(view, "storyboardLoad") is not null || Field<Task>(view, "storyboardGameplayLoad") is not null) && warm.Elapsed.TotalSeconds < 10);
            var emitted = new List<Hitsound>();
            view.RequestHitsound = sound => { if (sound.Name == "storyboard") emitted.Add(sound); };
            view.RequestPrepareHitsound = _ => { };
            view.UpdateTransport(0, 10000, false, false, false, null, null); view.StartTestplay(); Paint();
            Check(view.IsTestplaying, "Sample-layer testplay did not start.");
            if (hit) { view.KeyDown(9, false, false); view.KeyUp(9); }
            else view.KeyDown(view.LibrarySettings.TestplayRightKey, false, false);
            bool toggled = false;
            while (view.PlayheadMs < 1500)
            {
                clock.Advance(5); Paint();
                if (toggleDuringPlayback && !toggled && view.PlayheadMs >= 1100)
                {
                    double position = view.PlayheadMs;
                    typeof(EditorView).GetMethod("ToggleVideoStoryboard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);
                    Paint();
                    Check(!view.LibraryOpening && view.IsTestplaying && view.PlayheadMs >= position,
                        "Disabling visuals interrupted playback or reopened the loading gate.");
                    toggled = true;
                }
            }
            Check(emitted.Count == 3, "Disabling duplicated or omitted ordinary storyboard samples.");
            Check(emitted.Any(s => s.FilePath?.EndsWith(hit ? "pass.wav" : "fail.wav", StringComparison.Ordinal) == true)
                && !emitted.Any(s => s.FilePath?.EndsWith(hit ? "fail.wav" : "pass.wav", StringComparison.Ordinal) == true),
                "Disabled storyboard Sample layer did not follow actual caught/missed health.");
            view.StopTestplay(); view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
            Console.WriteLine($"PASS actual {(hit ? "caught" : "missed")} Pass/Fail samples, disabled {(toggleDuringPlayback ? "during playback" : "at open")}, uninterrupted Sample cursor");
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance(long value) => milliseconds += value;
    }

    private static T? Field<T>(object target, string name) where T : class => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target) as T;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class ProbeCanvas : ICanvas
    {
        internal readonly RecordingCanvas Inner = new();
        internal readonly List<string> Sprites = [];
        internal int LoadRequests, VideoRequests, Releases, Backgrounds;
        internal bool Ready = true;
        public void PrepareStoryboardImages(IReadOnlyList<string> paths) { LoadRequests += paths.Count; }
        public bool StoryboardImagesReady(IReadOnlyList<string> paths) { LoadRequests += paths.Count; return Ready; }
        public void ReleaseStoryboardResources() { Releases++; }
        public void StoryboardSprite(string path, float x, float y, float scaleX, float scaleY, uint tint, float opacity, float rotationRadians, bool additive, bool flipX, bool flipY, float originX, float originY) => Sprites.Add(path);
        public bool VideoImage(string path, Rect destination, double positionMs, bool playing, double rate) { VideoRequests++; return true; }
        public bool BackgroundImage(string path, Rect destination) { Backgrounds++; return true; }
        public void Fill(Rect r, uint color, float radius = 0, float opacity = 1) => Inner.Fill(r, color, radius, opacity);
        public void Stroke(Rect r, uint color, float width = 1, float radius = 0) => Inner.Stroke(r, color, width, radius);
        public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1) => Inner.Line(x1, y1, x2, y2, color, width, opacity);
        public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1) => Inner.Circle(x, y, radius, color, filled, width, opacity);
        public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false) => Inner.Text(text, x, y, size, color, maxWidth, bold);
        public bool Image(string path, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1) => Inner.Image(path, destination, tint, source, opacity);
        public void Clip(Rect r) => Inner.Clip(r);
        public void Unclip() => Inner.Unclip();
    }
}
