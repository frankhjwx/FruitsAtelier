using System.Diagnostics;
using NAudio.Wave;
using FruitsAtelier.App.Audio;

if (args.Contains("--hitsound-check")) { HitsoundMixerTests.Run(); return 0; }

string root = FindRoot();
string directory = Path.Combine(root, "artifacts", "tests", "audio");
Directory.CreateDirectory(directory);
string wave = Path.Combine(directory, "transport-tone.wav");
using (var writer = new WaveFileWriter(wave, new WaveFormat(44100, 16, 2)))
    for (int i = 0; i < 4 * 44100; i++)
    {
        short sample = (short)(Math.Sin(2 * Math.PI * 440 * i / 44100) * 900);
        writer.WriteByte((byte)(sample & 255)); writer.WriteByte((byte)(sample >> 8));
        writer.WriteByte((byte)(sample & 255)); writer.WriteByte((byte)(sample >> 8));
    }

var tests = new (string Name, Func<Task> Run)[]
{
    ("Hitsound PCM mix, volume and stop", () => { HitsoundMixerTests.Run(); return Task.CompletedTask; }),
    ("WAV real output drives the clock; pause and paused seek stay stopped", WavePlayback),
    ("Playing seeks preserve playback and latest rapid seek wins", PlayingSeek),
    ("EOF stops at duration and play restarts from zero", EndAndReplay),
    ("Missing or malformed files leave an error and a later load recovers", LoadFailures),
    ("Both supplied MP3s decode, play and seek using Media Foundation", Mp3Samples),
    ("An OGG Vorbis fixture decodes, plays and seeks", VorbisSample),
    ("Disposing one MP3 transport does not shut down another reader", MediaFoundationLifetime),
    ("A new load supersedes queued seeks without freezing the next device clock", LoadDuringSeek),
    ("MP3 seeks return the same PCM samples as continuous decode at the requested time", () => SeekAlignmentTests.Run(root)),
    ("Device-clock elapsed time stays aligned across MP3 pause and frame-exact seek", DeviceClockAlignment),
    ("Cancelling MP3 decode releases its runtime lease and allows a later load", DecodeCancellation),
    ("Repeated play/pause and seek retain a usable output session", RepeatedLifecycle),
    ("Delayed stop callbacks retire safely and seek recovers without changing play intent", () => OutputRecoveryTests.DelayedStop(wave)),
    ("Repeated output failure stays unavailable until an explicit reload", () => OutputRecoveryTests.RepeatedFailure(wave)),
    ("EOF replay does not reuse an output waiting for its stopped callback", () => OutputRecoveryTests.EndBeforeCallback(wave))
};
if (args.Contains("--lifecycle-check")) tests = tests.Where(test => test.Run == (Func<Task>)RepeatedLifecycle).ToArray();
if (args.Contains("--recovery-check")) tests = tests.TakeLast(3).ToArray();
int passed = 0;
foreach (var (name, test) in tests)
{
    try { await test(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{passed}/{tests.Length} audio integration tests passed. Device checks output silent PCM; waveform comparisons run before muting.");
return passed == tests.Length ? 0 : 1;

async Task WavePlayback()
{
    using var audio = new AudioTransport(outputGain: 0);
    True(await audio.LoadAsync(wave), audio.Error ?? "Could not open the WAV or output device");
    True(audio.CanPlay && !audio.IsPlaying && !audio.IsLoading, "Loaded audio is not paused and ready");
    Near(4000, audio.DurationMs, 1);
    await Task.Delay(100);
    Near(0, audio.PositionMs, 1);
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > 120 || audio.Error is not null, 2500);
    True(audio.IsPlaying && audio.PositionMs < 1000, audio.Error ?? "The device clock did not advance during playback");
    audio.Pause();
    await audio.WaitForCommandsAsync();
    double paused = audio.PositionMs;
    await Task.Delay(120);
    Near(paused, audio.PositionMs, 2);
    True(!audio.IsPlaying && audio.CanPlay, "Paused state is not retained");
    audio.Seek(1100);
    Near(1100, audio.PositionMs, 0.01);
    await audio.WaitForCommandsAsync();
    await Task.Delay(100);
    Near(1100, audio.PositionMs, 1);
    True(!audio.IsPlaying, "Paused seek started output");
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > 1180 || audio.Error is not null, 2500);
    True(audio.CanPlay && audio.IsPlaying && audio.Error is null && audio.PositionMs < 1600,
        audio.Error ?? "Resume did not retain the paused device position");
    audio.Pause();
    await audio.WaitForCommandsAsync();
}

async Task PlayingSeek()
{
    using var audio = new AudioTransport(outputGain: 0);
    True(await audio.LoadAsync(wave), audio.Error ?? "Load failed");
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > 80, 2000);
    audio.Seek(1500);
    Near(1500, audio.PositionMs, 0.01);
    await audio.WaitForCommandsAsync();
    True(audio.IsPlaying, "A playing seek paused output");
    for (int i = 0; i < 40; i++) audio.Seek(2000 + i * 10);
    Near(2390, audio.PositionMs, 5);
    await audio.WaitForCommandsAsync();
    True(audio.IsPlaying && audio.PositionMs >= 2389 && audio.PositionMs < 2600, "Rapid seek did not retain the latest requested position");
    await Until(() => audio.PositionMs > 2480, 2000);
    audio.Pause();
    await audio.WaitForCommandsAsync();
}

async Task EndAndReplay()
{
    using var audio = new AudioTransport(outputGain: 0);
    True(await audio.LoadAsync(wave), audio.Error ?? "Load failed");
    audio.Seek(audio.DurationMs - 100);
    await audio.WaitForCommandsAsync();
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => !audio.IsPlaying, 2500);
    Near(audio.DurationMs, audio.PositionMs, 1);
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > 100 && audio.PositionMs < 800, 2500);
    True(audio.IsPlaying, "Replay from EOF did not restart audio");
    audio.Pause();
    await audio.WaitForCommandsAsync();
}

async Task LoadFailures()
{
    using var audio = new AudioTransport(outputGain: 0);
    True(!await audio.LoadAsync(Path.Combine(directory, "missing.mp3")), "A missing audio file loaded successfully");
    True(!audio.CanPlay && !audio.IsLoading && audio.Error is not null, "Missing audio did not retain a clear error");
    audio.Play(); await Task.Delay(100);
    Near(0, audio.PositionMs, 1);
    string invalid = Path.Combine(directory, "invalid.wav");
    File.WriteAllText(invalid, "invalid wav fixture");
    True(!await audio.LoadAsync(invalid), "Malformed audio loaded successfully");
    True(await audio.LoadAsync(wave), audio.Error ?? "A valid load did not recover after errors");
    True(audio.CanPlay && audio.Error is null, "Recovered audio retained its error");
    var old = audio.LoadAsync(Path.Combine(directory, "missing-again.mp3"));
    var latest = audio.LoadAsync(wave);
    await old;
    True(await latest, audio.Error ?? "Latest load lost to a superseded request");
    True(audio.FilePath == wave && audio.CanPlay, "A superseded load changed the active file");
}

async Task Mp3Samples()
{
    string[] files = Directory.GetFiles(Path.Combine(root, "artifacts", "beatmaps"), "*.mp3", SearchOption.AllDirectories);
    True(files.Length >= 2, "Both supplied MP3 fixtures are required");
    foreach (string file in files.Take(2)) await ExerciseFile(file);
}

async Task VorbisSample()
{
    string file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "quiet-tone.ogg");
    True(File.Exists(file), "OGG fixture is missing");
    await ExerciseFile(file);
}

async Task MediaFoundationLifetime()
{
    string file = Directory.GetFiles(Path.Combine(root, "artifacts", "beatmaps"), "*.mp3", SearchOption.AllDirectories).First();
    using var first = new AudioTransport(outputGain: 0);
    using var second = new AudioTransport(outputGain: 0);
    var loads = await Task.WhenAll(first.LoadAsync(file), second.LoadAsync(file));
    True(loads.All(loaded => loaded), "Concurrent MP3 readers failed to load");
    first.Dispose();
    second.Play();
    await second.WaitForCommandsAsync();
    await Until(() => second.PositionMs > 80 || second.Error is not null, 3000);
    True(second.Error is null && second.IsPlaying, second.Error ?? "Disposing the sibling stopped Media Foundation");
    second.Pause();
    await second.WaitForCommandsAsync();
}

async Task LoadDuringSeek()
{
    using var audio = new AudioTransport(outputGain: 0);
    True(await audio.LoadAsync(wave), audio.Error ?? "Load failed");
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > 80, 2000);
    for (int i = 0; i < 20; i++) audio.Seek(1000 + 50 * i);
    True(await audio.LoadAsync(wave), audio.Error ?? "Replacement load failed");
    await audio.WaitForCommandsAsync();
    Near(0, audio.PositionMs, 1);
    True(!audio.IsPlaying, "New audio inherited the replaced stream's play state");
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > 80 && audio.PositionMs < 600, 2000);
    audio.Pause();
    await audio.WaitForCommandsAsync();
}

async Task DeviceClockAlignment()
{
    string file = Directory.GetFiles(Path.Combine(root, "artifacts", "beatmaps"), "*.mp3", SearchOption.AllDirectories).First();
    using var audio = new AudioTransport(outputGain: 0);
    True(await audio.LoadAsync(file), audio.Error ?? "MP3 load failed");
    const double requested = 12345.123;
    audio.Seek(requested);
    await audio.WaitForCommandsAsync();
    using var reference = MediaFoundationAudioReader.Open(file);
    double frameMs = 1000.0 / reference.WaveFormat.SampleRate;
    Near(Math.Floor(requested / frameMs) * frameMs, audio.PositionMs, 0.001);
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > requested + 120, 2500);
    double start = audio.PositionMs;
    var timer = Stopwatch.StartNew();
    await Task.Delay(1200);
    double elapsed = timer.Elapsed.TotalMilliseconds;
    double deviceElapsed = audio.PositionMs - start;
    Console.WriteLine($"  Device clock: {deviceElapsed:0.00}ms audio / {elapsed:0.00}ms elapsed");
    Near(elapsed, deviceElapsed, 40);
    audio.Pause();
    await audio.WaitForCommandsAsync();
    double paused = audio.PositionMs;
    await Task.Delay(160);
    Near(paused, audio.PositionMs, 0.001);
    audio.Seek(200.456);
    await audio.WaitForCommandsAsync();
    Near(Math.Floor(200.456 / frameMs) * frameMs, audio.PositionMs, 0.001);
    True(!audio.IsPlaying, "Frame-aligned backward seek unexpectedly resumed playback.");
}

async Task RepeatedLifecycle()
{
    using var audio = new AudioTransport(outputGain: 0);
    True(await audio.LoadAsync(wave), audio.Error ?? "Load failed");
    for (int i = 0; i < 200; i++)
    {
        audio.Play();
        audio.Pause();
        audio.Play();
        await audio.WaitForCommandsAsync().WaitAsync(TimeSpan.FromSeconds(8));
        audio.Pause();
        await audio.WaitForCommandsAsync().WaitAsync(TimeSpan.FromSeconds(8));
        audio.Seek(100 + i % 30 * 100);
        await audio.WaitForCommandsAsync().WaitAsync(TimeSpan.FromSeconds(8));
        True(audio.Error is null && audio.CanPlay && !audio.IsPlaying, audio.Error ?? $"Output failed in cycle {i}");
        if (i % 25 == 0) Console.WriteLine($"  Lifecycle cycle {i + 1}/200");
    }
    audio.Play();
    await audio.WaitForCommandsAsync().WaitAsync(TimeSpan.FromSeconds(8));
    double start = audio.PositionMs;
    await Until(() => audio.PositionMs > start + 80 || audio.Error is not null, 2500);
    True(audio.CanPlay && audio.IsPlaying && audio.Error is null, audio.Error ?? "Clock did not recover after cycles");
}

Task DecodeCancellation()
{
    string file = Directory.GetFiles(Path.Combine(root, "artifacts", "beatmaps"), "*.mp3", SearchOption.AllDirectories).First();
    int checks = 0;
    bool cancelled = false;
    try { using var ignored = MediaFoundationAudioReader.Open(file, () => ++checks > 8); }
    catch (OperationCanceledException) { cancelled = true; }
    True(cancelled, "The PCM decode did not observe cancellation.");
    using var recovered = MediaFoundationAudioReader.Open(file);
    True(recovered.Read(new byte[4096], 0, 4096) == 4096, "A cancelled decoder left Media Foundation unusable.");
    return Task.CompletedTask;
}

static async Task ExerciseFile(string file)
{
    using var audio = new AudioTransport(outputGain: 0);
    True(await audio.LoadAsync(file), audio.Error ?? $"Load failed: {Path.GetFileName(file)}");
    True(audio.DurationMs > 500, "Fixture duration is too short");
    Console.WriteLine($"  {Path.GetFileName(file)}: {audio.DurationMs:0.0} ms");
    double target = audio.DurationMs / 2;
    audio.Seek(target);
    await audio.WaitForCommandsAsync();
    Near(target, audio.PositionMs, 2);
    True(!audio.IsPlaying, "A paused codec seek started playback");
    audio.Play();
    await audio.WaitForCommandsAsync();
    await Until(() => audio.PositionMs > target + 80 || audio.Error is not null, 3000);
    True(audio.Error is null && audio.IsPlaying, audio.Error ?? "Decoder did not produce playable output");
    audio.Seek(200);
    await audio.WaitForCommandsAsync();
    True(audio.IsPlaying, "Codec seek did not preserve playback");
    await Until(() => audio.PositionMs > 280 || audio.Error is not null, 3000);
    True(audio.Error is null, audio.Error ?? "Codec failed after seek");
    audio.Pause();
    await audio.WaitForCommandsAsync();
}

static async Task Until(Func<bool> condition, int timeoutMs)
{
    var timer = Stopwatch.StartNew();
    while (!condition())
    {
        if (timer.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Audio condition did not become true");
        await Task.Delay(15);
    }
}
static void Near(double expected, double actual, double tolerance)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected:0.###}, got {actual:0.###}");
}
static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
static string FindRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "global.json"))) current = current.Parent;
    return current?.FullName ?? throw new DirectoryNotFoundException("Project root not found");
}

namespace FruitsAtelier.App
{
    internal static class AppLog
    {
        public static void Write(string message) => Trace.WriteLine(message);
    }
}
