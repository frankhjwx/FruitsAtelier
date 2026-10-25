namespace FruitsAtelier.App.Media;

internal sealed class StoryboardImageCache(Func<string, StoryboardImageCache.Pixels?> decode) : IDisposable
{
    internal sealed record Pixels(int Width, int Height, byte[] Data);
    private readonly object gate = new();
    private readonly Dictionary<string, (Pixels? Image, long Used)> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> decodedSizes = new(StringComparer.OrdinalIgnoreCase);
    private Queue<string> pending = new();
    private string[] prepared = [];
    private string? loadingPath;
    private long generation;
    private bool running, disposed;
    private long clock, bytes;
    internal const long Limit = 256L * 1024 * 1024;
    internal long Bytes { get { lock (gate) return bytes; } }
    internal int DecodeCount { get; private set; }
    internal string? Error { get; private set; }
    internal long Revision { get; private set; }
    internal bool Loading { get { lock (gate) return running; } }
    internal bool IsUnavailable(string path) { lock (gate) return decodedSizes.TryGetValue(path, out long size) && size < 0; }
    internal bool ExceedsCapacity(IReadOnlyList<string> paths)
    {
        lock (gate)
        {
            long size = 0; int count = 0;
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                count++;
                if (decodedSizes.TryGetValue(path, out long bytes) && bytes > 0) size += bytes;
            }
            return count > 512 || size > Limit;
        }
    }
    internal void Prepare(IReadOnlyList<string> paths)
    {
        lock (gate)
        {
            if (disposed) return;
            if (paths.Count == prepared.Length && paths.SequenceEqual(prepared, StringComparer.OrdinalIgnoreCase)) return;
            prepared = paths.Take(4096).ToArray();
            generation++;
            pending = new(prepared.Where(p => p != loadingPath && !cache.ContainsKey(p) && !IsUnavailable(p)).Distinct(StringComparer.OrdinalIgnoreCase));
            foreach (string path in paths) if (cache.TryGetValue(path, out var value)) cache[path] = (value.Image, ++clock);
            if (running || pending.Count == 0) return;
            running = true; _ = Task.Run(Load);
        }
    }
    internal Pixels? Get(string path, bool demand = true)
    {
        lock (gate)
        {
            if (!cache.TryGetValue(path, out var value))
            {
                if (demand && !disposed && !IsUnavailable(path) && !string.Equals(path, loadingPath, StringComparison.OrdinalIgnoreCase)
                    && (pending.Count == 0 || !string.Equals(path, pending.Peek(), StringComparison.OrdinalIgnoreCase)))
                {
                    // A current-frame image may already be queued behind speculative preloads.
                    pending = new(new[] { path }.Concat(pending.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))));
                    if (!running) { running = true; _ = Task.Run(Load); }
                }
                return null;
            }
            cache[path] = (value.Image, ++clock); return value.Image;
        }
    }
    private void Load()
    {
        while (true)
        {
            string path;
            long requestedGeneration;
            lock (gate)
            {
                if (disposed || pending.Count == 0) { running = false; return; }
                path = pending.Dequeue();
                if (cache.ContainsKey(path)) continue;
                loadingPath = path; requestedGeneration = generation;
            }
            Pixels? image = null;
            string? error = null;
            try { image = decode(path); if (image is null) error = "Image unavailable or exceeds the 64 MiB decoded image limit."; }
            catch (Exception ex) { error = ex.Message; }
            lock (gate)
            {
                if (disposed) { running = false; return; }
                loadingPath = null;
                if (generation != requestedGeneration && !prepared.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                DecodeCount++;
                if (error is not null) Error = $"{System.IO.Path.GetFileName(path)}: {error}";
                Revision++;
                long size = image?.Data.Length ?? 0;
                if (size > 64 * 1024 * 1024) image = null;
                size = image?.Data.Length ?? 0;
                if (decodedSizes.Count >= 8192) decodedSizes.Clear();
                decodedSizes[path] = image is null ? -1 : size;
                while (cache.Count > 0 && (bytes + size > Limit || cache.Count >= 512))
                {
                    var oldest = cache.MinBy(c => c.Value.Used);
                    bytes -= oldest.Value.Image?.Data.Length ?? 0; cache.Remove(oldest.Key);
                }
                if (cache.Remove(path, out var previous)) bytes -= previous.Image?.Data.Length ?? 0;
                cache[path] = (image, ++clock); bytes += size;
            }
        }
    }
    public void Dispose() { lock (gate) { disposed = true; cache.Clear(); decodedSizes.Clear(); pending.Clear(); prepared = []; bytes = 0; } }
}

internal static class MediaResourceIdentity
{
    private static long generation;
    internal static long Next() => Interlocked.Increment(ref generation);
}
