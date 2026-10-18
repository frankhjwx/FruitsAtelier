using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SliderDisplayTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (bool arc in new[] { false, true })
            foreach (bool derandomize in new[] { true, false })
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                string curve = arc ? "P|300:64|100:320,1,400" : "L|300:192,1,200";
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n100,192,1000,2,0," + curve + "\n");
                map.ImportedSliders.Single().PreserveNativeGeometry = true;
                map.DurationMs = 5000; map.DerandomizeFSliderDroplets = derandomize;
                if (!derandomize) map.RandomizeDropletStrength = 8;
                var drawn = ImportedSliderConverter.Path(map.ImportedSliders.Single()).ToArray();
                var original = map.DeepClone();
                var result = SliderRepresentationEditing.RefreshLegacyDroplets(map);
                Check(result.Success, string.Join("; ", result.Diagnostics));
                var processed = OsuBeatmapWriter.Serialize(map);
                if (derandomize)
                    Check(processed.PlayableObjects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).All(o =>
                        Math.Abs(o.X - ImportedSliderConverter.PositionAtTime(original, original.ImportedSliders.Single(), o.TimeMs)) < 1),
                        "Native straight/arc TinyDroplets did not return to the authored path.");
                var processedPath = ImportedSliderConverter.Path(processed.ReadBack.ImportedSliders.Single()).ToArray();
                Check(!drawn.SequenceEqual(processedPath), "Display fixture must have distinct drawn and processed geometry.");
                foreach (int width in new[] { 980, 1440 })
                {
                    var ui = new Ui(false); ui.Resize(width, 900); ui.LoadDocument(map);
                    ui.View.ToggleOsuCanvas(); ui.View.UpdateTransport(900, 5000, true, false, false, null, null); ui.Paint();
                    var head = ui.View.OsuCanvasScreen(100, 192); ui.Click(head.X, head.Y);
                    ui.View.ShowSliderInspector(); ui.Paint();
                    head = ui.View.OsuCanvasScreen(100, 192);
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get(derandomize ? "droplet.mode.Derandomize" : "droplet.mode.Randomize")),
                        "The inspector reported the wrong effective Legacy droplet mode.");
                    var baseline = ui.View.Document.DeepClone();
                    var conversion = ui.View.Conversion;
                    var exported = typeof(EditorView).GetField("playableExport", Hidden)!.GetValue(ui.View);
                    Check(Path(ui).SequenceEqual(drawn), "The drawn view must retain the source geometry.");
                    ui.View.PointerDown(head.X, head.Y, 0, false, false);
                    ui.View.PointerMove(head.X + 20, head.Y + 20, false, false);
                    ui.View.PointerUp(head.X + 20, head.Y + 20, 0); ui.Paint();
                    Check(baseline.ContentEquals(ui.View.Document), "Dragging an original-path preview edited processed geometry.");
                    Switch(true); Check(Path(ui).SequenceEqual(processedPath), "Processed view must match actual exported geometry.");
                    for (int i = 0; i < 5; i++) ui.Paint();
                    Switch(false); Check(Path(ui).SequenceEqual(drawn), "Returning to the drawn view lost the source path.");
                    Check(ReferenceEquals(conversion, ui.View.Conversion)
                        && ReferenceEquals(exported, typeof(EditorView).GetField("playableExport", Hidden)!.GetValue(ui.View)),
                        "A display switch rebuilt conversion or export.");
                    Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
                        "A display switch changed content or undo state.");

                    void Switch(bool processedView)
                    {
                        string current = L.Get("inspector.pathDisplay", L.Get(processedView ? "inspector.pathDrawn" : "inspector.pathProcessed"));
                        for (int i = 0; i < 20; i++)
                        {
                            var label = ui.Canvas.Texts.Single(t => t.Value == current);
                            var viewport = ui.View.SliderInspectorViewport;
                            if (label.Y >= viewport.Y && label.Y + 25 <= viewport.Bottom) break;
                            ui.View.Wheel(viewport.X + 10, viewport.Y + 20, -120, false, false); ui.Paint();
                        }
                        ui.ClickText(current); ui.ClickText(L.Get(processedView ? "inspector.pathProcessed" : "inspector.pathDrawn"));
                    }
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static SliderPathPoint[] Path(Ui ui)
    {
        var objects = (Array)typeof(EditorView).GetField("osuCanvasObjects", Hidden)!.GetValue(ui.View)!;
        var item = objects.GetValue(0)!;
        return (SliderPathPoint[])item.GetType().GetField("Path")!.GetValue(item)!;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
