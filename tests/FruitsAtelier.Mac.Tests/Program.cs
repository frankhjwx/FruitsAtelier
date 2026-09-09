using Avalonia.Input;
using FruitsAtelier.Mac;

if (args.Length == 2 && args[0] == "--profile-map") { await HitsoundPerformance.ProfileMap(args[1]); return; }
if (args.Contains("--profile-hitsounds")) { await HitsoundPerformance.Run(); return; }

void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
Check(MacInput.Control(KeyModifiers.Meta) && MacInput.Control(KeyModifiers.Control) && !MacInput.Control(KeyModifiers.Shift), "Command/Ctrl are mapped without treating Shift as Ctrl");
Check(MacInput.VirtualKey(Key.Z) == 90 && MacInput.VirtualKey(Key.Delete) == 46 && MacInput.VirtualKey(Key.Back) == 8 && MacInput.VirtualKey(Key.Back, false) == 46, "Shortcut and numeric backspace key mapping");
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string directory = Path.Combine(root, "artifacts", "macos-check"); Directory.CreateDirectory(directory);
string wav = Path.Combine(directory, "silence.wav");
using (var writer = new BinaryWriter(File.Create(wav)))
{
    const int count = 44100 * 3;
    writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
    writer.Write("data"u8); writer.Write(count * 2); writer.Write(new byte[count * 2]);
}
using var audio = new MacAudio(muted: true);
await audio.LoadAsync(wav);
Check(audio.State.CanPlay && Math.Abs(audio.State.DurationMs - 3000) < 2, "Native WAV opens with actual duration");
audio.Seek(1000); Check(Math.Abs(audio.State.PositionMs - 1000) < 5 && !audio.State.IsPlaying, "Paused seek uses the audio player and preserves pause");
audio.Play(); await Task.Delay(300);
Check(audio.State.IsPlaying && audio.State.PositionMs > 1100, "Native audio device advances the playback clock (muted)");
audio.Pause(); double paused = audio.State.PositionMs; await Task.Delay(100);
Check(!audio.State.IsPlaying && Math.Abs(audio.State.PositionMs - paused) < 3, "Pause freezes the real audio position");
audio.Play(); audio.Seek(500); await Task.Delay(80);
Check(audio.State.IsPlaying && audio.State.PositionMs < 1000, "Seek while playing preserves playback intent");
audio.Pause();
await audio.LoadAsync(Path.Combine(root, "tests", "FruitsAtelier.Audio.Tests", "Fixtures", "quiet-tone.ogg"));
Check(audio.State.CanPlay && audio.State.DurationMs > 0, "OGG fixture decodes and opens on the native player");
var oldLoad = audio.LoadAsync(Path.Combine(root, "tests", "FruitsAtelier.Audio.Tests", "Fixtures", "quiet-tone.ogg"));
var newLoad = audio.LoadAsync(wav); await Task.WhenAll(oldLoad, newLoad);
Check(audio.State.FilePath == wav && audio.State.CanPlay && Math.Abs(audio.State.DurationMs - 3000) < 2, "Superseded load cannot replace the latest audio");
await audio.LoadAsync(Path.Combine(directory, "missing.mp3"));
Check(!audio.State.CanPlay && !audio.State.IsLoading && audio.State.Error is not null, "Failed load disables playback and reports an error");
await audio.LoadAsync(wav); audio.Seek(2990); audio.Play(); await Task.Delay(200);
Check(!audio.State.IsPlaying && Math.Abs(audio.State.PositionMs - 3000) < 5, $"Playback ends at EOF and holds the final position: {audio.State}");
audio.Play(); await Task.Delay(100);
Check(audio.State.IsPlaying && audio.State.PositionMs < 1000, "Replay starts at the beginning after EOF");
audio.Pause();
using (var hitsounds = new MacHitsoundPlayer(muted: true))
{
    hitsounds.Prepare(new(FruitsAtelier.Core.CatchObjectKind.Fruit, wav, .5f));
    hitsounds.Prepare(new(FruitsAtelier.Core.CatchObjectKind.Banana, null, 1, "catch-banana"));
    hitsounds.Prepare(new(FruitsAtelier.Core.CatchObjectKind.Droplet, Path.Combine(root, "tests", "FruitsAtelier.Audio.Tests", "Fixtures", "quiet-tone.ogg"), .5f, "slidertick"));
    await hitsounds.Preparation;
    hitsounds.Play(new(FruitsAtelier.Core.CatchObjectKind.Fruit, wav, .5f));
    hitsounds.Play(new(FruitsAtelier.Core.CatchObjectKind.Banana, null, 1, "catch-banana"));
    Check(hitsounds.ActiveVoices >= 1, "Native hitsounds start alongside the music transport (muted)");
    hitsounds.Stop(); await Task.Delay(50); Check(hitsounds.ActiveVoices == 0, "Stopping clears all hitsound voices");
    hitsounds.Play(new(FruitsAtelier.Core.CatchObjectKind.Droplet, Path.Combine(root, "tests", "FruitsAtelier.Audio.Tests", "Fixtures", "quiet-tone.ogg"), .5f, "slidertick"));
    Check(hitsounds.ActiveVoices == 1, "Custom OGG hitsound decodes and plays (muted)");
}
await HitsoundLatencyTests.Run(wav);
await HitsoundPerformance.Run();
Console.WriteLine("Mac native checks passed.");
