using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundLibraryFileTests
{
    private sealed class Clock : TimeProvider
    {
        private long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => now;
        public void Advance(int milliseconds) => now += milliseconds;
    }

    public static void Run()
    {
        string language = L.Language;
        try { foreach (string locale in new[] { "en", "zh-CN" }) { L.SetLanguage(locale); CheckUi(); } }
        finally { L.SetLanguage(language); }
    }

    private static void CheckUi()
    {
        string workspace = Path.GetFullPath(Path.Combine("artifacts/tests/hitsound-files", Guid.NewGuid().ToString("N")));
        string root = Path.Combine(workspace, "HitsoundLibrary"), destination = Path.Combine(root, "Dest");
        Directory.CreateDirectory(destination);
        byte[] wave = HitsoundSamples.CreateWave(CatchObjectKind.Fruit);
        foreach (string name in new[] { "a.wav", "b.wav", "c.wav" }) File.WriteAllBytes(Path.Combine(root, name), wave);
        var clock = new Clock(); var ui = new Ui(false, timeProvider: clock);
        ui.LoadDocument(new MapDocument { DurationMs = 10000 }); ui.Resize(1440, 900);
        ui.View.LibrarySettings.Workspace = workspace; ui.View.ShowHitsoundingPage(); ui.Paint();
        var layouts = ui.View.CaptureDockLayouts(); layouts.Hitsounding!.Left!.Split = .3f;
        ui.View.LibrarySettings.DockLayouts = layouts; ui.View.RestoreDockLayouts(); ui.Paint();
        var baseline = ui.View.Document.DeepClone();
        Check(!ui.Canvas.Texts.Any(t => t.Value.Contains(workspace) || t.Value == "HitsoundLibrary"), "Root navigation omits the absolute path and root name");
        var auditions = new List<string>(); int stopped = 0;
        ui.View.RequestStartLibraryAudition = (sound, token) => { auditions.Add(sound.FilePath!); return Task.FromResult(1000d); };
        ui.View.RequestStopLibraryAudition = () => stopped++;
        Audition("a.wav"); clock.Advance(500); ui.Paint();
        var aRow = Text("a.wav");
        Check(ui.Canvas.Outlines.Any(o => o.Bounds.Y == aRow.Y - 7 && o.Bounds.Height == 30), "Playing row keeps its outline");
        var playingRow = ui.Canvas.Outlines.First(o => o.Bounds.Y == aRow.Y - 7 && o.Bounds.Height == 30).Bounds;
        Check(ui.Canvas.Fills.Any(f => f.Bounds.Y == playingRow.Y && f.Bounds.Height == 30 && Math.Abs(f.Bounds.Width - playingRow.Width / 2) < .1f), "Audition fills half the row at half duration");
        Audition("b.wav");
        Check(stopped == 1 && auditions.Count == 2, "Starting another audition stops the previous one");
        Audition("b.wav"); Check(stopped == 2 && auditions.Count == 2, "Stop button cancels without restarting the sound");
        Audition("a.wav"); clock.Advance(1000); ui.Paint(); Check(stopped == 3, "Completed audition restores its idle state");
        string? opened = null; ui.View.RequestOpenExternalPath = path => opened = path;
        var header = Text(L.Get("hitsounding.library"));
        ui.View.PointerDown(header.X + 5, header.Y + 5, 2, false, false); ui.View.PointerUp(header.X + 5, header.Y + 5, 2); ui.Paint();
        ui.ClickText(L.Get("hitsounding.openFolder"));
        Check(opened == root, "Real right-button events open the current library folder from the header");
        ui.View.PointerDown(header.X + 5, header.Y + 5, 1, false, false); ui.View.PointerUp(header.X + 5, header.Y + 5, 1); ui.Paint();
        Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.openFolder")), "Middle click does not open the context menu");

        Click("a.wav"); Click("b.wav", ctrl: true); Click("a.wav", alt: true); ui.Key(67, ctrl: true);
        Open("Dest"); ui.Key(86, ctrl: true); Wait();
        Check(ui.Canvas.Texts.Any(t => t.Value == "Dest") && !ui.Canvas.Texts.Any(t => t.Value.Contains(root)), "Subdirectory navigation displays a relative path");
        Check(File.Exists(Path.Combine(destination, "b.wav")) && !File.Exists(Path.Combine(destination, "a.wav")), "Ctrl adds and Alt subtracts files before copy/paste");
        ui.Key(86, ctrl: true); Wait();
        Check(File.Exists(Path.Combine(destination, "b (2).wav")), "Copy preserves existing names without overwriting");
        Click("b (2).wav"); ui.Key(113); ui.Key(65, ctrl: true); ui.Type("renamed.wav"); ui.Key(13); Wait();
        Check(File.Exists(Path.Combine(destination, "renamed.wav")), "F2 renames the selected file");
        Click("renamed.wav"); ui.Key(88, ctrl: true); ui.ClickText("←"); ui.Key(86, ctrl: true); Wait();
        Check(File.Exists(Path.Combine(root, "renamed.wav")) && !File.Exists(Path.Combine(destination, "renamed.wav")), "Cut/paste moves files across directories");

        ui.Key(78, ctrl: true, shift: true); ui.Key(65, ctrl: true); ui.Type("New"); ui.Key(13); Wait();
        Check(Directory.Exists(Path.Combine(root, "New")), "Ctrl+Shift+N creates the named folder");
        Click("a.wav"); Click("c.wav", ctrl: true);
        var start = Text("a.wav"); var folder = Text("New");
        ui.View.PointerDown(start.X + 4, start.Y + 4, 0, false, false);
        ui.View.PointerMove(folder.X + 4, folder.Y + 4, false, false); ui.View.PointerUp(folder.X + 4, folder.Y + 4, 0); ui.Paint(); Wait();
        Check(File.Exists(Path.Combine(root, "New", "a.wav")) && File.Exists(Path.Combine(root, "New", "c.wav"))
            && !File.Exists(Path.Combine(root, "a.wav")), "Dragging a selection moves all files into a folder");

        Open("New");
        var a = Text("a.wav"); var c = Text("c.wav");
        ui.View.PointerDown(a.X + 40, c.Y + 45, 0, false, false);
        ui.View.PointerMove(a.X + 2, a.Y, false, false); ui.View.PointerUp(a.X + 2, a.Y, 0); ui.Paint();
        ui.Key(67, ctrl: true); ui.ClickText("⌂"); ui.Key(86, ctrl: true); Wait();
        Check(File.Exists(Path.Combine(root, "a.wav")) && File.Exists(Path.Combine(root, "c.wav")), "Marquee selects multiple files for copying");

        foreach (string button in new[] { "←", "⌂" })
        {
            Open("New"); Click("a.wav"); Click("c.wav", ctrl: true);
            start = Text("a.wav"); var nav = Text(button);
            ui.View.PointerDown(start.X + 4, start.Y + 4, 0, false, false);
            ui.View.PointerMove(nav.X + 3, nav.Y + 3, false, false); ui.Paint();
            clock.Advance(999); ui.Paint();
            Check(!ui.Canvas.Texts.Any(t => t.Value == "Dest"), "Navigation waits a full second");
            clock.Advance(1); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == "Dest") && ui.View.WantsCapture, "Hover navigation retains a multi-file drag after one second");
            folder = Text("Dest"); ui.View.PointerMove(folder.X + 4, folder.Y + 4, false, false);
            ui.View.PointerUp(folder.X + 4, folder.Y + 4, 0); ui.Paint(); Wait();
            Check(File.Exists(Path.Combine(destination, "a.wav")) && File.Exists(Path.Combine(destination, "c.wav")), "Drop after navigation retains the original source paths");
            File.Move(Path.Combine(destination, "a.wav"), Path.Combine(root, "New", "a.wav"));
            File.Move(Path.Combine(destination, "c.wav"), Path.Combine(root, "New", "c.wav"));
            ui.View.ShowHitsoundingPage(); ui.Paint();
        }

        string[] recycled = [];
        ui.View.RequestRecycleHitsoundingFiles = paths =>
        {
            recycled = paths.ToArray();
            string trash = Path.Combine(workspace, "test-trash"); Directory.CreateDirectory(trash);
            foreach (string path in paths) File.Move(path, Path.Combine(trash, Path.GetFileName(path)));
        };
        Click("a.wav"); Click("c.wav", ctrl: true); ui.Key(46); Wait();
        Check(recycled.Length == 2 && !File.Exists(Path.Combine(root, "a.wav")), "Delete sends all selected files to the recycle callback");
        Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Library operations do not edit the beatmap or its history");

        bool rejected = false;
        try { HitsoundLibraryFiles.Transfer(root, [destination], Path.Combine(destination, "Sub"), true); }
        catch (IOException) { rejected = true; }
        Check(rejected && Directory.Exists(destination), "Moving a folder into itself is rejected");
        rejected = false;
        try { HitsoundLibraryFiles.Transfer(root, [destination], workspace, true); }
        catch (IOException) { rejected = true; }
        Check(rejected && Directory.Exists(destination), "Internal operations cannot escape the library root");

        for (int i = 0; i < 40; i++) File.WriteAllBytes(Path.Combine(root, $"scroll-{i:00}.wav"), wave);
        Open("Dest"); ui.ClickText("⌂"); ui.Paint();
        var bar = ui.View.HitsoundingLibraryScrollbarBounds;
        Check(bar.Height > 0, "Overflowing list displays a scrollbar");
        ui.View.PointerDown(bar.X + 3, bar.Y + 3, 0, false, false);
        ui.View.PointerMove(bar.X + 3, bar.Bottom - 1, false, false);
        ui.View.PointerUp(bar.X + 3, bar.Bottom - 1, 0); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "scroll-39.wav"), "Dragging the scrollbar reaches the last file: " + bar + " / " + string.Join(",", ui.Canvas.Texts.Where(t => t.Value.Contains("scroll-")).Select(t => t.Value)));
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("hitsounding.folderCounts", 2, 42)), "Footer counts the current folder independently of the visible rows");

        void Audition(string name)
        { var row = Text(name); ui.Click(row.X - 20, row.Y + 8); }

        (float X, float Y) Text(string name)
        { var text = ui.Canvas.Texts.First(t => t.Value == name); return (text.X, text.Y); }
        void Click(string name, bool ctrl = false, bool alt = false)
        {
            var text = Text(name); ui.View.SetModifiers(alt, false);
            ui.View.PointerDown(text.X + 4, text.Y + 4, 0, false, ctrl); ui.View.PointerUp(text.X + 4, text.Y + 4, 0);
            ui.View.SetModifiers(false, false); ui.Paint();
        }
        void Open(string name)
        { var text = Text(name); ui.View.PointerDoubleClick(text.X + 4, text.Y + 4, false, false); ui.Paint(); }
        void Wait()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do { Thread.Sleep(5); ui.Paint(); if (!ui.View.WaveformNeedsRedraw) { Check(!ui.View.ErrorVisible, "File operation completes without an error"); return; } } while (DateTime.UtcNow < deadline);
            throw new TimeoutException("Library file operation did not complete");
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
