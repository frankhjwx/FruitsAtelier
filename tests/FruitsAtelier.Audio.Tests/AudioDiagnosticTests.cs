using System.Text.Json;
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using NAudio.Wave;

internal static class AudioDiagnosticTests
{
    public static async Task Device(string wave, string directory)
    {
        string[] names = ["FRUITSATELIER_AUDIO_TEMPO", "FRUITSATELIER_AUDIO_BUFFER", "FRUITSATELIER_AUDIO_CAPTURE"];
        string?[] previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (var (profile, buffer) in new[] { ("default", "80"), ("short-window", "80"), ("default", "160") })
            {
                Environment.SetEnvironmentVariable(names[0], profile);
                Environment.SetEnvironmentVariable(names[1], buffer);
                Environment.SetEnvironmentVariable(names[2], "1");
                string path = Path.Combine(directory, "device-diagnostics", Guid.NewGuid().ToString("N"));
                using (var audio = new AudioTransport(0, diagnosticDirectory: path))
                {
                    audio.SetPlaybackSpeed(.25);
                    if (!await audio.LoadAsync(wave)) throw new Exception(audio.Error);
                    audio.Play(); await audio.WaitForCommandsAsync();
                    await Task.Delay(650);
                    audio.Pause(); await audio.WaitForCommandsAsync();
                    if (audio.PositionMs < 50) throw new Exception("Diagnostic device clock did not advance.");
                }
                var entries = File.ReadLines(Directory.GetFiles(path, "*.jsonl").Single())
                    .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
                if (!entries.Any(e => e.GetProperty("kind").GetString() == "deviceConfiguration"
                    && e.GetProperty("data").GetProperty("configuration").GetProperty("bufferFrames").GetInt32() > 0))
                    throw new Exception("Missing actual device buffer configuration.");
                if (!entries.Any(e => e.GetProperty("kind").GetString() == "clock"
                    && e.GetProperty("data").GetProperty("deviceBuffer").GetProperty("submittedFrames").GetInt64() > 0))
                    throw new Exception("Missing device submission counters.");
                using var clip = new WaveFileReader(Directory.GetFiles(path, "*.wav").Single());
                byte[] pcm = new byte[checked((int)clip.Length)];
                clip.Read(pcm, 0, pcm.Length);
                if (pcm.Length == 0 || pcm.Any(b => b != 0)) throw new Exception("Device test output was not silent.");
                Console.WriteLine($"PASS Silent diagnostic device: {profile}, {buffer} ms, buffer telemetry and PCM capture");
            }
        }
        finally { for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]); }
    }

    public static async Task Run(string wave, string directory)
    {
        CaptureChecks(wave, directory);
        string capture = Path.Combine(directory, "diagnostics", Guid.NewGuid().ToString("N"));
        var players = new List<PausePositionTests.BufferedPlayer>();
        using (var audio = new AudioTransport(0, () =>
        {
            var player = new PausePositionTests.BufferedPlayer(); players.Add(player); return player;
        }, diagnosticDirectory: capture))
        {
            if (!await audio.LoadAsync(wave)) throw new Exception(audio.Error);
            audio.SetPlaybackSpeed(.25); await audio.WaitForCommandsAsync();
            for (int i = 0; i < 5; i++)
            {
                audio.Play(); await audio.WaitForCommandsAsync();
                players[^1].Advance(200); await audio.WaitForCommandsAsync();
                double before = audio.PositionMs;
                audio.Pause(); await audio.WaitForCommandsAsync();
                if (Math.Abs(audio.PositionMs - before) > 1000d / 44100) throw new Exception("Logging changed the pause position.");
                audio.TracePresentation(before, true, audio.State);
            }
        }
        string transportLog = Directory.GetFiles(capture, "audio-*.jsonl").Single();
        var records = File.ReadLines(transportLog).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        var queued = records.Where(r => r.GetProperty("kind").GetString() == "commandQueued").ToArray();
        var ended = records.Where(r => r.GetProperty("kind").GetString() == "commandEnd").ToArray();
        foreach (var request in queued)
        {
            long id = request.GetProperty("data").GetProperty("Id").GetInt64();
            if (!ended.Any(r => r.GetProperty("data").GetProperty("detail").GetProperty("Id").GetInt64() == id))
                throw new Exception("Diagnostic command has no matching completion.");
        }
        foreach (string kind in new[] { "environment", "commandBegin", "stopBegin", "stopEnd", "resetEnd", "presentation", "clock", "sourceRead", "firstDeviceProgress", "logClosed" })
            if (!records.Any(r => r.GetProperty("kind").GetString() == kind)) throw new Exception("Missing diagnostic event: " + kind);
        var progress = records.Where(r => r.GetProperty("kind").GetString() == "firstDeviceProgress").ToArray();
        if (progress.Length != 5 || progress.Select(r => r.GetProperty("data").GetProperty("session").GetInt64()).Distinct().Count() != 5)
            throw new Exception("First device progress must be recorded once per playing session.");
        foreach (var entry in progress)
        {
            var data = entry.GetProperty("data");
            if (Math.Abs(data.GetProperty("deviceMs").GetDouble() - 200) > 0.001
                || Math.Abs(data.GetProperty("sourceReads").GetProperty("providedMs").GetDouble() - 280) > 0.001)
                throw new Exception("Read counters do not distinguish consumed audio from buffered audio.");
        }
        if (records[^1].GetProperty("dropped").GetInt64() != 0) throw new Exception("Diagnostic events were dropped.");
        var tempo = records.Where(r => r.GetProperty("kind").GetString() == "commandEnd")
            .Select(r => r.GetProperty("data").GetProperty("tempo"))
            .Where(t => t.ValueKind == JsonValueKind.Object && t.GetProperty("outputFrames").GetInt64() > 0).ToArray();
        if (tempo.Length == 0 || !tempo.Any(t => t.GetProperty("lastRms").GetDouble() > .001
            && t.GetProperty("inputFrames").GetInt64() > 0 && t.GetProperty("maximumProcessingMs").GetDouble() >= 0))
            throw new Exception("Low-speed diagnostics did not capture frames, processing time and pre-gain signal level.");
        if (File.ReadAllText(transportLog).Contains(wave.Replace("\\", "\\\\"))) throw new Exception("Diagnostic log contains the full source path.");

        string unsupported = Path.Combine(capture, "unsupported-alaw.wav");
        using (var writer = new WaveFileWriter(unsupported, WaveFormat.CreateALawFormat(8000, 1)))
            writer.Write(new byte[800], 0, 800);
        using (var hitsounds = new HitsoundPlayer(diagnosticDirectory: capture))
            hitsounds.Prepare(new Hitsound(CatchObjectKind.Fruit, unsupported, 1));
        var failed = Directory.GetFiles(capture, "audio-*.jsonl").SelectMany(File.ReadLines)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
            .Single(r => r.GetProperty("kind").GetString() == "hitsoundDecodeFailed");
        if (failed.GetProperty("data").GetProperty("fileName").GetString() != "unsupported-alaw.wav"
            || !failed.GetProperty("data").GetProperty("sourceFormat").GetString()!.Contains("ALaw"))
            throw new Exception("Unsupported hitsound diagnostics did not identify the file and encoding.");

        string blocked = Path.Combine(capture, "not-a-directory");
        File.WriteAllText(blocked, "file");
        using (var audio = new AudioTransport(0, () => new PausePositionTests.BufferedPlayer(), diagnosticDirectory: blocked))
            if (!await audio.LoadAsync(wave)) throw new Exception("An unwritable log prevented audio loading.");
        Console.WriteLine("PASS Audio diagnostic command correlation, pause positions, hitsound encoding, and unwritable log isolation");
    }

    private static void CaptureChecks(string wave, string directory)
    {
        string path = Path.Combine(directory, "pcm-diagnostics", Guid.NewGuid().ToString("N"));
        using (var log = new AudioDiagnosticLog(path))
        using (var source = new WaveFileReader(wave))
        using (var reference = new WaveFileReader(wave))
        {
            using (var capture = new DiagnosticPcmCapture(source, log, 1, () => true, 0, .25, "default", seconds: 1))
            {
                byte[] actual = new byte[4104], expected = new byte[4096];
                int read;
                while ((read = capture.Read(actual, 4, 4096)) > 0)
                {
                    int expectedRead = reference.Read(expected, 0, expected.Length);
                    if (read != expectedRead || !actual.AsSpan(4, read).SequenceEqual(expected.AsSpan(0, read)))
                        throw new Exception("PCM capture changed output bytes or read lengths.");
                }
            }
            using var saved = new WaveFileReader(Directory.GetFiles(path, "*.wav").Single());
            if (saved.Length != source.WaveFormat.AverageBytesPerSecond)
                throw new Exception("PCM capture did not stop at its duration limit.");
            reference.Position = 0;
            byte[] a = new byte[checked((int)saved.Length)], b = new byte[a.Length];
            saved.Read(a, 0, a.Length); reference.Read(b, 0, b.Length);
            if (!a.SequenceEqual(b)) throw new Exception("Saved WAV differs from supplied PCM.");
            source.Position = 0;
            using (var rejected = new DiagnosticPcmCapture(source, log, 2, () => false, 0, .25, "default", seconds: 1))
                rejected.Read(a, 0, a.Length);
            if (Directory.GetFiles(path, "*.wav").Length != 1)
                throw new Exception("Capture budget was ignored.");
        }
        var disabled = AudioDiagnosticOptions.Read(false);
        if (disabled != new AudioDiagnosticOptions("default", 80, false))
            throw new Exception("Disabled diagnostics changed normal playback options.");
        using (var reader = new WaveFileReader(wave))
        {
            var tempo = new TempoSampleProvider(reader.ToSampleProvider(), .25, true, "short-window");
            var snapshot = JsonSerializer.SerializeToElement(tempo.Snapshot());
            if (snapshot.GetProperty("quickSeek").GetInt32() != 1 || snapshot.GetProperty("sequenceMs").GetInt32() != 30
                || snapshot.GetProperty("overlapMs").GetInt32() != 4)
                throw new Exception("Short-window tempo profile was not applied.");
            float[] buffer = new float[4096];
            if (tempo.Read(buffer, 0, buffer.Length) != buffer.Length || !buffer.Any(v => Math.Abs(v) > .001f))
                throw new Exception("Short-window profile did not produce audio.");
        }
        Console.WriteLine("PASS Diagnostic PCM capture is byte-exact, bounded, budgeted, and tempo options are applied");
    }
}
