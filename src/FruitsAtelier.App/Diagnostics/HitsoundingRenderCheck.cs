using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class HitsoundingRenderCheck
{
    private sealed class AuditionClock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/hitsounding-native"); Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            foreach (bool precise in new[] { false, true })
            foreach (string scenario in new[] { "", "-timing", "-flash", "-sets", "-muted", "-dense", "-green-drag", "-green-menu", "-green-hover", "-red-selected", "-audition" })
            {
                if (scenario == "-audition" && !precise) continue;
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
                map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true, SampleSet = 2, SampleIndex = 1, Volume = 80, Effects = 1 });
                map.TimingPoints.Add(new() { TimeMs = 2000, BeatLengthMs = -100, Uninherited = false, SampleSet = 3, SampleIndex = 2, Volume = 65 });
                foreach (int time in new[] { 2100, 2150, 2200, 2250 })
                    map.TimingPoints.Add(new() { TimeMs = time, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, SampleIndex = 0, Volume = time % 100 == 0 ? 60 : 5 });
                foreach (int index in new[] { 12, 123 })
                    map.TimingPoints.Add(new() { TimeMs = index == 12 ? 3000 : 3500, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, SampleIndex = index, Volume = 80 });
                map.Fruits.AddRange(Enumerable.Range(0, 20).Select(i => new Fruit { TimeMs = i * 250, X = 256,
                    OriginalLine = $"256,192,{i * 250},1,{(i % 4 == 0 ? 4 : i % 3 == 0 ? 8 : i % 2 == 0 ? 2 : 0)},0:0:0:0:" }));
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 2500, X = 400 }]); map.Tracks.Add(track);
                if (scenario == "-timing") map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = -100, Uninherited = false, SampleSet = 2, SampleIndex = 1, Volume = 60 });
                if (scenario == "-sets")
                    for (int index = 3; index <= 12; index++) map.TimingPoints.Add(new() { TimeMs = index * 300, BeatLengthMs = -100, Uninherited = false, SampleIndex = index });
                if (scenario == "-dense")
                {
                    map.Fruits.Clear(); map.Tracks.Clear(); map.TimingPoints.Clear();
                    map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SampleSet = 2, SampleIndex = 79 });
                    foreach (int time in Enumerable.Range(0, 100).Select(i => i * 75).Concat(new[] { 1499, 3001 }))
                        map.Fruits.Add(new() { TimeMs = time, X = 256, OriginalLine = $"256,192,{time},1,2,0:0:0:0:" });
                }
                var clock = new AuditionClock();
                var editor = new EditorView(false, timeProvider: clock); editor.LoadDocument(map);
                editor.RequestWaveform = (path, token) => Task.FromResult(FruitsAtelier.App.Audio.WaveformDecoder.Load(path, token).GetAwaiter().GetResult());
                if (scenario == "-audition")
                {
                    editor.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.5f, 1000).ToArray(), 1));
                    editor.RequestStartLibraryAudition = (_, _) => Task.FromResult(1000d);
                }
                editor.LibrarySettings.Workspace = Path.Combine(folder, "workspace");
                string library = Path.Combine(editor.LibrarySettings.Workspace, "HitsoundLibrary");
                Directory.CreateDirectory(Path.Combine(library, "Drums", "Percussion"));
                if (HitsoundDefaults.Find(2, "hitnormal") is { } sample && File.Exists(sample))
                    File.Copy(sample, Path.Combine(library, "soft-tap" + Path.GetExtension(sample)), true);
                editor.ShowHitsoundingPage(); editor.UpdateTransport(scenario == "-timing" ? 0 : scenario == "-flash" ? 1000 : 1200, 10000, true, scenario == "-flash", false, null, null);
                var layout = editor.CaptureDockLayouts(); layout.Hitsounding!.PreciseHitsounds = precise;
                editor.LibrarySettings.DockLayouts = layout; editor.RestoreDockLayouts();
                canvas.Resize(size.Item1, size.Item2, 96);
                canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                if (scenario == "-audition")
                {
                    float zero = editor.HitsoundingTimingHandleBounds(editor.Document.TimingPoints.First(p => p.TimeMs == 0)).X + 4;
                    float twoSeconds = editor.HitsoundingTimingHandleBounds(editor.Document.TimingPoints.First(p => p.TimeMs == 2000)).X + 4;
                    var lane = editor.HitsoundingMuteBounds(3);
                    float x = (zero + twoSeconds) / 2 + 4, y = lane.Y + 12;
                    editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0);
                    editor.Render(canvas, size.Item1, size.Item2);
                    var play = editor.HitsoundingPreciseAuditionBounds;
                    editor.PointerDown(play.X + 14, play.Y + 12, 0, false, false); editor.PointerUp(play.X + 14, play.Y + 12, 0);
                    clock.Now += 500;
                    editor.Render(canvas, size.Item1, size.Item2);
                }
                if (scenario == "-green-hover")
                {
                    var point = editor.Document.TimingPoints.First(p => !p.Uninherited && p.TimeMs == 3000);
                    var handle = editor.HitsoundingTimingHandleBounds(point);
                    editor.PointerMove(handle.X + 4, handle.Y + 10, false, false);
                    editor.Render(canvas, size.Item1, size.Item2);
                }
                if (scenario == "-red-selected")
                {
                    var point = editor.Document.TimingPoints.First(p => p.Uninherited);
                    var handle = editor.HitsoundingTimingHandleBounds(point);
                    editor.PointerDown(handle.X + 4, handle.Y + 10, 0, false, false);
                    editor.PointerUp(handle.X + 4, handle.Y + 10, 0);
                    editor.Render(canvas, size.Item1, size.Item2);
                }
                if (scenario is "-green-drag" or "-green-menu")
                {
                    var point = editor.Document.TimingPoints.First(p => !p.Uninherited && p.TimeMs == 2000);
                    var handle = editor.HitsoundingTimingHandleBounds(point);
                    editor.PointerDown(handle.X + 4, handle.Y + 10, scenario == "-green-menu" ? 2 : 0, false, false);
                    if (scenario == "-green-drag") editor.PointerMove(handle.X + 44, handle.Y + 10, false, false);
                    editor.Render(canvas, size.Item1, size.Item2);
                }
                if (scenario == "-muted")
                {
                    foreach (int lane in new[] { 1, 3, 6 })
                    {
                        var button = editor.HitsoundingMuteBounds(lane);
                        editor.PointerDown(button.X + 12, button.Y + 12, 0, false, false);
                        editor.PointerUp(button.X + 12, button.Y + 12, 0);
                    }
                    editor.Render(canvas, size.Item1, size.Item2);
                }
                if (scenario == "-sets")
                {
                    editor.PointerDown(125, 210, 0, false, false); editor.PointerUp(125, 210, 0);
                    editor.Render(canvas, size.Item1, size.Item2);
                }
                canvas.End(capture: () =>
                {
                    var frame = canvas.CapturePixels();
                    using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{locale}-{size.Item1}{(precise ? "-precise" : "")}{scenario}.bmp")));
                    output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                    output.Write(40); output.Write(frame.Width); output.Write(-frame.Height);
                    output.Write((ushort)1); output.Write((ushort)32); output.Write(0); output.Write(frame.Pixels.Length);
                    output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                });
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Hitsounding panel native render checks passed.");
    }
}
