using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class StoryboardPresentationTests
{
    public static int Run()
    {
        string directory = Path.GetFullPath("artifacts/tests/video-storyboard/presentation-fixtures");
        Directory.CreateDirectory(directory);
        OpeningCancellation(directory);
        DifficultySwitchPreparation(directory);
        foreach (bool widescreen in new[] { false, true })
        {
            var map = new MapDocument { SourcePath = Path.Combine(directory, "map.osu"), DurationMs = 10000 };
            map.Fruits.AddRange([new() { TimeMs = 5000, X = 256 }, new() { TimeMs = 9000, X = 256 }]);
            map.OriginalSections.Add(new() { Name = "General", Lines = { "WidescreenStoryboard:" + (widescreen ? 1 : 0) } });
            map.OriginalSections.Add(new() { Name = "Events", Lines = { "0,0,\"background.png\",0,0", "Video,500,\"video.mp4\"",
                "Sprite,Foreground,Centre,\"sprite.png\",320,240", " F,0,0,10000,1,1",
                " M,0,0,10000,0,240,640,240",
                "Sprite,Overlay,Centre,\"overlay.png\",320,240", " F,0,0,10000,1,1" } });
            var view = new EditorView(false); var canvas = new MediaCanvas();
            view.LibrarySettings.TestplayStartupDelaySeconds = 0;
            view.LoadDocument(map); view.CloseLibrary();
            canvas.RequiredReady = false;
            var warm = Stopwatch.StartNew();
            do { Paint(); Thread.Sleep(5); } while (SourcePending(view) && warm.Elapsed.TotalSeconds < 5);
            if (!view.LibraryOpening || canvas.Backgrounds.Count != 0 || canvas.Videos.Count != 0 || canvas.Sprites.Count != 0)
                throw new Exception("Cold opening exposed map media before its required first-frame textures were ready.");
            canvas.RequiredReady = true;
            do { Paint(); Thread.Sleep(5); } while (Pending(view) && warm.Elapsed.TotalSeconds < 5);
            if (Pending(view)) throw new TimeoutException("Presentation fixture did not settle.");
            view.UpdateTransport(0, 10000, true, false, false, null, null); Paint();
            if (!view.CatchPreviewVisible)
            {
                var toggle = view.PreviewToggleBounds;
                view.PointerDown(toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2, 0, false, false);
                view.PointerUp(toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2, 0); Paint();
            }
            if (!view.CatchPreviewVisible) throw new Exception("Presentation fixture could not open the actual preview panel.");
            view.UpdateTransport(1000, 10000, true, false, false, null, null); Paint();
            var oldFrame = canvas.Sprites.ToArray(); canvas.RequiredReady = false;
            view.UpdateTransport(9000, 10000, true, false, false, null, null); Paint();
            if (view.PlayheadMs != 9000 || !canvas.Sprites.SequenceEqual(oldFrame) || view.LibraryOpening)
                throw new Exception("Unready seek delayed the playhead, exposed a partial media frame or reopened the map gate.");
            view.UpdateTransport(2000, 10000, true, false, false, null, null); Paint(); canvas.RequiredReady = true; Paint();
            var displayed = (List<StoryboardSpriteState>)typeof(EditorView).GetField("storyboardDisplayFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            if (Math.Abs(displayed.Single(s => s.Path.EndsWith("sprite.png", StringComparison.Ordinal)).X - 128) > .001)
                throw new Exception("The latest rapid seek did not replace the retained frame.");
            foreach (int previewMode in new[] { 0, 1, 2 })
            foreach (int dim in new[] { 0, 50, 100 })
            {
                typeof(EditorView).GetProperty("previewDisplayMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, previewMode);
                view.LibrarySettings.BackgroundDim = view.LibrarySettings.MappingCanvasBackgroundDim = dim;
                view.LibrarySettings.ForceBackgroundDim = false;
                view.UpdateTransport(6000, 10000, true, false, false, null, null); Paint();
                if (canvas.Videos.Count != 2 || canvas.Dims.Count != 2) throw new Exception("Mapping and preview did not each compose one video/background dim.");
                if (canvas.Dims.Any(d => Math.Abs(d.Opacity - dim / 100f) > .001)) throw new Exception("Composite dim differs between mapping and preview.");
                if (canvas.Sprites.Any(s => s.Opacity != 1)) throw new Exception("Dim changed source sprite opacity.");
                uint expectedTint = dim == 0 ? 0xFFFFFFu : dim == 50 ? 0x808080u : 0u;
                if (canvas.Sprites.Where(s => s.Path.EndsWith("overlay.png", StringComparison.Ordinal)).Any(s => s.Tint != expectedTint))
                    throw new Exception("Overlay brightness did not follow dim while retaining opacity.");
                foreach (var stage in canvas.Videos.Select(v => v.Bounds))
                {
                    float side = Math.Max(0, (stage.Width - stage.Height * 4 / 3) / 2);
                    if (widescreen || side <= 0) continue;
                    foreach (var bar in new[] { new Rect(stage.X, stage.Y, side, stage.Height), new Rect(stage.Right - side, stage.Y, side, stage.Height) })
                        if (!canvas.Fills.Any(f => f.Color == 0 && f.Opacity == 1 && Near(f.Bounds, bar)))
                            throw new Exception("A nonwidescreen storyboard exposed the underlying background outside its centered 4:3 mask.");
                }
                foreach (var sprite in canvas.Sprites)
                {
                    var stage = canvas.Videos.Single(v => v.Bounds.Contains(sprite.X, sprite.Y)).Bounds;
                    float nominalWidth = stage.Height * (widescreen ? 16f / 9 : 4f / 3);
                    float expectedWidth = Math.Min(stage.Width, nominalWidth);
                    if (Math.Abs(sprite.Clip.Width - expectedWidth) > .02 || Math.Abs(sprite.Clip.Height - stage.Height) > .02)
                        throw new Exception("Storyboard mask did not retain 480-high centered 4:3/16:9 coordinates.");
                }
                view.UpdateTransport(499, 10000, true, false, false, null, null); Paint();
                if (canvas.Videos.Count != 0 || canvas.Backgrounds.Count != 2) throw new Exception("Video offset boundary omitted the background or requested an early frame.");
                if (canvas.VideoActive || canvas.VideoStops == 0) throw new Exception("Seeking before the video offset retained the decoder.");
                view.UpdateTransport(500, 10000, true, false, false, null, null); Paint();
                if (canvas.Videos.Count != 2 || canvas.Videos.Any(v => v.TimeMs != 0)) throw new Exception("Video offset was not mapped to media time zero.");
            }
            view.LibrarySettings.ForceBackgroundDim = true; view.UpdateTransport(6000, 10000, true, false, false, null, null); Paint();
            if (canvas.Videos.Count != 1) throw new Exception("Force dim did not suppress preview media while retaining the mapping canvas.");
            for (int frame = 0; frame < 3; frame++)
            {
                Paint();
                if (canvas.Videos.Count != 1 || canvas.VideoStops != 0 || !canvas.VideoActive)
                    throw new Exception("Force dim retired the Mapping decoder while suppressing Preview media.");
            }
            canvas.VideoReady = false; Paint();
            if (canvas.VideoStops != 0 || !canvas.VideoActive)
                throw new Exception("An unready video request retired its decoder before a frame could arrive.");
            canvas.VideoReady = true;
            typeof(EditorView).GetMethod("ShowTimingPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [true]); Paint();
            if (!view.TimingPageVisible || canvas.Videos.Count != 0 || canvas.VideoActive || canvas.VideoStops == 0)
                throw new Exception("A frame without video viewports retained its decoder.");
            typeof(EditorView).GetMethod("ShowTimingPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [false]); Paint();
            if (!canvas.VideoActive || canvas.VideoStops != 0) throw new Exception("Returning to Mapping did not restore video demand.");
            typeof(EditorView).GetMethod("ToggleVideoStoryboard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null); Paint();
            if (canvas.Videos.Count != 0 || canvas.VideoActive || canvas.VideoStops == 0)
                throw new Exception("Disabling video retained the decoder.");
            typeof(EditorView).GetMethod("ToggleVideoStoryboard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
            warm.Restart();
            do { Paint(); if (Pending(view)) Thread.Sleep(5); } while (Pending(view) && warm.Elapsed.TotalSeconds < 5);
            if (Pending(view) || !canvas.VideoActive) throw new Exception("Enabling video did not restore Mapping demand.");
            view.UpdateTransport(6000, 10000, false, false, false, null, null); view.StartTestplay(); Paint();
            if (!view.IsTestplaying || canvas.Videos.Count != 0 || canvas.Sprites.Count != 0) throw new Exception("Force dim leaked media into actual testplay.");
            if (canvas.VideoActive || canvas.VideoStops == 0) throw new Exception("Force dim retained a video decoder during testplay.");
            view.StopTestplay(); Paint();
            if (!canvas.VideoActive) throw new Exception("Leaving testplay did not restore Mapping video demand.");
            view.ShowLibrary(); Paint();
            if (!view.LibraryVisible || canvas.Videos.Count != 0 || canvas.VideoActive || canvas.VideoStops == 0)
                throw new Exception("Library navigation retained the video decoder.");
            view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
            Console.WriteLine($"PASS {(widescreen ? "16:9" : "4:3")} storyboard mask, preview 4:3/16:9/Fit, dim 0/50/100, video offset and Force dim");

            void Paint() { canvas.Clear(); view.Render(canvas, 1440, 900); }
        }
        return 0;
    }

    private static void OpeningCancellation(string directory)
    {
        var map = new MapDocument { SourcePath = Path.Combine(directory, "cancel.osu"), DurationMs = 10000 };
        map.Fruits.Add(new() { TimeMs = 5000, X = 256 });
        map.OriginalSections.Add(new() { Name = "Events", Lines = { "Sprite,Foreground,Centre,\"sprite.png\",320,240", " F,0,0,10000,1,1" } });
        var view = new EditorView(false); var canvas = new MediaCanvas { RequiredReady = false };
        view.LibrarySettings.Workspace = Path.Combine(directory, "cancel-workspace"); view.LoadDocument(map); view.CloseLibrary();
        void Paint() { canvas.Clear(); view.Render(canvas, 1440, 900); }
        Paint();
        if (!view.LibraryOpening) throw new Exception("Controlled first-frame latency did not hold the loading gate.");
        view.ChangeAudioPath("unsaved.ogg");
        void LogExitState(string name)
        {
            object? Field(string field) => typeof(EditorView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view);
            Console.WriteLine($"Opening exit {name}: mediaOpening={Field("mediaOpening")}, exitPending={Field("mediaExitPending")}, aggregateDiscard={view.DiscardConfirmationVisible}, actualDiscard={Field("discardConfirmation") is not null}, dirty={view.IsDirty}, library={view.LibraryVisible}");
        }
        LogExitState("before"); view.ShowLibrary(); LogExitState("after ShowLibrary"); Paint(); LogExitState("after Render");
        if (!view.DiscardConfirmationVisible || !canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("mac.cancel")))
            throw new Exception("Loading swallowed the existing save/discard/cancel modal.");
        view.KeyDown(27, false, false); view.KeyUp(27); Paint();
        if (view.LibraryVisible || !view.HasEditorProject || !view.IsDirty || !view.LibraryOpening)
            throw new Exception("Cancel during loading did not retain the dirty project and resume preparation.");
        view.ShowLibrary(); Paint();
        var discard = canvas.Texts.Single(t => t.Value == FruitsAtelier.Localization.Strings.Get("mac.discard"));
        view.PointerDown(discard.X + 3, discard.Y + 3, 0, false, false); view.PointerUp(discard.X + 3, discard.Y + 3, 0); Paint();
        if (!view.LibraryVisible || view.HasEditorProject || view.LibraryOpening)
            throw new Exception("Discard during loading did not retire the project and return to Library.");
        view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
        Console.WriteLine("PASS loading retains save/discard/cancel input, Cancel resumes loading and Discard returns to Library");
    }

    private static void DifficultySwitchPreparation(string directory)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(EditorView view, string name) => typeof(EditorView).GetField(name, flags)!.GetValue(view);
        var first = new MapDocument { Name = "First difficulty", SourcePath = Path.Combine(directory, "first.osu"), DurationMs = 10000 };
        first.Fruits.Add(new() { TimeMs = 5000, X = 256 });
        first.OriginalSections.Add(new() { Name = "Events", Lines = { "Sprite,Foreground,Centre,\"sprite.png\",320,240", " F,0,0,10000,1,1" } });
        var second = first.DeepClone(); second.Name = "Second difficulty";
        var view = new EditorView(false); var canvas = new MediaCanvas { RequiredReady = false };
        view.LoadProject(BeatmapProject.FromDocuments([first, second])); view.CloseLibrary();
        void Paint() { canvas.Clear(); view.Render(canvas, 1440, 900); }
        void SettleSource()
        {
            var watch = Stopwatch.StartNew();
            do { Paint(); Thread.Sleep(5); } while (SourcePending(view) && watch.Elapsed.TotalSeconds < 5);
            if (SourcePending(view)) throw new TimeoutException("Difficulty storyboard source did not settle.");
        }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        SettleSource();
        Check(view.LibraryOpening && canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("library.opening")),
            "Initial project preparation lost its opening feedback.");
        canvas.RequiredReady = true; Paint();
        Check(!view.LibraryOpening && view.PrepareFileOperation(), "Initial preparation did not unlock input.");
        Check(!view.SwitchDifficulty(-1) && view.SwitchDifficulty(0), "Invalid or unchanged difficulty request failed.");
        canvas.RequiredReady = false;
        Check(view.SwitchDifficulty(1), "Ready editor rejected a difficulty switch.");
        Check(view.SwitchDifficulty(0) && view.SwitchDifficulty(1), "Consecutive switches before rendering were rejected.");
        SettleSource();
        Check(view.LibraryOpening && canvas.Texts.Any(t => t.Value.Contains("Second difficulty", StringComparison.Ordinal))
            && !canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("library.opening")
                || t.Value == FruitsAtelier.Localization.Strings.Get("media.loading")),
            "Difficulty preparation did not retain the visible editor without an opening overlay.");
        Check(!canvas.Fills.Any(f => f.Bounds == new Rect(0, 0, 1440, 900) && f.Opacity < 1),
            "Difficulty preparation dimmed the entire editor.");
        Check(!view.PrepareFileOperation() && !view.SwitchDifficulty(0) && !view.AddDifficulty(),
            "Difficulty preparation admitted competing file operations.");
        Check(!(bool)typeof(EditorView).GetProperty("DockContentInputAvailable", flags)!.GetValue(view)!,
            "Difficulty preparation left native dock content interactive.");
        var baseline = view.Document.DeepClone();
        var position = view.PlayheadMs;
        var viewStart = Field(view, "viewStart"); var zoom = Field(view, "canvasZoom");
        int actions = 0;
        view.RequestSave = view.RequestOpen = view.RequestNewProject = view.RequestExport = view.RequestTogglePlayback = () => actions++;
        view.RequestSeek = _ => actions++;
        foreach (int key in new[] { 83, 79, 78, 69, 90, 89, 9 })
        { view.KeyDown(key, true, false); view.KeyUp(key); }
        foreach (int key in new[] { 32, 112, 46, 81, 86, 35 })
        { view.KeyDown(key, false, false); view.KeyUp(key); }
        view.TextInput('x');
        view.PointerDown(650, 500, 0, false, false); view.PointerMove(720, 550, false, false); view.PointerUp(720, 550, 0);
        view.PointerDoubleClick(650, 500, false, false);
        view.PointerDown(300, 850, 0, false, false); view.PointerUp(300, 850, 0);
        foreach (bool ctrl in new[] { false, true }) view.Wheel(650, 500, 120, ctrl);
        Paint();
        Check(actions == 0 && baseline.ContentEquals(view.Document) && view.PlayheadMs == position
            && Equals(viewStart, Field(view, "viewStart")) && Equals(zoom, Field(view, "canvasZoom"))
            && view.ActiveDifficultyIndex == 1 && !view.IsTestplaying,
            "Difficulty preparation accepted keyboard, pointer, wheel or transport actions.");
        canvas.RequiredReady = true; Paint();
        Check(!view.LibraryOpening && view.PrepareFileOperation() && !view.IsTestplaying,
            "Completed preparation did not unlock the editor or queued blocked testplay input.");
        view.KeyDown(83, true, false); view.KeyUp(83);
        Check(actions == 1, "Save shortcut did not resume after preparation.");
        foreach (bool cancel in new[] { false, true })
        {
            Check(view.SwitchDifficulty(1 - view.ActiveDifficultyIndex), "Worker outcome switch failed.");
            _ = view.Conversion;
            var source = typeof(EditorView).GetField("storyboardLoad", flags)!;
            var worker = (Task<StoryboardScene>)source.GetValue(view)!;
            Check(worker.Wait(TimeSpan.FromSeconds(5)), "Source worker did not finish.");
            var publication = new TaskCompletionSource<StoryboardScene>();
            source.SetValue(view, publication.Task);
            Paint();
            Check(view.LibraryOpening && !view.PrepareFileOperation()
                && !canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("library.opening")),
                "Pending source publication obscured the editor or admitted input.");
            if (cancel) publication.SetCanceled(); else publication.SetException(new IOException("controlled source failure"));
            Paint();
            Check(!view.LibraryOpening && view.PrepareFileOperation() && !view.ErrorVisible,
                "Failed or canceled source preparation retained its input lock.");
        }
        canvas.RequiredReady = false; Check(view.SwitchDifficulty(0), "Second switch failed."); SettleSource();
        view.ChangeAudioPath("unsaved.ogg");
        view.ShowLibrary(); Paint();
        Check(view.DiscardConfirmationVisible && canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("mac.cancel")),
            "Difficulty preparation swallowed the save/discard/cancel dialog.");
        view.KeyDown(27, false, false); view.KeyUp(27); Paint();
        Check(!view.LibraryVisible && view.LibraryOpening && view.IsDirty
            && !canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("library.opening")),
            "Canceling Library exit did not resume preparation in the visible editor.");
        view.ShowLibrary(); Paint();
        var discard = canvas.Texts.Single(t => t.Value == FruitsAtelier.Localization.Strings.Get("mac.discard"));
        view.PointerDown(discard.X + 3, discard.Y + 3, 0, false, false); view.PointerUp(discard.X + 3, discard.Y + 3, 0); Paint();
        Check(view.LibraryVisible && !view.LibraryOpening, "Discard did not retire difficulty preparation.");
        view.LoadProject(BeatmapProject.FromDocuments([first, second])); view.CloseLibrary();
        canvas.RequiredReady = true; SettleSource(); Paint();
        canvas.RequiredReady = false; Check(view.SwitchDifficulty(1), "Escape fixture switch failed."); SettleSource();
        view.KeyDown(27, false, false); view.KeyUp(27); Paint();
        Check(view.LibraryVisible && !view.LibraryOpening, "Escape did not retire preparation and return to Library.");
        view.LoadDocument(first); view.CloseLibrary(); SettleSource();
        Check(view.LibraryOpening && canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("library.opening")),
            "Retired difficulty preparation suppressed the next project's opening feedback.");
        canvas.RequiredReady = true; Paint();
        view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
        Console.WriteLine("PASS difficulty preparation retains editor, blocks input until ready and retires on Library exit");
    }

    private static bool Pending(EditorView view) => view.LibraryOpening || typeof(EditorView).GetField("storyboardLoad", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) is Task;
    private static bool SourcePending(EditorView view) => typeof(EditorView).GetField("storyboardLoad", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) is Task;
    private static bool Near(Rect a, Rect b) => Math.Abs(a.X - b.X) < .02 && Math.Abs(a.Y - b.Y) < .02 && Math.Abs(a.Width - b.Width) < .02 && Math.Abs(a.Height - b.Height) < .02;
    private sealed class MediaCanvas : ICanvas
    {
        private readonly RecordingCanvas inner = new(); private readonly Stack<Rect> clips = new();
        internal bool RequiredReady = true, VideoReady = true, VideoActive;
        internal int VideoStops;
        public bool StoryboardImagesReady(IReadOnlyList<string> paths) => RequiredReady;
        internal readonly List<(Rect Bounds, float Opacity)> Dims = [];
        internal readonly List<(string Path, Rect Bounds, double TimeMs)> Videos = [];
        internal readonly List<(string Path, Rect Bounds)> Backgrounds = [];
        internal readonly List<(string Path, float X, float Y, float Opacity, uint Tint, Rect Clip)> Sprites = [];
        internal readonly List<(Rect Bounds, uint Color, float Opacity)> Fills = [];
        internal IReadOnlyList<RecordingCanvas.Label> Texts => inner.Texts;
        internal void Clear() { inner.Clear(); Dims.Clear(); Videos.Clear(); Backgrounds.Clear(); Sprites.Clear(); Fills.Clear(); clips.Clear(); VideoStops = 0; }
        public void Fill(Rect r, uint color, float radius = 0, float opacity = 1) { Fills.Add((r, color, opacity)); inner.Fill(r, color, radius, opacity); }
        public void Stroke(Rect r, uint color, float width = 1, float radius = 0) => inner.Stroke(r, color, width, radius);
        public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1) => inner.Line(x1, y1, x2, y2, color, width, opacity);
        public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1) => inner.Circle(x, y, radius, color, filled, width, opacity);
        public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false) => inner.Text(text, x, y, size, color, maxWidth, bold);
        public bool Image(string filePath, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1) => inner.Image(filePath, destination, tint, source, opacity);
        public bool BackgroundImage(string filePath, Rect destination) { Backgrounds.Add((filePath, destination)); return true; }
        public bool VideoImage(string path, Rect destination, double positionMs, bool playing, double rate) { Videos.Add((path, destination, positionMs)); VideoActive = true; return VideoReady; }
        public void StopVideo() { VideoStops++; VideoActive = false; }
        public void DimBackground(Rect bounds, uint color, float opacity) => Dims.Add((bounds, opacity));
        public void StoryboardSprite(string path, float x, float y, float scaleX, float scaleY, uint tint, float opacity, float rotationRadians, bool additive, bool flipX, bool flipY, float originX, float originY)
            => Sprites.Add((path, x, y, opacity, tint, clips.Peek()));
        public void Clip(Rect r) { clips.Push(r); inner.Clip(r); }
        public void Unclip() { clips.Pop(); inner.Unclip(); }
    }
}
