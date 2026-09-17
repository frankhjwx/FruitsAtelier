using L = FruitsAtelier.Localization.Strings;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FruitsAtelier.App.Audio;

public sealed record AudioState(string? FilePath, double PositionMs, double DurationMs, bool IsPlaying,
    bool CanPlay, bool IsLoading, string? Error);

public sealed class AudioTransport : IDisposable
{
    private enum CommandKind { Load, Play, Pause, Seek, Refresh, Barrier }
    private sealed record Command(CommandKind Kind, long LoadVersion, string? Path = null, double Position = 0,
        long SeekVersion = 0, long IntentVersion = 0, TaskCompletionSource<bool>? Completion = null);
    private sealed class OutputSession : IDisposable
    {
        private static long nextId;
        public long Id { get; } = Interlocked.Increment(ref nextId);
        public IWavePlayer Player { get; }
        public long PositionBytes => ((IWavePosition)Player).GetPosition();
        public TaskCompletionSource<bool> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Started { get; set; }
        public Exception? Error { get; private set; }
        public OutputSession(IWaveProvider source, Action wake, Func<IWavePlayer> createPlayer)
        {
            Player = createPlayer();
            Player.PlaybackStopped += (_, e) => { Error = e.Exception; Stopped.TrySetResult(true); wake(); };
            try { Player.Init(source); }
            catch { Player.Dispose(); throw; }
        }
        public void Dispose() => Player.Dispose();
    }

    private readonly object stateLock = new();
    private readonly Channel<Command> commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private readonly Func<IWavePlayer> createPlayer;
    private readonly float outputGain;
    internal HitsoundPlayer? Hitsounds { get; set; }
    private readonly TimeSpan stopTimeout;
    private AudioState state = new(null, 0, 0, false, false, false, null);
    private long loadVersion, seekVersion, appliedSeekVersion, loadedVersion;
    private long intentVersion, appliedIntentVersion;
    private double requestedPosition, basePosition, duration;
    private bool playIntent;
    private bool requestedPlaying;
    private WaveStream? reader;
    private OutputSession? output;
    private string? loadedPath;
    private bool recoveryAttempted;
    private int disposed;

    public AudioTransport() : this(1) { }
    internal AudioTransport(float outputGain, Func<IWavePlayer>? createPlayer = null, TimeSpan? stopTimeout = null)
    {
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
            commands.Writer.TryWrite(new(CommandKind.Load, version, path, Completion: completion));
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
            commands.Writer.TryWrite(new(CommandKind.Play, loadVersion, IntentVersion: ++intentVersion));
        }
    }

    public void Pause()
    {
        lock (stateLock)
        {
            if (disposed != 0) return;
            requestedPlaying = false;
            Volatile.Write(ref state, state with { IsPlaying = false });
            commands.Writer.TryWrite(new(CommandKind.Pause, loadVersion, IntentVersion: ++intentVersion));
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
            commands.Writer.TryWrite(new(CommandKind.Seek, loadVersion, Position: requestedPosition, SeekVersion: version));
        }
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
                    if (command.LoadVersion != Interlocked.Read(ref loadVersion)) { command.Completion?.TrySetResult(false); continue; }
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
                                // Rebuild from the consumed position, not the decoder's read-ahead position.
                                if (output is { Started: true } && reader is not null)
                                    await ResetOutputAsync(output.Stopped.Task.IsCompleted ? duration : DevicePosition());
                                Interlocked.Exchange(ref appliedIntentVersion, command.IntentVersion);
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
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException(L.Get("audio.noDuration"));
        if (reader.WaveFormat.Channels is < 1 or > 2) throw new NotSupportedException(L.Get("audio.channels"));
        basePosition = 0;
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
        if (Hitsounds is not null) samples = Hitsounds.MixWithMusic(samples, basePosition);
        var pcm = new SampleToWaveProvider16(samples) { Volume = outputGain };
        long version = loadedVersion;
        return new(pcm, () => commands.Writer.TryWrite(new(CommandKind.Refresh, version)), createPlayer);
    }

    private async Task ResetOutputAsync(double position, long requestedSeek = 0)
    {
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
    }

    private void StartOutput()
    {
        if (output is null || output.Started) return;
        if (basePosition >= duration - 0.5) { playIntent = false; return; }
        output.Player.Play();
        output.Started = true;
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
    }

    private double DevicePosition() => Math.Clamp(basePosition
        + output!.PositionBytes * 1000.0 / output.Player.OutputWaveFormat.AverageBytesPerSecond, 0, duration);

    private void Publish(double position, bool playing)
    {
        lock (stateLock)
        {
            if (loadedVersion != loadVersion || disposed != 0) return;
            // A queued seek owns the displayed location until its decoder and device reset have finished.
            if (appliedSeekVersion != seekVersion) position = requestedPosition;
            if (appliedIntentVersion != intentVersion) playing = requestedPlaying;
            Volatile.Write(ref state, state with { PositionMs = position, DurationMs = duration, IsPlaying = playing,
                CanPlay = reader is not null && output is not null, IsLoading = false, Error = null });
        }
    }

    private void PublishError(long version, Exception exception)
    {
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
        try
        {
            current.Player.Stop();
            if (current.Started) await current.Stopped.Task.WaitAsync(stopTimeout);
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
        finally { cancellation.Dispose(); }
    }
}
