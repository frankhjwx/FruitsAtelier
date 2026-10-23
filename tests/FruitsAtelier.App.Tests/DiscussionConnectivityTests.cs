using System.Net;
using FruitsAtelier.Core;

internal static class DiscussionConnectivityTests
{
    internal static void Run(string fixture, OsuDiscussionSnapshot snapshot)
    {
        string cache = Path.GetFullPath(Path.Combine("artifacts", "tests", "discussion-cache", Guid.NewGuid().ToString("N")));
        using (var http = new HttpClient(new NetworkHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture) }))))
        {
            var client = new OsuDiscussionClient(http);
            client.LoadAsync(777, cache).GetAwaiter().GetResult();
            Check(client.ReadCachedAsync(777, cache).GetAwaiter().GetResult()?.Discussions.Count == 7, "validated public snapshot survives offline restart");
            File.WriteAllText(Path.Combine(cache, "777.json"), "incomplete");
            Check(client.ReadCachedAsync(777, cache).GetAwaiter().GetResult() is null, "corrupt cache is ignored");
        }
        using (var http = new HttpClient(new NetworkHandler(async token =>
        { await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new Exception("unreachable"); })))
        {
            var client = new OsuDiscussionClient(http, TimeSpan.FromMilliseconds(40));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { client.LoadAsync(777).GetAwaiter().GetResult(); throw new Exception("missing timeout"); }
            catch (HttpRequestException) { }
            Check(watch.Elapsed.TotalSeconds < 5, "slow requests have a bounded deadline");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { client.LoadAsync(777, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("missing cancellation"); }
            catch (OperationCanceledException) { }
        }

        using (var http = new HttpClient(new NetworkHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DelayedBody()) }))))
        {
            var client = new OsuDiscussionClient(http, TimeSpan.FromMilliseconds(40));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { client.LoadAsync(777).GetAwaiter().GetResult(); throw new Exception("missing slow-body timeout"); }
            catch (HttpRequestException) { }
            Check(watch.Elapsed.TotalSeconds < 5, "deadline also covers a stalled body after successful headers");
        }

        var network = new TaskCompletionSource<OsuDiscussionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Ui(false);
        ui.View.DiscussionCacheLoader = (_, _) => Task.FromResult<OsuDiscussionSnapshot?>(snapshot);
        ui.View.DiscussionLoader = (_, _) => network.Task;
        ui.View.LoadProject(DiscussionTests.Project()); ui.View.ShowDiscussionPanel();
        Wait(ui, () => ui.View.DiscussionSnapshot is not null);
        Check(!ui.View.DiscussionNeedsRedraw, "waiting for a weak connection does not schedule continuous frames");
        var before = ui.View.Document.DeepClone(); var plot = ui.Plot;
        ui.Key('2'); ui.Click(plot.X + plot.Width / 2, plot.Y + 15);
        Check(ui.View.Document.Fruits.Count == before.Fruits.Count + 1, "editing remains available while network response is pending");
        ui.Key('Z', ctrl: true);
        network.SetException(new HttpRequestException("offline"));
        Wait(ui, () => !ui.View.DiscussionLoading);
        Check(ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("discussion.cachedOffline")), "offline refresh status is non-modal");
        Check(ui.View.DiscussionSnapshot == snapshot && ui.View.Document.ContentEquals(before), "network failure retains cached discussions and undo");
        ui.View.DiscussionLoader = (_, _) => Task.FromException<OsuDiscussionSnapshot>(new HttpRequestException("offline"));
        ui.View.RefreshDiscussions();
        Check(ui.View.DiscussionSnapshot == snapshot, "refresh keeps the current conversation visible");
        for (int i = 0; i < 8; i++) { Thread.Sleep(1); ui.Paint(); }
        Check(ui.View.DiscussionSnapshot == snapshot && !ui.View.IsDirty, "repeated offline refresh never clears content or edits the map");
        ui.View.StopDiscussions();
    }

    private static void Wait(Ui ui, Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        do { ui.Paint(); if (condition()) return; Thread.Sleep(1); } while (watch.Elapsed.TotalSeconds < 5);
        throw new Exception("Discussion background operation did not settle.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class DelayedBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class NetworkHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(cancellationToken);
    }
}
