using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

internal sealed class HitsoundingAudioCache
{
    private sealed class Entry(Task<AudioWaveform> task)
    {
        public Task<AudioWaveform> Task = task;
        public bool Observed;
        public long LastUse;
    }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> durations = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource cancellation = new();
    private long clock;
    public bool NeedsRedraw
    {
        get
        {
            bool completed = false;
            foreach (var entry in entries.Values)
                if (!entry.Observed && entry.Task.IsCompleted) { entry.Observed = true; completed = true; }
            return completed;
        }
    }
    public AudioWaveform? Get(Hitsound sound, Func<string, CancellationToken, Task<AudioWaveform>>? decode)
    {
        if (sound.FilePath is not { } path || decode is null) return null;
        if (!entries.TryGetValue(path, out var entry))
        {
            if (entries.Values.Count(e => !e.Task.IsCompleted) >= 2) return null;
            if (entries.Count >= 128)
            {
                var oldest = entries.Where(e => e.Value.Task.IsCompleted).MinBy(e => e.Value.LastUse);
                if (oldest.Key is null) return null;
                entries.Remove(oldest.Key);
            }
            entry = new(decode(path, cancellation.Token)); entries.Add(path, entry);
            _ = entry.Task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        entry.LastUse = ++clock;
        if (!entry.Task.IsCompleted) return null;
        entry.Observed = true;
        if (!entry.Task.IsCompletedSuccessfully) return null;
        durations[path] = entry.Task.Result.DurationMs;
        return entry.Task.Result;
    }
    public double Duration(Hitsound sound) => sound.FilePath is { } path ? durations.GetValueOrDefault(path)
        : sound.Name == "storyboard" ? 0 : 62.5;
    public void Clear()
    {
        cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
        entries.Clear(); durations.Clear();
    }
}
