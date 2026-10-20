using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class NativeImportedPathTests
{
    public static void Run()
    {
        foreach (string geometry in new[] { "L|400:300,1,350", "P|400:100|250:350,2,400", "B|180:80|300:300|400:100,1,400" })
        {
            var source = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:0\n[TimingPoints]\n0,500\n[HitObjects]\n100,192,1000,2,0," + geometry);
            source.DurationMs = 6000;
            var ui = new Ui(false);
            ui.LoadDocument(source);
            ui.View.UpdateTransport(1000, 6000, true, false, false, null, null);
            ui.Paint();
            var originalLines = Lines();
            Check(originalLines.Length > 0, "Original Catch path was not drawn.");
            source.SliderImportState = SliderImportState.Pending;
            source.DerandomizeFSliderDroplets = true;
            var result = SliderImportEditing.Initialize(source, false);
            Check(result.Success, string.Join("; ", result.Diagnostics));
            var export = OsuBeatmapWriter.Serialize(source).Text;
            ui.LoadDocument(source); ui.View.UpdateTransport(1000, 6000, true, false, false, null, null); ui.Paint();
            Check(originalLines.SequenceEqual(Lines()), "Derandomize changed the original Legacy path on Catch canvas.");
            Check(OsuBeatmapWriter.Serialize(ui.View.Document).Text == export, "Drawing changed export compensation.");
            var cold = Lines();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int n = 0; n < 30; n++) ui.Paint();
            watch.Stop();
            Check(cold.SequenceEqual(Lines()), "Cached Catch path changed across redraws.");
            Console.WriteLine($"Native Legacy Catch path warm paint: {watch.Elapsed.TotalMilliseconds / 30:F3} ms ({geometry[0]}; recording canvas).");
            ui.View.ShowRandomDock(); ui.Paint();
            ui.ClickText(L.Get("randomize.derandomizeOn")); Wait();
            Check(!ui.View.Document.DerandomizeFSliderDroplets, "Map toggle did not change the effective mode.");
            var random = OsuBeatmapWriter.Serialize(ui.View.Document);
            ui.Key('Z', ctrl: true); Wait();
            Check(ui.View.Document.DerandomizeFSliderDroplets && OsuBeatmapWriter.Serialize(ui.View.Document).Text == export,
                "Mode undo changed compensation or the authored baseline.");
            ui.Key('Y', ctrl: true); Wait();
            Check(OsuBeatmapWriter.Serialize(ui.View.Document).Text == random.Text, "Mode redo was not deterministic.");

            (float, float, float, float)[] Lines() => ui.Canvas.Lines.Where(l => l.Color == 0xAB9DF2)
                .Select(l => (l.X1, l.Y1, l.X2, l.Y2)).ToArray();
            void Wait()
            {
                var until = DateTime.UtcNow.AddSeconds(30);
                while (ui.View.SliderConversionBusy && DateTime.UtcNow < until) { ui.Paint(); Thread.Sleep(5); }
                ui.Paint(); Check(!ui.View.SliderConversionBusy, "Mode change timed out.");
            }
        }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
