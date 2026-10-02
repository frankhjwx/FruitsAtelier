using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class DraftConversionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var map = DemoMap.Create();
            map.Tracks.RemoveAll(t => t.Nodes[0].TimeMs < 6000);
            map.BananaShowers.Add(new BananaShower { TimeMs = 10000, EndTimeMs = 11000 });
            map.AudioPath = null; map.SourcePath = null;
            var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode);
            var baseline = ui.View.Conversion;
            ui.Key('B'); ui.ClickMap(1000, 25);
            var export = Export(ui.View);
            for (int i = 0; i < 10; i++)
            {
                ui.MoveMap(1500 + i * 125, 100 + i * 20);
                Check(ReferenceEquals(export, Export(ui.View)), "draft exports the full map");
                Check(!ui.View.ConversionRefreshing, "draft starts background full-map work");
            }
            ui.View.CancelInteraction(); ui.Paint();
            Check(map.ContentEquals(ui.View.Document), "cancel changed content");
            Check(ReferenceEquals(baseline, ui.View.Conversion), "cancel discarded the committed conversion");

            ui.Key('B'); ui.ClickMap(1000, 25); ui.ClickMap(2375, 320, ctrl: true);
            ui.View.KeyDown(13, false, false);
            Check(ui.View.ConversionRefreshing, "completion did not defer full-map conversion");
            ui.Paint();
            // Supersede the running snapshot before the UI can publish it.
            ui.View.Document.Fruits.Add(new Fruit { TimeMs = 3000, X = 321 });
            Wait(ui);
            AssertExport(ui.View);
            ui.Key('Z', ctrl: true); Wait(ui);
            Check(map.ContentEquals(ui.View.Document), "draft did not undo atomically");
            AssertExport(ui.View);
            ui.Key('Y', ctrl: true); Wait(ui); AssertExport(ui.View);

            ui.Key('B'); ui.ClickMap(4000, 30); ui.ClickMap(4500, 300, ctrl: true);
            ui.View.KeyDown(13, false, false);
            ui.View.StartTestplay();
            if (ui.View.ConversionRefreshing) Check(!ui.View.IsTestplaying, "testplay consumed provisional events");
            Wait(ui);
            Check(ui.View.IsTestplaying, "testplay request was lost while converting");
            ui.View.StopTestplay();

            ui.Key('B'); ui.ClickMap(4750, 50); ui.ClickMap(4875, 350, ctrl: true); ui.Key(13);
            ui.ClickMap(5000, 50);
            var previousExport = Export(ui.View);
            var pending = (Task?)typeof(EditorView).GetField("deferredConversionTask", Private)!.GetValue(ui.View);
            pending?.Wait(TimeSpan.FromSeconds(10));
            ui.MoveMap(5500, 350);
            Check(ReferenceEquals(previousExport, Export(ui.View)), "older worker published during the next draft");
            ui.ClickMap(5500, 350, ctrl: true); ui.Key(13); Wait(ui); AssertExport(ui.View);

            ui.Key('B'); ui.ClickMap(5625, 50); ui.ClickMap(5875, 350, ctrl: true); ui.Key(13);
            ui.View.NewProject(); ui.Paint(); Thread.Sleep(30); ui.Paint();
            Check(!ui.View.ConversionRefreshing && ui.View.Conversion.Objects.Count == 0,
                "retired worker overwrote a new project");
        }
    }

    public static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ui.View.ConversionRefreshing && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(1); }
        Check(!ui.View.ConversionRefreshing, "background conversion timed out");
        ui.Paint();
    }

    private static OsuWriteResult? Export(EditorView view)
        => (OsuWriteResult?)typeof(EditorView).GetField("playableExport", Private)!.GetValue(view);

    public static void AssertExport(EditorView view)
    {
        var expected = OsuBeatmapWriter.Serialize(view.Document, view.CompensateTinyDroplets);
        var actual = Export(view);
        Check(actual is not null && actual.ObjectSequenceMatches && actual.Text == expected.Text,
            "completed export differs from uncached serialization");
        Check(actual!.PlayableObjects.SequenceEqual(expected.PlayableObjects)
            && actual.PlayableHardRockObjects.SequenceEqual(expected.PlayableHardRockObjects),
            "completed NM/HR events differ from uncached serialization");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
