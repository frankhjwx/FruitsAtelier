using System.Globalization;
using System.Numerics;
using System.Text;

namespace FruitsAtelier.Core;

public enum StoryboardLayer { Background, Fail, Pass, Foreground, Overlay }
public readonly record struct StoryboardVideoInfo(string Path, double OffsetMs);
public readonly record struct StoryboardSample(string Path, double TimeMs, int Volume, StoryboardLayer Layer);
public readonly record struct StoryboardTrigger(string Name, double TimeMs, int Group = 0);
public readonly record struct StoryboardSpriteState(string Path, double X, double Y, double ScaleX, double ScaleY,
    double Rotation, uint Colour, double Opacity, double OriginX, double OriginY, bool FlipX, bool FlipY,
    bool Additive, StoryboardLayer Layer, int Order);

/// <summary>Immutable source-derived scene; queries accept unrelated timestamps and retain no playback cursor.</summary>
public sealed class StoryboardScene
{
    private readonly StoryboardSprite[] sprites;
    private readonly double[] subtreeEnds;
    public IReadOnlyList<StoryboardVideoInfo> Videos { get; }
    public IReadOnlyList<StoryboardSample> Samples { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public bool Widescreen { get; }
    public bool ReplacesBackground { get; }
    public string? BackgroundPath { get; }
    public IReadOnlyList<string> ImagePaths { get; }
    public IReadOnlyList<string> TriggerNames { get; }
    public int SpriteCount => sprites.Length;
    public bool RequiresGameplay { get; }
    public double EndTimeMs { get; }

    internal StoryboardScene(List<StoryboardSprite> source, List<StoryboardVideoInfo> videos,
        List<StoryboardSample> samples, List<string> diagnostics, bool widescreen, string? background)
    {
        sprites = source.Where(s => s.HasCommands).OrderBy(s => s.Start).ToArray();
        subtreeEnds = new double[sprites.Length];
        BuildIntervals(0, sprites.Length);
        Videos = videos.ToArray(); Samples = samples.OrderBy(s => s.TimeMs).ToArray(); Diagnostics = diagnostics.ToArray();
        Widescreen = widescreen;
        BackgroundPath = background;
        ReplacesBackground = source.Any(s => s.Layer == StoryboardLayer.Background && string.Equals(s.Path, background, StringComparison.OrdinalIgnoreCase));
        ImagePaths = source.SelectMany(s => s.Frames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        TriggerNames = source.SelectMany(s => s.Groups).Select(g => g.Trigger).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        RequiresGameplay = TriggerNames.Count > 0 || source.Any(s => s.Layer is StoryboardLayer.Fail or StoryboardLayer.Pass)
            || samples.Any(s => s.Layer is StoryboardLayer.Fail or StoryboardLayer.Pass);
        EndTimeMs = Math.Max(sprites.Where(s => double.IsFinite(s.End) && Math.Abs(s.End) < double.MaxValue / 2).Select(s => s.End).DefaultIfEmpty(0).Max(), samples.Select(s => s.TimeMs).DefaultIfEmpty(0).Max());
    }

    public StoryboardScene WithoutVisuals() => new([], [], Samples.ToList(), [], false, BackgroundPath);

    private double BuildIntervals(int low, int high)
    {
        if (low == high) return double.NegativeInfinity;
        int middle = low + (high - low) / 2;
        // A long-lived background must not keep every expired sprite in the query range.
        return subtreeEnds[middle] = Math.Max(sprites[middle].End,
            Math.Max(BuildIntervals(low, middle), BuildIntervals(middle + 1, high)));
    }

    public void Evaluate(double timeMs, List<StoryboardSpriteState> destination, bool passing = true,
        IReadOnlyList<StoryboardTrigger>? triggers = null)
    {
        destination.Clear();
        Visit(0, sprites.Length);
        destination.Sort(static (a, b) => { int layer = ((int)a.Layer).CompareTo((int)b.Layer); return layer != 0 ? layer : a.Order.CompareTo(b.Order); });

        void Visit(int low, int high)
        {
            if (low == high || !(sprites[low].Start <= timeMs)) return;
            int middle = low + (high - low) / 2;
            if (subtreeEnds[middle] < timeMs) return;
            Visit(low, middle);
            var sprite = sprites[middle];
            if (!(sprite.Start <= timeMs)) return;
            if (!(sprite.End < timeMs || sprite.Layer == StoryboardLayer.Fail && passing || sprite.Layer == StoryboardLayer.Pass && !passing))
            {
                var state = sprite.Evaluate(timeMs, triggers);
                if (state.Opacity > 0 && state.ScaleX != 0 && state.ScaleY != 0) destination.Add(state);
            }
            Visit(middle + 1, high);
        }
    }

    public void Preload(double timeMs, double lookAheadMs, List<string> destination, HashSet<string>? seen = null)
    {
        destination.Clear();
        seen ??= new(StringComparer.OrdinalIgnoreCase); seen.Clear();
        Visit(0, sprites.Length);

        void Visit(int low, int high)
        {
            if (low == high || !(sprites[low].Start <= timeMs + lookAheadMs)) return;
            int middle = low + (high - low) / 2;
            if (subtreeEnds[middle] < timeMs) return;
            Visit(low, middle);
            var sprite = sprites[middle];
            if (!(sprite.Start <= timeMs + lookAheadMs)) return;
            if (!(sprite.End < timeMs))
                foreach (string path in sprite.Frames) if (seen.Add(path)) destination.Add(path);
            Visit(middle + 1, high);
        }
    }
}

internal readonly record struct StoryboardCommand(int Property, int Easing, double Start, double End,
    double From, double To, int Order);

internal sealed class StoryboardCommandGroup
{
    public double Offset;
    public int Count = 1;
    public string? Trigger;
    public double TriggerStart = double.MinValue;
    public double TriggerEnd = double.MaxValue;
    public int TriggerGroup;
    public StoryboardCommandGroup[] CancellationPeers = [];
    public double CancellationWindowEnd;
    public readonly List<StoryboardCommand> Commands = [];
    public StoryboardCommand[][] Tracks = [];
    public double First, Last, Period;
    public void Compile()
    {
        Span<int> counts = stackalloc int[14]; counts.Clear();
        First = Last = 0;
        bool sorted = true;
        for (int i = 0; i < Commands.Count; i++)
        {
            var command = Commands[i];
            if (i == 0) { First = command.Start; Last = command.End; }
            else
            {
                First = Math.Min(First, command.Start);
                if (command.End > Last || double.IsNaN(Last)) Last = command.End;
                var previous = Commands[i - 1];
                int comparison = previous.Start.CompareTo(command.Start);
                if (comparison > 0 || comparison == 0 && previous.Order > command.Order) sorted = false;
            }
            if ((uint)command.Property < 14) counts[command.Property]++;
        }
        Period = Math.Max(0, Last - First);
        Tracks = new StoryboardCommand[14][];
        for (int p = 0; p < Tracks.Length; p++) Tracks[p] = counts[p] == 0 ? [] : new StoryboardCommand[counts[p]];
        IReadOnlyList<StoryboardCommand> ordered = sorted ? Commands : Commands.OrderBy(c => c.Start).ThenBy(c => c.Order).ToArray();
        counts.Clear();
        for (int i = 0; i < ordered.Count; i++)
        {
            var command = ordered[i];
            if ((uint)command.Property < 14) Tracks[command.Property][counts[command.Property]++] = command;
        }
    }

    public bool TryValue(int property, double time, out double value, out double effectiveStart, out int order)
    {
        value = 0; effectiveStart = double.MinValue; order = -1;
        var track = Tracks[property];
        if (track.Length == 0) return false;
        double relative = time - Offset;
        if (relative < track[0].Start) return false;
        int iteration = Period > 0 && relative >= First ? (int)Math.Min(Count - 1d, Math.Floor((relative - First) / Period)) : 0;
        double local = relative - iteration * Period;
        int low = 0, high = track.Length;
        while (low < high) { int mid = (low + high) / 2; if (track[mid].Start <= local) low = mid + 1; else high = mid; }
        // The first transform's initial value applies before it starts, matching osu! drawable setup.
        int index = Math.Max(0, low - 1);
        var command = track[index];
        if (low == 0 && iteration > 0)
        {
            command = track[^1]; iteration--; local += Period;
        }
        effectiveStart = Offset + iteration * Period + command.Start;
        order = command.Order;
        double t = command.End <= command.Start ? (local >= command.Start ? 1 : 0) : Math.Clamp((local - command.Start) / (command.End - command.Start), 0, 1);
        value = property is >= 10 and <= 12 ? (local < command.End ? command.From : command.To)
            : command.From + (command.To - command.From) * new StoryboardEasing((StoryboardEasingKind)command.Easing).ApplyEasing(t);
        return true;
    }
}

internal sealed class StoryboardSprite
{
    public string Path = "";
    public string[] Frames = [];
    public double FrameDelay;
    public bool LoopAnimation = true;
    public StoryboardLayer Layer;
    public int Order;
    public double X, Y, OriginX, OriginY;
    public readonly List<StoryboardCommandGroup> Groups = [];
    public double Start, End;
    private readonly double[] initial = new double[14];
    private bool ordinaryCommands;
    public bool HasCommands => Groups.Any(g => g.Commands.Count != 0);
    public void Compile()
    {
        foreach (var group in Groups) group.Compile();
        foreach (var group in Groups)
        {
            if (group.Trigger is null) continue;
            int count = 0;
            foreach (var candidate in Groups)
                if (candidate.Trigger is not null && (ReferenceEquals(candidate, group) || group.TriggerGroup != 0 && candidate.TriggerGroup == group.TriggerGroup)) count++;
            group.CancellationPeers = new StoryboardCommandGroup[count];
            count = 0; group.CancellationWindowEnd = double.MinValue;
            foreach (var candidate in Groups)
                if (candidate.Trigger is not null && (ReferenceEquals(candidate, group) || group.TriggerGroup != 0 && candidate.TriggerGroup == group.TriggerGroup))
                {
                    group.CancellationPeers[count++] = candidate;
                    group.CancellationWindowEnd = Math.Max(group.CancellationWindowEnd, candidate.TriggerEnd);
                }
        }
        initial[0] = X; initial[1] = Y; initial[2] = initial[3] = initial[4] = initial[9] = 1;
        initial[6] = initial[7] = initial[8] = 255;
        for (int p = 0; p < 13; p++)
        {
            StoryboardCommand first = default;
            double firstStart = 0; bool found = false;
            foreach (var group in Groups)
            {
                if (group.Trigger is not null) continue;
                foreach (var command in group.Tracks[p])
                {
                    double start = command.Start + group.Offset;
                    int comparison = start.CompareTo(firstStart);
                    if (!found || comparison < 0 || comparison == 0 && command.Order < first.Order)
                    { first = command; firstStart = start; found = true; }
                }
            }
            if (first != default && (p < 10 || first.Start == first.End)) initial[p] = first.From;
        }
        ordinaryCommands = false; Start = double.MaxValue; End = double.MinValue;
        bool alphaFound = false, visibleAlphaFound = false;
        double firstAlphaStart = 0, firstAlphaFrom = 0, firstVisibleAlphaStart = 0;
        foreach (var group in Groups)
        {
            if (group.Trigger is not null || group.Commands.Count == 0) continue;
            double end = group.Offset + group.Last + group.Period * (group.Count - 1);
            if (!ordinaryCommands) { Start = group.Offset + group.First; End = end; }
            else
            {
                Start = Math.Min(Start, group.Offset + group.First);
                if (end > End || double.IsNaN(End)) End = end;
            }
            ordinaryCommands = true;
            foreach (var command in group.Tracks[9])
            {
                double start = command.Start + group.Offset;
                if (!alphaFound || start.CompareTo(firstAlphaStart) < 0)
                { firstAlphaStart = start; firstAlphaFrom = command.From; alphaFound = true; }
                if ((command.From > 0 || command.To > 0) && (!visibleAlphaFound || start.CompareTo(firstVisibleAlphaStart) < 0))
                { firstVisibleAlphaStart = start; visibleAlphaFound = true; }
            }
        }
        if (alphaFound && firstAlphaFrom == 0 && visibleAlphaFound) Start = firstVisibleAlphaStart;
        foreach (var trigger in Groups)
        {
            if (trigger.Trigger is null) continue;
            Start = Math.Min(Start, trigger.TriggerStart + trigger.First);
            End = Math.Max(End, trigger.TriggerEnd + trigger.Last);
        }
    }

    public StoryboardSpriteState Evaluate(double time, IReadOnlyList<StoryboardTrigger>? triggers)
    {
        Span<double> values = stackalloc double[14]; initial.CopyTo(values);
        Span<double> starts = stackalloc double[14]; starts.Fill(double.MinValue);
        Span<int> orders = stackalloc int[14]; orders.Fill(-1);
        double animationStart = ordinaryCommands ? Start : double.MinValue;
        bool triggerAlive = false;
        foreach (var group in Groups)
        {
            if (group.Trigger is null) Apply(group, time, 0, values, starts, orders);
            else if (triggers is not null)
            {
                int low = 0, high = triggers.Count;
                double earliest = group.TriggerStart;
                while (low < high) { int mid = (low + high) / 2; if (triggers[mid].TimeMs < earliest) low = mid + 1; else high = mid; }
                for (int i = low; i < triggers.Count && triggers[i].TimeMs <= Math.Min(time, group.TriggerEnd); i++)
                {
                    var trigger = triggers[i];
                    if (MatchesTrigger(group, trigger))
                    {
                        double cancelAt = time;
                        // A retrigger cancels future commands of its previous instance; explicit groups
                        // additionally cancel peer triggers on this sprite (osu! compound command contract).
                        for (int j = i + 1; j < triggers.Count && triggers[j].TimeMs <= Math.Min(time, group.CancellationWindowEnd); j++)
                        {
                            bool cancel = false;
                            foreach (var peer in group.CancellationPeers)
                                if (MatchesTrigger(peer, triggers[j])) { cancel = true; break; }
                            if (cancel) { cancelAt = triggers[j].TimeMs; break; }
                        }
                        Apply(group, cancelAt - trigger.TimeMs, trigger.TimeMs, values, starts, orders);
                        bool active = time <= trigger.TimeMs + group.Last && cancelAt >= time;
                        triggerAlive |= active;
                        if (!ordinaryCommands && active) animationStart = Math.Max(animationStart, trigger.TimeMs + group.First);
                    }
                }
            }
        }
        if (!ordinaryCommands && !triggerAlive) values[9] = 0;
        double alpha = values[9]; if (alpha > 1) alpha %= 1;
        string path = Path;
        if (Frames.Length > 1 && FrameDelay > 0)
        {
            double frame = Math.Max(0, Math.Floor((time - animationStart) / FrameDelay));
            int index = LoopAnimation ? (int)(frame % Frames.Length) : (int)Math.Min(Frames.Length - 1, frame);
            path = Frames[index];
        }
        uint colour = (uint)Math.Clamp(Math.Round(values[6]), 0, 255) << 16 | (uint)Math.Clamp(Math.Round(values[7]), 0, 255) << 8 | (uint)Math.Clamp(Math.Round(values[8]), 0, 255);
        bool flipX = values[11] >= .5, flipY = values[12] >= .5;
        double originX = flipX ^ (values[3] < 0) ? 1 - OriginX : OriginX;
        double originY = flipY ^ (values[4] < 0) ? 1 - OriginY : OriginY;
        // osu! keeps S on Drawable.Scale and V on VectorScale; both participate in DrawScale.
        return new(path, values[0], values[1], values[2] * values[3], values[2] * values[4], values[5], colour,
            Math.Max(0, alpha), originX, originY, flipX, flipY, values[10] >= .5, Layer, Order);
    }

    private static void Apply(StoryboardCommandGroup group, double time, double offset, Span<double> values, Span<double> starts, Span<int> orders)
    {
        for (int p = 0; p < 14; p++)
            if (group.TryValue(p, time, out double value, out double start, out int order) && (start + offset > starts[p] || start + offset == starts[p] && order > orders[p]))
            { values[p] = value; starts[p] = start + offset; orders[p] = order; }
    }

    private static bool MatchesTrigger(StoryboardCommandGroup group, StoryboardTrigger trigger)
        => trigger.TimeMs >= group.TriggerStart && trigger.TimeMs <= group.TriggerEnd
            && string.Equals(trigger.Name, group.Trigger, StringComparison.OrdinalIgnoreCase)
            && (trigger.Group == 0 || group.TriggerGroup == 0 || Math.Abs((long)trigger.Group) == Math.Abs((long)group.TriggerGroup));
}

/// <summary>Legacy osu! storyboard grammar. Source: ppy/osu 48c4800e (MIT; THIRD_PARTY_NOTICES.md).</summary>
public static class StoryboardReader
{
    public static StoryboardScene Read(string beatmapText, string? sharedText, string directory, CancellationToken cancellationToken = default, bool samplesOnly = false)
    {
        List<StoryboardSprite> sprites = []; List<StoryboardVideoInfo> videos = []; List<StoryboardSample> samples = []; List<string> diagnostics = [];
        bool wide = false; string? background = null; int order = 0;
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var variables = new StoryboardVariables();
        Parse(beatmapText); if (sharedText is not null) Parse(sharedText);
        foreach (var sprite in sprites) { cancellationToken.ThrowIfCancellationRequested(); sprite.Compile(); }
        return new(sprites, videos, samples, diagnostics, wide, background);

        void Parse(string text)
        {
            string section = "Events"; StoryboardSprite? sprite = null;
            StoryboardCommandGroup? group = null; int lineNumber = 0, offset = 0;
            while (offset <= text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested(); lineNumber++;
                int lineEnd = text.IndexOf('\n', offset);
                if (lineEnd < 0) lineEnd = text.Length;
                string line = text.AsSpan(offset, lineEnd - offset).TrimEnd('\r').ToString(); string trimmed = line.Trim();
                offset = lineEnd + 1;
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                if (trimmed[0] == '[') { section = trimmed.Trim('[', ']'); continue; }
                if (section == "General") { if (trimmed.StartsWith("WidescreenStoryboard:", StringComparison.Ordinal)) wide = trimmed.Split(':')[1].Trim() == "1"; continue; }
                if (section == "Variables") { int eq = line.IndexOf('='); if (eq > 0) variables.Set(line[..eq], line[(eq + 1)..]); continue; }
                if (section != "Events") continue;
                if (samplesOnly)
                {
                    // Sample declarations are top-level; skip visual commands before allocating fields or resolving images.
                    if (line.Length == 0 || line[0] is ' ' or '_' or '\t') continue;
                    int comma = line.IndexOf(',');
                    string type = (comma < 0 ? line : line[..comma]).Trim();
                    if (type is not ("5" or "Sample") && !type.Contains('$')) continue;
                }
                line = variables.Expand(line, cancellationToken);
                if (samplesOnly)
                {
                    int comma = line.IndexOf(',');
                    if (comma < 0 || line[..comma].Trim() is not ("5" or "Sample")) continue;
                }
                int depth = 0; while (depth < line.Length && (line[depth] == ' ' || line[depth] == '_')) depth++;
                var f = Fields(line[depth..]);
                try
                {
                    if (depth == 0)
                    {
                        sprite = null; group = null;
                        string type = f[0];
                        if (type is "0" or "Background") { background ??= Resolve(f[2]); continue; }
                        if (type is "1" or "Video")
                        {
                            string path = Resolve(f[2]);
                            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                            if (extension is ".mp4" or ".mov" or ".avi" or ".flv" or ".mpg" or ".wmv" or ".m4v") videos.Add(new(path, Number(f[1])));
                            else if (extension is ".png" or ".jpg" or ".jpeg") background ??= path;
                            continue;
                        }
                        if (type is "5" or "Sample") { samples.Add(new(Resolve(f[3]), Number(f[1]), f.Count > 4 ? (int)Number(f[4]) : 100, Layer(f[2]))); continue; }
                        if (type is not ("4" or "Sprite" or "6" or "Animation")) continue;
                        var origin = Origin(f[2]);
                        sprite = new() { Path = Resolve(f[3]), Layer = Layer(f[1]), OriginX = origin.X, OriginY = origin.Y, X = Number(f[4]), Y = Number(f[5]), Order = order++ };
                        sprite.Frames = [sprite.Path];
                        if (type is "6" or "Animation")
                        {
                            int count = Math.Clamp((int)Number(f[6]), 1, 100000); sprite.FrameDelay = Number(f[7]);
                            sprite.LoopAnimation = f.Count < 9 || f[8] is not ("LoopOnce" or "1");
                            sprite.Frames = Enumerable.Range(0, count).Select(i => Resolve(System.IO.Path.ChangeExtension(f[3], null) + i + System.IO.Path.GetExtension(f[3]))).ToArray();
                        }
                        sprites.Add(sprite); sprite.Groups.Add(group = new()); continue;
                    }
                    if (sprite is null) continue;
                    if (depth == 1) group = sprite.Groups[0];
                    if (f[0] == "L") { sprite.Groups.Add(group = new() { Offset = Number(f[1]), Count = Math.Max(1, (int)Number(f[2])) }); continue; }
                    if (f[0] == "T") { sprite.Groups.Add(group = new() { Trigger = f[1], TriggerStart = f.Count > 2 && f[2].Length > 0 ? Number(f[2]) : double.MinValue, TriggerEnd = f.Count > 3 && f[3].Length > 0 ? Number(f[3]) : double.MaxValue, TriggerGroup = f.Count > 4 ? -(int)Number(f[4]) : 0 }); continue; }
                    if (group is null || f.Count < 5) continue;
                    int easing = (int)Number(f[1]); double start = Number(f[2]), end = f[3].Length == 0 ? start : Number(f[3]);
                    int commandOrder = order++;
                    void Add(int property, int offset, int dimensions)
                    {
                        int groups = (f.Count - 4) / dimensions;
                        for (int segment = 0; segment < Math.Max(1, groups - 1); segment++)
                        {
                            double from = Number(f[4 + offset + segment * dimensions]);
                            double to = groups > segment + 1 ? Number(f[4 + offset + (segment + 1) * dimensions]) : from;
                            double duration = Math.Max(0, end - start);
                            group.Commands.Add(new(property, easing, start + duration * segment, end + duration * segment, from, to, commandOrder));
                        }
                    }
                    switch (f[0])
                    {
                        case "M": Add(0, 0, 2); Add(1, 1, 2); break;
                        case "MX": Add(0, 0, 1); break; case "MY": Add(1, 0, 1); break;
                        case "S": Add(2, 0, 1); break; case "V": Add(3, 0, 2); Add(4, 1, 2); break;
                        case "R": Add(5, 0, 1); break; case "C": Add(6, 0, 3); Add(7, 1, 3); Add(8, 2, 3); break;
                        case "F": Add(9, 0, 1); break;
                        case "P": int p = f[4] switch { "A" => 10, "H" => 11, "V" => 12, _ => -1 }; if (p >= 0) group.Commands.Add(new(p, 0, start, end, 1, start == end ? 1 : 0, commandOrder)); break;
                        default: diagnostics.Add($"{lineNumber}: {f[0]}"); break;
                    }
                }
                catch (Exception error) when (error is FormatException or ArgumentException or OverflowException or IndexOutOfRangeException or InvalidDataException)
                { diagnostics.Add($"{lineNumber}: {error.Message}"); }
            }
        }

        string Resolve(string name)
        {
            if (resources.TryGetValue(name, out string? resolved)) return resolved;
            string original = name;
            name = name.Trim().Trim('"').Replace('\\', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, name));
            string root = System.IO.Path.GetFullPath(directory).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Storyboard resource is outside its mapset.");
            if (!samplesOnly && !System.IO.Path.HasExtension(path)) foreach (string extension in new[] { ".jpg", ".jpeg", ".png" }) if (File.Exists(path + extension)) { path += extension; break; }
            resources[original] = path; return path;
        }
    }

    private static double Number(string value)
    {
        double parsed = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(parsed)) throw new FormatException("Non-finite storyboard value.");
        return parsed;
    }
    private static StoryboardLayer Layer(string value) => value switch
    { "0" or "Background" => StoryboardLayer.Background, "1" or "Fail" => StoryboardLayer.Fail, "2" or "Pass" => StoryboardLayer.Pass, "3" or "Foreground" => StoryboardLayer.Foreground, "4" or "Overlay" => StoryboardLayer.Overlay, _ => throw new FormatException("Unknown storyboard layer.") };
    private static Vector2 Origin(string value) => value switch
    { "0" or "TopLeft" => new(0, 0), "1" or "Centre" => new(.5f, .5f), "2" or "CentreLeft" => new(0, .5f), "3" or "TopRight" => new(1, 0), "4" or "BottomCentre" => new(.5f, 1), "5" or "TopCentre" => new(.5f, 0), "7" or "CentreRight" => new(1, .5f), "8" or "BottomLeft" => new(0, 1), "9" or "BottomRight" => new(1, 1), _ => new(0, 0) };
    private static List<string> Fields(string line)
    {
        List<string> result = []; var field = new StringBuilder(); bool quoted = false;
        foreach (char c in line) { if (c == '"') quoted = !quoted; else if (c == ',' && !quoted) { result.Add(field.ToString().Trim()); field.Clear(); } else field.Append(c); }
        result.Add(field.ToString().Trim()); return result;
    }
}
