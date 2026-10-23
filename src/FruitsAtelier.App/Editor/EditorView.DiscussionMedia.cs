using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal Func<string, CancellationToken, Task<DiscussionImage?>>? DiscussionImageLoader { get; set; }
    private Task? discussionImageLoad;
    internal bool DiscussionImagesLoading => discussionImageLoad is not null;
    private OsuDiscussionSnapshot? discussionImageSnapshot;
    private readonly Dictionary<string, DiscussionImage?> discussionImages = new(StringComparer.Ordinal);
    private System.Collections.Concurrent.ConcurrentQueue<(string Url, DiscussionImage? Image)> discussionImageUpdates = new();

    private void BeginDiscussionImages()
    {
        if (discussionImageLoad is not null || discussionSnapshot is null || discussionCancellation is null
            || ReferenceEquals(discussionImageSnapshot, discussionSnapshot)) return;
        discussionImageSnapshot = discussionSnapshot;
        if (DiscussionLoader is not null && DiscussionImageLoader is null) return;
        var snapshot = discussionSnapshot; var existing = discussionImages.Keys.ToHashSet(StringComparer.Ordinal);
        var token = discussionCancellation.Token; var updates = discussionImageUpdates; var loader = DiscussionImageLoader;
        string directory = Path.Combine(LibrarySettings.Workspace, "Cache", "DiscussionImages");
        discussionImageLoad = Task.Run(async () =>
        {
            var urls = snapshot.Discussions.SelectMany(thread => thread.Posts.Where(post => !post.System))
                .SelectMany(post => DiscussionText.ReviewBlocks(post.Message)).SelectMany(block => DiscussionMarkdown.Parse(block.Text))
                .SelectMany(paragraph => paragraph.Spans).Where(span => span.Image && span.Url is not null)
                .Select(span => span.Url!).Distinct(StringComparer.Ordinal).Where(url => !existing.Contains(url));
            await Parallel.ForEachAsync(urls,
            new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, async (url, cancellation) =>
            {
                DiscussionImage? image = null;
                try { image = await (loader is null ? discussionClient.CacheImageAsync(url, directory, cancellation) : loader(url, cancellation)).ConfigureAwait(false); }
                catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or NotSupportedException) { }
                if (!cancellation.IsCancellationRequested) updates.Enqueue((url, image));
            }).ConfigureAwait(false);
        }, token);
    }

    private void PumpDiscussionImages()
    {
        bool changed = false;
        while (discussionImageUpdates.TryDequeue(out var update))
        { discussionImages[update.Url] = update.Image; changed = true; }
        if (changed) { InvalidateDiscussionLayout(); discussionQuoteLayouts.Clear(); }
        if (discussionImageLoad is { IsCompleted: true } completed)
        { _ = completed.Exception; discussionImageLoad = null; }
        BeginDiscussionImages();
    }
}
