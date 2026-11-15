using FruitsAtelier.Core;
using FruitsAtelier.Mac;

static class MusicPcmAlignmentTests
{
    // --pcm-alignment-check observes actual prepared music PCM, not a second
    // derived transport clock. Both native mixers mute after the observations.
    internal static async Task Run()
    {
        string folder = Path.Combine(MacPaths.Artifacts, "tests", "mac-pcm-alignment");
        Directory.CreateDirectory(folder);
        string musicPath = Path.Combine(folder, "music.wav"), hitPath = Path.Combine(folder, "hit.wav");
        Wave(musicPath, 0, 3); Wave(hitPath, 0, .1);
        using var music = new MacAudio(muted: true);
        using var hits = new MacHitsoundPlayer(muted: true);
        await music.LoadAsync(musicPath);
        if (!music.State.CanPlay) throw new Exception(music.State.Error);
        music.MonitorPcm();
        var sound = new Hitsound(CatchObjectKind.Fruit, hitPath, 1);
        hits.Prepare(sound); await hits.Preparation;
        var cases = Enumerable.Range(2, 29).Select(i => (i / 20d, false, 200d, 1000d))
            .Concat(new (double, bool, double, double)[] {
                (1, false, 0, 1000), (1.5, true, 200, 1000), (.75, true, 200, 1000),
                (1, false, -100, 1000), (1, false, 200, 1000),
                (.1, false, 0, 110), (.5, false, 0, 110), (1.5, false, 0, 110),
                (.1, false, 0, 220), (.5, false, 0, 220), (1.5, false, 0, 220) });
        double currentFrequency = 1000;
        foreach (var (speed, pitch, position, sourceFrequency) in cases)
        {
            music.Pause(); hits.Stop();
            if (sourceFrequency != currentFrequency)
            {
                string tone = Path.Combine(folder, $"music-{sourceFrequency}.wav");
                Wave(tone, 0, 3, sourceFrequency);
                await music.LoadAsync(tone); music.MonitorPcm(); currentFrequency = sourceFrequency;
            }
            music.SetPlaybackSpeed(speed, pitch); music.Seek(position);
            await Task.Delay(100);
            music.Play();
            // Compare the session's first source PCM, not the shape of an interior
            // transient (a time stretcher can redistribute energy within a grain).
            double expected = music.HitsoundHostTime(Math.Max(0, position)) ?? throw new Exception("Missing scheduling clock");
            hits.Schedule(sound, expected);
            var timeout = DateTime.UtcNow.AddSeconds(2 + Math.Max(0, -position) / (1000 * speed));
            while (music.RenderedOnset == 0 && DateTime.UtcNow < timeout) await Task.Delay(10);
            await Task.Delay(700);
            if (music.RenderedOnset == 0) throw new Exception($"No music PCM at {speed}x");
            double delta = (music.RenderedOnset - hits.LastRenderedStart) * 1000;
            Console.WriteLine($"PCM {speed}x pitch={pitch} seek={position}: music-minus-hit={delta:F3} ms");
            if (Math.Abs(delta) > 25) throw new Exception($"Music PCM and hitsound render differ by {delta:F3} ms at {speed}x");
            double frequency = music.RenderedFrequency, expectedFrequency = sourceFrequency * (pitch ? speed : 1);
            if (Math.Abs(frequency - expectedFrequency) > expectedFrequency * .02)
                throw new Exception($"Unexpected PCM pitch at {speed}x: {frequency:F2} Hz, expected {expectedFrequency:F2}");
            Console.WriteLine($"PASS PCM frequency {frequency:F2} Hz (expected {expectedFrequency:F2})");
        }
        Console.WriteLine("PASS actual PCM alignment at all supported speed extremes, pitch modes, seek and resume");
    }

    private static void Wave(string path, double start, double length, double frequency = 1000)
    {
        using var writer = new BinaryWriter(File.Create(path));
        const int rate = 44100, frames = rate * 3;
        writer.Write("RIFF"u8); writer.Write(36 + frames * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(frames * 2);
        for (int i = 0; i < frames; i++)
            writer.Write((short)(i / (double)rate >= start && i / (double)rate < start + length
                ? 12000 * Math.Sin(2 * Math.PI * frequency * i / rate) : 0));
    }
}
