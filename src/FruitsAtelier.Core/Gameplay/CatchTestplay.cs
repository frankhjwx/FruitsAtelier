// Movement and catch rules based on ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f,
// osu.Game.Rulesets.Catch/UI/Catcher.cs and CatcherArea.cs.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public sealed class CatchTestplay
{
    private readonly List<CatchTrail> trails = [];
    private double lastTrail = double.NegativeInfinity;
    public IReadOnlyList<CatchTrail> Trails => trails;
    private readonly IReadOnlyList<ConvertedCatchObject> objects;
    private readonly HyperDashState[] hyper;
    private readonly double halfWidth;
    private readonly double circleSize;
    private CatchAutoPreview? automatic;
    private bool automaticMovement;
    private int next;
    private double time, hyperSpeed = 1, hyperTarget;
    public const double MissLifetimeMs = 250;
    private readonly Queue<ConvertedCatchObject> missed = new();
    public IEnumerable<ConvertedCatchObject> MissedObjects => missed;
    public bool HasMissAnimations => missed.Count > 0;
    public IEnumerable<ConvertedCatchObject> PendingUntil(double end)
    {
        for (int i = next; i < objects.Count && objects[i].TimeMs <= end; i++) yield return objects[i];
    }
    public double X { get; private set; } = 256;
    public bool FacingLeft { get; private set; }
    public int Combo { get; private set; }
    public bool Finished => next >= objects.Count;
    public int JudgedCount => next;
    public bool HyperDashing => automaticMovement ? automatic!.HyperDashingAt(time) : hyperSpeed > 1;
    public Action<ConvertedCatchObject>? Caught { get; set; }
    public Action<int, ConvertedCatchObject>? ComboChanged { get; set; }
    public Action<ConvertedCatchObject, double, bool>? Judged { get; set; }

    public CatchTestplay(IReadOnlyList<ConvertedCatchObject> objects, double circleSize, double start)
    {
        this.objects = objects;
        this.circleSize = circleSize;
        hyper = HyperDashCalculator.Calculate(objects, circleSize);
        halfWidth = CatchSize.CatchWidth(circleSize) / 2;
        time = start;
        while (next < objects.Count && objects[next].TimeMs < start) next++;
    }

    public void Advance(double now, bool left, bool right, bool dash, bool autoplay = false)
    {
        if (!double.IsFinite(now) || now < time) return;
        if (autoplay) automatic ??= new(objects, circleSize);
        automaticMovement = autoplay;
        int direction = (right ? 1 : 0) - (left ? 1 : 0);
        while (next < objects.Count && objects[next].TimeMs <= now)
        {
            var item = objects[next];
            Move(item.TimeMs);
            bool caught = autoplay || item.X >= X - halfWidth && item.X <= X + halfWidth;
            Judged?.Invoke(item, X, caught);
            if (caught) Caught?.Invoke(item);
            else missed.Enqueue(item);
            if (item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet)
            {
                int previousCombo = Combo;
                Combo = caught ? Combo + 1 : 0;
                if (Combo != previousCombo) ComboChanged?.Invoke(Combo, item);
                bool wasHyper = HyperDashing;
                hyperSpeed = 1;
                if (caught && hyper[next].TargetIndex is int target)
                {
                    hyperTarget = objects[target].X;
                    hyperSpeed = Math.Max(1, Math.Abs(hyperTarget - X) / Math.Max(1, objects[target].TimeMs - item.TimeMs - 1000d / 60));
                }
                if (!wasHyper && HyperDashing) trails.Add(new(item.TimeMs, X, FacingLeft, true, true));
            }
            next++;
        }
        Move(now);
        while (missed.TryPeek(out var item) && now - item.TimeMs >= MissLifetimeMs) missed.Dequeue();
        trails.RemoveAll(t => now - t.TimeMs >= (t.AfterImage ? 1200 : 800));

        void Move(double until)
        {
            // Capture the path at osu's 16 ms trail interval, including input changes between frames.
            if (autoplay || dash || HyperDashing)
            {
                double sample = Math.Max(time, lastTrail + 16);
                for (; sample <= until; sample += 16)
                {
                    Step(sample);
                    if ((autoplay ? automatic!.At(sample).Dashing : dash) || HyperDashing)
                    {
                        trails.Add(new(sample, X, FacingLeft, HyperDashing, false));
                        lastTrail = sample;
                    }
                }
            }
            Step(until);
        }

        void Step(double until)
        {
            if (autoplay)
            {
                bool wasHyper = HyperDashing;
                X = automatic!.At(until).X;
                FacingLeft = automatic.FacingLeftAt(until);
                time = until;
                if (!wasHyper && HyperDashing) trails.Add(new(time, X, FacingLeft, true, true));
                return;
            }
            double previous = X;
            double distance = (until - time) * (dash ? 1 : .5) * direction;
            double position = X + distance * hyperSpeed;
            if (hyperSpeed > 1 && direction == Math.Sign(hyperTarget - X) && Math.Abs(position - X) >= Math.Abs(hyperTarget - X))
            {
                // Consume the boosted part only up to the target; remaining time uses normal speed.
                double used = Math.Abs(hyperTarget - X) / hyperSpeed;
                position = hyperTarget + direction * (Math.Abs(distance) - used);
                hyperSpeed = 1;
            }
            X = Math.Clamp(position, 0, 512);
            if (X != previous) FacingLeft = X < previous;
            time = until;
        }
    }
}
