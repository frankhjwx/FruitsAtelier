using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Media;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class MediaPanelTests
{
    internal static void Performance()
    {
        var ui = new Ui(false); ui.LoadDocument(DemoMap.Create()); ui.Resize(1440, 900);
        Measure("Media hidden");
        ui.View.ShowMediaPanel(); ui.Paint(); Measure("Media visible");
        void Measure(string label)
        {
            for (int i = 0; i < 20; i++) ui.Paint();
            long bytes = GC.GetAllocatedBytesForCurrentThread(); var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) { ui.View.PointerMove(ui.Plot.X + i % 100, ui.Plot.Y + 30, false, false); ui.Paint(); }
            Console.WriteLine($"{label}: pointer + recorded render {timer.Elapsed.TotalMilliseconds / 200:F3} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 200} B per iteration");
        }
    }
    internal static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/media-panel", Guid.NewGuid().ToString("N")));
        string input = Path.Combine(root, "input"), first = Path.Combine(root, "map"), second = Path.Combine(root, "other");
        Directory.CreateDirectory(input); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        string image = Path.Combine(input, "bg.png"), video = Path.Combine(input, "video.mp4");
        File.WriteAllText(image, "image"); File.WriteAllText(video, "video");
        File.WriteAllText(Path.Combine(first, "bg.png"), "old");
        File.WriteAllText(Path.Combine(second, "bg (1).png"), "unrelated");
        string previous = L.Language;
        try
        {
            foreach (string language in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(language);
                var document = new MapDocument { IsDemo = false, SourcePath = Path.Combine(first, "a.osu") };
                document.OriginalSections.Add(new OsuSection { Name = "Events", Lines = { "// keep", "2,5000,7000", "Sprite,Background,Centre,\"sb.png\",320,240", " F,0,0,1000,1" } });
                var other = document.DeepClone(); other.SourcePath = Path.Combine(second, "b.osu");
                SongSetup.SetColours(other, [0x123456, 0x654321]);
                var ui = new Ui(false); ui.View.LoadProject(BeatmapProject.FromDocuments([document, other])); ui.PaintUntilReady(); ui.Resize(1440, 900);
                ui.View.LibrarySettings.Workspace = root;
                ui.View.ShowMediaPanel(); ui.Paint();
                Check(ui.Canvas.Texts.Count(t => t.Value == L.Get("media.unbound")) == 2, "Empty bindings displayed");
                Check(ui.View.MediaOffsetBounds.Width == 0 && !ui.Canvas.Texts.Any(t => t.Value == L.Get("media.clear")), "Empty cards omit offset and unbind controls");
                Check(ui.View.CanDropFile(video), "Video accepted in mapping");
                ui.View.DropLibraryFiles([image]); Wait(ui);
                var bg = BeatmapMedia.Read(ui.View.Document).Background!;
                Check(bg != "bg.png" && File.ReadAllText(Path.Combine(first, "bg.png")) == "old", "Collision preserves original");
                ui.View.RequestMediaFile = (isVideo, answer) => answer(isVideo ? video : image);
                ui.Paint();
                var videoPicker = ui.Canvas.Texts.Where(t => t.Value == L.Get("media.choose")).OrderBy(t => t.Y).Last();
                ui.Click(videoPicker.X + 3, videoPicker.Y + 3); Wait(ui);
                Check(BeatmapMedia.Read(ui.View.Document) is { Video: not null, OffsetMs: 0 }, "Video starts at zero");
                var offset = ui.View.MediaOffsetBounds; ui.Click(offset.X + 8, offset.Y + 8); ui.Type("-1234"); ui.Key(13);
                Check(BeatmapMedia.Read(ui.View.Document).OffsetMs == -1234, "Signed offset editing");
                ui.Key('Z', ctrl: true); Check(BeatmapMedia.Read(ui.View.Document).OffsetMs == 0, "Offset undo");
                ui.Key('Y', ctrl: true);
                ui.Click(ui.View.MediaSyncBounds.X + 10, ui.View.MediaSyncBounds.Y + 10); Wait(ui);
                var synced = ui.View.CaptureProject().Difficulties[1].Document;
                var media = BeatmapMedia.Read(synced);
                Check(media.OffsetMs == -1234 && File.ReadAllText(Path.Combine(second, media.Background!)) == "image"
                    && File.ReadAllText(Path.Combine(second, media.Video!)) == "video", "Sync copies resources across directories");
                Check(SongSetup.Colours(synced).SequenceEqual(new uint[] { 0x123456, 0x654321 })
                    && synced.OriginalSections.Single(s => s.Name == "Events").Lines.Contains(" F,0,0,1000,1"), "Sync preserves colours and storyboard");
                ui.Key('Z', ctrl: true); Check(BeatmapMedia.Read(ui.View.CaptureProject().Difficulties[1].Document).Video is null, "Set-wide undo");
                ui.Key('Y', ctrl: true); Check(BeatmapMedia.Read(ui.View.CaptureProject().Difficulties[1].Document) == media, "Set-wide redo");
                int resourceCount = Directory.GetFiles(second).Length;
                ui.Click(ui.View.MediaSyncBounds.X + 10, ui.View.MediaSyncBounds.Y + 10); Wait(ui);
                Check(Directory.GetFiles(second).Length == resourceCount, "Repeated sync reuses identical resources");
                var pixels = Fixture();
                ui.View.DecodePaletteImage = _ => pixels;
                ui.ClickText(L.Get("media.palette"));
                var plus = ui.Canvas.Texts.Single(t => t.Value == "+" && t.X >= ui.View.MediaPanelViewport.X);
                ui.Click(plus.X + 3, plus.Y + 3);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("media.colourCount", 5)), "Palette count increment");
                var minus = ui.Canvas.Texts.Single(t => t.Value == "−" && t.X >= ui.View.MediaPanelViewport.X);
                ui.Click(minus.X + 3, minus.Y + 3);
                ui.Click(ui.View.MediaGenerateBounds.X + 10, ui.View.MediaGenerateBounds.Y + 10); Wait(ui);
                ui.Click(ui.View.MediaApplyBounds.X + 10, ui.View.MediaApplyBounds.Y + 10);
                Check(SongSetup.Colours(ui.View.Document).Length == 4, "Default palette applies");
                ui.Key('Z', ctrl: true); Check(SongSetup.Colours(ui.View.Document).Length == 0, "Palette undo");
                Check(BeatmapMedia.Read(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document))) == BeatmapMedia.Read(ui.View.Document), "Project media roundtrip");
                Check(BeatmapMedia.Read(OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack) == BeatmapMedia.Read(ui.View.Document), "Osu media roundtrip");
                foreach (int count in Enumerable.Range(3, 6))
                {
                    var colours = BackgroundPalette.Generate(pixels, count);
                    Check(colours.Length == count && colours.Distinct().Count() == count, "Requested distinct colour count");
                    Check(colours.All(c => BackgroundPalette.Luminosity(c) is >= 60 and <= 210), "RC luminosity margin");
                    Check(colours.All(c => BackgroundPalette.PerceivedBrightness(c) is >= 60 and <= 225), "Perceived brightness margin");
                    Check(colours.SequenceEqual(BackgroundPalette.Generate(pixels, count)), "Deterministic palette");
                }
            }
            string choices = Path.Combine(root, "choices");
            Directory.CreateDirectory(choices);
            File.WriteAllText(Path.Combine(choices, "current.png"), "image");
            var choiceMap = new MapDocument { IsDemo = false, SourcePath = Path.Combine(choices, "map.osu") };
            new BeatmapMedia("current.png", null, 0).Apply(choiceMap);
            var choiceUi = new Ui(false); choiceUi.LoadDocument(choiceMap); choiceUi.Resize(1440, 900);
            choiceUi.View.LibrarySettings.Workspace = root;
            choiceUi.View.ShowMediaPanel(); choiceUi.Paint();
            while (choiceUi.View.MediaFilesBusy) { Thread.Sleep(10); choiceUi.Paint(); }
            Check(choiceUi.View.MediaLibraryBounds.Width == 0, "No alternatives section when only bound media exist");
            File.WriteAllText(Path.Combine(choices, "other.png"), "other");
            var choiceTimer = System.Diagnostics.Stopwatch.StartNew();
            string choiceLabel = "▸ " + L.Get("media.library", 1);
            while (!choiceUi.Canvas.Texts.Any(t => t.Value == choiceLabel))
            {
                Thread.Sleep(10); choiceUi.Paint();
                if (choiceTimer.Elapsed.TotalSeconds > 12) throw new TimeoutException("Existing media did not refresh automatically");
            }
            choiceUi.ClickText(choiceLabel);
            Check(choiceUi.Canvas.Texts.Count(t => t.Value == "current.png") == 1, "Bound image is not duplicated in alternatives");
            choiceUi.ClickText(L.Get("media.useBackground")); Wait(choiceUi);
            Check(BeatmapMedia.Read(choiceUi.View.Document).Background == "other.png", "Existing image assigned through explicit action");
            var gray = new StoryboardImageCache.Pixels(1, 1, [0, 0, 0, 255]);
            var grayColours = BackgroundPalette.Generate(gray, 8);
            Check(grayColours.Length == 8 && grayColours.All(c => (c >> 16 & 255) == (c >> 8 & 255) && (c & 255) == (c >> 8 & 255)), "Gray source remains gray");
            foreach (uint colour in new uint[] { 0, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF })
            {
                var solid = BackgroundPalette.Generate(new(1, 1, [(byte)colour, (byte)(colour >> 8), (byte)(colour >> 16), 255]), 8);
                Check(solid.Length == 8 && solid.All(c => BackgroundPalette.Luminosity(c) is >= 60 and <= 210
                    && BackgroundPalette.PerceivedBrightness(c) is >= 60 and <= 225), "Solid/extreme backgrounds produce safe variants");
            }
            var preserved = new MapDocument();
            preserved.OriginalSections.Add(new OsuSection { Name = "Events", Lines = { "0,0,\"bg.png\",10,20", "1,10,\"v.mp4\",30,40", "// storyboard" } });
            (BeatmapMedia.Read(preserved) with { OffsetMs = -20 }).Apply(preserved);
            Check(preserved.OriginalSections[0].Lines.Contains("0,0,\"bg.png\",10,20")
                && preserved.OriginalSections[0].Lines.Contains("Video,-20,\"v.mp4\",30,40"), "Offset edit preserves media placement");
            preserved.SourcePath = Path.Combine(first, "positioned.osu");
            var clear = new Ui(false); clear.View.LoadProject(BeatmapProject.FromDocuments([new MapDocument(), preserved])); clear.PaintUntilReady();
            clear.View.LibrarySettings.Workspace = root; clear.View.ShowMediaPanel(); clear.Paint();
            clear.Click(clear.View.MediaSyncBounds.X + 10, clear.View.MediaSyncBounds.Y + 10); Wait(clear);
            Check(BeatmapMedia.Read(clear.View.CaptureProject().Difficulties[1].Document).Background is null, "Sync propagates unbound state");
            clear.Key('Z', ctrl: true);
            var restored = clear.View.CaptureProject().Difficulties[1].Document.OriginalSections.Single(s => s.Name == "Events").Lines;
            Check(restored.Contains("0,0,\"bg.png\",10,20") && restored.Contains("Video,-20,\"v.mp4\",30,40"), "Sync undo restores original media placement");
            var local = new Ui(false); local.LoadDocument(new MapDocument { IsDemo = false });
            local.View.LibrarySettings.Workspace = Path.Combine(root, "workspace"); local.View.LibrarySettings.OsuRoot = "";
            local.View.DropLibraryFiles([image]);
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (BeatmapMedia.Read(local.View.Document).Background is null && timeout.Elapsed.TotalSeconds < 10) { Thread.Sleep(5); local.Paint(); }
            Wait(local); Check(local.View.WorkspaceSession is not null, "FA storage created");
            local.View.SaveWorkspace();
            string directory = local.View.WorkspaceSession!.Directory;
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(local.View.Document.SourcePath!)!, BeatmapMedia.Read(local.View.Document).Background!)), "Workspace save retains physical media");
            local.View.LoadWorkspace(WorkspaceProject.Open(directory)); local.PaintUntilReady();
            Check(BeatmapMedia.Read(local.View.Document).Background is not null, "FA media reopens");
            string export = Path.Combine(root, "export"); Directory.CreateDirectory(export);
            FruitsAtelier.App.Platform.BeatmapResources.Copy(local.View.Document, export, local.View.Document);
            Check(File.Exists(Path.Combine(export, BeatmapMedia.Read(local.View.Document).Background!)), "FA media accompanies export");
            var external = new Ui(false);
            external.LoadDocument(new MapDocument { SourcePath = Path.Combine(first, "external.osu"), IsDemo = false });
            external.View.LibrarySettings.Workspace = Path.Combine(root, "external-workspace");
            external.View.LibrarySettings.OsuRoot = "";
            external.View.DropLibraryFiles([image]);
            timeout.Restart();
            while (BeatmapMedia.Read(external.View.Document).Background is null && timeout.Elapsed.TotalSeconds < 10) { Thread.Sleep(5); external.Paint(); }
            Wait(external);
            Check(WorkspaceProject.Within(external.View.LibrarySettings.Workspace, external.View.Document.SourcePath!)
                && File.Exists(Path.Combine(Path.GetDirectoryName(external.View.Document.SourcePath!)!, "bg.png")), "External map media moves into FA resources");
            external.Key('Z', ctrl: true);
            Check(external.View.Document.SourcePath == Path.Combine(first, "external.osu"), "Relocation undo restores original resource base");
            Console.WriteLine("PASS Media panel imports, offset, sync, undo, palette, RC bounds and persistence");
        }
        finally { L.SetLanguage(previous); }
    }
    internal static StoryboardImageCache.Pixels Fixture()
    {
        byte[] data = new byte[64 * 64 * 4];
        for (int i = 0; i < 64 * 64; i++) { data[i * 4] = (byte)(i % 256); data[i * 4 + 1] = (byte)(i / 16 % 256); data[i * 4 + 2] = (byte)(255 - i % 256); data[i * 4 + 3] = 255; }
        return new(64, 64, data);
    }
    internal static void Wait(Ui ui)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do { Thread.Sleep(5); ui.Paint(); if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException("Media worker did not finish"); }
        while (ui.View.MediaEditingBusy || ui.View.MediaPaletteBusy || ui.View.LibraryOpening);
        ui.Paint();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
