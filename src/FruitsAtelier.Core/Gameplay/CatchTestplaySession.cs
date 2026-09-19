namespace FruitsAtelier.Core;

public readonly record struct CatchComboChange(int Combo, ConvertedCatchObject Object);

public sealed record CatchTestplayFrame(double TimeMs, double X, int Combo, bool FacingLeft, bool HyperDashing,
    bool Ended, Exception? Error, int JudgedCount, ConvertedCatchObject[] MissedObjects, CatchTrail[] Trails,
    CatchComboChange[] ComboChanges, CatchPlateSprite[] Plate, bool Autoplay = false);

/// <summary>Serialises gameplay mutations; drawing consumes a detached snapshot without holding the lock.</summary>
public sealed class CatchTestplaySession
{
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
    public bool Autoplay { get { lock (gate) return autoplay; } }
    private Exception? error;
    private double time;
    public bool WithAudio { get; }
    public bool Ended { get { lock (gate) return ended; } }
    public double X { get { lock (gate) return game.X; } }
    public int Combo { get { lock (gate) return game.Combo; } }
    public bool UsesKey(int key) => key == left || key == right || key == dash;
    private double Realtime => timeProvider.GetTimestamp() * 1000d / timeProvider.TimestampFrequency;

    public CatchTestplaySession(CatchTestplay game, CatchTestplayClock clock, double start, bool withAudio,
        bool audioPlaying, int left, int right, int dash, TimeProvider timeProvider, double circleSize,
        HashSet<(Guid SourceId, int EventIndex)> comboEnds, Action<ConvertedCatchObject>? caught = null)
    {
        this.game = game; this.clock = clock; time = start; WithAudio = withAudio;
        audioStarted = audioPlaying; this.left = left; this.right = right; this.dash = dash;
        this.timeProvider = timeProvider; this.comboEnds = comboEnds;
        plate = new(circleSize);
        game.Caught = caught;
        game.ComboChanged = (value, item) => comboChanges.Add(new(value, item));
        game.Judged = (item, x, hit) => plate.Judge(item, x, hit, this.comboEnds.Contains((item.SourceId, item.EventIndex)));
    }

    public void UpdateAudio(double position, double sampledAt, double duration, bool ready, bool playing, bool loading, bool failed)
    {
        lock (gate)
        {
            if (ended || !WithAudio) return;
            audioStarted |= playing;
            if (!ready || failed || audioStarted && !playing && !loading || duration > 0 && position >= duration)
            { ended = true; keys.Clear(); return; }
            if (playing) clock.Synchronize(position, sampledAt, Realtime);
            Advance();
        }
    }

    public void Tick() { lock (gate) Advance(); }
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
            if (ended) return;
            Advance();
            if (down) keys.Add(key); else keys.Remove(key);
        }
    }
    public void Cancel(Exception? failure = null)
    {
        lock (gate) { ended = true; keys.Clear(); error = failure; }
    }
    private void Advance()
    {
        if (ended || WithAudio && !audioStarted || !clock.IsRunning) return;
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
                plate.At(time, game.X).ToArray(), autoplay);
            comboChanges.Clear();
            return frame;
        }
    }
}
