using Avalonia.Input;
using FruitsAtelier.Mac;

if (args.Length == 2 && args[0] == "--profile-map") { await HitsoundPerformance.ProfileMap(args[1]); return; }
if (args.Contains("--profile-hitsounds")) { await HitsoundPerformance.Run(); return; }

void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
Check(MacInput.Control(KeyModifiers.Meta) && MacInput.Control(KeyModifiers.Control) && !MacInput.Control(KeyModifiers.Shift), "Command/Ctrl are mapped without treating Shift as Ctrl");
Check(MacInput.VirtualKey(Key.Z) == 90 && MacInput.VirtualKey(Key.Delete) == 46 && MacInput.VirtualKey(Key.Back) == 8 && MacInput.VirtualKey(Key.Back, false) == 46, "Shortcut and numeric backspace key mapping");
Check(MacInput.VirtualKey(Key.OemSemicolon) == 186 && MacInput.VirtualKey(Key.OemQuotes) == 222 &&
    MacInput.VirtualKey(Key.OemOpenBrackets) == 219 && MacInput.VirtualKey(Key.OemCloseBrackets) == 221,
    "Testplay punctuation keys map to shared virtual keys");
foreach (var (key, expected) in new (Key, int)[] {
    (Key.OemPlus, 187), (Key.OemComma, 188), (Key.OemMinus, 189), (Key.OemPeriod, 190),
    (Key.OemQuestion, 191), (Key.OemTilde, 192), (Key.OemPipe, 220), (Key.Oem8, 223), (Key.OemBackslash, 226),
    (Key.Back, 8), (Key.Clear, 12), (Key.Enter, 13), (Key.LeftCtrl, 17), (Key.RightCtrl, 17),
    (Key.LeftAlt, 18), (Key.RightAlt, 18), (Key.CapsLock, 20), (Key.PageUp, 33), (Key.PageDown, 34),
    (Key.End, 35), (Key.Home, 36), (Key.Insert, 45), (Key.Delete, 46), (Key.Multiply, 106),
    (Key.Add, 107), (Key.Separator, 108), (Key.Subtract, 109), (Key.Decimal, 110), (Key.Divide, 111),
    (Key.NumLock, 144), (Key.Scroll, 145) })
    Check(MacInput.VirtualKey(key) == expected, $"{key} maps to shared virtual key {expected}");
for (int i = 0; i < 10; i++) Check(MacInput.VirtualKey(Key.NumPad0 + i) == 96 + i, $"NumPad{i} mapping");
for (int i = 0; i < 24; i++) Check(MacInput.VirtualKey(Key.F1 + i) == 112 + i, $"F{i + 1} mapping");
if (args.Contains("--offline-audio-check"))
{
    HitsoundPerformance.OfflinePcm(FruitsAtelier.Core.HitsoundDefaults.Find(1, "hitnormal") ?? throw new Exception("Missing default sample"));
    Console.WriteLine("PASS Native offline hitsound mixing and dynamic volume");
}
if (args.Contains("--input-check") || args.Contains("--offline-audio-check")) return;
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
if (args.Length == 2 && args[0] == "--check-resume-music") { await HitsoundLatencyTests.Run(wav, args[1]); return; }
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
foreach (double speed in new[] { .25, .5, .75, 1 })
{
    audio.Seek(500); audio.SetPlaybackSpeed(speed); audio.Play();
    await Task.Delay(250);
    double start = audio.State.PositionMs;
    await Task.Delay(400);
    Check(Math.Abs(audio.State.PositionMs - start - 400 * speed) < 55, $"Native map clock follows {speed}x tempo");
    double before = audio.State.PositionMs;
    audio.SetPlaybackSpeed(speed == 1 ? .5 : 1);
    Check(Math.Abs(audio.State.PositionMs - before) < 30, "Changing tempo retains the source position");
    audio.Pause(); double atPause = audio.State.PositionMs;
    await Task.Delay(80); audio.Play();
    Check(Math.Abs(audio.State.PositionMs - atPause) < 3, "Tempo resume retains pause position");
    audio.Pause();
}
audio.SetPlaybackSpeed(1);
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
