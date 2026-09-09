using System.Diagnostics;
using FruitsAtelier.Core;
using FruitsAtelier.Mac;

static class HitsoundLatencyTests
{
    public static async Task Run(string wav)
    {
        using var music = new MacAudio(muted: true);
        using var sounds = new MacHitsoundPlayer(muted: true);
        var sound = new Hitsound(CatchObjectKind.Fruit, wav, .5f);
        await music.LoadAsync(wav);
        sounds.Prepare(sound);
        int prepared = sounds.NativePlayerCreations;
        music.Play();
        double deadline = music.HitsoundDeviceTime(350) ?? throw new Exception("Music clock unavailable");
        var watch = Stopwatch.StartNew(); sounds.Schedule(sound, deadline); watch.Stop();
        if (sounds.NativePlayerCreations != prepared) throw new Exception("Scheduling allocated a new native player after preparation");
        await Task.Delay(250);
        if (sounds.LastVoicePositionMs > 1) throw new Exception("Future sound started before its deadline");
        // Both players continue on the device clock while the UI/managed thread is idle.
        await Task.Delay(400);
        double error = sounds.LastVoicePositionMs - (music.State.PositionMs - 350);
        Console.WriteLine($"Scheduled hitsound: submit={watch.Elapsed.TotalMilliseconds:F2} ms, native clock alignment={error:F2} ms");
        if (Math.Abs(error) > 25) throw new Exception($"Music and hitsound clocks differ by {error:F2} ms");
        sounds.Stop(); music.Pause();
        if (music.HitsoundDeviceTime(350) is not null) throw new Exception("Paused music exposes an active scheduling clock");
        music.Seek(1000); music.Play();
        sounds.Schedule(sound, music.HitsoundDeviceTime(1300)!.Value);
        sounds.Stop();
        await Task.Delay(550);
        if (sounds.ActiveVoices != 0 || sounds.LastVoicePositionMs > 1) throw new Exception("Pause/seek cancellation left a future voice running");
        sounds.Schedule(sound, music.HitsoundDeviceTime(1600)!.Value);
        if (sounds.NativePlayerCreations != prepared) throw new Exception("Stopped native voices were recreated instead of reused");
        Console.WriteLine("PASS Prepared native voices, future deadlines, device-clock alignment, cancellation and reuse (muted)");
    }
}
