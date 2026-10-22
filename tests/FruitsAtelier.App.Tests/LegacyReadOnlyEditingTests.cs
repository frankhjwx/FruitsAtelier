using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LegacyReadOnlyEditingTests
{
    public static void Run()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var ui = Load(mode);
            var before = ui.View.Document.DeepClone();
            foreach (bool head in new[] { false, true })
            {
                double time = head ? 1000 : 2000, x = head ? 100 : 300;
                ui.ClickMap(time, x); ui.DownMap(time, x); ui.MoveMap(time + 125, x); ui.UpMap(time + 125, x);
                Check(before.ContentEquals(ui.View.Document), "Catch endpoint drag changed a read-only Legacy Slider.");
            }
            foreach (var kind in new[] { CatchObjectKind.Droplet, CatchObjectKind.TinyDroplet })
            {
                var child = ui.View.Conversion.Objects.First(o => o.Kind == kind);
                ui.ClickMap(child.TimeMs, child.X); ui.DownMap(child.TimeMs, child.X);
                ui.MoveMap(child.TimeMs, child.X + 10); ui.UpMap(child.TimeMs, child.X + 10);
                Check(before.ContentEquals(ui.View.Document), "Catch child drag changed a read-only Legacy Slider.");
            }
            ui.ClickMap(1000, 100); ui.ClickMap(1500, 400, ctrl: true);
            ui.Key(39, ctrl: true);
            Check(before.ContentEquals(ui.View.Document), "Catch insertion/nudge changed a read-only Legacy Slider.");
            ui.Key('1'); ui.ClickMap(1000, 100);
            var p = ui.ScreenAt(1000, 100);
            ui.View.PointerDoubleClick(p.X, p.Y, false, false); ui.Paint();
            Check(before.ContentEquals(ui.View.Document), "Double-click converted the Legacy Slider.");

            Guid id = ui.View.Document.ImportedSliders.Single().Id;
            ui.View.ConvertSelectedSliderRepresentation(id); ui.Paint();
            Check(ui.View.Document.ImportedSliders.Count == 0 && ui.View.Document.Tracks.Single().Id == id,
                "Explicit conversion did not preserve the slider identity.");
            Check(OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches, "Explicitly converted path failed export verification.");
            var converted = ui.View.Document.DeepClone();
            ui.EditTrack(id);
            var headNode = ui.View.Document.Tracks.Single().Nodes[0];
            ui.DownMap(headNode.TimeMs, headNode.X); ui.MoveMap(headNode.TimeMs + 125, headNode.X);
            ui.View.CancelInteraction(); ui.Paint();
            Check(converted.ContentEquals(ui.View.Document), "Cancelling FSlider editing changed the explicit conversion.");
            headNode = ui.View.Document.Tracks.Single().Nodes[0];
            double start = headNode.TimeMs, xStart = headNode.X;
            ui.DownMap(start, xStart); ui.MoveMap(start + 125, xStart); ui.UpMap(start + 125, xStart);
            Check(!converted.ContentEquals(ui.View.Document), "An explicitly converted FSlider remained read-only in Catch.");
            var edited = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); Check(converted.ContentEquals(ui.View.Document), "FSlider edit did not undo independently.");
            ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Explicit conversion did not restore the original Legacy Slider in one undo.");
            ui.Key('Y', ctrl: true); ui.Key('Y', ctrl: true);
            Check(edited.ContentEquals(ui.View.Document), "Redo changed converted geometry.");
            var accepted = ui.View.Document.DeepClone();
            ui.View.ConvertSelectedSliderRepresentation(Guid.NewGuid());
            Check(accepted.ContentEquals(ui.View.Document) && ui.View.StatusMessage.Length > 0, "Failed conversion changed source content or omitted its reason.");
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
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
