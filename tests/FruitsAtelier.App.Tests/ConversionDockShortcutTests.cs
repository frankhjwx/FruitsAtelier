using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using System.Collections;
using System.Reflection;
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
                CheckContextMenuParity();
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

    private static void CheckContextMenuParity()
    {
        foreach (string kind in new[] { "fruit", "fruits", "slider", "legacy", "stream", "stack", "mixed", "banana" })
        {
            var map = new MapDocument { DurationMs = 6000, DerandomizeFSliderDroplets = true };
            if (kind is "fruit" or "fruits" or "mixed") map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
            if (kind == "fruits") map.Fruits.Add(new() { TimeMs = 1500, X = 300 });
            if (kind is "slider" or "stream" or "stack" or "mixed")
            {
                var track = new CurveTrack { Kind = CurveKind.Linear,
                    StreamSnapDivisor = kind is "stream" or "stack" ? 4 : null,
                    Stack = kind == "stack" ? new StackEnvelope() : null };
                track.Nodes.AddRange([new() { TimeMs = kind == "mixed" ? 2000 : 1000, X = 100 }, new() { TimeMs = 3000, X = 300 }]);
                map.Tracks.Add(track);
            }
            if (kind == "legacy")
            {
                var slider = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 200 };
                slider.ControlPoints.AddRange([new(100, 192), new(300, 192)]);
                map.ImportedSliders.Add(slider);
            }
            if (kind == "banana") map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 2000 });
            var clock = new ManualTime();
            var ui = new Ui(false, clock); ui.LoadDocument(map);
            ui.View.LibrarySettings.LazerEditing = true;
            ui.View.UpdateTransport(500, 6000, true, false, false, null, null);
            ui.Paint(); ui.Key('1'); ui.Key('A', ctrl: true);
            var before = ui.View.Document.DeepClone();
            var conversion = ui.View.Conversion;
            var point = ((float X, float Y))Call("Screen", new MapPoint(1000, 100))!;
            // Banana sprites are randomized; use the selected shower's interior for its hold target.
            if (kind == "banana") point = ((float, float))Call("Screen", new MapPoint(1500, 256))!;
            RightClick(point.X, point.Y);
            string[] right = Items();
            Check(right.Any(s => s.StartsWith(L.Get("editor.command.copy") + "|")), kind + " right-click opens object commands");
            ui.Key(27);
            ui.View.PointerDown(point.X, point.Y, 0, false, false); ui.Paint();
            clock.Ticks += 1000; ui.Paint();
            ui.View.PointerUp(point.X, point.Y, 0); ui.Paint();
            Check(right.SequenceEqual(Items()), kind + " hold and right-click labels, states and shortcuts match");
            ui.Key(27); ui.View.PointerMove(point.X, point.Y, false, false); ui.Key('F', shift: true);
            Check(right.SequenceEqual(Items()), kind + " keyboard action list matches pointer menus");
            ui.Key(27);
            var timeline = ((IEnumerable)Field("timelineObjects")!).Cast<object>().First();
            var bounds = (Rect)timeline.GetType().GetField("Item3")!.GetValue(timeline)!;
            RightClick(bounds.X + 19 * ui.View.ObjectTimelineBounds.Height / 64, bounds.Y + bounds.Height / 2);
            Check(right.SequenceEqual(Items()), kind + " timeline context menu matches canvas");
            Check(ui.View.Document.ContentEquals(before) && ReferenceEquals(conversion, ui.View.Conversion),
                kind + " opening menus preserves content and conversion");
            ui.ClickObjectAction(L.Get("editor.command.delete"));
            Check(!ui.View.Document.ContentEquals(before), kind + " shared delete action operates on selection");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(before), kind + " menu delete undoes in one step");
            ui.View.NewProject();

            void RightClick(float x, float y)
            { ui.View.PointerDown(x, y, 2, false, false); ui.View.PointerUp(x, y, 2); ui.Paint(); }
            object? Field(string name) => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View);
            object? Call(string name, params object?[] args) => typeof(EditorView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui.View, args);
            string[] Items() => ((IEnumerable)Field("contextItems")!).Cast<object>().Select(item =>
                string.Join("|", new[] { "Label", "Enabled", "Shortcut", "SliderAction" }.Select(p => item.GetType().GetProperty(p)!.GetValue(item)))).ToArray();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
