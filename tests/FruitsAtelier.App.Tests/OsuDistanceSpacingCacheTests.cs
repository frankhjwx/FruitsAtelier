using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class OsuDistanceSpacingCacheTests
{
    public static void Run()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n64,64,1000,1,0\n100,192,2000,2,0,L|240:192,1,140\n256,192,3500,8,0,3750");
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 4000, X = 100 }, new() { TimeMs = 4500, X = 200 }]);
        map.Tracks.Add(track); map.DurationMs = 8000;
        var ui = new Ui(false); ui.View.LibrarySettings.LazerEditing = false;
        ui.LoadDocument(map); ui.View.ToggleOsuCanvas(); ui.Paint();
        var baseline = ui.View.Document.DeepClone();
        var conversion = ui.View.Conversion;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var exportField = typeof(EditorView).GetField("playableExport", flags)!;
        var snapshotField = typeof(EditorView).GetField("convertedSnapshot", flags)!;
        var export = (OsuWriteResult)exportField.GetValue(ui.View)!;
        var snapshot = snapshotField.GetValue(ui.View);
        Check(export is not null, "DS cache fixture did not export");
        var control = ui.View.OsuDistanceSpacingTrackBounds;
        float y = control.Y + control.Height / 2;
        ui.View.PointerDown(control.X, y, 0, false, false); ui.Paint();
        for (int i = 0; i <= 32; i++)
        {
            ui.View.PointerMove(control.X + control.Width * i / 32, y, false, false); ui.Paint();
            SameCache();
        }
        ui.View.PointerUp(control.Right, y, 0); ui.Paint(); SameCache();
        Check(ui.View.Document.OsuDistanceSpacing == 3.2 && !baseline.ContentEquals(ui.View.Document)
            && baseline.ContentEqualsIgnoringOsuDistanceSpacing(ui.View.Document) && ui.View.IsDirty,
            "Ignoring DS in conversion changed history/content equality");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document)).OsuDistanceSpacing == 3.2,
            "DS gesture did not persist");
        Check(OsuBeatmapWriter.Serialize(ui.View.Document).Text == export!.Text, "DS-only changes affect exported content");
        ui.Key('Z', ctrl: true); SameCache();
        Check(ui.View.Document.ContentEquals(baseline) && !ui.View.IsDirty, "DS undo did not restore the saved baseline");
        ui.Key('Y', ctrl: true); SameCache();
        Check(ui.View.Document.OsuDistanceSpacing == 3.2 && ui.View.IsDirty, "DS redo did not restore preference history");
        var geometry = ui.View.Document.DeepClone(); geometry.Fruits[0].Y++;
        Check(!geometry.ContentEqualsIgnoringOsuDistanceSpacing(ui.View.Document), "Conversion equality ignored geometry changes");

        void SameCache()
        {
            Check(ReferenceEquals(conversion, ui.View.Conversion) && ReferenceEquals(export, exportField.GetValue(ui.View))
                && ReferenceEquals(snapshot, snapshotField.GetValue(ui.View)), "DS dragging or undo rebuilt conversion/export snapshots");
            Check(((MapDocument)snapshot!).ContentEquals(ui.View.Document), "Reused conversion snapshot retained an obsolete DS preference");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
