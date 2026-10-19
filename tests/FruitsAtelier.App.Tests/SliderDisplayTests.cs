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
                    Check(Path(ui).SequenceEqual(drawn), "The drawn view must retain the source geometry.");
                    var raw = baseline.ImportedSliders.Single().RandomizationSource!;
                    int control = arc ? 1 : raw.ControlPoints.Count - 1;
                    var anchor = raw.ControlPoints[control];
                    foreach (var p in raw.ControlPoints)
                    {
                        var at = ui.View.OsuCanvasScreen(p.X, p.GeometryY);
                        Check(ui.Canvas.Fills.Any(f => f.Color == 0xB8B8B8 && f.Bounds.Width == 4 && f.Bounds.Height == 4
                            && Math.Abs(f.Bounds.X - at.X + 2) < .05 && Math.Abs(f.Bounds.Y - at.Y + 2) < .05),
                            "Selected drawn slider did not show an original anchor.");
                    }
                    var from = ui.View.OsuCanvasScreen(anchor.X, anchor.GeometryY);
                    var to = ui.View.OsuCanvasScreen(anchor.X + 16, anchor.GeometryY + 16);
                    ui.View.PointerDown(from.X, from.Y, 0, false, false);
                    ui.View.PointerMove(to.X, to.Y, false, false);
                    ui.View.PointerUp(to.X, to.Y, 0); Wait();
                    var edited = ui.View.Document.ImportedSliders.Single();
                    Check(edited.RandomizationSource is { } changed
                        && changed.ControlPoints.Count == raw.ControlPoints.Count
                        && changed.ControlPoints[control] != anchor,
                        "The drawn path's original control was not editable: " + ui.View.StatusMessage);
                    Check(edited.SliderVelocityMultiplier == baseline.ImportedSliders.Single().SliderVelocityMultiplier
                        && ui.View.Document.TimingPoints.Count == baseline.TimingPoints.Count,
                        "Editing an original control changed authored SV or inserted timing.");
                    ui.Key('Z', ctrl: true); Wait();
                    Check(baseline.ContentEquals(ui.View.Document), "Original control editing did not undo atomically.");
                    head = ui.View.OsuCanvasScreen(100, 192); ui.Click(head.X, head.Y);
                    ui.View.ShowSliderInspector(); ui.Paint();
                    from = ui.View.OsuCanvasScreen(anchor.X, anchor.GeometryY);
                    to = ui.View.OsuCanvasScreen(anchor.X + 32, anchor.GeometryY + 16);
                    ui.View.PointerDown(from.X, from.Y, 0, false, false);
                    ui.View.PointerMove(to.X, to.Y, true, false);
                    ui.View.PointerUp(to.X, to.Y, 0, true); Wait();
                    edited = ui.View.Document.ImportedSliders.Single();
                    Check(edited.RandomizationSource!.ControlPoints[control] != anchor
                        && Math.Abs(ImportedSliderConverter.DurationMs(ui.View.Document, edited)
                            - ImportedSliderConverter.DurationMs(baseline, baseline.ImportedSliders.Single())) < .01,
                        "Shift editing an original control failed to preserve duration: " + ui.View.StatusMessage);
                    ui.Key('Z', ctrl: true); Wait();
                    Check(baseline.ContentEquals(ui.View.Document), "Original Shift control editing did not undo atomically.");
                    head = ui.View.OsuCanvasScreen(100, 192); ui.Click(head.X, head.Y);
                    ui.View.ShowSliderInspector(); ui.Paint();
                    var conversion = ui.View.Conversion;
                    var exported = typeof(EditorView).GetField("playableExport", Hidden)!.GetValue(ui.View);
                    Switch(true); Check(Path(ui).SequenceEqual(processedPath), "Processed view must match actual exported geometry.");
                    for (int i = 0; i < 5; i++) ui.Paint();
                    Switch(false); Check(Path(ui).SequenceEqual(drawn), "Returning to the drawn view lost the source path.");
                    Check(ReferenceEquals(conversion, ui.View.Conversion)
                        && ReferenceEquals(exported, typeof(EditorView).GetField("playableExport", Hidden)!.GetValue(ui.View)),
                        "A display switch rebuilt conversion or export.");
                    Check(baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
                        "A display switch changed content or undo state.");

                    void Wait()
                    {
                        var deadline = DateTime.UtcNow.AddSeconds(15);
                        do { ui.Paint(); Thread.Sleep(2); } while (ui.View.LegacyDropletRefreshBusy && DateTime.UtcNow < deadline);
                        Check(!ui.View.LegacyDropletRefreshBusy, "Original control processing did not finish.");
                    }

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

    private static IReadOnlyList<SliderPathPoint> Path(Ui ui)
    {
        var objects = (Array)typeof(EditorView).GetField("osuCanvasObjects", Hidden)!.GetValue(ui.View)!;
        var item = objects.GetValue(0)!;
        return (IReadOnlyList<SliderPathPoint>)item.GetType().GetField("Path")!.GetValue(item)!;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
