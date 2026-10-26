using System.Collections.Concurrent;
using FruitsAtelier.App.Media;

internal static class StoryboardImageCacheTests
{
    public static int Run()
    {
        Check(["active", "speculative", "visible"], ["visible"], ["active", "visible", "speculative"]);
        Check(["active", "speculative", "first", "visible"], ["first", "visible"], ["active", "visible", "first", "speculative"], prepareAgain: true);
        Console.WriteLine("PASS current-image demand precedes queued prefetch, including repeated preparation and multiple required images");
        return 0;
    }

    private static void Check(string[] prepared, string[] required, string[] expected, bool prepareAgain = false)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new CountdownEvent(prepared.Length);
        var order = new ConcurrentQueue<string>();
        using var cache = new StoryboardImageCache(path =>
        {
            if (path == "active") { started.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            order.Enqueue(path); completed.Signal();
            return new(1, 1, new byte[4]);
        });
        cache.Prepare(prepared);
        try
        {
            if (!started.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Decoder did not start.");
            for (int i = 0; i < 10; i++)
            {
                if (prepareAgain) cache.Prepare(prepared);
                foreach (string path in required) cache.Get(path);
            }
        }
        finally { release.Set(); }
        if (!completed.Wait(TimeSpan.FromSeconds(5)) || !SpinWait.SpinUntil(() => !cache.Loading, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Decoder did not complete.");
        string[] actual = order.ToArray();
        Console.WriteLine("Decode order: " + string.Join(", ", actual));
        if (!actual.SequenceEqual(expected))
            throw new Exception("A visible image already queued for prefetch waited behind speculative images.");
        cache.Prepare(prepared); foreach (string path in required) cache.Get(path);
        if (cache.DecodeCount != prepared.Length || cache.Loading || cache.Bytes != prepared.Length * 4 || cache.Bytes > StoryboardImageCache.Limit)
            throw new Exception("Demand promotion or repeated preparation duplicated work or exceeded the cache budget.");
    }
}
