using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderCanvasLockTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 5000 };
                var legacy = new ImportedSlider { TimeMs = 1000, X = 64, Y = 64, PixelLength = 128, PathType = 'L' };
                legacy.ControlPoints.AddRange([new(64, 64), new(192, 64)]);
                map.ImportedSliders.Add(legacy);
                var track = new CurveTrack();
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 320 }, new() { TimeMs = 1500, X = 448 }]);
                map.Tracks.Add(track);
                var ui = new Ui(false); ui.LoadDocument(map); ui.Key('1');
                string path = Path.GetFullPath($"artifacts/tests/slider-canvas-lock-{locale}.json");
                int saves = 0;
                ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
                Check(!ui.View.LibrarySettings.LockSlidersToCanvas, "Canvas selection lock must start disabled.");
                Toggle();
                var restored = LibrarySettings.Load(path);
                Check(restored.LockSlidersToCanvas && saves == 1, "Canvas selection lock was not saved globally.");
                SelectBoth();
                Check(ui.View.SelectedObjectIds.SequenceEqual(new[] { track.Id }), "Catch must select only FSliders when locked.");
                ui.View.ToggleOsuCanvas(); ui.View.UpdateTransport(900, 5000, true, false, false, null, null); ui.Paint();
                SelectBoth();
                Check(ui.View.SelectedObjectIds.SequenceEqual(new[] { legacy.Id }), "osu must select only Legacy Sliders when locked.");
                var head = ui.View.OsuCanvasScreen(320, 192);
                Check(ui.View.OsuCanvasHit(head.X, head.Y) is null, "Locked FSlider remained pickable in osu.");
                head = ui.View.OsuCanvasScreen(64, 64);
                Check(ui.View.OsuCanvasHit(head.X, head.Y) == legacy.Id, "Native Legacy Slider became unpickable.");
                Toggle(); SelectBoth();
                Check(ui.View.SelectedObjectIds.Count == 2 && !LibrarySettings.Load(path).LockSlidersToCanvas,
                    $"Disabling the canvas lock did not restore cross-canvas selection or persistence: selected={ui.View.SelectedObjectIds.Count}, live={ui.View.LibrarySettings.LockSlidersToCanvas}, saved={LibrarySettings.Load(path).LockSlidersToCanvas}, saves={saves}.");
                Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Canvas selection preferences changed content.");

                void Toggle()
                {
                    var button = ui.View.AssistButtonBounds[6];
                    ui.View.PointerMove(button.X + 5, button.Y + 5, false, false); ui.Paint();
                    ui.ClickText(L.Get("assist.lockSlidersToCanvas"));
                }
                void SelectBoth() => typeof(EditorView).GetMethod("SelectObjects",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(ui.View, [new[] { legacy.Id, track.Id }, legacy.Id]);
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
