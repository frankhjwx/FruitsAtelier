using System.Diagnostics;
using System.Runtime.InteropServices;
using FruitsAtelier.Core;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Mac;

static class HitsoundPerformance
{
    public static async Task Run()
    {
        var first = new MapDocument(); first.Fruits.Add(new() { TimeMs = 0 });
        var second = new MapDocument(); second.Fruits.Add(new() { TimeMs = 180000, OriginalLine = "0,192,180000,1,8,3:3:0:100:" });
        using var player = new MacHitsoundPlayer(muted: true);
        player.PreloadProject(new[] { first, second }); await player.Preparation;
        if (player.SampleLoads != 3) throw new Exception("Project preload missed an inactive difficulty or a distant sample");
        int loads = player.SampleLoads, engines = player.NativePlayerCreations;
        var sound = new Hitsound(CatchObjectKind.Fruit, HitsoundDefaults.Find(1, "hitnormal"), 1);
        var durations = new List<double>();
        for (int tick = 0; tick < 120; tick++)
        {
            double start = HostTime() + .1;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 32; i++) player.Schedule(sound, start);
            durations.Add(watch.Elapsed.TotalMilliseconds);
            await Task.Delay(16);
        }
        if (player.SampleLoads != loads || player.NativePlayerCreations != engines) throw new Exception("Playback loaded samples or opened a native engine");
        durations.Sort();
        Console.WriteLine($"Hitsound profile: 120 batches x 32 simultaneous notes; batch p50={durations[60]:F3} ms, p95={durations[114]:F3} ms, max={durations[^1]:F3} ms; PCM={player.DecodedBytes} bytes; playback loads=0, engine creations=0");
        player.Stop();
        OfflinePcm(sound.FilePath!);
        player.PreloadProject(new[] { first }); await player.Preparation;
        if (player.DecodedBytes <= 0 || player.ActiveVoices != 0) throw new Exception("Project replacement retained stale voices or lost its PCM bank");
        Console.WriteLine("PASS All project difficulties preload before playback, PCM overlaps/clips/cancels, and project banks replace safely");
    }
    public static async Task ProfileMap(string path)
    {
        // Read-only: do not create/recover a workspace session or save the user's document.
        var document = ProjectSerializer.ReadFile(path);
        using var sounds = new MacHitsoundPlayer(muted: true);
        var load = Stopwatch.StartNew(); sounds.PreloadProject(new[] { document.DeepClone() }); await sounds.Preparation;
        Console.WriteLine($"Map {document.Name}: preload={load.Elapsed.TotalMilliseconds:F1} ms; {sounds.SampleLoads} samples; PCM={sounds.DecodedBytes / 1048576.0:F2} MiB");
        var objects = CatchStreamConverter.Convert(document).Objects;
        var resolver = new HitsoundResolver(document, objects);
        var events = objects.SelectMany(o => resolver.Resolve(o).Select(s => o.TimeMs)).ToArray();
        int peak = events.GroupBy(t => (int)(t / 16)).Max(g => g.Count());
        Console.WriteLine($"{objects.Count} catch objects; {events.Length} sample events; peak={peak} sounds/16ms");
        int prepared = sounds.SampleLoads;
        foreach (bool enabled in new[] { false, true })
        {
            var view = new EditorView(); view.LoadDocument(document.DeepClone());
            var canvas = new CountCanvas(); view.Render(canvas, 1440, 900);
            for (int i = 0; i < 1000 && view.StarRatingsRefreshing; i++) { _ = view.CurrentStarRating; await Task.Delay(10); }
            double dispatch = 0; int submitted = 0;
            if (enabled) view.RequestScheduleHitsound = (sound, _) => {
                var watch = Stopwatch.StartNew(); sounds.Schedule(sound, HostTime() + .1);
                dispatch += watch.Elapsed.TotalMilliseconds; submitted++;
            };
            view.RequestStopHitsounds = sounds.Stop;
            view.StartHitsounds(0);
            var frames = new List<double>(); var polls = new List<double>(); var draws = new List<double>();
            for (double time = 0; time <= document.DurationMs; time += 16)
            {
                var watch = Stopwatch.StartNew();
                view.UpdateTransport(time, document.DurationMs, true, true, false, null, document.AudioPath);
                double poll = watch.Elapsed.TotalMilliseconds;
                view.Render(canvas, 1440, 900);
                double frame = watch.Elapsed.TotalMilliseconds;
                polls.Add(poll); draws.Add(frame - poll); frames.Add(frame);
            }
            double Q(List<double> values, double q) { values.Sort(); return values[Math.Min(values.Count - 1, (int)(values.Count * q))]; }
            Console.WriteLine($"hitsounds={enabled}: {frames.Count} simulated frames; CPU p50={Q(frames,.5):F3} p95={Q(frames,.95):F3} max={Q(frames,1):F3} ms; poll p95={Q(polls,.95):F3}, draw-command p95={Q(draws,.95):F3} ms; submissions={submitted}, dispatch total={dispatch:F3} ms");
            sounds.Stop(); view.NewProject();
        }
        if (sounds.SampleLoads != prepared) throw new Exception("Map playback loaded extra samples");
        Console.WriteLine("Read-only map profile complete; native GPU rasterization and real-time frame pacing are not measured.");
    }
    private sealed class CountCanvas : ICanvas
    {
        public void Fill(Rect r,uint c,float radius=0) { }
        public void Stroke(Rect r,uint c,float width=1,float radius=0) { }
        public void Line(float x,float y,float x2,float y2,uint c,float width=1,float opacity=1) { }
        public void Circle(float x,float y,float radius,uint c,bool filled=true,float width=1) { }
        public void Text(string t,float x,float y,float size,uint c,float maxWidth=10000,bool bold=false) { }
        public bool Image(string p,Rect r,uint tint=0xFFFFFF,Rect? source=null) => false;
        public void Clip(Rect r) { }
        public void Unclip() { }
    }
    private static void OfflinePcm(string path)
    {
        nint mixer = Open(-1), sample = Sample(path);
        if (mixer == 0 || sample == 0) throw new Exception("Offline PCM setup failed");
        try
        {
            var reference = new float[4096]; var mixed = new float[4096];
            Queue(mixer, sample, 1, .5f); Render(mixer, 1, (uint)reference.Length, reference);
            if (!reference.Any(x => Math.Abs(x) > .001)) throw new Exception("Default normal sample renders silence");
            Stop(mixer); Queue(mixer, sample, 2, .5f); Queue(mixer, sample, 2, .5f); Render(mixer, 2, (uint)mixed.Length, mixed);
            for (int i = 0; i < mixed.Length; i++) if (Math.Abs(mixed[i] - Math.Clamp(reference[i] * 2, -1, 1)) > .0001) throw new Exception("Native PCM overlap/gain mismatch");
            Stop(mixer); Queue(mixer, sample, 2, 8); Render(mixer, 2, (uint)mixed.Length, mixed);
            for (int i = 0; i < mixed.Length; i++) if (Math.Abs(mixed[i] - Math.Clamp(reference[i] * 16, -1, 1)) > .0001) throw new Exception("Native PCM clipping mismatch");
            Stop(mixer); Queue(mixer, sample, 4, 1); Render(mixer, 3, (uint)mixed.Length, mixed);
            if (mixed.Any(x => x != 0)) throw new Exception("Future PCM played early");
            Stop(mixer); Render(mixer, 4, (uint)mixed.Length, mixed);
            if (mixed.Any(x => x != 0)) throw new Exception("Canceled PCM remained audible");
        }
        finally { Close(mixer); SampleClose(sample); }
    }
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_audio_host_time")] static extern double HostTime();
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_open")] static extern nint Open(int muted);
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_close")] static extern void Close(nint handle);
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_sample")] static extern nint Sample([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_sample_close")] static extern void SampleClose(nint handle);
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_schedule")] static extern int Queue(nint handle, nint sample, double start, float volume);
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_stop")] static extern void Stop(nint handle);
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_render_test")] static extern void Render(nint handle, double time, uint frames, [Out] float[] output);
}
