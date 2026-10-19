using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class InspectorSectionTests
{
    public static void Run()
    {
        var localizationErrors = L.Validate();
        Check(localizationErrors.Count == 0, string.Join("; ", localizationErrors));
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int width in new[] { 980, 1440 })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 700, DerandomizeFSliderDroplets = true };
                var track = new CurveTrack { Kind = CurveKind.Linear, EndpointSnap = new() };
                track.Nodes.Add(new() { TimeMs = 875, X = 100 });
                track.Nodes.Add(new() { TimeMs = 2240, X = 350 }); map.Tracks.Add(track);
                ObjectFlags.SetNewCombo(map, track.Id, true);
                var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(width, 1000); ui.SelectTrack(track.Id);
                ui.View.ShowSliderInspector(); ui.Paint();
                var before = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("sliderSnap.startPosition"))
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("sliderSnap.endPosition")), "Inspector displays endpoint beat positions");
                var start = ui.Canvas.Texts.Single(t => t.Value == "1/4" && t.X < ui.View.SliderInspectorViewport.X);
                Check(ui.Canvas.Texts.Any(t => t.Value == "1/5" && t.X < ui.View.SliderInspectorViewport.X), "Canvas displays the actual reduced end fraction");
                var nc = ui.Canvas.Texts.Single(t => t.Value == "NC" && Math.Abs(t.Y - start.Y) < 30);
                Check(start.Y >= nc.Y + nc.Size + 3, "Endpoint label does not overlap NC");
                foreach (int section in new[] { 1, 2, 3, 4 })
                {
                    ClickSection(section);
                    string key = section switch { 1 => "timing", 2 => "conversion", 3 => "droplets", _ => "structure" };
                    Check(ui.Canvas.Texts.Any(t => t.Value == "▸ " + L.Get("inspector.section." + key)), "Section collapses");
                    if (section == 1) Check(ui.View.SliderEndpointToggleBounds.Width == 0, "Collapsed timing hides its input targets");
                    ClickSection(section);
                    Check(ui.Canvas.Texts.Any(t => t.Value == "▾ " + L.Get("inspector.section." + key)), "Section expands");
                }
                Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty && ReferenceEquals(conversion, ui.View.Conversion),
                    "Collapsing sections preserves content, history and conversion");
                ui.DownMap(2240, 350); ui.MoveMap(1960, 350); ui.UpMap(1960, 350);
                Check(ui.Canvas.Texts.Any(t => t.Value == "4/5" && t.X < ui.View.SliderInspectorViewport.X), "Canvas endpoint fraction refreshes after snapping");
                ui.Key('Z', ctrl: true); ui.SelectTrack(track.Id); ui.Paint();
                Check(ui.View.Document.ContentEquals(before) && ui.Canvas.Texts.Any(t => t.Value == "1/5" && t.X < ui.View.SliderInspectorViewport.X), "Undo restores endpoint position labels");
                ClickSection(1); ui.ClickMap(4000, 400); ui.SelectTrack(track.Id); ui.Paint();
                Check(ui.View.SliderEndpointToggleBounds.Width == 0, "Section collapse state survives selection changes");
                var repeated = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,700\n[HitObjects]\n100,192,875,2,0,L|350:192,2,97.5\n");
                repeated.ImportedSliders.Single().EndpointSnap = new();
                ui.LoadDocument(repeated); ui.View.ToggleOsuCanvas(); ui.View.UpdateTransport(900, 10000, true, false, false, null, null); ui.Paint();
                var head = ui.View.OsuCanvasScreen(100, 192); ui.Click(head.X, head.Y);
                var repeatedStart = ui.Canvas.Texts.Single(t => t.Value == "1/4" && t.X < ui.View.SliderInspectorViewport.X);
                var repeatedEnd = ui.Canvas.Texts.Single(t => t.Value == "1/5" && t.X < ui.View.SliderInspectorViewport.X);
                Check(repeatedEnd.Y >= repeatedStart.Y + repeatedStart.Size + 3, "Coincident repeated endpoints use separate fraction lines");
                ui.View.Document.TimingPoints[0].BeatLengthMs = 1000; ui.Paint();
                Check(ui.Canvas.Texts.Any(t => t.Value == "7/8" && t.X < ui.View.SliderInspectorViewport.X)
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("sliderSnap.offGrid") && t.X < ui.View.SliderInspectorViewport.X),
                    "Timing changes refresh actual fractions and mark unmatched endpoints off grid");

                void ClickSection(int section)
                {
                    for (int i = 0; i < 30; i++)
                    {
                        var box = ui.View.SliderInspectorSectionBounds(section);
                        if (box.Width > 0) { ui.Click(box.X + 12, box.Y + 10); return; }
                        var panel = ui.View.SliderInspectorViewport;
                        string key = section switch { 1 => "timing", 2 => "conversion", 3 => "droplets", _ => "structure" };
                        var text = ui.Canvas.Texts.Single(t => t.Value.EndsWith(L.Get("inspector.section." + key)));
                        ui.View.Wheel(panel.X + 10, panel.Y + 10, text.Y < panel.Y ? 120 : -120, false, false); ui.Paint();
                    }
                    throw new Exception("Inspector section is unreachable");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
