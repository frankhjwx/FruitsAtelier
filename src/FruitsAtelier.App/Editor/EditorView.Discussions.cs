using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly OsuDiscussionClient discussionClient = new();
    internal Func<long, CancellationToken, Task<OsuDiscussionSnapshot>>? DiscussionLoader { get; set; }
    internal Func<long, CancellationToken, Task<OsuDiscussionSnapshot?>>? DiscussionCacheLoader { get; set; }
    private Task<OsuDiscussionSnapshot?>? discussionCacheLoad;
    private Task<OsuDiscussionSnapshot>? discussionLoad;
    private bool discussionUsingCache;
    private Task<Dictionary<long, string>>? discussionAvatarLoad;
    private OsuDiscussionSnapshot? discussionAvatarSnapshot;
    private CancellationTokenSource? discussionCancellation;
    private OsuDiscussionSnapshot? discussionSnapshot;
    private long discussionSetId, discussionBeatmapId;
    private string? discussionError;
    private DateTimeOffset discussionRelativeRefresh;
    private (long Post, bool Canvas)? discussionCopiedLink;
    private DateTimeOffset discussionCopiedUntil;
    private Rect discussionCopyFeedbackBounds, discussionCopyHoverBounds;
    private readonly Dictionary<long, string> discussionAvatars = [];
    private System.Collections.Concurrent.ConcurrentQueue<(long User, string Path)> discussionAvatarUpdates = new();
    private readonly Dictionary<(DiscussionCategory Category, long Beatmap), float> discussionScrolls = [];
    private readonly HashSet<long> discussionCollapsed = [];
    private DiscussionCategory discussionCategory = DiscussionCategory.Timeline;
    private float discussionScroll, discussionTabScroll, discussionTabMaximum, discussionTabWidth;
    private bool discussionRevealTab = true, discussionScrollbarDragging;
    private float discussionScrollbarOffset;
    private Rect discussionBounds, discussionTabsBounds, discussionListBounds, discussionScrollbarTrack, discussionScrollbarThumb;
    private readonly List<(DiscussionCategory Category, Rect Bounds)> discussionTabHits = [];
    private readonly List<(Rect Bounds, double Time, long Discussion)> discussionReferenceHits = [];
    private readonly List<(Rect Bounds, long Discussion)> discussionMarkerHits = [];
    private readonly Dictionary<DiscussionCategory, bool> discussionSorts = [];
    private bool discussionSortUpdated
    {
        get => discussionSorts.GetValueOrDefault(discussionCategory, discussionCategory != DiscussionCategory.Timeline);
        set => discussionSorts[discussionCategory] = value;
    }
    internal bool DiscussionSortByUpdate => discussionSortUpdated;
    private sealed class DiscussionLayout
    {
        internal readonly List<DiscussionLine> Lines = [];
        internal readonly List<(float Top, float Bottom, long Id)> Cards = [];
        internal readonly List<DiscussionPostBlock> PostBlocks = [];
        internal float[] LineEnds = [];
        internal string SelectionText = "";
        internal float ContentHeight;
    }
    private readonly DiscussionLayout discussionPanelLayout = new();
    private List<DiscussionPostBlock> discussionPostBlocks => discussionPanelLayout.PostBlocks;
    private List<DiscussionLine> discussionLines => discussionPanelLayout.Lines;
    private List<(float Top, float Bottom, long Id)> discussionCards => discussionPanelLayout.Cards;
    private float discussionContentHeight { get => discussionPanelLayout.ContentHeight; set => discussionPanelLayout.ContentHeight = value; }
    private (OsuDiscussionSnapshot? Snapshot, long Beatmap, DiscussionCategory Category, float Width, string Language, Type Canvas)? discussionLayoutKey;
    private DiscussionAnchor[] discussionAnchors = [];
    private BeatmapDiscussion[][] discussionRoots = [[], [], [], []];
    private string[] discussionTabLabels = ["", "", "", ""];
    private (string Language, string Difficulty)? discussionTabLabelKey;
    private long? discussionFocused;
    private long? discussionRevealThread;
    private sealed record DiscussionRun(float X, float Width, string Text, uint Colour, double? Time = null, long Discussion = 0, string? Url = null, DiscussionTextStyle Style = DiscussionTextStyle.None, IReadOnlyList<int>? Numbers = null, int TextStart = -1, int LinkId = -1);
    private sealed record DiscussionLine(float Top, float Height, float Indent, IReadOnlyList<DiscussionRun> Runs,
        long? User = null, long? OwnerBeatmap = null, long? Toggle = null, float AuthorWidth = 0,
        long? Permalink = null, long Thread = 0, DateTimeOffset? PostedAt = null, long? Header = null, string? Icon = null, bool Reply = false,
        DiscussionImage? Image = null, string? ImageUrl = null, bool Quoted = false, bool CodeBlock = false, double? CardTime = null);
    private sealed record DiscussionPostBlock(float Top, float Bottom, float Indent, float Divider, uint Colour, bool Reply);
    private sealed record DiscussionAnchor(double Time, long Discussion, long User, string Message, IReadOnlyList<int> Numbers);
    internal Rect DiscussionBounds => discussionBounds;
    internal Rect DiscussionListBounds => discussionListBounds;
    internal Rect DiscussionTabsBounds => discussionTabsBounds;
    internal Rect DiscussionThreadBounds(long id)
    {
        var card = discussionCards.FirstOrDefault(card => card.Id == id);
        return DiscussionIntersection(new(discussionBounds.X + 36, discussionListBounds.Y + card.Top - discussionScroll,
            discussionBounds.Width - 46, card.Bottom - card.Top - 8), discussionListBounds);
    }
    internal float DiscussionScrollOffset => discussionScroll;
    internal int DiscussionLayoutBuilds { get; private set; }
    internal Rect DiscussionCategoryBounds(DiscussionCategory category) => discussionTabHits.FirstOrDefault(t => t.Category == category).Bounds;
    internal IReadOnlyList<(Rect Bounds, double Time, long Discussion)> DiscussionReferenceHits => discussionReferenceHits;
    internal IReadOnlyList<(Rect Bounds, long Discussion)> DiscussionMarkerHits => discussionMarkerHits;
    internal DiscussionCategory ActiveDiscussionCategory => discussionCategory;
    internal OsuDiscussionSnapshot? DiscussionSnapshot => discussionSnapshot;
    internal bool DiscussionLoading => discussionLoad is not null;
    public bool DiscussionNeedsRedraw => !LibraryVisible && !PatternEditingActive
        && (discussionCopiedLink is not null && timeProvider.GetUtcNow() >= discussionCopiedUntil
            || !discussionAvatarUpdates.IsEmpty || !discussionImageUpdates.IsEmpty || discussionImageLoad is { IsCompleted: true } || discussionLoad is { IsCompleted: true } || discussionAvatarLoad is { IsCompleted: true } || discussionCacheLoad is { IsCompleted: true }
            || PanelVisible(DockPanel.Discussion) && discussionSnapshot is not null && timeProvider.GetUtcNow() >= discussionRelativeRefresh);

    public void ShowDiscussionPanel()
    {
        if (TimingPageVisible) return;
        if (!DockPanelExists(DockPanel.Discussion)) AddDockPanel(DockPanel.Discussion, false);
        var group = PanelGroup(DockPanel.Discussion); group.Active = DockPanel.Discussion; group.Collapsed = false;
    }

    private static long DiscussionOnlineId(MapDocument document, string key)
        => long.TryParse(OsuBeatmapReader.Setting(document, "Metadata", key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long id) ? Math.Max(0, id) : 0;

    private (DiscussionCategory, long) DiscussionScrollKey => (discussionCategory,
        discussionCategory is DiscussionCategory.Reviews or DiscussionCategory.GeneralAll ? 0 : discussionBeatmapId);

    private void PumpDiscussions()
    {
        discussionCopyFeedbackBounds = discussionCopyHoverBounds = default;
        discussionTextHits.Clear();
        discussionHandHits.Clear(); discussionBodyHits.Clear();
        if (timeProvider.GetUtcNow() >= discussionCopiedUntil) discussionCopiedLink = null;
        if (LibraryVisible || !HasEditorProject || PatternEditingActive) return;
        if (timeProvider.GetUtcNow() >= discussionRelativeRefresh) discussionRelativeRefresh = timeProvider.GetUtcNow().AddMinutes(1);
        long set = DiscussionOnlineId(Document, "BeatmapSetID"), beatmap = DiscussionOnlineId(Document, "BeatmapID");
        if (set != discussionSetId)
        {
            StopDiscussions(); discussionSetId = set;
            if (set > 0) BeginDiscussionLoad();
        }
        if (beatmap != discussionBeatmapId)
        {
            discussionScrolls[DiscussionScrollKey] = discussionScroll;
            discussionBeatmapId = beatmap; discussionScroll = discussionScrolls.GetValueOrDefault(DiscussionScrollKey);
            InvalidateDiscussionLayout(); RefreshDiscussionAnchors(); discussionRevealTab = true;
        }
        if (discussionCacheLoad is { IsCompleted: true } cached)
        {
            discussionCacheLoad = null;
            if (cached.IsCompletedSuccessfully && cached.Result is { } saved && discussionSnapshot is null)
            {
                discussionSnapshot = saved; discussionUsingCache = true;
                InvalidateDiscussionLayout(); RefreshDiscussionAnchors(); BeginDiscussionAvatars();
            }
            else _ = cached.Exception;
        }
        if (discussionLoad is { IsCompleted: true } load)
        {
            discussionLoad = null;
            if (load.IsCompletedSuccessfully)
            {
                discussionSnapshot = load.Result; discussionError = null;
                InvalidateDiscussionLayout(); RefreshDiscussionAnchors();
                discussionUsingCache = false; BeginDiscussionAvatars();
            }
            else if (!load.IsCanceled) { _ = load.Exception; discussionError = L.Get("discussion.loadFailed"); discussionUsingCache = discussionSnapshot is not null; }
        }
        while (discussionAvatarUpdates.TryDequeue(out var update)) discussionAvatars[update.User] = update.Path;
        if (discussionAvatarLoad is { IsCompleted: true } avatars)
        {
            discussionAvatarLoad = null;
            if (avatars.IsCompletedSuccessfully)
                foreach (var pair in avatars.Result) discussionAvatars[pair.Key] = pair.Value;
            else _ = avatars.Exception;
            BeginDiscussionAvatars();
        }
        PumpDiscussionImages();
    }

    private void BeginDiscussionAvatars()
    {
        if (discussionAvatarLoad is not null || discussionSnapshot is null || ReferenceEquals(discussionAvatarSnapshot, discussionSnapshot)) return;
        if (DiscussionLoader is null && discussionCancellation is { } cancellation)
        {
            discussionAvatarSnapshot = discussionSnapshot;
            var users = discussionSnapshot.Users.Values.ToArray();
            var updates = discussionAvatarUpdates;
            string directory = Path.Combine(LibrarySettings.Workspace, "Cache", "DiscussionAvatars");
            discussionAvatarLoad = Task.Run(async () =>
            {
                var paths = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();
                await Parallel.ForEachAsync(users, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellation.Token }, async (user, token) =>
                {
                    try
                    {
                        if (await discussionClient.CacheAvatarAsync(user.AvatarUrl, directory, token).ConfigureAwait(false) is { } path && !token.IsCancellationRequested)
                        { paths[user.Id] = path; updates.Enqueue((user.Id, path)); }
                    }
                    catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException) { }
                }).ConfigureAwait(false);
                return new Dictionary<long, string>(paths);
            }, cancellation.Token);
        }
    }

    private void BeginDiscussionLoad()
    {
        if (discussionSetId <= 0 || discussionLoad is not null) return;
        discussionCancellation ??= new(); discussionError = null;
        long set = discussionSetId; var token = discussionCancellation.Token;
        var loader = DiscussionLoader;
        string directory = Path.Combine(LibrarySettings.Workspace, "Cache", "Discussions");
        var cacheLoader = DiscussionCacheLoader;
        if (discussionSnapshot is null && (loader is null || cacheLoader is not null))
            discussionCacheLoad = Task.Run(() => cacheLoader is null ? discussionClient.ReadCachedAsync(set, directory, token) : cacheLoader(set, token), token);
        discussionLoad = Task.Run(() => loader is null ? discussionClient.LoadAsync(set, directory, token) : loader(set, token), token);
    }

    internal void RefreshDiscussions()
    {
        foreach (string url in discussionImages.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray()) discussionImages.Remove(url);
        CancelDiscussionLoads(); discussionUsingCache = discussionSnapshot is not null; BeginDiscussionLoad();
    }

    private void CancelDiscussionLoads()
    {
        var cancellation = discussionCancellation; discussionCancellation = null;
        cancellation?.Cancel();
        Task[] work = new Task?[] { discussionLoad, discussionAvatarLoad, discussionCacheLoad, discussionImageLoad }.OfType<Task>().ToArray();
        if (work.Length > 0) _ = Task.WhenAll(work).ContinueWith(t => { _ = t.Exception; cancellation?.Dispose(); }, TaskScheduler.Default);
        else cancellation?.Dispose();
        discussionLoad = null; discussionAvatarLoad = null; discussionCacheLoad = null;
        discussionAvatarUpdates = new(); discussionAvatarSnapshot = null;
        discussionImageUpdates = new(); discussionImageLoad = null; discussionImageSnapshot = null;
    }

    public void StopDiscussions()
    {
        CancelDiscussionLoads(); discussionSnapshot = null; discussionUsingCache = false;
        discussionImages.Clear();
        CloseDiscussionImage();
        ClearDiscussionTextSelection();
        discussionTextHits.Clear();
        discussionHandHits.Clear(); discussionBodyHits.Clear();
        discussionCopiedLink = null;
        discussionSetId = discussionBeatmapId = 0; discussionError = null; discussionAvatars.Clear();
        discussionRoots = [[], [], [], []]; discussionTabLabelKey = null;
        discussionScrolls.Clear(); discussionQuoteScrolls.Clear(); discussionCollapsed.Clear(); discussionScroll = discussionTabScroll = 0;
        discussionAnchors = []; ClearDiscussionRegions(); discussionMarkerHits.Clear(); discussionReferenceHits.Clear();
        discussionFocused = discussionRevealThread = null; discussionScrollbarDragging = false;
        discussionScrollbarTrack = discussionScrollbarThumb = default;
        InvalidateDiscussionLayout();
    }

    private void InvalidateDiscussionLayout()
    { ClearDiscussionTextSelection(); discussionLayoutKey = null; discussionLines.Clear(); discussionCards.Clear(); discussionPostBlocks.Clear(); discussionContentHeight = 0; }

    private void RefreshDiscussionAnchors()
    {
        discussionRoots = Enum.GetValues<DiscussionCategory>().Select(category => discussionSnapshot?.For(category, discussionBeatmapId) ?? []).ToArray();
        discussionTabLabelKey = null;
        if (discussionSnapshot is not { } snapshot || discussionBeatmapId <= 0) { discussionAnchors = []; return; }
        var anchors = new List<DiscussionAnchor>();
        foreach (var thread in snapshot.Discussions.Where(d => d.BeatmapId == discussionBeatmapId))
        {
            foreach (var post in thread.Posts.Where(p => !p.System))
                foreach (var reference in DiscussionText.References(post.Message))
                    anchors.Add(new(reference.TimeMs, thread.Id, post.UserId, post.Message, reference.Numbers));
            if (thread.Timestamp is { } timestamp && !anchors.Any(a => a.Discussion == thread.Id && a.Time == timestamp))
            {
                var first = thread.Posts.FirstOrDefault(p => !p.System);
                if (first is not null) anchors.Add(new(timestamp, thread.Id, first.UserId, first.Message, []));
            }
        }
        discussionAnchors = anchors.DistinctBy(a => (a.Time, a.Discussion, a.User, a.Message)).OrderBy(a => a.Time).ThenBy(a => a.Discussion).ToArray();
    }

    private void SelectDiscussionCategory(DiscussionCategory category)
    {
        discussionScrolls[DiscussionScrollKey] = discussionScroll; discussionCategory = category;
        discussionScroll = discussionScrolls.GetValueOrDefault(DiscussionScrollKey);
        InvalidateDiscussionLayout(); discussionRevealTab = true;
    }

    private string DiscussionCategoryTitle(DiscussionCategory category) => category switch
    {
        DiscussionCategory.Reviews => L.Get("discussion.reviews"),
        DiscussionCategory.GeneralAll => L.Get("discussion.generalAll"),
        DiscussionCategory.General => L.Get("discussion.general", CurrentDifficultyName),
        _ => L.Get("discussion.timeline")
    };

    private void DrawDiscussionPanel(ICanvas c, Rect bounds)
    {
        discussionBounds = bounds; discussionReferenceHits.Clear(); discussionTabHits.Clear();
        discussionTabsBounds = new(bounds.X, bounds.Y, Math.Max(0, bounds.Width - 30), 30);
        Button(c, new(bounds.Right - 26, bounds.Y, 26, 28), "", RefreshDiscussions, enabled: discussionSetId > 0 && discussionLoad is null);
        DiscussionIcon(c, "sync-alt", new(bounds.Right - 20, bounds.Y + 8, 12, 12), discussionLoad is null ? Muted : Accent);
        var categories = Enum.GetValues<DiscussionCategory>();
        float[] widths = categories.Select(category => c.MeasureText(DiscussionTabLabel(category), 11) + 16).ToArray();
        float total = widths.Sum(), arrows = total > discussionTabsBounds.Width ? 20 : 0;
        var strip = new Rect(bounds.X + arrows, bounds.Y, Math.Max(1, discussionTabsBounds.Width - arrows * 2), 30);
        discussionTabMaximum = Math.Max(0, total - strip.Width);
        if (discussionRevealTab || discussionTabWidth != strip.Width)
        {
            float start = widths.Take((int)discussionCategory).Sum(), end = start + widths[(int)discussionCategory];
            discussionTabScroll = Math.Clamp(start < discussionTabScroll ? start : end > discussionTabScroll + strip.Width ? end - strip.Width : discussionTabScroll, 0, discussionTabMaximum);
            discussionRevealTab = false; discussionTabWidth = strip.Width;
        }
        discussionTabScroll = Math.Clamp(discussionTabScroll, 0, discussionTabMaximum);
        c.Clip(strip); float x = strip.X - discussionTabScroll;
        for (int i = 0; i < categories.Length; i++)
        {
            var category = categories[i]; var tab = new Rect(x, strip.Y, widths[i], 28);
            var clipped = DiscussionIntersection(tab, strip);
            bool selected = discussionCategory == category;
            c.Text(DiscussionTabLabel(category), tab.X + 8, tab.Y + 7, 11, selected ? Accent : Muted, tab.Width - 16);
            if (selected) c.Line(tab.X + 6, tab.Bottom - 1, tab.Right - 6, tab.Bottom - 1, Accent, 2);
            if (clipped.Width > 0) { discussionTabHits.Add((category, clipped)); hits.Add(new(clipped, () => SelectDiscussionCategory(category), true)); }
            x += widths[i];
        }
        c.Unclip();
        if (arrows > 0)
        {
            Button(c, new(bounds.X, bounds.Y, 20, 28), "‹", () => discussionTabScroll = Math.Max(0, discussionTabScroll - strip.Width * .7f), enabled: discussionTabScroll > 0, textPadding: 5);
            Button(c, new(discussionTabsBounds.Right - 20, bounds.Y, 20, 28), "›", () => discussionTabScroll = Math.Min(discussionTabMaximum, discussionTabScroll + strip.Width * .7f), enabled: discussionTabScroll < discussionTabMaximum, textPadding: 5);
        }
        float toolbarHeight = DrawDiscussionToolbar(c, bounds);
        if (discussionSnapshot is not null && discussionUsingCache)
        {
            string statusKey = discussionError is not null ? "discussion.cachedOffline" : "discussion.cachedUpdating";
            c.Text(L.Get(statusKey), bounds.X + 6, bounds.Y + 34 + toolbarHeight, 10, discussionError is null ? Muted : DiscussionPendingColour, Math.Max(1, bounds.Width - 12));
            toolbarHeight += 22;
        }
        discussionListBounds = new(bounds.X, bounds.Y + 34 + toolbarHeight, bounds.Width, Math.Max(0, bounds.Height - 34 - toolbarHeight));
        if (discussionSnapshot is null)
        {
            string message = discussionSetId <= 0 ? L.Get("discussion.noOnlineSet") : discussionError is { } error ? L.Reformat(error) : L.Get("discussion.loading");
            c.Text(message, bounds.X + 8, discussionListBounds.Y + 10, 12, Muted, Math.Max(0, bounds.Width - 16)); return;
        }
        var key = (discussionSnapshot, discussionBeatmapId, discussionCategory, bounds.Width, L.Language, c.GetType());
        if (discussionLayoutKey != key) { BuildDiscussionLayout(c, Math.Max(20, bounds.Width - 64)); discussionLayoutKey = key; }
        if (discussionRevealThread is { } id)
        {
            var card = discussionCards.FirstOrDefault(card => card.Id == id);
            discussionScroll = card.Top; discussionRevealThread = null;
        }
        float maximum = Math.Max(0, discussionContentHeight - discussionListBounds.Height);
        discussionScroll = Math.Clamp(discussionScroll, 0, maximum);
        c.Clip(discussionListBounds);
        foreach (var card in discussionCards)
        {
            if (card.Bottom <= discussionScroll) continue; if (card.Top > discussionScroll + discussionListBounds.Height) break;
            var r = new Rect(bounds.X + 36, discussionListBounds.Y + card.Top - discussionScroll, bounds.Width - 46, card.Bottom - card.Top - 8);
            var thread = discussionRoots[(int)discussionCategory].First(d => d.Id == card.Id);
            uint state = thread.Resolved ? DiscussionResolvedColour : thread.CanResolve ? DiscussionPendingColour : Accent;
            c.Line(bounds.X + 8, r.Y, bounds.X + 8, r.Bottom + 8, 0x151B20, 3);
            c.Fill(new(bounds.X, r.Y + 10, DiscussionHasState(thread) ? 30 : 14, 20), Background);
            DrawDiscussionTypeIcons(c, thread, bounds.X + 1, r.Y + 14);
            c.Fill(r, 0x343E43, 5);
            c.Fill(new(r.X, r.Y + 4, 3, r.Height - 8), state, 1);
            if (card.Id == discussionFocused) c.Stroke(r, Accent, radius: 5);
            if (thread.Timestamp is { } time)
            {
                var target = DiscussionIntersection(r, discussionListBounds);
                if (target.Height > 0) hits.Add(new(target, () => JumpToDiscussionTime(time, thread.Id), true));
            }
        }
        DrawDiscussionContent(c, discussionPanelLayout, discussionListBounds, bounds.X + 46,
            Math.Max(20, bounds.Width - 64), discussionScroll, canvasQuote: false);
        c.Unclip();
        discussionScrollbarTrack = new(bounds.Right - 7, discussionListBounds.Y, 7, discussionListBounds.Height);
        discussionScrollbarThumb = default;
        if (maximum > 0 && discussionListBounds.Height > 0)
        {
            float h = Math.Min(discussionListBounds.Height, Math.Max(20, discussionListBounds.Height * discussionListBounds.Height / discussionContentHeight));
            discussionScrollbarThumb = new(bounds.Right - 5, discussionListBounds.Y + discussionScroll / maximum * (discussionListBounds.Height - h), 4, h);
            c.Fill(discussionScrollbarThumb, Muted, 2);
        }
    }

    private void DrawDiscussionContent(ICanvas c, DiscussionLayout layout, Rect viewport, float contentX,
        float contentWidth, float scroll, bool canvasQuote)
    {
        int? hoveredLink = DiscussionHoveredLink(layout, viewport, contentX, scroll);
        foreach (var block in layout.PostBlocks)
        {
            if (block.Bottom < scroll) continue;
            if (block.Top > scroll + viewport.Height) break;
            float top = viewport.Y + block.Top - scroll;
            if (block.Reply) c.Fill(new(contentX - 5 + block.Indent, top - 5,
                Math.Max(1, contentWidth - block.Indent + 8), block.Bottom - block.Top + 3), 0x2C3439);
            float divider = contentX + block.Divider;
            c.Line(divider, top, divider, top + block.Bottom - block.Top - 6, block.Colour, 2, .7f);
        }
        int first = DiscussionFirstVisible(layout.LineEnds, scroll);
        for (int i = first; i < layout.Lines.Count; i++)
        {
            var line = layout.Lines[i]; if (line.Top >= scroll + viewport.Height) break;
            float y = viewport.Y + line.Top - scroll;
            float left = contentX + line.Indent;
            if (line.Runs.Any(run => run.TextStart >= 0))
                discussionBodyHits.Add(DiscussionIntersection(new(left, y, Math.Max(0, contentWidth - line.Indent), line.Height), viewport));
            if (line.Quoted) c.Line(left - 7, y, left - 7, y + line.Height, 0xA6C5D0, 2, .65f);
            if (line.CodeBlock) c.Fill(new(left - 2, y, Math.Max(1, contentWidth - line.Indent + 2), line.Height), 0x20262D, 2);
            if (line.Image is { } image && line.ImageUrl is { } imageUrl)
            {
                float imageWidth = Math.Min(image.Width, Math.Max(12, contentWidth - line.Indent));
                var imageBounds = new Rect(left, y, imageWidth, line.Height - 6);
                if (!c.Image(image.Path, imageBounds)) c.Text(imageUrl, left, y + 2, 12, 0xA6C5D0, imageWidth);
                var imageHit = DiscussionIntersection(imageBounds, viewport);
                if (imageHit.Height > 0) { discussionHandHits.Add(imageHit); Register(imageHit, () => OpenDiscussionImage(image)); }
            }
            if (line.User is { } userId)
            {
                DrawDiscussionAuthor(c, line, left, y, userId, line.AuthorWidth);
                if (discussionSnapshot!.Users.TryGetValue(userId, out var author))
                {
                    float nameWidth = Math.Min(Math.Max(10, line.AuthorWidth - 37), c.MeasureText(author.Name, 12, true));
                    var nameHit = DiscussionIntersection(new(left + 37, y, nameWidth, 19), viewport);
                    if (DiscussionLinkHovered(nameHit)) c.Line(left + 37, y + 17, left + 37 + nameWidth, y + 17, Foreground);
                    if (nameHit.Height > 0) { discussionHandHits.Add(nameHit); Register(nameHit, () => RequestVisitOsuWeb?.Invoke($"https://osu.ppy.sh/users/{userId}")); }
                }
            }
            if (line.Reply) DiscussionIcon(c, "reply", new(left - 12, y + 8, 9, 9), Muted);
            if (line.Icon is { } symbol) DiscussionIcon(c, symbol, new(left - 17, y + 3, 12, 12), line.Runs.FirstOrDefault()?.Colour ?? Muted);
            if (line.Header is { } headerId)
            {
                var thread = discussionSnapshot!.Discussions.First(d => d.Id == headerId);
                float edge = left + contentWidth - line.Indent;
                if (canvasQuote || thread.ParentId is not null) DrawDiscussionTypeIcons(c, thread, canvasQuote ? left : edge - 112, y + 2);
                if (canvasQuote && contentWidth - line.Indent < 100) continue;
                float x = Math.Max(left + (canvasQuote ? 32 : 0), edge - (canvasQuote ? 60 : 80));
                DiscussionIcon(c, "thumbs-up", new(x, y + 2, 11, 11), Muted);
                c.Text(thread.UpVotes.ToString(CultureInfo.InvariantCulture), x + 14, y + 1, 10, Muted, 18);
                DiscussionIcon(c, "thumbs-down", new(x + 31, y + 2, 11, 11), Muted);
                c.Text(thread.DownVotes.ToString(CultureInfo.InvariantCulture), x + 45, y + 1, 10, Muted, 18);
                if (!canvasQuote)
                {
                    DiscussionIcon(c, discussionCollapsed.Contains(headerId) ? "chevron-down" : "chevron-up", new(edge - 12, y + 2, 11, 11), Muted);
                    var toggleBounds = DiscussionIntersection(new(edge - 16, y, 16, 18), viewport);
                    if (toggleBounds.Height > 0) Register(toggleBounds, () => { if (!discussionCollapsed.Add(headerId)) discussionCollapsed.Remove(headerId); InvalidateDiscussionLayout(); });
                }
            }
            foreach (var run in line.Runs)
            {
                var r = new Rect(left + run.X, y, run.Width, line.Height);
                if (run.Time is not null) c.Fill(r, 0x28413F, 3);
                else if (run.Style.HasFlag(DiscussionTextStyle.Code)) c.Fill(r, 0x20262D, 2);
                if (run.TextStart >= 0)
                {
                    var textBounds = new Rect(r.X + (run.Time is null ? 0 : 3), r.Y, Math.Max(0, run.Width - (run.Time is null ? 0 : 6)), r.Height);
                    var textHit = DiscussionIntersection(textBounds, viewport);
                    if (textHit.Width > 0 && textHit.Height > 0)
                    {
                        Action? click = run.Time is { } targetTime ? () => JumpToDiscussionTime(targetTime, run.Discussion, run.Numbers)
                            : run.Url is { } targetUrl ? () => RequestVisitOsuWeb?.Invoke(targetUrl)
                            : canvasQuote ? () => OpenDiscussion(line.Thread)
                            : line.CardTime is { } anchorTime
                                ? () => JumpToDiscussionTime(anchorTime, line.Thread) : null;
                        discussionTextHits.Add(new(textHit, textBounds, layout, run, c, canvasQuote, click));
                        DrawDiscussionTextSelection(c, layout, run, textBounds);
                    }
                }
                c.StyledText(run.Text, r.X + (run.Time is null ? 0 : 3), r.Y + 2, 12, run.Colour, r.Width + 2,
                    run.Style.HasFlag(DiscussionTextStyle.Bold), run.Style.HasFlag(DiscussionTextStyle.Italic));
                if (run.Style.HasFlag(DiscussionTextStyle.Strike)) c.Line(r.X, r.Y + 10, r.Right, r.Y + 10, run.Colour);
                var clipped = DiscussionIntersection(r, viewport);
                if (run.Url is not null && hoveredLink == run.LinkId) c.Line(r.X, r.Y + 17, r.Right, r.Y + 17, run.Colour);
                if (clipped.Width > 0 && clipped.Height > 0 && (run.Url is not null || run.Time is not null)) discussionHandHits.Add(clipped);
                if (clipped.Height > 0 && run.Time is { } time)
                {
                    if (!canvasQuote) discussionReferenceHits.Add((clipped, time, run.Discussion));
                    Register(clipped, () => JumpToDiscussionTime(time, run.Discussion, run.Numbers));
                }
                if (clipped.Height > 0 && run.Url is { } url) Register(clipped, () => RequestVisitOsuWeb?.Invoke(url));
            }
            if (line.Permalink is { } postId)
            {
                float footerWidth = Math.Max(1, contentWidth - line.Indent);
                string label = L.Get("discussion.permalink"); float labelWidth = c.MeasureText(label, 10);
                c.Text(label, left, y + 2, 10, 0xA6C5D0, Math.Min(footerWidth, labelWidth + 1));
                var link = DiscussionIntersection(new(left, y, Math.Min(footerWidth, labelWidth + 4), line.Height), viewport);
                if (link.Height > 0)
                {
                    discussionHandHits.Add(link);
                    Register(link, () => CopyDiscussionPermalink(postId, line.Thread, canvasQuote));
                    if (DiscussionLinkHovered(link))
                    { c.Line(left, y + 15, left + labelWidth, y + 15, 0xA6C5D0); discussionCopyHoverBounds = link; }
                    if (discussionCopiedLink == (postId, canvasQuote)) discussionCopyFeedbackBounds = link;
                }
                if (line.PostedAt is { } date)
                {
                    string age = DiscussionRelativeAge(date, timeProvider.GetUtcNow());
                    float room = Math.Max(0, footerWidth - labelWidth - 8), size = 9;
                    if (c.MeasureText(age, size) > room) age = DiscussionRelativeAge(date, timeProvider.GetUtcNow(), compact: true);
                    float w = Math.Min(room, c.MeasureText(age, size));
                    if (w > 0) c.Text(age, left + footerWidth - w, y + 3, size, 0x829097, w);
                }
            }
            if (line.Toggle is { } toggle)
            {
                float iconWidth = line.Icon is null ? 0 : 18;
                var clipped = DiscussionIntersection(new(left - iconWidth, y, contentWidth - line.Indent + iconWidth, line.Height), viewport);
                if (clipped.Height > 0) Register(clipped, () => { if (!discussionCollapsed.Add(toggle)) discussionCollapsed.Remove(toggle); InvalidateDiscussionLayout(); });
            }
        }
        void Register(Rect bounds, Action action)
        {
            if (canvasQuote) discussionQuoteActions.Add((bounds, action));
            else hits.Add(new(bounds, action, true));
        }
    }

    internal static string DiscussionRelativeAge(DateTimeOffset date, DateTimeOffset now, bool compact = false)
    {
        double seconds = Math.Max(0, (now - date).TotalSeconds);
        if (seconds < 60) return L.Get("discussion.ageNow");
        var (unit, count) = seconds switch
        {
            < 3600 => ("Minute", (long)(seconds / 60)),
            < 86400 => ("Hour", (long)(seconds / 3600)),
            < 2592000 => ("Day", (long)(seconds / 86400)),
            < 31536000 => ("Month", (long)(seconds / 2592000)),
            _ => ("Year", (long)(seconds / 31536000))
        };
        return L.Get("discussion.age" + unit + (compact ? "Short" : count == 1 ? "One" : "Many"), count);
    }

    private string DiscussionTabLabel(DiscussionCategory category)
    {
        if (discussionTabLabelKey != (L.Language, CurrentDifficultyName))
        {
            foreach (var value in Enum.GetValues<DiscussionCategory>())
                discussionTabLabels[(int)value] = L.Get("discussion.tabCount", DiscussionCategoryTitle(value), discussionRoots[(int)value].Length);
            discussionTabLabelKey = (L.Language, CurrentDifficultyName);
        }
        return discussionTabLabels[(int)category];
    }

    private float DrawDiscussionToolbar(ICanvas c, Rect bounds)
    {
        bool compact = bounds.Width < 390;
        float y = bounds.Y + 32, x = bounds.X + 4;
        Control(discussionCategory == DiscussionCategory.Timeline ? "discussion.sortTimeline" : "discussion.sortCreated", !discussionSortUpdated, () => { discussionSortUpdated = false; InvalidateDiscussionLayout(); });
        Control("discussion.sortUpdated", discussionSortUpdated, () => { discussionSortUpdated = true; InvalidateDiscussionLayout(); });
        if (compact) { y += 26; x = bounds.X + 4; }
        Control("discussion.collapseAll", false, () => Collapse(true), "chevron-up");
        Control("discussion.expandAll", false, () => Collapse(false), "chevron-down");
        return compact ? 54 : 28;
        void Control(string key, bool selected, Action action, string? icon = null)
        {
            string label = L.Get(key); float w = Math.Min(bounds.Width / 2 - 5, c.MeasureText(label, 10) + (icon is null ? 14 : 28));
            Button(c, new(x, y, Math.Max(20, w), 23), label, action, active: selected, fontSize: 10, textPadding: icon is null ? 7 : 21);
            if (icon is not null) DiscussionIcon(c, icon, new(x + 6, y + 7, 9, 9), Muted);
            x += w + 3;
        }
        void Collapse(bool value)
        {
            var children = discussionSnapshot?.Discussions.ToLookup(d => d.ParentId);
            var pending = new Stack<long>(discussionRoots[(int)discussionCategory].Select(d => d.Id));
            var seen = new HashSet<long>();
            while (pending.TryPop(out long id))
            {
                if (!seen.Add(id)) continue;
                if (value) discussionCollapsed.Add(id); else discussionCollapsed.Remove(id);
                if (children is not null) foreach (var child in children[id]) pending.Push(child.Id);
            }
            InvalidateDiscussionLayout();
        }
    }

    private List<(string Label, uint Colour, IReadOnlyList<string> Modes)> DiscussionBadges(long userId, long? beatmap)
    {
        var labels = new List<(string Label, uint Colour, IReadOnlyList<string> Modes)>(); var snapshot = discussionSnapshot!;
        if (snapshot.IsMapper(userId, beatmap)) labels.Add(("MAPPER", 0xB9DBE4, []));
        if (snapshot.Users.TryGetValue(userId, out var user)) foreach (var group in user.Groups)
            if (group.Label.Length > 0) labels.Add((group.Label, group.Colour, group.Modes));
        return labels;
    }

    private void DrawDiscussionAuthor(ICanvas c, DiscussionLine line, float x, float y, long userId, float available)
    {
        var snapshot = discussionSnapshot!; snapshot.Users.TryGetValue(userId, out var user);
        var avatar = new Rect(x, y + 1, 30, 30); c.Fill(avatar, 0x4E405F, 4);
        if (discussionAvatars.TryGetValue(userId, out string? path)) c.Thumbnail(path, avatar);
        else c.Text(user?.Name.Length > 0 ? StringInfo.GetNextTextElement(user.Name) : "?", x + 8, y + 5, 14, Foreground, 20);
        c.Text(user?.Name ?? L.Get("discussion.unknownUser", userId), x + 37, y + 1, 12, Foreground, Math.Max(10, available - 37), true);
        float badgeX = x + 37, badgeY = y + 20;
        foreach (var label in DiscussionBadges(userId, line.OwnerBeatmap))
        {
            float w = Math.Min(Math.Max(20, available - 37), DiscussionBadgeWidth(c, label.Label, label.Modes));
            if (badgeX + w > x + available && badgeX > x + 37) { badgeX = x + 37; badgeY += 18; }
            c.Fill(new(badgeX, badgeY, w, 15), Background, 7);
            float labelWidth = c.MeasureText(label.Label, 9, true);
            c.Text(label.Label, badgeX + 5, badgeY + 1, 9, label.Colour, Math.Max(1, w - 10 - label.Modes.Count * 14), true);
            float iconX = badgeX + 8 + labelWidth;
            foreach (string mode in label.Modes)
            {
                if (iconX + 12 <= badgeX + w - 2) DrawDiscussionModeIcon(c, mode, new(iconX, badgeY + 1, 12, 12));
                iconX += 14;
            }
            badgeX += w + 4;
        }
    }

    private static float DiscussionBadgeWidth(ICanvas c, string label, IReadOnlyList<string> modes)
        => c.MeasureText(label, 9, true) + 10 + (modes.Count > 0 ? 3 + modes.Count * 14 : 0);

    private static void DrawDiscussionModeIcon(ICanvas c, string mode, Rect bounds)
    {
        if (mode == "fruits" && c.Image(catchIconPath, bounds, 0xFFFFFF)) return;
        float x = bounds.X + bounds.Width / 2, y = bounds.Y + bounds.Height / 2, r = bounds.Width * .45f;
        c.Circle(x, y, r, 0xFFFFFF, false, 1);
        switch (mode)
        {
            case "fruits":
                c.Circle(x - r * .15f, y - r * .33f, r * .16f, 0xFFFFFF);
                c.Circle(x + r * .25f, y, r * .16f, 0xFFFFFF);
                c.Circle(x - r * .15f, y + r * .33f, r * .16f, 0xFFFFFF); break;
            case "mania":
                for (int i = -1; i <= 1; i++) c.Line(x + i * r * .34f, y - r * (i == 0 ? .6f : .25f),
                    x + i * r * .34f, y + r * (i == 0 ? .6f : .25f), 0xFFFFFF, 1.2f);
                break;
            case "taiko":
                c.Circle(x, y, r * .55f, 0xFFFFFF, false, 1); c.Line(x, y - r * .55f, x, y + r * .55f, 0xFFFFFF); break;
            default: c.Circle(x, y, r * .42f, 0xFFFFFF); break;
        }
    }

    private void BuildDiscussionLayout(ICanvas c, float available, BeatmapDiscussion[]? selectedThreads = null,
        DiscussionLayout? target = null)
    {
        if (target is null) DiscussionLayoutBuilds++;
        var layout = target ?? discussionPanelLayout;
        if (ReferenceEquals(discussionSelectedLayout, layout)) ClearDiscussionTextSelection();
        var selectionText = new System.Text.StringBuilder();
        var lines = layout.Lines; var cards = layout.Cards; var postBlocks = layout.PostBlocks;
        lines.Clear(); cards.Clear(); postBlocks.Clear(); float y = 8;
        var snapshot = discussionSnapshot!; var threads = selectedThreads ?? discussionRoots[(int)discussionCategory];
        if (target is null && discussionSortUpdated) threads = threads.OrderByDescending(d => d.Posts.Select(p => p.CreatedAt).Append(d.CreatedAt).Append(d.UpdatedAt).Append(d.LastPostAt).Max()).ToArray();
        var children = snapshot.Discussions.Where(d => d.ParentId is not null).ToLookup(d => d.ParentId!.Value);
        var byId = snapshot.Discussions.ToDictionary(d => d.Id);
        if (threads.Length == 0) TextLine(L.Get(discussionCategory is DiscussionCategory.General or DiscussionCategory.Timeline && discussionBeatmapId <= 0 ? "discussion.noOnlineDifficulty" : "discussion.empty"), 0, 0, Muted);
        var visited = new HashSet<long>();
        foreach (var thread in threads) Thread(thread, 0);
        lines.Sort((a, b) => a.Top.CompareTo(b.Top));
        float end = 0; layout.LineEnds = lines.Select(line => end = Math.Max(end, line.Top + line.Height)).ToArray();
        postBlocks.Sort((a, b) => a.Top.CompareTo(b.Top));
        layout.ContentHeight = y + 8;
        layout.SelectionText = selectionText.ToString();

        void Thread(BeatmapDiscussion thread, int depth)
        {
            if (!visited.Add(thread.Id)) return;
            float top = y; y += 8; float indent = Math.Min(depth, 3) * 10;
            if (target is not null) { lines.Add(new(y, 18, indent, [], Header: thread.Id)); y += 21; }
            int postIndex = 0;
            int replyCount = Math.Max(0, thread.Posts.Count(p => !p.System) - 1);
            foreach (var post in thread.Posts)
            {
                if (post.System)
                {
                    bool? resolved = DiscussionText.ResolvedSystemMessage(post);
                    string user = snapshot.Users.TryGetValue(post.UserId, out var author) ? author.Name : L.Get("discussion.unknownUser", post.UserId);
                    string userLink = DiscussionProfileLink(post.UserId, user);
                    TextLine(resolved is { } state ? L.Get(state ? "discussion.resolvedBy" : "discussion.reopenedBy", userLink)
                        : L.Get("discussion.systemPost", post.Message), indent + 6, thread.Id,
                        resolved == true ? DiscussionResolvedColour : DiscussionPendingColour,
                        resolved == true ? "check-circle" : resolved == false ? "clock" : "sticky-note");
                    continue;
                }
                bool reply = postIndex++ > 0;
                if (reply && target is null && discussionCollapsed.Contains(thread.Id)) continue;
                float postIndent = indent + (reply ? 12 : 0), postTop = y;
                bool columns = available - postIndent >= 390;
                float authorWidth = columns ? Math.Clamp((available - postIndent) * .34f, 130, 175) : available - postIndent;
                var badges = DiscussionBadges(post.UserId, thread.BeatmapId);
                float badgeX = 37, authorHeight = 40;
                foreach (var badge in badges)
                {
                    float w = Math.Min(Math.Max(20, authorWidth - 37), DiscussionBadgeWidth(c, badge.Label, badge.Modes));
                    if (badgeX + w > authorWidth && badgeX > 37) { authorHeight += 18; badgeX = 37; }
                    badgeX += w + 4;
                }
                lines.Add(new(y, authorHeight, postIndent, [], post.UserId, thread.BeatmapId, AuthorWidth: authorWidth, Reply: reply));
                if (!columns) y += authorHeight + 3;
                float bodyIndent = postIndent + (columns ? authorWidth + 14 : 6);
                float firstLineReserve = 0;
                if (!reply && target is null)
                {
                    float actions = thread.ParentId is null ? 80 : 112;
                    lines.Add(new(y, 18, bodyIndent, [], Header: thread.Id));
                    if (available - bodyIndent > actions + 65) firstLineReserve = actions + 25;
                    else y += 21;
                }
                if (thread.MessageType == "review" && !reply)
                {
                    foreach (var block in DiscussionText.ReviewBlocks(post.Message))
                        if (block.DiscussionId is { } childId && byId.TryGetValue(childId, out var child) && child.ParentId == thread.Id) Thread(child, depth + 1);
                        else { TextLine(block.Text, bodyIndent, thread.Id, Foreground, firstLineReserve: firstLineReserve); firstLineReserve = 0; }
                }
                else TextLine(post.Message, bodyIndent, thread.Id, Foreground, firstLineReserve: firstLineReserve);
                lines.Add(new(y + 4, 18, bodyIndent, [], Permalink: post.Id, Thread: thread.Id, PostedAt: post.CreatedAt)); y += 22;
                y = Math.Max(y, postTop + authorHeight) + 12;
                // Review embeds own their reply backgrounds and dividers.
                if (thread.MessageType != "review") postBlocks.Add(new(postTop, y, postIndent,
                    bodyIndent - 4, snapshot.IsMapper(post.UserId, thread.BeatmapId) ? 0xB9DBE4 : Grid, reply));
                if (!reply && replyCount > 0 && target is null)
                {
                    string label = L.Get("discussion.replyCount", replyCount);
                    lines.Add(new(y, 25, indent + 20, [new(0, available - indent - 20, label, Muted)], Toggle: thread.Id,
                        Icon: discussionCollapsed.Contains(thread.Id) ? "chevron-down" : "chevron-up")); y += 25;
                }
            }
            if (thread.CanResolve && !thread.Posts.Any(p => DiscussionText.ResolvedSystemMessage(p) == thread.Resolved)) TextLine(L.Get(thread.Resolved ? "discussion.resolved" : "discussion.pending"), indent + 6, thread.Id,
                thread.Resolved ? DiscussionResolvedColour : DiscussionPendingColour, thread.Resolved ? "check-circle" : "clock");
            foreach (var child in children[thread.Id]) Thread(child, depth + 1);
            y += 12;
            if (depth == 0) cards.Add((top, y, thread.Id));
        }

        void TextLine(string text, float indent, long owner, uint colour, string? symbol = null, float firstLineReserve = 0)
        {
            bool decorated = false;
            if (symbol is not null) indent += 18;
            float fullWidth = Math.Max(12, available - indent), width = Math.Max(12, available - indent - firstLineReserve), x = 0;
            var runs = new List<DiscussionRun>();
            DiscussionTextStyle style = DiscussionTextStyle.None;
            int textPosition = selectionText.Length;
            bool quoted = false, codeBlock = false;
            float baseIndent = indent;
            foreach (var paragraph in DiscussionMarkdown.Parse(text))
            {
                indent = baseIndent + paragraph.QuoteDepth * 12;
                quoted = paragraph.QuoteDepth > 0; codeBlock = paragraph.Code;
                fullWidth = Math.Max(12, available - indent);
                width = Math.Max(12, fullWidth - (decorated ? 0 : firstLineReserve));
                int firstParagraphLine = lines.Count;
                foreach (var span in paragraph.Spans)
                {
                    textPosition = selectionText.Length; selectionText.Append(span.Image ? span.Url : span.Text);
                    style = span.Style;
                    if (span.Image && span.Url is { } imageUrl)
                    {
                        if (runs.Count > 0 || width < fullWidth) Flush();
                        if (discussionImages.GetValueOrDefault(imageUrl) is { } image)
                        {
                            float imageWidth = Math.Min(fullWidth, image.Width);
                            float height = imageWidth * image.Height / image.Width;
                            lines.Add(new(y, height + 6, indent, [], Image: image, ImageUrl: imageUrl, Quoted: quoted));
                            y += height + 6; decorated = true;
                        }
                        else Link(imageUrl, null, imageUrl);
                        continue;
                    }
                    if (span.Url is { } url) { Link(span.Text, null, url); continue; }
                    if (style.HasFlag(DiscussionTextStyle.Code)) { Plain(span.Text); continue; }
                    var references = DiscussionText.References(span.Text).Select(r => (r.Start, r.Length, Time: (double?)r.TimeMs, Url: (string?)null, Numbers: (IReadOnlyList<int>?)r.Numbers))
                        .Concat(DiscussionText.Urls(span.Text).Select(r => (r.Start, r.Length, Time: (double?)null, Url: (string?)r.Url, Numbers: (IReadOnlyList<int>?)null))).OrderBy(r => r.Start);
                    int position = 0;
                    foreach (var reference in references)
                    {
                        Plain(span.Text[position..reference.Start]);
                        Link(span.Text.Substring(reference.Start, reference.Length), reference.Time, reference.Url, reference.Numbers);
                        position = reference.Start + reference.Length;
                    }
                    Plain(span.Text[position..]);
                }
                if (runs.Count > 0 || lines.Count == firstParagraphLine) Flush();
                selectionText.Append('\n');
            }
            selectionText.Append('\n');

            float Measure(string value) => c.MeasureStyledText(value, 12, style.HasFlag(DiscussionTextStyle.Bold), style.HasFlag(DiscussionTextStyle.Italic));
            void Flush()
            { lines.Add(new(y, 20, indent, runs.ToArray(), Thread: owner, Icon: decorated ? null : symbol, Quoted: quoted, CodeBlock: codeBlock, CardTime: byId.GetValueOrDefault(owner)?.Timestamp)); decorated = true; runs.Clear(); x = 0; width = fullWidth; y += 20; }
            void Plain(string value)
            {
                foreach (System.Text.RegularExpressions.Match token in System.Text.RegularExpressions.Regex.Matches(value.Replace("\r", ""), @"\n|[^\S\n]+|[^\s]+"))
                {
                    string word = token.Value;
                    int wordStart = textPosition; textPosition += word.Length;
                    if (word == "\n") { Flush(); continue; }
                    bool space = char.IsWhiteSpace(word[0]);
                    float w = Measure(word);
                    if (w > width && w <= fullWidth && x > 0) Flush();
                    if (w <= width)
                    {
                        if (x + w > width && x > 0) Flush();
                        if (!space || x > 0 || style.HasFlag(DiscussionTextStyle.Code)) { runs.Add(new(x, w, word, colour, Style: style, TextStart: wordStart)); x += w; }
                        continue;
                    }
                    // CJK paragraphs and long unbroken tokens wrap at grapheme boundaries.
                    string chunk = ""; float chunkWidth = 0; int chunkStart = wordStart;
                    foreach (int offset in StringInfo.ParseCombiningCharacters(word))
                    {
                        string glyph = StringInfo.GetNextTextElement(word, offset); float g = Measure(glyph);
                        if (x + chunkWidth + g > width && x + chunkWidth > 0) { Commit(); Flush(); }
                        if (chunk.Length == 0) chunkStart = wordStart + offset;
                        chunk += glyph; chunkWidth += g;
                    }
                    Commit();
                    void Commit() { if (chunk.Length == 0) return; runs.Add(new(x, chunkWidth, chunk, colour, Style: style, TextStart: chunkStart)); x += chunkWidth; chunk = ""; chunkWidth = 0; }
                }
            }
            void Link(string value, double? time, string? url, IReadOnlyList<int>? numbers = null)
            {
                int linkStart = textPosition; textPosition += value.Length;
                float padding = time is null ? 0 : 6;
                uint linkColour = url is null ? Accent : symbol is not null ? colour : 0xA6C5D0;
                float w = Measure(value) + padding;
                if (x + w > width && x > 0) Flush();
                if (w <= width) { runs.Add(new(x, w, value, linkColour, time, owner, url, style, numbers, linkStart, linkStart)); x += w + (time is null ? 0 : 3); return; }
                // Wrapped link segments retain the complete navigation target.
                string chunk = ""; float size = padding; int chunkStart = linkStart;
                foreach (int offset in StringInfo.ParseCombiningCharacters(value))
                {
                    string glyph = StringInfo.GetNextTextElement(value, offset); float g = Measure(glyph);
                    if (size + g > width && chunk.Length > 0) { runs.Add(new(0, size, chunk, linkColour, time, owner, url, style, numbers, chunkStart, linkStart)); Flush(); chunk = ""; size = padding; }
                    if (chunk.Length == 0) chunkStart = linkStart + offset;
                    chunk += glyph; size += g;
                }
                if (chunk.Length > 0) { runs.Add(new(0, size, chunk, linkColour, time, owner, url, style, numbers, chunkStart, linkStart)); x = size; }
            }
        }
    }

    private static int DiscussionFirstVisible(float[] ends, float scroll)
    {
        int lo = 0, hi = ends.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (ends[mid] <= scroll) lo = mid + 1; else hi = mid; }
        return lo;
    }
    private static Rect DiscussionIntersection(Rect a, Rect b)
        => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y)));

    private bool WheelDiscussions(float x, float y, float delta)
    {
        if (!DockContentInputAvailable) return false;
        foreach (var quote in discussionQuoteViews)
            if (DiscussionAnnotationsVisible && quote.Maximum > 0 && quote.Bounds.Contains(x, y))
            {
                discussionQuoteScrolls[quote.Id] = Math.Clamp(discussionQuoteScrolls.GetValueOrDefault(quote.Id) - delta / 120 * 60, 0, quote.Maximum);
                return true;
            }
        if (!PanelVisible(DockPanel.Discussion)) return false;
        if (discussionTabsBounds.Contains(x, y)) { discussionTabScroll = Math.Clamp(discussionTabScroll - delta / 120 * 48, 0, discussionTabMaximum); return true; }
        if (!discussionListBounds.Contains(x, y)) return false;
        discussionScroll = Math.Clamp(discussionScroll - delta / 120 * 60, 0, Math.Max(0, discussionContentHeight - discussionListBounds.Height)); return true;
    }

    private bool DiscussionPointerDown(float x, float y, int button)
    {
        if (button != 0 || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0) return false;
        if (BeginDiscussionTextSelection(x, y)) return true;
        if (DiscussionQuotePointerDown(x, y)) return true;
        foreach (var marker in discussionMarkerHits)
            if (marker.Bounds.Contains(x, y)) { OpenDiscussion(marker.Discussion); return true; }
        if (!PanelVisible(DockPanel.Discussion) || !discussionScrollbarTrack.Contains(x, y) || discussionScrollbarThumb.Height <= 0) return false;
        discussionScrollbarOffset = discussionScrollbarThumb.Contains(x, y) ? y - discussionScrollbarThumb.Y : discussionScrollbarThumb.Height / 2;
        discussionScrollbarDragging = true; MoveDiscussionScrollbar(y); return true;
    }
    private void MoveDiscussionScrollbar(float y)
    {
        float travel = discussionScrollbarTrack.Height - discussionScrollbarThumb.Height;
        if (travel > 0) discussionScroll = Math.Clamp((y - discussionScrollbarTrack.Y - discussionScrollbarOffset) / travel, 0, 1)
            * Math.Max(0, discussionContentHeight - discussionListBounds.Height);
    }

    private void JumpToDiscussionTime(double time, long id, IReadOnlyList<int>? numbers = null)
    {
        if (history.HasActiveTransaction || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        var thread = discussionSnapshot?.Discussions.FirstOrDefault(d => d.Id == id);
        if (thread?.BeatmapId is { } target && target != discussionBeatmapId)
        {
            int index = difficulties.FindIndex(d => DiscussionOnlineId(d.History.Document, "BeatmapID") == target);
            if (index < 0) { SetNotice(L.Get("discussion.openDifficulty")); return; }
            if (!SwitchDifficulty(index)) return;
            PumpDiscussions();
        }
        discussionFocused = thread?.ParentId ?? id; SeekTo(time);
        if (numbers is { Count: > 0 }) SelectDiscussionObjects(time, numbers);
    }

    private void SelectDiscussionObjects(double time, IReadOnlyList<int> numbers)
    {
        EnsureConversion(); RefreshTimelineSources();
        int first = -1;
        for (int i = 0; i < timelineMarkers.Length && timelineMarkers[i].Start <= time + 1; i++)
            if (Math.Abs(timelineMarkers[i].Start - time) <= 1
                && timelineEventNumbers.GetValueOrDefault((timelineMarkers[i].Id, timelineMarkers[i].EventIndex)) == numbers[0])
            { if (first >= 0) return; first = i; }
        if (first < 0) return;
        var ids = new List<Guid> { timelineMarkers[first].Id }; int next = 1;
        for (int i = first + 1; i < timelineMarkers.Length && next < numbers.Count; i++)
            if (timelineEventNumbers.GetValueOrDefault((timelineMarkers[i].Id, timelineMarkers[i].EventIndex)) == numbers[next])
            { ids.Add(timelineMarkers[i].Id); next++; }
        if (next != numbers.Count) return;
        tool = Tool.Select; SelectObjects(ids);
        StatusMessage = L.Get("editor.status.objectsSelected", objectSelection.Count);
    }

    private void OpenDiscussion(long id)
    {
        if (discussionSnapshot is not { } snapshot) return;
        var thread = snapshot.Discussions.FirstOrDefault(d => d.Id == id);
        if (thread is null) return;
        if (thread.ParentId is { } parent) { id = parent; discussionCategory = DiscussionCategory.Reviews; }
        else discussionCategory = thread.Timestamp is not null ? DiscussionCategory.Timeline : DiscussionCategory.General;
        discussionCollapsed.Remove(id); discussionFocused = discussionRevealThread = id;
        InvalidateDiscussionLayout(); discussionRevealTab = true; ShowDiscussionPanel();
    }

    private void DrawDiscussionOverview(ICanvas c)
    {
        if (!DiscussionAnnotationsVisible || PatternEditingActive || discussionAnchors.Length == 0 || TimelineDurationMs <= 0) return;
        c.Clip(overview); float previous = float.NegativeInfinity;
        foreach (var anchor in discussionAnchors)
        {
            float x = overview.X + (float)(anchor.Time / TimelineDurationMs) * overview.Width;
            if (x < overview.X || x > overview.Right || x - previous < 8) continue;
            c.Circle(x, overview.Y + 4, 2.5f, Accent);
            discussionMarkerHits.Add((new(x - 4, overview.Y, 8, 9), anchor.Discussion)); previous = x;
        }
        c.Unclip();
    }
}
