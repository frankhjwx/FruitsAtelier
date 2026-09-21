using L = FruitsAtelier.Localization.Strings;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FruitsAtelier.App.Audio;

public sealed record AudioState(string? FilePath, double PositionMs, double DurationMs, bool IsPlaying,
    bool CanPlay, bool IsLoading, string? Error)
{
    public double PositionTimestampMs { get; init; }
}

public sealed class AudioTransport : IDisposable
{
    private enum CommandKind { Load, Play, Pause, Seek, Speed, Refresh, Barrier }
    private sealed record Command(CommandKind Kind, long LoadVersion, string? Path = null, double Position = 0,
        long SeekVersion = 0, long IntentVersion = 0, TaskCompletionSource<bool>? Completion = null)
    {
        public long Id { get; } = Interlocked.Increment(ref nextCommandId);
        public double QueuedMs { get; } = AudioDiagnosticLog.NowMs;
    }
    private static long nextCommandId;
    private sealed class OutputSession : IDisposable
    {
        private static long nextId;
        public long Id { get; } = Interlocked.Increment(ref nextId);
        public IWavePlayer Player { get; }
        public long PositionBytes => ((IWavePosition)Player).GetPosition();
        public TaskCompletionSource<bool> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Started { get; set; }
        public double PlayBeganMs { get; set; }
        public bool ProgressRecorded { get; set; }
        public DiagnosticWaveProvider? Probe { get; }
        public TempoSampleProvider? Tempo { get; }
        public Exception? Error { get; private set; }
        public OutputSession(IWaveProvider source, Action wake, Func<IWavePlayer> createPlayer, AudioDiagnosticLog diagnostics,
            TempoSampleProvider? tempo)
        {
            Tempo = tempo;
            Player = createPlayer();
            if (diagnostics.Enabled) source = Probe = new DiagnosticWaveProvider(source, diagnostics, Id);
            Player.PlaybackStopped += (_, e) =>
            {
                Error = e.Exception;
                if (diagnostics.Enabled) diagnostics.Write("playbackStopped", new { session = Id,
                    errorType = e.Exception?.GetType().FullName, hresult = e.Exception?.HResult });
                Stopped.TrySetResult(true); wake();
            };
            try { Player.Init(source); }
            catch { Player.Dispose(); throw; }
        }
        public void Dispose() => Player.Dispose();
    }

    private readonly object stateLock = new();
    private readonly Channel<Command> commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private readonly AudioDiagnosticLog diagnostics;
    private double nextDiagnosticMs, nextPresentationMs, lastPresentationMs, maximumPresentationGapMs;
    private readonly Func<IWavePlayer> createPlayer;
    private readonly float outputGain;
    internal HitsoundPlayer? Hitsounds { get; set; }
    private readonly TimeSpan stopTimeout;
    private AudioState state = new(null, 0, 0, false, false, false, null);
    private long loadVersion, seekVersion, appliedSeekVersion, loadedVersion;
    private long intentVersion, appliedIntentVersion;
    private double requestedPosition, basePosition, duration;
    private double playbackSpeed = 1, requestedSpeed = 1;
    public double PlaybackSpeed => Volatile.Read(ref playbackSpeed);
    private bool playIntent;
    private bool requestedPlaying;
    private WaveStream? reader;
    private OutputSession? output;
    private string? loadedPath;
    private bool recoveryAttempted;
    private int disposed;

    public AudioTransport() : this(1) { }
    internal AudioTransport(float outputGain, Func<IWavePlayer>? createPlayer = null, TimeSpan? stopTimeout = null,
        string? diagnosticDirectory = null)
    {
        diagnostics = new AudioDiagnosticLog(diagnosticDirectory);
        this.outputGain = outputGain;
        this.createPlayer = createPlayer ?? (() => new WasapiOut(AudioClientShareMode.Shared, true, 80));
        this.stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(3);
        worker = Task.Run(WorkAsync);
    }
    public AudioState State => Volatile.Read(ref state);
    public string? FilePath => State.FilePath;
    public double PositionMs => State.PositionMs;
    public double DurationMs => State.DurationMs;
    public bool IsPlaying => State.IsPlaying;
    public bool CanPlay => State.CanPlay;
    public bool IsLoading => State.IsLoading;
    public string? Error => State.Error;

    private bool Enqueue(Command command)
    {
        if (diagnostics.Enabled && command.Kind is not (CommandKind.Refresh or CommandKind.Barrier))
            diagnostics.Write("commandQueued", new { command.Id, command = command.Kind.ToString(), command.LoadVersion,
                command.SeekVersion, command.IntentVersion, target = command.Position, queuedMs = command.QueuedMs,
                displayedPositionMs = State.PositionMs, requestedPlaying, extension = System.IO.Path.GetExtension(command.Path) });
        return commands.Writer.TryWrite(command);
    }

    private void TraceSnapshot(string kind, object? detail = null)
    {
        if (!diagnostics.Enabled) return;
        try
        {
            diagnostics.Write(kind, new { detail, session = output?.Id, basePositionMs = basePosition,
                devicePositionBytes = output?.PositionBytes, bytesPerSecond = output?.Player.OutputWaveFormat.AverageBytesPerSecond,
                readerPositionMs = reader?.CurrentTime.TotalMilliseconds, publishedPositionMs = State.PositionMs,
                publishedPlaying = State.IsPlaying, playIntent, playbackSpeed, loadVersion = loadedVersion,
                appliedIntentVersion, requestedIntentVersion = Interlocked.Read(ref intentVersion),
                appliedSeekVersion, requestedSeekVersion = Interlocked.Read(ref seekVersion),
                outputState = output?.Player.PlaybackState.ToString(), started = output?.Started,
                stopCallback = output?.Stopped.Task.IsCompleted, sourceReads = output?.Probe?.Snapshot(),
                tempo = diagnostics.Enabled ? output?.Tempo?.Snapshot() : null });
        }
        catch (Exception ex) { diagnostics.Write("snapshotFailed", new { kind, type = ex.GetType().FullName, ex.HResult }); }
    }

    internal void TracePresentation(double previousPositionMs, bool previousPlaying, AudioState presented)
    {
        if (!diagnostics.Enabled) return;
        double now = AudioDiagnosticLog.NowMs;
        if (previousPlaying && lastPresentationMs != 0)
            maximumPresentationGapMs = Math.Max(maximumPresentationGapMs, now - lastPresentationMs);
        lastPresentationMs = now;
        if (previousPlaying == presented.IsPlaying && now < nextPresentationMs
            && (presented.IsPlaying || Math.Abs(previousPositionMs - presented.PositionMs) < 0.01)) return;
        nextPresentationMs = now + 250;
        diagnostics.Write("presentation", new { previousPositionMs, previousPlaying, positionMs = presented.PositionMs,
            playing = presented.IsPlaying, snapshotAgeMs = now - presented.PositionTimestampMs,
            maximumUpdateGapMs = maximumPresentationGapMs });
        maximumPresentationGapMs = 0;
    }

    public void Load(string path) => _ = LoadAsync(path);

    public Task<bool> LoadAsync(string path)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (stateLock)
        {
            if (disposed != 0) { completion.SetResult(false); return completion.Task; }
            long version = ++loadVersion;
            appliedSeekVersion = ++seekVersion;
            appliedIntentVersion = ++intentVersion;
            requestedPlaying = false;
            requestedPosition = 0;
            Volatile.Write(ref state, new(path, 0, 0, false, false, true, null));
            Enqueue(new(CommandKind.Load, version, path, Completion: completion));
        }
        return completion.Task;
    }

    public void Play()
    {
        lock (stateLock)
        {
            if (disposed != 0 || !state.CanPlay) return;
            requestedPlaying = true;
            Volatile.Write(ref state, state with { IsPlaying = true });
            Enqueue(new(CommandKind.Play, loadVersion, IntentVersion: ++intentVersion));
        }
    }

    public void Pause()
    {
        lock (stateLock)
        {
            if (disposed != 0) return;
            requestedPlaying = false;
            requestedPosition = state.PositionMs;
            long positionVersion = ++seekVersion;
            Volatile.Write(ref state, state with { IsPlaying = false });
            Enqueue(new(CommandKind.Pause, loadVersion, Position: requestedPosition,
                SeekVersion: positionVersion, IntentVersion: ++intentVersion));
        }
    }

    public void Seek(double positionMs)
    {
        if (!double.IsFinite(positionMs)) return;
        lock (stateLock)
        {
            if (disposed != 0 || !state.CanPlay) return;
            requestedPosition = Math.Clamp(positionMs, 0, state.DurationMs);
            long version = ++seekVersion;
            Volatile.Write(ref state, state with { PositionMs = requestedPosition });
            Enqueue(new(CommandKind.Seek, loadVersion, Position: requestedPosition, SeekVersion: version));
        }
    }

    public void SetPlaybackSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed < .1 || speed > 1.5) return;
        lock (stateLock)
            if (disposed == 0) { requestedSpeed = speed; Enqueue(new(CommandKind.Speed, loadVersion, Position: speed)); }
    }

    public Task WaitForCommandsAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (stateLock)
        {
            if (disposed != 0) completion.SetResult(false);
            else commands.Writer.TryWrite(new(CommandKind.Barrier, loadVersion, Completion: completion));
        }
        return completion.Task;
    }

    private async Task WorkAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                while (commands.Reader.TryRead(out var command))
                {
                    if (command.LoadVersion != Interlocked.Read(ref loadVersion))
                    {
                        if (diagnostics.Enabled) diagnostics.Write("commandSuperseded", new { command.Id });
                        command.Completion?.TrySetResult(false); continue;
                    }
                    double beganMs = AudioDiagnosticLog.NowMs;
                    bool traceCommand = diagnostics.Enabled && command.Kind is not (CommandKind.Refresh or CommandKind.Barrier);
                    if (traceCommand) TraceSnapshot("commandBegin", new { command.Id, command = command.Kind.ToString(),
                        queueDelayMs = beganMs - command.QueuedMs });
                    try
                    {
                        switch (command.Kind)
                        {
                            case CommandKind.Load: await LoadCoreAsync(command); break;
                            case CommandKind.Play:
                                if (reader is null || output is null) break;
                                playIntent = true;
                                if (output.Started)
                                {
                                    var playbackState = output.Player.PlaybackState;
                                    if (output.Stopped.Task.IsCompleted || playbackState == PlaybackState.Stopped)
                                        await ResetOutputAsync(0);
                                }
                                else if (basePosition >= duration - 0.5)
                                    await ResetOutputAsync(0);
                                StartOutput();
                                Interlocked.Exchange(ref appliedIntentVersion, command.IntentVersion);
                                break;
                            case CommandKind.Pause:
                                playIntent = false;
                                // WasapiOut.Pause leaves submitted buffers and the device clock running.
                                // The request owns the pause point; the device can advance while this command waits.
                                if (reader is not null)
                                    await ResetOutputAsync(command.Position, command.SeekVersion);
                                Interlocked.Exchange(ref appliedIntentVersion, command.IntentVersion);
                                break;
                            case CommandKind.Speed:
                                if (playbackSpeed == command.Position) break;
                                double position = output is null ? basePosition : DevicePosition();
                                playbackSpeed = command.Position;
                                if (reader is not null)
                                {
                                    await ResetOutputAsync(position);
                                    if (playIntent) StartOutput();
                                }
                                break;
                            case CommandKind.Seek:
                                if (command.SeekVersion != Interlocked.Read(ref seekVersion) || reader is null) break;
                                await ResetOutputAsync(command.Position, command.SeekVersion);
                                if (playIntent) StartOutput();
                                break;
                        }
                        UpdateDeviceClock();
                        command.Completion?.TrySetResult(command.LoadVersion == Interlocked.Read(ref loadVersion));
                    }
                    catch (Exception ex)
                    {
                        playIntent = false;
                        PublishError(command.LoadVersion, ex);
                        command.Completion?.TrySetResult(false);
                        await TryReleaseAudioAsync();
                    }
                    finally
                    {
                        if (traceCommand) TraceSnapshot("commandEnd", new { command.Id,
                            command = command.Kind.ToString(), elapsedMs = AudioDiagnosticLog.NowMs - beganMs });
                    }
                }
                try { UpdateDeviceClock(); }
                catch (Exception ex)
                {
                    playIntent = false;
                    PublishError(loadedVersion, ex);
                    await TryReleaseAudioAsync();
                }
                if (playIntent) await Task.Delay(10, cancellation.Token);
                else await commands.Reader.WaitToReadAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { PublishError(loadedVersion, ex); }
        finally
        {
            await TryReleaseAudioAsync();
            while (commands.Reader.TryRead(out var pending)) pending.Completion?.TrySetResult(false);
        }
    }

    private async Task LoadCoreAsync(Command command)
    {
        playIntent = false;
        await ReleaseAudioAsync();
        loadedVersion = command.LoadVersion;
        loadedPath = command.Path;
        recoveryAttempted = false;
        if (string.IsNullOrWhiteSpace(command.Path) || !File.Exists(command.Path)) throw new FileNotFoundException(L.Get("audio.fileMissing"), command.Path);
        reader = OpenReader(command.Path, () => cancellation.IsCancellationRequested || command.LoadVersion != Interlocked.Read(ref loadVersion));
        duration = reader.TotalTime.TotalMilliseconds;
        if (diagnostics.Enabled) diagnostics.Write("sourceLoaded", new { extension = Path.GetExtension(command.Path),
            decoder = reader.GetType().Name, format = reader.WaveFormat.ToString(), durationMs = duration });
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException(L.Get("audio.noDuration"));
        if (reader.WaveFormat.Channels is < 1 or > 2) throw new NotSupportedException(L.Get("audio.channels"));
        basePosition = 0;
        lock (stateLock) playbackSpeed = requestedSpeed;
        output = CreateOutput();
        lock (stateLock)
            if (loadedVersion == loadVersion) appliedSeekVersion = seekVersion;
        Publish(0, false);
    }

    private static WaveStream OpenReader(string path, Func<bool> cancelled)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => new WaveFileReader(path),
            ".mp3" => MediaFoundationAudioReader.Open(path, cancelled),
            ".ogg" => new VorbisWaveReader(path),
            _ => throw new NotSupportedException(L.Get("audio.formats"))
        };
    }

    private OutputSession CreateOutput()
    {
        ISampleProvider samples = reader!.ToSampleProvider();
        TempoSampleProvider? tempo = null;
        if (playbackSpeed != 1) samples = tempo = new TempoSampleProvider(samples, playbackSpeed, diagnostics.Enabled);
        samples = new PlaybackGain(samples, () => SongVolume);
        if (Hitsounds is not null) samples = Hitsounds.MixWithMusic(samples, basePosition, playbackSpeed);
        var pcm = new SampleToWaveProvider16(samples) { Volume = outputGain };
        long version = loadedVersion;
        var session = new OutputSession(pcm, () => commands.Writer.TryWrite(new(CommandKind.Refresh, version)), createPlayer, diagnostics, tempo);
        if (diagnostics.Enabled) diagnostics.Write("outputCreated", new { session = session.Id,
            backend = session.Player.GetType().Name, outputFormat = session.Player.OutputWaveFormat.ToString(),
            sourceFormat = pcm.WaveFormat.ToString(), requestedLatencyMs = session.Player is WasapiOut ? 80 : (int?)null,
            basePositionMs = basePosition, playbackSpeed });
        return session;
    }

    private float songVolume = 1;
    public float SongVolume { get => Volatile.Read(ref songVolume); set => Volatile.Write(ref songVolume, float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1); }

    private async Task ResetOutputAsync(double position, long requestedSeek = 0)
    {
        TraceSnapshot("resetBegin", new { targetMs = position, requestedSeek });
        try { await ReleaseOutputAsync(); }
        catch (TimeoutException) when (!recoveryAttempted && loadedPath is not null
            && loadedVersion == Interlocked.Read(ref loadVersion) && !cancellation.IsCancellationRequested)
        {
            recoveryAttempted = true;
            long version = loadedVersion;
            AppLog.Write($"Audio rebuilding output after stop timeout; load={version}; seek={requestedSeek}; positionMs={position:0.###}");
            // The retired thread may still read its decoder. Recovery must own a separate reader and device.
            reader = OpenReader(loadedPath, () => cancellation.IsCancellationRequested || version != Interlocked.Read(ref loadVersion));
            duration = reader.TotalTime.TotalMilliseconds;
        }
        if (reader is null) return;
        if (requestedSeek != 0 && requestedSeek != Interlocked.Read(ref seekVersion)) return;
        // Stop and wait for the playback thread before moving the decoder; queued device buffers must not survive a seek.
        reader.CurrentTime = TimeSpan.FromMilliseconds(Math.Clamp(position, 0, duration));
        basePosition = reader.CurrentTime.TotalMilliseconds;
        output = CreateOutput();
        lock (stateLock)
            if (requestedSeek != 0 && loadedVersion == loadVersion && requestedSeek == seekVersion) appliedSeekVersion = requestedSeek;
        Publish(basePosition, false);
        TraceSnapshot("resetEnd", new { targetMs = position, actualMs = basePosition, requestedSeek });
    }

    private void StartOutput()
    {
        if (output is null || output.Started) return;
        if (basePosition >= duration - 0.5) { playIntent = false; return; }
        TraceSnapshot("playBegin");
        output.PlayBeganMs = AudioDiagnosticLog.NowMs;
        output.Player.Play();
        output.Started = true;
        TraceSnapshot("playEnd");
    }

    private void UpdateDeviceClock()
    {
        if (output is null || reader is null) return;
        if (output.Error is { } error) throw new InvalidOperationException(L.Get("audio.deviceFailed"), error);
        if (output.Started && output.Stopped.Task.IsCompleted)
        {
            playIntent = false;
            basePosition = duration;
            Publish(duration, false);
            return;
        }
        Publish(DevicePosition(), output.Player.PlaybackState == PlaybackState.Playing);
        if (diagnostics.Enabled && AudioDiagnosticLog.NowMs >= nextDiagnosticMs)
        {
            nextDiagnosticMs = AudioDiagnosticLog.NowMs + 250;
            TraceSnapshot("clock", new { threadPoolThreads = ThreadPool.ThreadCount,
                pendingWork = ThreadPool.PendingWorkItemCount, gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2) });
        }
    }

    private double DevicePosition()
    {
        long bytes = output!.PositionBytes;
        double deviceMs = bytes * 1000d / output.Player.OutputWaveFormat.AverageBytesPerSecond;
        if (diagnostics.Enabled && output.Started && !output.ProgressRecorded && bytes > 0)
        {
            output.ProgressRecorded = true;
            diagnostics.Write("firstDeviceProgress", new { session = output.Id,
                sincePlayMs = AudioDiagnosticLog.NowMs - output.PlayBeganMs, deviceMs,
                basePositionMs = basePosition, playbackSpeed, sourceReads = output.Probe?.Snapshot() });
        }
        return Math.Clamp(basePosition + deviceMs * playbackSpeed, 0, duration);
    }

    private void Publish(double position, bool playing)
    {
        lock (stateLock)
        {
            if (loadedVersion != loadVersion || disposed != 0) return;
            // A queued seek or pause owns the displayed location until its output reset has finished.
            if (appliedSeekVersion != seekVersion) position = requestedPosition;
            if (appliedIntentVersion != intentVersion) playing = requestedPlaying;
            Volatile.Write(ref state, state with { PositionMs = position, DurationMs = duration, IsPlaying = playing,
                CanPlay = reader is not null && output is not null, IsLoading = false, Error = null,
                PositionTimestampMs = System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency });
        }
    }

    private void PublishError(long version, Exception exception)
    {
        if (diagnostics.Enabled) diagnostics.Write("error", new { version, type = exception.GetType().FullName,
            exception.HResult, exception.StackTrace, innerType = exception.InnerException?.GetType().FullName });
        lock (stateLock)
        {
            if (version != loadVersion || disposed != 0) return;
            Volatile.Write(ref state, state with { IsPlaying = false, CanPlay = false, IsLoading = false,
                Error = L.Get("audio.unavailable", L.Localized(exception.Message)) });
        }
        AppLog.Write(exception.ToString());
    }

    private async Task ReleaseOutputAsync()
    {
        var current = output;
        if (current is null) return;
        double stopBeganMs = AudioDiagnosticLog.NowMs;
        TraceSnapshot("stopBegin");
        try
        {
            current.Player.Stop();
            if (current.Started) await current.Stopped.Task.WaitAsync(stopTimeout);
            TraceSnapshot("stopEnd", new { elapsedMs = AudioDiagnosticLog.NowMs - stopBeganMs });
        }
        catch (Exception error)
        {
            output = null;
            var retiredReader = reader;
            reader = null;
            AppLog.Write($"Audio output retirement: session={current.Id}; started={current.Started}; state={current.Player.PlaybackState}; "
                + $"stopCallback={current.Stopped.Task.IsCompleted}; threadPoolThreads={ThreadPool.ThreadCount}; pendingWork={ThreadPool.PendingWorkItemCount}; {error}");
            _ = DisposeRetiredOutputAsync(current, retiredReader);
            throw;
        }
        output = null;
        current.Dispose();
    }

    private static async Task DisposeRetiredOutputAsync(OutputSession current, WaveStream? retiredReader)
    {
        // Stop changes PlaybackState before the playback thread exits; only its callback releases reader ownership.
        if (current.Started) await current.Stopped.Task;
        try { current.Dispose(); }
        catch (Exception error) { AppLog.Write($"Retired audio output {current.Id} disposal failed: {error}"); }
        finally
        {
            try { retiredReader?.Dispose(); }
            catch (Exception error) { AppLog.Write($"Retired audio reader {current.Id} disposal failed: {error}"); }
        }
        AppLog.Write($"Retired audio output {current.Id} released after stop callback.");
    }

    private async Task ReleaseAudioAsync()
    {
        try { await ReleaseOutputAsync(); }
        catch (TimeoutException) { /* The retired session owns its reader until the stop callback. */ }
        reader?.Dispose();
        reader = null;
    }

    private async Task TryReleaseAudioAsync()
    {
        try { await ReleaseAudioAsync(); }
        catch (Exception ex) { AppLog.Write("Audio cleanup failed: " + ex); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancellation.Cancel();
        commands.Writer.TryComplete();
        try { worker.GetAwaiter().GetResult(); }
        finally { cancellation.Dispose(); diagnostics.Dispose(); }
    }
}
