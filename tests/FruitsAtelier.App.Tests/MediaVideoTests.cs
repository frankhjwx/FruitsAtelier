#if WINDOWS
using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Media;
using FFmpeg.AutoGen;

internal static class MediaVideoTests
{
    private sealed record Probe(double RequestedMs, double Rate, bool Playing, bool Ready, double? FrameMs, double? FrameEndMs,
        int? Width, int? Height, long? Serial, uint? PixelHash, double RecoveryMs, long BufferedBytes, string? Error);

    public static int Corpus(string inventoryPath, string reportPath)
    {
        using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        FFmpegRuntime.Initialize();
        var runtime = new { version = ffmpeg.av_version_info(), license = ffmpeg.avcodec_license(), configuration = ffmpeg.avcodec_configuration() };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "ffmpeg-runtime.json"), JsonSerializer.Serialize(runtime, new JsonSerializerOptions { WriteIndented = true }));
        FailedOpenLifecycle(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        var results = new List<object>();
        int failures = 0, index = 0;
        foreach (var video in inventory.RootElement.GetProperty("videos").EnumerateArray())
        {
            string path = video.GetProperty("path").GetString()!;
            double durationMs = double.Parse(video.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture) * 1000;
            var probes = new List<Probe>();
            double disposalMs;
            using (var playback = new VideoPlayback(path))
            {
                // Repeated reverse jumps exercise decoder generations and cache retirement.
                foreach (var request in new[] { (300d, 1d, true), (durationMs * .75, 1.5, true),
                    (durationMs * .25, .5, true), (durationMs * .9, 1d, false), (durationMs * .1, 1d, false), (300d, 1d, false) })
                    probes.Add(WaitForFrame(playback, request.Item1, request.Item2, request.Item3));
                long before = Stopwatch.GetTimestamp(); playback.Dispose();
                if (!playback.Completion.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Disposed video worker did not retire.");
                disposalMs = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
                if (playback.WithFrame(100, false, 1, _ => throw new Exception("Disposed decoder published a frame.")))
                    throw new Exception("Disposed decoder remained active.");
            }
            bool passed = probes.All(p => p.Ready && p.Error is null && p.BufferedBytes <= 96 * 1024 * 1024);
            if (!passed) failures++;
            results.Add(new { path, durationMs, passed, probes, disposalMs });
            Console.WriteLine($"[{++index}] {(passed ? "PASS" : "FAIL")} {path}: {probes.Count(p => p.Ready)}/{probes.Count} random-access frames");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                count = index, failures,
                note = "Every inventoried file is decoded at six nonmonotonic timestamps with playback rates 0.5/1/1.5 and paused requests. This is sampled decoder validation, not complete playback or native presentation.",
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failures == 0 ? 0 : 1;
    }

    private static void FailedOpenLifecycle(string directory)
    {
        string invalid = Path.Combine(directory, "invalid-video.fixture");
        File.WriteAllText(invalid, "invalid video fixture");
        foreach (string path in new[] { invalid, Path.Combine(directory, "missing-video.fixture") })
        {
            using var playback = new VideoPlayback(path);
            playback.WithFrame(0, true, 1, _ => throw new Exception("Failed input published a frame."));
            if (!playback.Completion.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Failed decoder did not retire.");
            if (playback.Error is null) throw new Exception("Failed input did not report its error.");
            for (int i = 0; i < 5; i++)
                if (playback.WithFrame(i * 100, true, 1, _ => { })) throw new Exception("Failed decoder published another frame.");
            playback.Dispose(); playback.Dispose();
        }
        Console.WriteLine("PASS missing/invalid input, repeated requests and disposal after failed-open retirement");
    }

    private static Probe WaitForFrame(VideoPlayback playback, double time, double rate, bool playing)
    {
        var timer = Stopwatch.StartNew();
        double? frameTime = null, frameEnd = null; int? width = null, height = null; long? serial = null; uint? hash = null;
        bool ready;
        do
        {
            ready = playback.WithFrame(time, playing, rate, frame =>
            {
                if (frame.Time > time + 1 || double.IsFinite(frame.EndTime) && time >= frame.EndTime + 1)
                    throw new Exception("A frame from an expired seek was presented.");
                if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length < frame.Width * frame.Height * 4)
                    throw new Exception("Decoded frame has invalid bounds.");
                frameTime = frame.Time; frameEnd = frame.EndTime; width = frame.Width; height = frame.Height; serial = frame.Serial;
                uint digest = 2166136261;
                for (int i = 0; i < frame.Width * frame.Height * 4; i += 97) digest = (digest ^ frame.Pixels[i]) * 16777619;
                hash = digest;
            });
            if (!ready) Thread.Sleep(5);
        } while (!ready && playback.Error is null && timer.Elapsed.TotalSeconds < 5);
        return new(time, rate, playing, ready, frameTime, frameEnd, width, height, serial, hash, timer.Elapsed.TotalMilliseconds, playback.BufferedBytes, playback.Error);
    }
}
#else
internal static class MediaVideoTests
{
    public static int Corpus(string inventoryPath, string reportPath) => throw new PlatformNotSupportedException("Native video validation requires Windows.");
}
#endif
