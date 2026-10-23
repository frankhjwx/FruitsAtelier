using System.Net;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class DiscussionMarkdownTests
{
    internal static void Run(OsuDiscussionSnapshot source)
    {
        var blocks = DiscussionMarkdown.Parse("**bold** *italic* ***both*** ~~removed~~ `https://example.org/ 00:01:000`\n> quoted [named link](https://osu.ppy.sh/a_(b))\n```text\n**literal** ![](https://example.org/no-download)\n```\n- item\n![alt](https://koiyuki.s-ul.eu/L1ONATEm)");
        var spans = blocks.SelectMany(block => block.Spans).ToArray();
        Check(spans.Any(span => span.Text == "bold" && span.Style.HasFlag(DiscussionTextStyle.Bold))
            && spans.Any(span => span.Text == "italic" && span.Style.HasFlag(DiscussionTextStyle.Italic))
            && spans.Any(span => span.Text == "both" && span.Style == (DiscussionTextStyle.Bold | DiscussionTextStyle.Italic)), "nested emphasis");
        Check(spans.Any(span => span.Style.HasFlag(DiscussionTextStyle.Strike))
            && spans.Any(span => span.Style.HasFlag(DiscussionTextStyle.Code) && span.Url is null && span.Text.Contains("https://")), "strike and literal inline code");
        Check(blocks[1].QuoteDepth == 1 && blocks[2].Code && blocks[2].Spans.Single().Text.StartsWith("**literal**")
            && spans.Any(span => span.Text.StartsWith("• ")), "quote, fenced code and list blocks");
        Check(spans.Single(span => span.Text == "named link").Url == "https://osu.ppy.sh/a_(b)"
            && spans.Single(span => span.Image).Url == "https://koiyuki.s-ul.eu/L1ONATEm", "named links and extensionless image markup");
        Check(!DiscussionMarkdown.Parse("[unsafe](javascript:alert(1))").SelectMany(block => block.Spans).Any(span => span.Url is not null), "unsafe schemes stay text");
        Check(DiscussionMarkdown.Parse("a_b_c **unclosed \\*literal\\*").Single().Spans.All(span => span.Style == DiscussionTextStyle.None), "unmatched and escaped markers remain readable");
        Transport(); UiMedia(source);
    }

    private static void Transport()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fa-discussion-images-" + Guid.NewGuid().ToString("N"));
        byte[] png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "discussion", "bullhorn.png"));
        var handler = new ImageHandler(png);
        using var http = new HttpClient(handler); var client = new OsuDiscussionClient(http);
        try
        {
            var image = client.CacheImageAsync("https://koiyuki.s-ul.eu/extensionless", directory).GetAwaiter().GetResult();
            Check(image is { Width: 128, Height: 128 } && File.Exists(image.Path), "extensionless images are recognized by content and cached");
            Check(client.CacheImageAsync("https://koiyuki.s-ul.eu/extensionless", directory).GetAwaiter().GetResult() == image && handler.Requests == 1, "saved image works without a second request");
            Check(client.CacheImageAsync("https://127.0.0.1/private", directory).GetAwaiter().GetResult() is null
                && client.CacheImageAsync("file:///image.png", directory).GetAwaiter().GetResult() is null && handler.Requests == 1, "non-web and loopback images are not fetched");
            handler.Bytes = "<html>not an image</html>"u8.ToArray();
            Check(client.CacheImageAsync("https://example.org/not-image", directory).GetAwaiter().GetResult() is null, "HTML response falls back to a link");
            handler.Bytes = png.ToArray(); handler.Bytes[16] = 127;
            Check(client.CacheImageAsync("https://example.org/oversize", directory).GetAwaiter().GetResult() is null, "oversized dimensions are rejected before rendering");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void UiMedia(OsuDiscussionSnapshot source)
    {
        const string url = "https://koiyuki.s-ul.eu/L1ONATEm";
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "discussion", "bullhorn.png");
        var posts = new DiscussionPost[] { new(501, 10, "**Bold** *italic* [osu!](https://osu.ppy.sh/)\n> Quote\n![](https://koiyuki.s-ul.eu/L1ONATEm)", false, null),
            new(502, 20, "![reply](https://koiyuki.s-ul.eu/L1ONATEm)", false, null) };
        var thread = new BeatmapDiscussion(500, 101, null, 10, 0, "mapper_note", false, false, null, posts);
        var snapshot = new OsuDiscussionSnapshot(777, 20, [thread], source.Users, source.Owners);
        var pending = new TaskCompletionSource<DiscussionImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        int requests = 0; ui.View.DiscussionImageLoader = (_, _) => { Interlocked.Increment(ref requests); return pending.Task; };
        var opened = new List<string>(); ui.View.RequestVisitOsuWeb = opened.Add;
        ui.View.LoadProject(DiscussionTests.Project()); ui.View.ShowDiscussionPanel();
        Wait(() => ui.View.DiscussionSnapshot is not null && Volatile.Read(ref requests) == 1);
        for (int i = 0; i < 8; i++) ui.Paint();
        Check(!ui.View.DiscussionNeedsRedraw && ui.Canvas.Texts.Any(text => text.Value == "Bold" && text.Bold), "pending images leave the editor idle and Markdown bold is rendered");
        var before = ui.View.Document.DeepClone();
        pending.SetResult(new(path, 128, 128));
        Wait(() => ui.Canvas.Images.Count(image => image.Path == path && image.Bounds.Width > 30) >= 3);
        var panel = ui.View.DiscussionBounds;
        var inline = ui.Canvas.Images.Where(image => image.Path == path && image.Bounds.X > panel.X && image.Bounds.Width > 30).ToArray();
        Check(inline.Length == 2 && requests == 1, "main post and reply images share one background request");
        foreach (var image in inline) ui.Click(image.Bounds.X + 5, image.Bounds.Y + 5);
        Check(opened.SequenceEqual(new[] { url, url }) && ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "image clicks open originals without editing or seeking");
        int builds = ui.View.DiscussionLayoutBuilds; for (int i = 0; i < 5; i++) ui.Paint();
        Check(ui.View.DiscussionLayoutBuilds == builds, "loaded image geometry is reused on warm frames");
        ui.View.StopDiscussions();
        var stale = new TaskCompletionSource<DiscussionImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.View.DiscussionImageLoader = (_, _) => stale.Task; ui.View.LoadProject(DiscussionTests.Project()); ui.View.ShowDiscussionPanel();
        Wait(() => ui.View.DiscussionSnapshot is not null); ui.View.LoadDocument(new MapDocument()); stale.SetResult(new(path, 128, 128));
        for (int i = 0; i < 8; i++) { Thread.Sleep(1); ui.Paint(); }
        Check(ui.View.DiscussionSnapshot is null && !ui.View.DiscussionNeedsRedraw, "retired image work cannot publish into another project");
        var tall = new Ui(false); tall.View.DiscussionLoader = (_, _) => Task.FromResult(new OsuDiscussionSnapshot(777, 20,
            [thread with { Posts = [new(501, 10, "![](https://koiyuki.s-ul.eu/L1ONATEm)", false, null)] }], source.Users, source.Owners));
        tall.View.DiscussionImageLoader = (_, _) => Task.FromResult<DiscussionImage?>(new(path, 128, 2048));
        tall.View.LoadProject(DiscussionTests.Project()); tall.View.ShowDiscussionPanel();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do { tall.Paint(); Thread.Sleep(1); if (timer.Elapsed.TotalSeconds > 5) throw new Exception("Tall image timed out"); }
        while (tall.View.DiscussionSnapshot is null || tall.View.DiscussionImagesLoading);
        var list = tall.View.DiscussionListBounds;
        tall.View.Wheel(list.X + 30, list.Y + 40, -600, false); tall.Paint();
        Check(tall.Canvas.Images.Any(image => image.Path == path && image.Bounds.X > list.X && image.Bounds.Y < list.Y
            && image.Bounds.Bottom > list.Y), "scrolling through the middle of a tall image retains its visible slice");
        tall.View.StopDiscussions();
        void Wait(Func<bool> ready)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            do { ui.Paint(); Thread.Sleep(1); if (timer.Elapsed.TotalSeconds > 5) throw new Exception("Discussion image timed out"); } while (!ready());
        }
    }

    private sealed class ImageHandler(byte[] bytes) : HttpMessageHandler
    {
        internal byte[] Bytes = bytes;
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Check(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "image requests remain anonymous");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) });
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
