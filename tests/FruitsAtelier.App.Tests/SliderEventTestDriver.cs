using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class SliderEventTestDriver
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    // Exercise the DS candidate/undo boundary independently of higher-priority canvas transforms.
    public static void Down(Ui ui, double time, double x)
    {
        var target = Target(ui, time, x);
        typeof(EditorView).GetMethod("SelectObjects", PrivateInstance)!.Invoke(ui.View, [new[] { target.SourceId }, target.SourceId]);
        var pointer = ui.ScreenAt(time, x);
        typeof(EditorView).GetMethod("BeginSliderObjectDrag", PrivateInstance)!.Invoke(ui.View, [target, pointer.X, pointer.Y]);
        ui.Paint();
    }

    public static void Select(Ui ui, double time, double x)
    {
        var target = Target(ui, time, x);
        typeof(EditorView).GetMethod("SelectObjects", PrivateInstance)!.Invoke(ui.View, [new[] { target.SourceId }, target.SourceId]);
        typeof(EditorView).GetMethod("PickSoundEdge", PrivateInstance)!.Invoke(ui.View, [target]);
        ui.Paint();
    }

    private static ConvertedCatchObject Target(Ui ui, double time, double x)
        => OsuBeatmapWriter.Serialize(ui.View.Document).PlayableObjects
            .Where(o => ui.View.Document.Tracks.Any(t => t.Id == o.SourceId) || ui.View.Document.ImportedSliders.Any(s => s.Id == o.SourceId))
            .OrderBy(o => Math.Abs(o.TimeMs - time)).ThenBy(o => Math.Abs(o.X - x)).First();
}
