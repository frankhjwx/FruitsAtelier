using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class ImplicitSliderEditingTests
{
    public static void Run()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var selectionUi = Load(mode);
            selectionUi.View.Document.Fruits.Add(new() { TimeMs = 1500, X = 400 }); selectionUi.Paint();
            var selectionBefore = selectionUi.View.Document.DeepClone();
            selectionUi.ClickMap(1000, 100); selectionUi.ClickMap(1500, 400, ctrl: true);
            Check(selectionUi.View.SelectedObjectIds.Count == 2 && selectionBefore.ContentEquals(selectionUi.View.Document),
                "Ctrl-selecting another object converted the selected Legacy Slider.");
            foreach (bool head in new[] { false, true })
            {
                var ui = Load(mode);
                var before = ui.View.Document.DeepClone();
                double time = head ? 1000 : 2000, x = head ? 100 : 300;
                ui.ClickMap(time, x);
                Check(before.ContentEquals(ui.View.Document), "Selection converted a Legacy Slider.");
                ui.DownMap(time, x); ui.MoveMap(time + 125, x);
                Check(ui.View.Document.Tracks.Count == 1 && !ui.View.SliderImportPromptVisible, "Vertical drag did not convert silently.");
                ui.Key(27); ui.UpMap(time + 125, x);
                Check(before.ContentEquals(ui.View.Document), "Cancelled conversion drag changed the source.");
                ui.ClickMap(time, x); ui.DownMap(time, x); ui.MoveMap(time + 125, x); ui.UpMap(time + 125, x);
                var track = ui.View.Document.Tracks.Single();
                Near(time + 125, head ? track.Nodes[0].TimeMs : track.Nodes[^1].TimeMs);
                Check(ui.View.Document.ImportedSliders.Count == 0 && track.Id == before.ImportedSliders[0].Id, "Drag lost parent identity.");
                var after = ui.View.Document.DeepClone();
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Drag and conversion need more than one undo.");
                ui.Key('Y', ctrl: true); Check(after.ContentEquals(ui.View.Document), "Redo changed converted geometry.");
            }
            {
                var ui = Load(mode);
                var before = ui.View.Document.DeepClone();
                ui.ClickMap(1000, 100); ui.ClickMap(1500, 400, ctrl: true);
                var track = ui.View.Document.Tracks.Single();
                Check(!ui.View.SliderImportPromptVisible && ui.View.Document.ImportedSliders.Count == 0, "Insertion did not convert silently.");
                Check(SliderControlEditing.Vertices(track).Any(n => Math.Abs(n.Point.TimeMs - 1500) < .001 && Math.Abs(n.Point.X - 400) < .001), "Insertion ignored the pointer away from the curve.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Insertion did not restore the Legacy Slider in one undo.");
                ui.Key('1'); ui.ClickMap(1000, 100); ui.Key('B');
                Check(before.ContentEquals(ui.View.Document) && ui.View.ActiveTool == "Slider"
                    && ui.View.SelectedObjectIds.Count == 0, "Placement converted or retained the selected Legacy Slider.");
                ui.Key('1');
                var point = ui.ScreenAt(1000, 100);
                ui.View.PointerDoubleClick(point.X, point.Y, false, false); ui.Paint();
                Check(!ui.View.SliderImportPromptVisible && ui.View.Document.Tracks.Count == 1, "Entering slider editing asked for conversion.");
            }
            foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
            {
                var ui = Load(mode);
                var before = ui.View.Document.DeepClone();
                var child = ui.View.Conversion.Objects.First(o => o.Kind == kind);
                ui.ClickMap(child.TimeMs, child.X);
                ui.DownMap(child.TimeMs, child.X); ui.MoveMap(child.TimeMs, child.X + 10); ui.UpMap(child.TimeMs, child.X + 10);
                Check(ui.View.Document.Tracks.Count == 1 && !ui.View.SliderImportPromptVisible, "Child drag did not convert its parent.");
                ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Child conversion drag did not undo atomically.");
            }
        }
    }

    private static Ui Load(SliderEditingMode mode)
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        map.DurationMs = 12000;
        var ui = new Ui(); ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode);
        ui.View.LibrarySettings.DerandomizeDroplets = true;
        return ui;
    }
    private static void Near(double a, double b) => Check(Math.Abs(a - b) < .001, $"Expected {a}, got {b}.");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
