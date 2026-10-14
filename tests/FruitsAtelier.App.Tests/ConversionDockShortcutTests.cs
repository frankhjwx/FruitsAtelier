using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ConversionDockShortcutTests
{
    private sealed class ManualTime : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ticks;
    }

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument();
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]);
                map.Tracks.Add(track);
                var clock = new ManualTime();
                var ui = new Ui(false, timeProvider: clock); ui.LoadDocument(map);
                ui.Key('F', ctrl: true, shift: true);
                Check(!ui.View.StreamDialogVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("dock.patternSelection")), "F opens an empty dock with selection guidance");
                ui.SelectTrack(track.Id); ui.Key('F', ctrl: true, shift: true);
                Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0
                    && ui.View.Document.Tracks.Single().StreamSnapDivisor is not null, "F converts through the live dock");
                var converted = ui.View.Document.DeepClone();
                var collapsed = ui.View.CaptureDockLayouts(); collapsed.Mapping!.Right!.Collapsed = true;
                ui.View.LibrarySettings.DockLayouts = collapsed; ui.View.RestoreDockLayouts(); ui.Paint();
                ui.Key('F', ctrl: true, shift: true);
                Check(ui.View.Document.ContentEquals(converted)
                    && !ui.View.CaptureDockLayouts().Mapping!.Right!.Collapsed
                    && ui.View.CaptureDockLayouts().Mapping!.Right!.Panels!.Count(p => p == "PatternEditor") == 1,
                    "Repeated shortcut focuses the same dock without another edit");
                ui.Key('Z', ctrl: true);
                Check(ui.View.Document.ContentEquals(map), "Dock conversion is one undoable edit");

                ui = new Ui(false, timeProvider: clock); ui.LoadDocument(map); ui.SelectTrack(track.Id); ui.MoveMap(1500, 200);
                ui.Key('F', shift: true);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("conversion.title"))
                    && !ui.View.CaptureDockLayouts().Mapping!.Right!.Panels!.Contains("PatternEditor")
                    && ui.View.Document.ContentEquals(map), "Shift+F shows the action list without opening or converting");
                ui.ClickText(L.Get("conversion.title"));
                Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0, "Action list jumps to the live dock");

                var fruits = new MapDocument();
                fruits.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 180 }, new() { TimeMs = 2000, X = 300 }]);
                ui = new Ui(false, timeProvider: clock); ui.LoadDocument(fruits); ui.Key('A', ctrl: true);
                ui.Key('M', ctrl: true, shift: true);
                Check(!ui.View.MergeDialogVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.confirm"))
                    && ui.View.Document.ContentEquals(fruits), "M opens merge choices in Inspector without editing");
                ui.ClickText(L.Get("merge.confirm"));
                Check(ui.View.Document.Tracks.Count == 1 && ui.View.Document.Fruits.Count == 0, "Merge applies from the dock");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(fruits), "Merge undo restores the selected objects");

                ui = new Ui(false, timeProvider: clock); ui.LoadDocument(fruits); ui.Key('A', ctrl: true);
                ui.Key('F', shift: true);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.title")) && ui.View.Document.ContentEquals(fruits),
                    "Shift+F supports a selected fruit group");
                ui.Key(27);
                ui.HoldMap(1500, 180, () => clock.Ticks += 1000);
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.title")) && !ui.View.MergeDialogVisible
                    && ui.View.Document.ContentEquals(fruits), "Holding a group shows actions without merging");
                ui.ClickText(L.Get("merge.title"));
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("merge.confirm")) && !ui.View.MergeDialogVisible,
                    "Group action jumps to merge choices in the dock");
                ui.View.NewProject();
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
