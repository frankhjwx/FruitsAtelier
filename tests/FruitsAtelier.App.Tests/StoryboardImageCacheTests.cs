using System.Collections.Concurrent;
using FruitsAtelier.App.Media;

internal static class StoryboardImageCacheTests
{
    public static int Run()
    {
        Check(["active", "speculative", "visible"], ["visible"], ["active", "visible", "speculative"]);
        Check(["active", "speculative", "first", "visible"], ["first", "visible"], ["active", "visible", "first", "speculative"], prepareAgain: true);
        Reuse();
        Console.WriteLine("PASS current-image demand precedes queued prefetch, including repeated preparation and multiple required images");
        return 0;
    }

    private static void Reuse()
    {
        string directory = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetFullPath("artifacts/tests/storyboard-image-cache"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string common = Path.Combine(directory, "common"), first = Path.Combine(directory, "first"), second = Path.Combine(directory, "second");
        File.WriteAllBytes(common, [1]); File.WriteAllBytes(first, [2]); File.WriteAllBytes(second, [3]);
        using var cache = new StoryboardImageCache(path => File.Exists(path) ? new(1, 1, new byte[] { File.ReadAllBytes(path)[0], 0, 0, 255 }) : null);
        Ready(common, first); var pixels = cache.Get(common);
        cache.ReuseResources(); Ready(common, second);
        cache.ReuseResources(); Ready(common, first);
        if (!ReferenceEquals(pixels, cache.Get(common)) || cache.DecodeCount != 3 || cache.Bytes != 12)
            throw new Exception("Alternating scenes decoded unchanged shared images or retained unbounded scene state.");
        File.WriteAllBytes(common, [9, 8]); cache.ReuseResources(); Ready(common);
        if (cache.Get(common)?.Data[0] != 9 || ReferenceEquals(pixels, cache.Get(common)) || cache.DecodeCount != 4)
            throw new Exception("A changed shared image reused stale decoded pixels.");
        File.Delete(common); cache.ReuseResources(); Ready(common);
        if (cache.Get(common) is not null || !cache.IsUnavailable(common) || cache.Error is null)
            throw new Exception("A deleted image retained its previous pixels or lost its diagnostic.");
        cache.ReuseResources(); Ready(common);
        if (cache.DecodeCount != 5 || cache.Error is null) throw new Exception("An unchanged unavailable image was decoded again or lost its diagnostic.");
        File.WriteAllBytes(common, [7]); cache.ReuseResources(); Ready(common);
        if (cache.Get(common)?.Data[0] != 7 || cache.DecodeCount != 6) throw new Exception("A restored missing image did not invalidate its unavailable entry.");

        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var pending = new StoryboardImageCache(path =>
        {
            if (path == first) { started.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            return new(1, 1, new byte[4]);
        });
        pending.Prepare([first, second]);
        if (!started.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Pending decoder did not start.");
        pending.ReuseResources(); pending.Prepare([first]); pending.Get(first); release.Set();
        if (!SpinWait.SpinUntil(() => !pending.Loading, TimeSpan.FromSeconds(5)) || pending.Get(first) is null || pending.DecodeCount != 1 || pending.Bytes != 4)
            throw new Exception("Reused in-flight common image was lost, duplicated or published an obsolete preload.");
        pending.Dispose();
        if (pending.Bytes != 0) throw new Exception("Full retirement retained reused decoded images.");
        Console.WriteLine("PASS bounded cross-scene pixels, version/deletion/restoration invalidation, missing diagnostics and in-flight preparation retirement");

        void Ready(params string[] paths)
        {
            cache.Prepare(paths);
            if (!SpinWait.SpinUntil(() => !cache.Loading, TimeSpan.FromSeconds(5))) throw new TimeoutException("Cache did not finish preparing.");
            foreach (string path in paths) if (cache.Get(path) is null && !cache.IsUnavailable(path)) throw new Exception("Prepared image is unavailable without a terminal diagnostic.");
        }
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
