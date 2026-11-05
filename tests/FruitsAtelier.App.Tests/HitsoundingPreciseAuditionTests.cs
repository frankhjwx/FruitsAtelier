using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingPreciseAuditionTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    public static void Run()
    {
        string workspace = Path.GetFullPath("artifacts/tests/precise-audition");
        string library = Path.Combine(workspace, "HitsoundLibrary"); Directory.CreateDirectory(library);
        File.WriteAllBytes(Path.Combine(library, "audition.wav"), HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        var map = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 1 });
        map.Fruits.Add(new() { TimeMs = 1000, X = 256, OriginalLine = "256,192,1000,1,2,0:0:0:0:" });
        map.Fruits.Add(new() { TimeMs = 2000, X = 256, OriginalLine = "256,192,2000,1,4,0:0:0:0:" });
        var clock = new Clock(); var ui = new Ui(false, timeProvider: clock);
        ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.LibrarySettings.Workspace = workspace;
        ui.View.ShowHitsoundingPage(); ui.View.UpdateTransport(1200, 8000, true, false, false, null, null);
        ui.View.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.5f, 1000).ToArray(), 1));
        var played = new List<Hitsound>(); int stops = 0;
        ui.View.RequestStartLibraryAudition = (sound, token) => { played.Add(sound); return Task.FromResult(1000d); };
        ui.View.RequestStopLibraryAudition = () => stops++;
        var baseline = ui.View.Document.DeepClone(); ui.Paint();
        Select(2); Check(ui.View.HitsoundingPreciseAuditionBounds.Width == 0, "Note-based selection does not show the Precise audition control");
        ui.ClickText(L.Get("hitsounding.precise")); Select(2);
        Check(played.Count == 0 && ui.View.HitsoundingPreciseAuditionBounds.Width > 0, "Selecting a Precise block exposes Play without automatically playing");
        var selectedBox = Block(2); var playButton = ui.View.HitsoundingPreciseAuditionBounds;
        Check(playButton.X >= selectedBox.X && playButton.Right < selectedBox.X + selectedBox.Width / 2
            && playButton.Y > selectedBox.Y + selectedBox.Height / 2 - 1 && playButton.Bottom <= selectedBox.Bottom,
            "Play sits inside the block's lower-left waveform area");
        Play(); Check(played.Count == 1 && played[0].Name == "hitwhistle", "Play auditions the selected track's resolved sample");
        clock.Now += 500; ui.Paint(); var box = Block(2);
        Check(ui.Canvas.Fills.Any(f => f.Bounds.Y == box.Y && f.Bounds.Height == box.Height && Math.Abs(f.Bounds.Width - box.Width / 2) < .1f), "Precise audition shows elapsed progress inside its outlined block");
        var button = ui.View.HitsoundingPreciseAuditionBounds;
        Check(ui.Canvas.Fills.Any(f => f.Bounds.Width == 6 && f.Bounds.Height == 6 && button.Contains(f.Bounds.X, f.Bounds.Y)), "Active audition uses a Stop icon");
        Select(3); Check(played.Count == 1, "Selecting a different sound does not start it");
        Play(); Check(stops == 1 && played.Count == 2 && played[1].Name == "hitfinish", "Starting another Precise sound replaces the previous audition");
        Play(); Check(stops == 2 && played.Count == 2, "Stop cancels without replaying");
        LibraryPlay(); Check(played.Count == 3, "Library audition starts");
        Play(); Check(stops == 3 && played.Count == 4, "Precise audition stops an active library sound");
        LibraryPlay(); Check(stops == 4 && played.Count == 5, "Library audition stops an active Precise sound");
        clock.Now += 1000; ui.Paint(); Check(stops == 5, "Audition completes and resets its control");
        var selected = Block(3);
        ui.View.Wheel(selected.X, selected.Y, -960, false, false, true); ui.Paint();
        Check(Block(3).Width is >= 24 and < 48 && ui.View.HitsoundingPreciseAuditionBounds.Width > 0, "Compact blocks still show the small audition control");
        ui.View.Wheel(selected.X, selected.Y, -240, false, false, true); ui.Paint();
        Check(Block(3).Width < 24 && ui.View.HitsoundingPreciseAuditionBounds.Width == 0, "Blocks too narrow to fit the control hide it");
        ui.View.Wheel(selected.X, selected.Y, 1200, false, false, true); ui.Paint();
        Check(ui.View.HitsoundingPreciseAuditionBounds.Width > 0, "The control returns when the selected block is wide enough");
        ui.ClickText(L.Get("hitsounding.noteBased"));
        Check(ui.View.HitsoundingPreciseAuditionBounds.Width == 0 && baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty && ui.View.PlayheadMs == 1200,
            "Audition and mode changes preserve document history and transport position");
        void Play() { var r = ui.View.HitsoundingPreciseAuditionBounds; Check(r.Width > 0, "Selected sound exposes Play"); ui.Click(r.X + r.Width / 2, r.Y + r.Height / 2); }
        void LibraryPlay() { var label = ui.Canvas.Texts.Single(t => t.Value == "audition.wav"); ui.Click(label.X - 20, label.Y + 8); }
        void Select(int lane) { var r = Block(lane); ui.Click(r.X + Math.Min(4, r.Width / 2), r.Y + 6); }
        Rect Block(int lane)
        {
            var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("hitsounding.lane" + lane) && t.X > 340 && t.X < 1100);
            return ui.Canvas.Outlines.First(o => o.Color == 0x65BF8A && o.Bounds.Y <= label.Y + 8 && o.Bounds.Bottom > label.Y + 8).Bounds;
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
