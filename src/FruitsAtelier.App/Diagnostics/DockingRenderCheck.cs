using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class DockingRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/docking-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
                map.Fruits.AddRange(Enumerable.Range(0, 24).Select(i => new Fruit { TimeMs = i * 350, X = i % 2 == 0 ? 100 : 400 }));
                var editor = new EditorView(false);
                editor.LoadDocument(map);
                editor.UpdateTransport(1500, 10000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint(); Click(editor.DifficultyCurveToggleBounds); Click(editor.PreviewToggleBounds); Paint("separate");
                Drag(EditorView.DockPanel.MovementStrain, size.Item1 - 60, 220, "drag-right"); Paint("merged-right");
                Click(editor.DockTabBounds(EditorView.DockPanel.Preview)); Paint("preview-tab");
                Drag(EditorView.DockPanel.Preview, 8, 220, "drag-left"); Paint("swapped");
                var preview = editor.PreviewViewport;
                editor.KeyDown(114, false, false); Paint("timing");
                if (!editor.TimingPageVisible || editor.DockTabBounds(EditorView.DockPanel.Preview).Width > 0)
                    throw new InvalidOperationException("Timing exposed a draggable tab.");
                editor.KeyDown(27, false, false); Paint("mapping-restored");
                if (editor.TimingPageVisible || editor.PreviewViewport != preview)
                    throw new InvalidOperationException("Mapping layout did not restore.");
                Drag(EditorView.DockPanel.MovementStrain, 60, 220, "drag-merge-left"); Paint("merged-left");
                var firstTab = editor.DockTabBounds(EditorView.DockPanel.Preview);
                Drag(EditorView.DockPanel.MovementStrain, firstTab.X + 5, firstTab.Y + 10, "reorder-preview"); Paint("reordered");
                var handle = editor.DockResizeBounds(true);
                editor.PointerDown(handle.X + 4, handle.Y + 60, 0, false, false);
                editor.PointerMove(180, handle.Y + 60, false, false);
                editor.PointerUp(180, handle.Y + 60, 0); Paint("minimum-left");
                Click(editor.DockTabBounds(EditorView.DockPanel.Preview)); Paint("minimum-preview");
                editor.PointerDown(280, 18, 0, false, false); editor.PointerUp(280, 18, 0); Paint("layout-menu");
                editor.KeyDown(27, false, false); Paint();
                if (!map.ContentEquals(editor.Document) || editor.IsDirty || editor.WantsCapture)
                    throw new InvalidOperationException("Docking modified content or retained capture.");
                Click(editor.DockTabBounds(EditorView.DockPanel.Notes));
                Click(editor.NotesBounds); editor.PasteFieldText(L.Get("dock.notesHint") + "\nReview the ending pattern.\nKeep this note across difficulties."); Paint("notes");
                var patternMap = new MapDocument { DurationMs = 10000 };
                var patternTrack = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 8, Stack = new() };
                patternTrack.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
                patternMap.Tracks.Add(patternTrack);
                editor.LoadDocument(patternMap); editor.KeyDown(65, true, false); Paint();
                Click(new(268, 6, 70, 28)); Click(new(274, 259, 270, 31)); Paint("stack-dock");
                patternMap = new MapDocument { DurationMs = 10000 };
                patternMap.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 3000, Pattern = new() });
                editor.LoadDocument(patternMap); editor.KeyDown(65, true, false); Paint("banana-dock");
                editor.Wheel(size.Item1 - 60, 400, -1200, false, false); Paint("banana-dock-scrolled");
                void Click(Rect r)
                { editor.PointerDown(r.X + 10, r.Y + 10, 0, false, false); editor.PointerUp(r.X + 10, r.Y + 10, 0); Paint(); }
                void Drag(EditorView.DockPanel panel, float x, float y, string name)
                {
                    var tab = editor.DockTabBounds(panel);
                    editor.PointerDown(tab.X + 10, tab.Y + 10, 0, false, false);
                    editor.PointerMove(x, y, false, false); Paint(name);
                    editor.PointerUp(x, y, 0); Paint();
                }
                editor.ShowSongDock(); Paint();
                for (int category = 0; category < 4; category++)
                {
                    editor.ShowSongDock(); Paint();
                    var songBounds = editor.SongDockViewport;
                    Click(new(songBounds.X, songBounds.Y, 30, 30));
                    Click(new(songBounds.X + 6, songBounds.Y + 34 + 6 + category * 32, 30, 30));
                    Paint($"song-dock-{category}");
                    editor.Wheel(songBounds.X + 20, songBounds.Y + 80, -12000, false, false);
                    Paint($"song-dock-{category}-scrolled");
                    foreach (var field in editor.SongSetupFieldBounds.Values)
                        if (field.Width <= 0 || field.X < songBounds.X || field.Right > songBounds.Right + .1f)
                            throw new InvalidOperationException("Song dock field exceeds its width.");
                }
                var inspectorMap = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.92 };
                var inspectorTrack = new CurveTrack { Kind = CurveKind.Linear, DropletRandomization = new() { Enabled = true } };
                inspectorTrack.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 3000, X = 300 }]);
                inspectorMap.Tracks.Add(inspectorTrack);
                editor.LoadDocument(inspectorMap); editor.KeyDown(65, true, false);
                editor.ShowSliderInspector(); Paint("inspector");
                var inspectorBounds = editor.SliderInspectorViewport;
                editor.Wheel(inspectorBounds.X + 10, inspectorBounds.Y + 40, -12000, false, false); Paint("inspector-scrolled");
                editor.ShowRandomDock(); Paint();
                var randomField = editor.RandomDockValueBounds(0); Click(randomField);
                editor.KeyDown(65, true, false); editor.PasteFieldText("70"); Paint();
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!editor.RandomPreviewReady && DateTime.UtcNow < deadline) { Thread.Sleep(5); Paint(); }
                if (!editor.RandomPreviewReady || !editor.Document.ContentEquals(inspectorMap))
                    throw new InvalidOperationException("Random draft did not preserve content or complete preview.");
                Paint("random-preview");
                var randomBounds = editor.RandomDockViewport;
                editor.Wheel(randomBounds.X + 10, randomBounds.Y + 40, -12000, false, false); Paint("random-scrolled");
                void Paint(string? name = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height);
                        output.Write((ushort)1); output.Write((ushort)32); output.Write(0); output.Write(frame.Pixels.Length);
                        output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Docking and independent Mapping/Timing workspace native checks passed.");
    }
}
