using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class HitsoundingMuteTests
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
        foreach (bool scheduled in new[] { false, true })
        {
            string root = Path.GetFullPath("artifacts/tests/hitsounding-mute"); Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "sb.wav"), HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[Events]\nSample,750,0,\"sb.wav\",100\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n256,192,1000,1,14,0:0:0:0:\n256,192,1250,2,0,L|356:192,1,200\n");
            map.SourcePath = Path.Combine(root, "map.osu"); map.DurationMs = 4000;
            var clock = new Clock(); var ui = new Ui(false, timeProvider: clock);
            var prepared = new List<Hitsound>(); ui.View.RequestPrepareHitsound = prepared.Add;
            ui.LoadDocument(map); ui.Resize(1440, 900); ui.View.ShowHitsoundingPage();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do { Thread.Sleep(5); ui.Paint(); } while (!prepared.Any(s => s.Name == "storyboard") && DateTime.UtcNow < deadline);
            Check(prepared.Any(s => s.Name == "storyboard"), "Storyboard sample fixture is ready");
            var baseline = ui.View.Document.DeepClone();
            var sounds = new List<Hitsound>(); ui.View.RequestHitsound = sounds.Add;
            if (scheduled) ui.View.RequestScheduleHitsound = (sound, _) => sounds.Add(sound);
            string[] names = ["hitnormal", "hitwhistle", "hitfinish", "hitclap", "slidertick", "storyboard"];
            var all = Replay();
            Check(names.All(name => all.Contains(name)), "All six tracks dispatch before muting");
            for (int lane = 1; lane <= 6; lane++)
            {
                Toggle(lane);
                var button = ui.View.HitsoundingMuteBounds(lane);
                Check(ui.Canvas.PaintCalls.Any(p => p.Opacity == .65f && p.FillBounds is { } bounds
                    && bounds.X > button.Right && bounds.Width > 300 && bounds.Y <= button.Y && bounds.Bottom >= button.Bottom),
                    "Muted track dims the entire grid row");
                var heard = Replay();
                Check(!heard.Contains(names[lane - 1]) && names.Where((_, i) => i != lane - 1).All(heard.Contains), "Mute filters only its own track");
                Toggle(lane);
                Check(Replay().Contains(names[lane - 1]), "Unmute restores the track");
            }
            int stops = 0; ui.View.RequestStopHitsounds = () => stops++;
            ui.View.UpdateTransport(900, 4000, true, true, false, null, null); Toggle(2);
            Check(stops > 0 && ui.View.AudioPlaying && ui.View.PlayheadMs == 900, "Live mute cancels queued sounds without changing transport");
            ui.View.UpdateTransport(900, 4000, true, false, false, null, null); Toggle(2);
            for (int lane = 1; lane <= 6; lane++) Toggle(lane);
            Check(Replay().Length == 0, "All muted tracks are silent in Hitsounding");
            ui.ClickText(L.Get("hitsounding.panel") + " ▾"); ui.ClickText(L.Get("timing.detailsPanel"));
            Check(names.All(Replay().Contains), "Mapping playback ignores Hitsounding mute states");
            ui.View.ShowHitsoundingPage(); ui.Paint();
            Check(Replay().Length == 0, "Returning to Hitsounding restores its session mute choices");
            ui.View.RequestScheduleHitsound = null;
            ui.View.UpdateTransport(0, 4000, true, false, false, null, null);
            ui.View.UpdateTransport(0, 4000, false, false, false, null, null);
            ui.View.LibrarySettings.TestplayStartupDelaySeconds = 0;
            ui.View.StartTestplay();
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (!ui.View.IsTestplaying && DateTime.UtcNow < deadline) { Thread.Sleep(5); ui.Paint(); }
            Check(ui.View.IsTestplaying, "Testplay starts with muted tracks: " + ui.View.StatusMessage);
            ui.Key(9); ui.View.KeyUp(9); sounds.Clear(); clock.Advance(3500); ui.Paint();
            Check(names.Take(4).All(name => sounds.Any(s => s.Name == name)), "Testplay plays normal and additions despite editor track mutes");
            ui.View.StopTestplay();
            Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Mute never changes map content or undo state");
            string[] Replay()
            {
                ui.View.UpdateTransport(0, 4000, true, false, false, null, null); ui.View.StartHitsounds(0); sounds.Clear();
                for (int time = 0; time <= 2500; time += 50) ui.View.UpdateTransport(time, 4000, true, true, false, null, null);
                ui.View.UpdateTransport(2500, 4000, true, false, false, null, null); ui.Paint();
                return sounds.Select(s => s.Name).ToArray();
            }
            void Toggle(int lane)
            { var button = ui.View.HitsoundingMuteBounds(lane); ui.Click(button.X + 12, button.Y + 12); }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
