namespace FruitsAtelier.Core;

public readonly record struct CatchComboChange(int Combo, ConvertedCatchObject Object);

public sealed record CatchTestplayFrame(double TimeMs, double X, int Combo, bool FacingLeft, bool HyperDashing,
    bool Ended, Exception? Error, int JudgedCount, ConvertedCatchObject[] MissedObjects, CatchTrail[] Trails,
    CatchComboChange[] ComboChanges, CatchPlateSprite[] Plate, bool Autoplay = false, bool Dashing = false);

/// <summary>Serialises gameplay mutations; drawing consumes a detached snapshot without holding the lock.</summary>
public sealed class CatchTestplaySession
{
    // Allow for a stale transport poll and the next output callback after the measured PCM lead.
    public static double LiveHitsoundLead(double bufferedAheadMs, double rate) => bufferedAheadMs > 0
        ? Math.Clamp(bufferedAheadMs, 0, 100 * rate) + 25 * rate : 0;
    private readonly object gate = new();
    private readonly CatchTestplay game;
    private readonly CatchTestplayClock clock;
    private readonly TimeProvider timeProvider;
    private readonly HashSet<int> keys = [];
    private readonly List<CatchComboChange> comboChanges = [];
    private readonly int left, right, dash;
    private readonly CatchPlate plate;
    private readonly HashSet<(Guid SourceId, int EventIndex)> comboEnds;
    private bool audioStarted, ended;
    private bool autoplay;
    private bool paused, awaitingResume;
    private double resumeRequestedAt;
    private readonly double startPosition;
    private bool waitingForDeviceProgress;
    private double resumePosition;
    private double outputLead;
    public bool Paused { get { lock (gate) return paused; } }
    public bool Autoplay { get { lock (gate) return autoplay; } }
    private Exception? error;
    private double time;
    public bool WithAudio { get; }
    public bool Ended { get { lock (gate) return ended; } }
    public double X { get { lock (gate) return game.X; } }
    public int Combo { get { lock (gate) return game.Combo; } }
    public double TransportPosition { get { lock (gate) return time - outputLead; } }
    public bool UsesKey(int key) => key is not (114 or 123) && (key == left || key == right || key == dash);
    private double Realtime => timeProvider.GetTimestamp() * 1000d / timeProvider.TimestampFrequency;

    public CatchTestplaySession(CatchTestplay game, CatchTestplayClock clock, double start, bool withAudio,
        bool audioPlaying, int left, int right, int dash, TimeProvider timeProvider, double circleSize,
        HashSet<(Guid SourceId, int EventIndex)> comboEnds, Action<ConvertedCatchObject>? caught = null)
    {
        this.game = game; this.clock = clock; time = start; startPosition = start; WithAudio = withAudio;
        audioStarted = audioPlaying; this.left = left; this.right = right; this.dash = dash;
        waitingForDeviceProgress = withAudio && !audioPlaying;
        this.timeProvider = timeProvider; this.comboEnds = comboEnds;
        plate = new(circleSize);
        game.Caught = caught;
        game.ComboChanged = (value, item) => comboChanges.Add(new(value, item));
        game.Judged = (item, x, hit) => plate.Judge(item, x, hit, this.comboEnds.Contains((item.SourceId, item.EventIndex)));
    }

    public void UpdateAudio(double position, double sampledAt, double duration, bool ready, bool playing,
        bool loading, bool failed, double outputBufferAheadMs = 0)
    {
        lock (gate)
        {
            if (ended || !WithAudio) return;
            if (paused) return;
            if (awaitingResume)
            {
                if (failed || !ready && !loading) { ended = true; keys.Clear(); return; }
                if (!playing || sampledAt < resumeRequestedAt) return;
                if (position <= resumePosition) return;
                awaitingResume = false;
            }
            audioStarted |= playing;
            if (!ready || failed || audioStarted && !playing && !loading || duration > 0 && position >= duration)
            { ended = true; keys.Clear(); return; }
            if (waitingForDeviceProgress)
            {
                if (!playing || position <= startPosition) return;
                waitingForDeviceProgress = false;
            }
            if (playing)
            {
                outputLead = LiveHitsoundLead(outputBufferAheadMs, clock.Rate);
                clock.Synchronize(position + outputLead, sampledAt, Realtime);
            }
            Advance();
        }
    }

    public void Tick() { lock (gate) Advance(); }
    public bool SkipIntro(double target)
    {
        lock (gate)
        {
            Advance();
            if (ended || paused || !double.IsFinite(target) || target <= time ||
                game.JudgedCount != 0 || game.PendingUntil(target).Any()) return false;
            keys.Clear();
            time = target;
            outputLead = 0;
            game.Advance(target, false, false, false, autoplay);
            waitingForDeviceProgress = false;
            awaitingResume = WithAudio;
            resumePosition = target;
            resumeRequestedAt = Realtime;
            clock.Restart(target, resumeRequestedAt, awaitingResume);
            return true;
        }
    }
    public void SetPlaybackSpeed(double speed)
    {
        lock (gate)
        {
            Advance();
            clock.SetRate(speed, time, Realtime);
        }
    }
    public double TogglePause()
    {
        lock (gate)
        {
            Advance();
            if (ended) return time;
            paused = !paused; keys.Clear();
            if (!paused)
            {
                awaitingResume = WithAudio;
                resumeRequestedAt = Realtime;
                resumePosition = time - outputLead;
                clock.Restart(time, resumeRequestedAt, WithAudio);
            }
            return time - outputLead;
        }
    }
    public void ToggleAutoplay()
    {
        lock (gate)
        {
            Advance();
            if (!ended) autoplay = !autoplay;
        }
    }
    public void SetKey(int key, bool down)
    {
        if (!UsesKey(key)) return;
        lock (gate)
        {
            if (ended || paused || awaitingResume) return;
            Advance();
            if (down)
            {
                autoplay = false;
                keys.Add(key);
            }
            else keys.Remove(key);
        }
    }
    public void ReleaseKeys() { lock (gate) keys.Clear(); }
    public void Cancel(Exception? failure = null)
    {
        lock (gate) { ended = true; keys.Clear(); error = failure; }
    }
    private void Advance()
    {
        if (ended || paused || awaitingResume || WithAudio && !audioStarted || !clock.IsRunning) return;
        time = clock.At(Realtime);
        game.Advance(time, keys.Contains(left), keys.Contains(right), keys.Contains(dash), autoplay);
        plate.Prune(time);
        if (game.Finished && !game.HasMissAnimations && !plate.HasTransientAt(time)) ended = true;
    }
    public CatchTestplayFrame Capture()
    {
        lock (gate)
        {
            var frame = new CatchTestplayFrame(time, game.X, game.Combo, game.FacingLeft, game.HyperDashing,
                ended, error, game.JudgedCount, game.MissedObjects.ToArray(), game.Trails.ToArray(), comboChanges.ToArray(),
                plate.At(time, game.X).ToArray(), autoplay, keys.Contains(dash));
            comboChanges.Clear();
            return frame;
        }
    }
}
