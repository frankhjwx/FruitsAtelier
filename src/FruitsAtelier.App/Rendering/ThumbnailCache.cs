namespace FruitsAtelier.App.Rendering;

// Only two decoders run at once. Missing entries retry on the next frame, so
// fast scrolling cannot build an unbounded queue of off-screen images.
internal sealed class ThumbnailCache<T>(Func<string, T?> decode) : IDisposable where T : class
{
    private sealed class Entry
    {
        public Task<T?>? Pending;
        public T? Value;
        public long Used, Expires;
    }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private long clock;
    private int running;
    public T? Get(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        long now = Environment.TickCount64;
        foreach (var entry in entries.Values)
            if (entry.Pending is { IsCompleted: true } task)
            {
                entry.Value = task.GetAwaiter().GetResult(); entry.Pending = null; running--;
                entry.Expires = now + 60_000;
            }
        if (entries.TryGetValue(path, out var found))
        {
            found.Used = ++clock;
            if (found.Pending is not null || now < found.Expires) return found.Value;
            Release(found); entries.Remove(path);
        }
        if (running >= 2) return null;
        if (entries.Count >= 128)
        {
            var oldest = entries.Where(pair => pair.Value.Pending is null).MinBy(pair => pair.Value.Used);
            Release(oldest.Value); entries.Remove(oldest.Key);
        }
        entries[path] = new() { Used = ++clock, Pending = Task.Run(() =>
        {
            try { return decode(path); }
            catch { return null; }
        }) };
        running++;
        return null;
    }
    private static void Release(Entry entry)
    {
        if (entry.Value is IDisposable disposable) disposable.Dispose();
        if (entry.Pending is { } pending)
            _ = pending.ContinueWith(task => { if (task.Result is IDisposable result) result.Dispose(); }, TaskScheduler.Default);
    }
    public void Dispose()
    {
        foreach (var entry in entries.Values) Release(entry);
        entries.Clear(); running = 0;
    }
}
