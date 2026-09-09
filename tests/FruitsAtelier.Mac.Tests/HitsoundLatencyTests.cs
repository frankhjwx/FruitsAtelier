using System.Diagnostics;
using FruitsAtelier.Core;
using FruitsAtelier.Mac;

static class HitsoundLatencyTests
{
    public static async Task Run(string wav, string? musicPath = null)
    {
        using var music = new MacAudio(muted: true);
        using var sounds = new MacHitsoundPlayer(muted: true);
        var sound = new Hitsound(CatchObjectKind.Fruit, wav, .5f);
        await music.LoadAsync(musicPath ?? wav);
        sounds.Prepare(sound); await sounds.Preparation;
        int prepared = sounds.NativePlayerCreations;
        music.Play();
        double deadline = music.HitsoundHostTime(350) ?? throw new Exception("Music clock unavailable");
        var watch = Stopwatch.StartNew(); sounds.Schedule(sound, deadline); watch.Stop();
        if (sounds.NativePlayerCreations != prepared) throw new Exception("Scheduling allocated a new native player after preparation");
        await Task.Delay(250);
        if (sounds.LastVoicePositionMs > 1) throw new Exception("Future sound started before its deadline");
        // Both players continue on the device clock while the UI/managed thread is idle.
        await Task.Delay(400);
        if (Math.Abs(sounds.LastRenderedStart - deadline) > .005) throw new Exception("PCM render callback missed the scheduled timestamp");
        double error = sounds.LastVoicePositionMs - (music.State.PositionMs - 350);
        Console.WriteLine($"Scheduled hitsound: submit={watch.Elapsed.TotalMilliseconds:F2} ms, host clock alignment={error:F2} ms");
        if (Math.Abs(error) > 25) throw new Exception($"Music and hitsound clocks differ by {error:F2} ms");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            music.Pause(); sounds.Stop();
            double paused = music.State.PositionMs;
            await Task.Delay(new[] { 0, 200, 800 }[cycle]);
            if (Math.Abs(music.State.PositionMs - paused) > 3) throw new Exception("Pause changed the music position");
            music.Play();
            double note = paused + 100;
            double resumedDeadline = music.HitsoundHostTime(note)!.Value;
            sounds.Schedule(sound, resumedDeadline);
            await Task.Delay(350);
            if (Math.Abs(sounds.LastRenderedStart - resumedDeadline) > .005) throw new Exception("Resumed PCM missed its render timestamp");
            double resumedError = sounds.LastVoicePositionMs - (music.State.PositionMs - note);
            Console.WriteLine($"Resume {cycle}: paused={paused:F2}, position={music.State.PositionMs:F2}, error={resumedError:F2} ms");
            if (Math.Abs(resumedError) > 25) throw new Exception($"Resume clock mismatch: {resumedError:F2} ms");
        }
        sounds.Stop(); music.Pause();
        if (music.HitsoundHostTime(350) is not null) throw new Exception("Paused music exposes an active scheduling clock");
        music.Seek(1000); music.Play();
        sounds.Schedule(sound, music.HitsoundHostTime(1300)!.Value);
        sounds.Stop();
        await Task.Delay(550);
        if (sounds.ActiveVoices != 0 || sounds.LastVoicePositionMs > 1) throw new Exception("Pause/seek cancellation left a future voice running");
        sounds.Schedule(sound, music.HitsoundHostTime(1600)!.Value);
        if (sounds.NativePlayerCreations != prepared) throw new Exception("Stopped native voices were recreated instead of reused");
        music.Pause(); sounds.Stop();
        double beforeQuickPause = music.State.PositionMs;
        music.Play(); await Task.Delay(30); music.Pause(); sounds.Stop();
        if (Math.Abs(music.State.PositionMs - beforeQuickPause) > 5) throw new Exception($"Pausing inside the startup lead changed the playhead: {beforeQuickPause:F2} -> {music.State.PositionMs:F2}");
        music.Play();
        double quickNote = music.State.PositionMs + 100;
        double quickDeadline = music.HitsoundHostTime(quickNote)!.Value;
        sounds.Schedule(sound, quickDeadline); await Task.Delay(350);
        double quickError = sounds.LastVoicePositionMs - (music.State.PositionMs - quickNote);
        if (Math.Abs(quickError) > 25 || Math.Abs(sounds.LastRenderedStart - quickDeadline) > .005)
            throw new Exception($"Rapid pause/resume changed alignment: {quickError:F2} ms");
        Console.WriteLine("PASS Repeated resume, variable pause duration and cancellation during startup lead");
        Console.WriteLine("PASS Prepared PCM bank, future deadlines, render-callback timing, cancellation and reuse (muted)");
    }
}
