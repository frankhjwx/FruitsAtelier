using System.Text.Json;
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using NAudio.Wave;

internal static class AudioDiagnosticTests
{
    public static async Task Run(string wave, string directory)
    {
        foreach (string name in new[] { "event-10", "event-50", "poll-10", "poll-50" })
        {
            var profile = AudioDiagnosticProfile.Select(true, name);
            if (profile.Name != name || profile.EventDriven != name.StartsWith("event")
                || profile.BufferMs != (name.EndsWith("50") ? 50 : 10)) throw new Exception("Incorrect diagnostic profile.");
            if (AudioDiagnosticProfile.Select(false, name).Name != "event-10") throw new Exception("Diagnostic overrides affect normal playback.");
        }
        string capture = Path.Combine(directory, "diagnostics", Guid.NewGuid().ToString("N"));
        var players = new List<PausePositionTests.BufferedPlayer>();
        using (var audio = new AudioTransport(0, () =>
        {
            var player = new PausePositionTests.BufferedPlayer(); players.Add(player); return player;
        }, diagnosticDirectory: capture))
        {
            if (!await audio.LoadAsync(wave)) throw new Exception(audio.Error);
            audio.MarkDiagnosticIssue();
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
        foreach (string kind in new[] { "environment", "userReportedDelay", "outputConfiguration", "decodeBegin", "sourceIdentity", "outputInitialized", "commandBegin", "stopBegin", "stopEnd", "resetEnd", "presentation", "clock", "sourceRead", "firstDeviceProgress", "logClosed" })
            if (!records.Any(r => r.GetProperty("kind").GetString() == kind)) throw new Exception("Missing diagnostic event: " + kind);
        using (var source = File.OpenRead(wave))
        {
            string expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
            if (records.Single(r => r.GetProperty("kind").GetString() == "sourceIdentity")
                .GetProperty("data").GetProperty("sha256").GetString() != expectedHash)
                throw new Exception("Source identity does not match the decoded file.");
        }
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

        File.WriteAllText(Path.Combine(capture, "editor.log"), "capture metadata");
        File.WriteAllText(Path.Combine(capture, "private-map.osu"), "not part of the report");
        string report = await AudioDiagnosticCapture.ExportAsync(capture);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(report))
        {
            if (!archive.Entries.Any(e => e.Name == Path.GetFileName(transportLog))
                || !archive.Entries.Any(e => e.Name == "editor.log")
                || !archive.Entries.Any(e => e.Name == "capture.json")
                || archive.Entries.Any(e => e.Name == "private-map.osu")) throw new Exception("Report included incorrect files.");
        }
        using (var active = new AudioDiagnosticLog(capture))
        {
            active.Write("liveExport", new { });
            string liveReport = await AudioDiagnosticCapture.ExportAsync(capture);
            using var archive = System.IO.Compression.ZipFile.OpenRead(liveReport);
            if (archive.Entries.Count == 0) throw new Exception("Live log export failed.");
        }
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
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedLog = new AudioDiagnosticLog(capture, gate.Task);
        for (int i = 0; i < 3000; i++) delayedLog.Write("queuePressure", new { i });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        delayedLog.Dispose();
        if (timer.ElapsedMilliseconds > 1000) throw new Exception("A blocked diagnostic writer held shutdown.");
        gate.SetResult(true);
        await delayedLog.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var closed = JsonSerializer.Deserialize<JsonElement>(File.ReadLines(delayedLog.FilePath!).Last());
        if (closed.GetProperty("dropped").GetInt64() < 900) throw new Exception("Diagnostic queue was not bounded.");

        var textGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var textLog = new AsyncDiagnosticTextLog(Path.Combine(capture, "blocked-editor.log"), writerGate: textGate.Task);
        timer.Restart();
        for (int i = 0; i < 3000; i++) textLog.Write("message\n");
        if (timer.ElapsedMilliseconds > 1000 || textLog.Dropped < 900) throw new Exception("Editor log blocked a caller or exceeded its queue.");
        timer.Restart(); textLog.Dispose();
        if (timer.ElapsedMilliseconds > 1000) throw new Exception("Editor log blocked shutdown.");
        textGate.SetResult(true);
        await textLog.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        if (textLog.Failed || File.ReadAllLines(Path.Combine(capture, "blocked-editor.log")).Length != 2048)
            throw new Exception("Editor log failed to drain accepted records.");
        using (var denied = new AsyncDiagnosticTextLog(Path.Combine(blocked, "editor.log")))
        {
            denied.Write("message\n");
            denied.Dispose();
            if (!denied.Failed) throw new Exception("Editor log did not report a write failure.");
        }

        var limitedText = new AsyncDiagnosticTextLog(Path.Combine(capture, "limited-editor.log"), maximumBytes: 8);
        limitedText.Write("short\n"); limitedText.Write("short\n"); limitedText.Dispose();
        await limitedText.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        if (!limitedText.LimitReached || new FileInfo(Path.Combine(capture, "limited-editor.log")).Length > 8)
            throw new Exception("Editor capture exceeded its byte limit.");

        AudioDiagnosticCapture.Configure(new LibrarySettings { AudioDiagnostics = true,
            AudioDiagnosticFrames = true, AudioDiagnosticProfile = "poll-50" });
        if (!AudioDiagnosticLog.Requested || !AudioDiagnosticCapture.Frames
            || AudioDiagnosticCapture.Profile != "poll-50" || AudioDiagnosticLog.CaptureDirectory is null)
            throw new Exception("Saved settings did not activate release capture.");
        using (var logger = new AudioDiagnosticLog()) logger.Write("settingsCapture", new { });
        if (!Directory.GetFiles(AudioDiagnosticLog.CaptureDirectory, "audio-*.jsonl").Any())
            throw new Exception("Release capture did not create its run folder.");
        if (AudioDiagnosticCapture.Reserve(64 * 1024 * 1024 + 1) || !AudioDiagnosticCapture.LimitReached)
            throw new Exception("Audio event capture exceeded its shared byte budget.");
        Console.WriteLine("PASS Audio diagnostic command correlation, pause positions, hitsound encoding, and unwritable log isolation");
    }
}
