using System.Reflection;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SimplifiedSynchronizationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                using var fixture = new Fixture(size);
                var ui = fixture.Ui;
                fixture.Change(Fixture.Text.Replace("Title:Original", "Title:Incoming").Replace("1500,1,0", "1500,5,0"));
                var before = ui.View.Document.DeepClone();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.mode.title")), "mode prompt appears first");
                var promptButton = ui.View.SyncModeSimpleBounds;
                ui.View.PointerMove(promptButton.X + 8, promptButton.Y + 8, false, false); ui.Paint();
                Check(ui.Canvas.Fills.Any(f => f.Bounds == promptButton && f.Color == 0x3D495A), "prompt buttons follow pointer hover");
                Check(ui.Canvas.Texts.Last(t => t.Value == L.Get("sync.mode.auto")).Size == 12, "secondary prompt uses compact button text");
                var choices = Choices(ui);
                ui.Click(40, 98); ui.Click(ui.Width - 160, ui.Height - 35);
                ui.View.Wheel(200, 250, -1200, false); ui.View.PointerDoubleClick(200, 250, false, false);
                ui.Key('A', ctrl: true); ui.Key(46); ui.Key('S', ctrl: true); ui.Key('Z', ctrl: true); ui.View.TextInput('x'); ui.Paint();
                Check(choices.Count == 0 && ui.View.Document.ContentEquals(before), "prompt blocks underlying choices and editing");
                ui.ClickText(L.Get("sync.mode.auto")); Wait(ui);
                Check(!ui.View.SynchronizationVisible && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Incoming",
                    "one click imports metadata");
                Check(ObjectFlags.NewCombo(ui.View.Document, ui.View.Document.Fruits.Single(f => f.TimeMs == 1500).Id), "one click imports NC");
                Console.WriteLine($"PASS simplified metadata and NC, modal isolation {locale} {size}");

                fixture.Change(File.ReadAllText(fixture.Source).Replace("Title:Incoming", "Title:Revised").Replace("123,192,1000", "300,192,1000"));
                ui.ClickText(L.Get("sync.mode.auto"));
                Check(ui.View.SynchronizationVisible, "actual object changes still require review");
                Check(!ui.Canvas.Texts.Any(t => t.Value == "Metadata"), "only actual changes are browsable");
                ui.ClickText(L.Get("sync.chooseLocal"));
                var decisions = new Dictionary<string, bool>(Choices(ui));
                ui.ClickText(L.Get("sync.mode.detailed"));
                Check(decisions.All(p => Choices(ui).TryGetValue(p.Key, out bool choice) && choice == p.Value), "switching modes retains choices");
                ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(ui.View.Document.Fruits.Single(f => f.TimeMs == 1000).X == 123
                    && OsuBeatmapReader.Setting(ui.View.Document, "Metadata", "Title") == "Revised", "local geometry and external metadata apply together");
                Console.WriteLine($"PASS mixed choices and detailed-mode switch {locale} {size}");

                fixture.Change(File.ReadAllText(fixture.Source).Replace("Title:Revised", "Title:Cancelled"));
                if (size.Item1 == 980) ui.ClickText(L.Get("sync.mode.detailed"));
                before = ui.View.Document.DeepClone(); string outside = File.ReadAllText(fixture.Source);
                ui.View.GetType().GetField("sliderImportTargets", Private)!.SetValue(ui.View, new[] { 0 });
                var cancel = ui.Canvas.Texts.Last(t => t.Value == L.Get("mac.cancel"));
                ui.Click(cancel.X + 8, cancel.Y + 8);
                ui.View.GetType().GetField("mediaOpening", Private)!.SetValue(ui.View, true);
                ui.Paint();
                ui.Key('F', ctrl: true);
                Check((int)ui.View.GetType().GetField("libraryField", Private)!.GetValue(ui.View)! == 2,
                    "hidden import prompts do not intercept Library search after Cancel");
                ui.Key(27);
                Check(ui.View.LibraryVisible && !ui.View.LibraryOpening && !ui.View.SynchronizationVisible,
                    "cancel releases the Library input gate while media preparation is pending");
                ui.Key(27);
                Check(ui.View.DiscardConfirmationVisible, "Library responds to Escape after cancellation");
                Check(ui.View.Document.ContentEquals(before) && File.ReadAllText(fixture.Source) == outside, "cancellation retains authoring and external data");
                Console.WriteLine($"PASS cancel to responsive Library {locale} {size}");
            }
            using (var closing = new Fixture((980, 620)))
            {
                closing.Change(Fixture.Text.Replace("Title:Original", "Title:Incoming"));
                var beforeClose = closing.Ui.View.Document.DeepClone();
                closing.Ui.View.PrepareApplicationClose();
                Check(!closing.Ui.View.SynchronizationVisible && closing.Ui.View.PrepareFileOperation(), "window close exits review and reaches normal close validation");
                Check(closing.Ui.View.Document.ContentEquals(beforeClose), "closing review retains unsaved authoring");
            }
            using var timing = new Fixture((980, 620), slider: true);
            timing.Change(Fixture.Text.Replace("150,192,1500,1,0,0:0:0:0:", "150,192,1500,2,0,L|350:192,1,400")
                .Replace("SliderTickRate:1", "SliderTickRate:2"));
            timing.Ui.ClickText(L.Get("sync.mode.auto"));
            Check(timing.Ui.View.SynchronizationVisible, "tick-rate changes affecting nested objects require review");
            Console.WriteLine("PASS timing/difficulty gameplay classification");
            foreach (bool retainSlider in new[] { true, false })
            {
                using var objects = new Fixture((980, 620), slider: retainSlider);
                int count = CatchStreamConverter.Convert(objects.Ui.View.Document, true).Objects.Count;
                string incoming = Fixture.Text.Replace("SliderTickRate:1", "SliderTickRate:2");
                if (retainSlider) incoming = incoming.Replace("150,192,1500,1,0,0:0:0:0:\n", "");
                else incoming = incoming.Replace("150,192,1500,1,0,0:0:0:0:", "150,192,1500,2,0,L|350:192,1,400");
                objects.Change(incoming);
                objects.Ui.ClickText(L.Get("sync.mode.auto"));
                Check(objects.Ui.View.SynchronizationVisible && !Choices(objects.Ui).ContainsKey("Difficulty/SliderTickRate"),
                    "tick-rate changes require review for retained local sliders and external additions");
                objects.Ui.ClickText(L.Get("sync.chooseLocal"));
                objects.Ui.ClickText(L.Get("sync.chooseLocal"));
                Check(Choices(objects.Ui).TryGetValue("Difficulty/SliderTickRate", out bool externalTickRate) && !externalTickRate,
                    "simplified review permits retaining the local tick rate independently of object changes");
                objects.Ui.ClickText(L.Get("sync.applyChoices")); Wait(objects.Ui);
                Check(!objects.Ui.View.SynchronizationVisible && objects.Ui.View.Document.SliderTickRate == 1
                    && CatchStreamConverter.Convert(objects.Ui.View.Document, true).Objects.Count == count,
                    "retaining local objects and tick rate preserves the playable sequence");
            }
            Console.WriteLine("PASS timing classification across local retention and external addition choices");
            using (var streams = new Fixture((980, 620)))
            {
                var ui = streams.Ui;
                var history = (EditorHistory)ui.View.GetType().GetProperty("history", Private)!.GetValue(ui.View)!;
                history.Begin("Create stream");
                history.Document.Fruits.Clear();
                history.Document.BeatLengthMs = history.Document.TimingPoints[0].BeatLengthMs = 1;
                var track = new CurveTrack { StreamSnapDivisor = 4, SourceOrder = 0 };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1002, X = 400 }]);
                history.Document.Tracks.Add(track); history.Commit();
                ui.View.SaveCurrentDifficulty(); Wait(ui);
                ui.View.WorkspaceSession!.Manifest.Difficulties[0].ExportConfirmed = false;
                string incoming = File.ReadAllText(streams.Source);
                var lines = WorkspaceSynchronization.ObjectLines(incoming);
                Check(lines.GroupBy(line => line.Split(',')[2]).Any(g => g.Count() > 1), "stream fixture has colliding published event times");
                for (int i = 0; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(','); parts[4] = i % 2 == 0 ? "2" : "8";
                    incoming = incoming.Replace(lines[i], string.Join(',', parts));
                }
                streams.Change(incoming);
                ui.ClickText(L.Get("sync.mode.auto"));
                Check(ui.View.SynchronizationVisible && Choices(ui).Keys.All(k => !k.StartsWith("$objects:")),
                    "simplified mode requires a choice before a sound edit can flatten a stream");
                ui.ClickText(L.Get("sync.chooseLocal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                var retained = ui.View.Document.Tracks.Single();
                Check(retained.Id == track.Id && retained.StreamSnapDivisor == track.StreamSnapDivisor
                    && retained.Nodes.Select(n => (n.Id, n.TimeMs, n.X, n.HandleIn, n.HandleOut))
                        .SequenceEqual(track.Nodes.Select(n => (n.Id, n.TimeMs, n.X, n.HandleIn, n.HandleOut)))
                    && ui.View.Document.Fruits.Count == 0, "retaining FA in simplified mode preserves stream authoring");
                ui.View.RefreshSynchronization(reviewResolved: true); Wait(ui);
                ui.ClickText(L.Get("sync.mode.auto"));
                Check(ui.View.SynchronizationVisible
                    && Choices(ui).Any(p => p.Key.StartsWith("$objects:resolved:") && !p.Value),
                    "simplified review retains the explicit local choice when reopening an unrepresentable stream conflict");
                ui.ClickText(L.Get("sync.chooseLocal")); ui.ClickText(L.Get("sync.applyChoices")); Wait(ui);
                Check(ui.View.Document.Tracks.Single().Id == track.Id && ui.View.Document.Fruits.Count == 0,
                    "repeated simplified review preserves retained stream authoring");
            }
            Console.WriteLine("PASS simplified review protects unrepresentable stream samples");
        }
        finally { L.SetLanguage(language); }
    }
    private static Dictionary<string, bool> Choices(Ui ui) => (Dictionary<string, bool>)ui.View.GetType().GetField("syncChoices", Private)!.GetValue(ui.View)!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        do { ui.Paint(); if (!ui.View.SynchronizationBusy && !ui.View.LibraryOpening) { ui.Paint(); return; } Thread.Sleep(5); } while (DateTime.UtcNow < deadline);
        throw new TimeoutException("Simplified synchronization timed out");
    }
    private sealed class Fixture : IDisposable
    {
        public const string Text = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Original\nArtist:Artist\nCreator:Mapper\nVersion:Catch\n[Difficulty]\nCircleSize:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n123,192,1000,1,0,0:0:0:0:\n150,192,1500,1,0,0:0:0:0:\n";
        public Ui Ui { get; }
        public string Source => WorkspaceSynchronization.Target(Ui.View.WorkspaceSession!.Manifest.Difficulties[0])!;
        public Fixture((int Width, int Height) size, bool slider = false)
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/simplified-sync", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, slider ? Text.Replace("150,192,1500,1,0,0:0:0:0:", "150,192,1500,2,0,L|350:192,1,400") : Text);
            Ui = new Ui(false); Ui.Resize(size.Width, size.Height);
            Ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); Ui.View.LibrarySettings.Songs = songs;
            Ui.View.LoadWorkspace(WorkspaceProject.Create(Ui.View.LibrarySettings.Workspace, BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs));
            Wait(Ui); Ui.View.StopFileMonitoring();
            Ui.View.WorkspaceSession!.Manifest.Difficulties[0].ExportConfirmed = true;
        }
        public void Change(string text) { File.WriteAllText(Source, text); Ui.View.RefreshSynchronization(); Wait(Ui); }
        public void Dispose() => Ui.View.StopFileMonitoring();
    }
}
