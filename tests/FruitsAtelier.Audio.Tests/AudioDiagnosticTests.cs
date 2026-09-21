using System.Text.Json;
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using NAudio.Wave;

internal static class AudioDiagnosticTests
{
    public static async Task Run(string wave, string directory)
    {
        string capture = Path.Combine(directory, "diagnostics", Guid.NewGuid().ToString("N"));
        var players = new List<PausePositionTests.BufferedPlayer>();
        using (var audio = new AudioTransport(0, () =>
        {
            var player = new PausePositionTests.BufferedPlayer(); players.Add(player); return player;
        }, diagnosticDirectory: capture))
        {
            if (!await audio.LoadAsync(wave)) throw new Exception(audio.Error);
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
}
