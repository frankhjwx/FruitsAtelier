using System.Net;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DiscussionTests
{
    private static readonly string Fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "discussions.json"));
    public static void Run()
    {
        var snapshot = OsuDiscussionClient.Parse(Fixture, 777);
        Check(snapshot.Discussions.Count == 7 && snapshot.Discussions.Single(d => d.Id == 3).Posts.Count == 4, "complete threads and system posts");
        Check(DiscussionText.ResolvedSystemMessage(snapshot.Discussions.Single(d => d.Id == 3).Posts[^1]) == true, "object system messages");
        Check(snapshot.For(DiscussionCategory.Reviews, 101).Length == 1 && snapshot.For(DiscussionCategory.GeneralAll, 102).Length == 1, "shared categories");
        Check(snapshot.For(DiscussionCategory.General, 101).Length == 1 && snapshot.For(DiscussionCategory.General, 102).Length == 0, "difficulty general scope");
        Check(snapshot.For(DiscussionCategory.Timeline, 101).Select(d => d.Id).SequenceEqual(new long[] { 3, 7 })
            && snapshot.For(DiscussionCategory.Timeline, 102).Single().Id == 4, "difficulty timeline excludes review embeds");
        var review = DiscussionText.ReviewBlocks(snapshot.Discussions.Single(d => d.Id == 5).Posts[0].Message);
        Check(review.Length == 3 && review[1].DiscussionId == 6 && review[2].Text.Contains("ending"), "review order and embedded discussion");
        Check(snapshot.Users[30].Groups.Count == 2 && snapshot.Users[10].Groups[0].Modes.Single() == "fruits"
            && snapshot.IsMapper(20, 101) && snapshot.IsMapper(30, 102), "all team badges and difficulty owners");
        var refs = DiscussionText.References("00:01:500 (2,3) and 02:03.456 (8,9,10), 00:99:000");
        Check(refs.Length == 2 && refs[1].TimeMs == 123456 && refs[1].Numbers.SequenceEqual(new[] { 8, 9, 10 }), "timestamp variants and object ranges");
        var urls = DiscussionText.Urls("see (https://osu.ppy.sh/beatmapsets/2518389#taiko/5556878), https://example.org/a_(b). http://example.org/?t=00:01:500!");
        Check(urls.Length == 3 && urls[0].Url.EndsWith("#taiko/5556878") && urls[1].Url.EndsWith("a_(b)")
            && urls[2].Url == "http://example.org/?t=00:01:500", "URLs preserve fragments and balanced parentheses while excluding sentence punctuation");
        Check(DiscussionText.References("https://example.org/?t=00:01:500 00:02:000").Single().TimeMs == 2000
            && DiscussionText.Urls("javascript:alert(1) https://").Length == 0, "URLs take priority over embedded timestamps and require a valid HTTP host");
        Reject(() => OsuDiscussionClient.Parse(Fixture, 778));
        Reject(() => OsuDiscussionClient.Parse("{\"id\":777,\"discussions\":[{\"id\":1}]}", 777));
        var handler = new SnapshotHandler(Fixture);
        using (var http = new HttpClient(handler))
            Check(new OsuDiscussionClient(http).LoadAsync(777).GetAwaiter().GetResult().Discussions.Count == 7 && handler.AnonymousJson, "anonymous public JSON transport");

        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale); UiChecks(snapshot);
            }
            BrowserTab(snapshot);
            TypeCardLayout(snapshot);
            ResolutionProfileLinks(snapshot);
            ObjectReferenceSelection(snapshot);
            TextSelection(snapshot);
            UrlNavigation(snapshot);
            DiscussionMarkdownTests.Run(snapshot);
            LongAnnotationAtZero(snapshot);
            StaleRequest(snapshot);
            DiscussionConnectivityTests.Run(Fixture, snapshot);
        }
        finally { L.SetLanguage(language); }
        Console.WriteLine("PASS Discussion scopes, replies, reviews, timestamps, annotation ranges, overflow and request lifetime");
    }

    private static void BrowserTab(OsuDiscussionSnapshot snapshot)
    {
        var ui = new Ui(false); ui.Resize(1440, 900);
        ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        ui.View.LoadProject(Project());
        var layout = ui.View.CaptureDockLayouts();
        layout.Mapping!.Right!.Active = "OsuWeb"; layout.Mapping.Right.Collapsed = false;
        ui.View.LibrarySettings.DockLayouts = layout; ui.View.RestoreDockLayouts(); Wait(ui);
        var original = ui.View.Document.DeepClone();
        string page = "https://osu.ppy.sh/beatmapsets/777/discussion/101/timeline";
        Check(ui.View.BrowserUrl == page && ui.View.BrowserViewport.Width > 0, "web tab opens current difficulty discussions");
        ui.View.UpdateBrowserPage(page); ui.Paint();
        Check(ui.View.DiscussionQuoteBounds.Count > 0, "matching web discussion shows native note annotations");
        Check(ui.View.HandleBrowserEditorLink("osu://edit/00:01:000%20(1)", page)
            && ui.View.PlayheadMs == 1000 && ui.View.SelectedObjectIds.Contains(ui.View.Document.Fruits[0].Id), "web timestamp seeks and selects its note");
        foreach (string other in new[] { "https://osu.ppy.sh/beatmapsets/777#fruits/101", "https://osu.ppy.sh/beatmapsets/777/discussion/102/timeline", "https://osu.ppy.sh/beatmapsets/778/discussion/101/timeline", "https://example.org/beatmapsets/777/discussion/101/timeline" })
        {
            ui.View.UpdateBrowserPage(other); ui.Paint();
            Check(ui.View.DiscussionQuoteBounds.Count == 0, "unrelated web pages hide note annotation cards");
        }
        Check(!ui.View.HandleBrowserEditorLink("osu://edit/00:01:500", "https://example.org/beatmapsets/777/discussion/101/timeline"), "external pages cannot drive editor navigation");
        var commands = new List<BrowserCommand>(); string? copied = null;
        ui.View.RequestBrowserCommand = commands.Add; ui.View.RequestCopyText = text => copied = text;
        ui.View.UpdateBrowserState(new(page, true, true)); ui.Paint();
        var address = ui.View.BrowserAddressBounds;
        double zoom = ui.View.PixelsPerMs, position = ui.View.PlayheadMs;
        foreach (float x in new[] { address.X - 77, address.X - 47, address.X - 17, address.Right + 17 })
        {
            ui.Click(x, address.Y + 14);
            ui.View.PointerDoubleClick(x, address.Y + 14, false, false);
            ui.View.PointerDown(x, address.Y + 14, 2, false, false); ui.View.PointerUp(x, address.Y + 14, 2);
            ui.View.Wheel(x, address.Y + 14, 120, true);
            Check(!ui.View.WantsCapture, "toolbar never starts a canvas or browser drag");
        }
        Check(commands.SequenceEqual(new[] { BrowserCommand.Back, BrowserCommand.Forward, BrowserCommand.Refresh, BrowserCommand.External }), "toolbar dispatches each command once without click-through");
        ui.Click(address.X + 4, address.Y + 12);
        Check(copied == page && ui.View.BrowserCopyOpacity == 1, "address copies the full URL with green feedback");
        Check(ui.View.PixelsPerMs == zoom && ui.View.PlayheadMs == position, "toolbar wheel and double click leave canvas navigation unchanged");
        ui.View.OpenSettings(); ui.Paint();
        Check(ui.View.BrowserViewport.Width == 0, "settings and its full-screen dimmer cover the browser");
        ui.Key(27); ui.Paint();
        Check(ui.View.Document.ContentEquals(original) && !ui.View.IsDirty, "browser navigation does not modify content");
        var otherTab = ui.View.CaptureDockLayouts();
        otherTab.Mapping!.Right!.Active = "CollaborationMapping";
        otherTab.Mapping.Right.Panels = [.. otherTab.Mapping.Right.Panels!, "CollaborationMapping"];
        ui.View.LibrarySettings.DockLayouts = otherTab; ui.View.RestoreDockLayouts(); ui.Paint();
        Check(ui.View.BrowserViewport.Width == 0 && ui.View.DiscussionQuoteBounds.Count == 0 && ui.View.DiscussionMarkerHits.Count == 0
            && !ui.Canvas.Fills.Any(f => f.Color is 0xA6D9AE or 0xE6AA64 && Math.Abs(f.Bounds.Width - ui.View.PlayfieldBounds.Width) < .1),
            "switching to another tab clears web viewport, quotes and highlight bands in the next frame");
        ui.View.ShowError("fixture"); ui.Paint();
        Check(ui.View.BrowserViewport.Width == 0, "native browser yields to editor modal overlays");
        ui.View.StopDiscussions();

        var praise = new OsuDiscussionSnapshot(777, 20, [new BeatmapDiscussion(900, 101, null, 10, 1000, "praise", false, false, null, [new(901, 10, "00:01:000 (1)", false, null)])], snapshot.Users, snapshot.Owners);
        var positive = new Ui(false); positive.View.DiscussionLoader = (_, _) => Task.FromResult(praise);
        positive.View.LoadProject(Project()); positive.View.ShowDiscussionPanel(); Wait(positive);
        Check(positive.Canvas.Fills.Any(f => f.Color == 0xA6D9AE && Math.Abs(f.Bounds.Width - positive.View.PlayfieldBounds.Width) < .1)
            && !positive.Canvas.Fills.Any(f => f.Color == 0xE6AA64), "praise uses green annotation bands and borders without a resolved flag");
        positive.View.StopDiscussions();
    }

    private static void UiChecks(OsuDiscussionSnapshot snapshot)
    {
        var ui = new Ui(false); int loads = 0;
        ui.View.DiscussionLoader = (_, _) => { Interlocked.Increment(ref loads); return Task.FromResult(snapshot); };
        ui.View.LoadProject(Project()); ui.View.ShowDiscussionPanel(); Wait(ui);
        var plot = ui.View.CanvasPlotBounds;
        ui.View.Wheel(plot.X, plot.Bottom, (float)(120 * Math.Log(.09 / ui.View.PixelsPerMs) / Math.Log(1.16)), false, false, true); ui.Paint();
        var before = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        Check(!ui.View.DiscussionSortByUpdate, "Timeline defaults to chronological ordering");
        var now = DateTimeOffset.UtcNow;
        Check(EditorView.DiscussionRelativeAge(now.AddMinutes(-5), now) == L.Get("discussion.ageMinuteMany", 5), "relative posting minutes are localized");
        Check(EditorView.DiscussionRelativeAge(now.AddHours(-2), now) == L.Get("discussion.ageHourMany", 2), "relative posting hours");
        Check(EditorView.DiscussionRelativeAge(now.AddDays(-21), now) == L.Get("discussion.ageDayMany", 21), "relative posting days");
        Check(EditorView.DiscussionRelativeAge(now.AddSeconds(10), now) == L.Get("discussion.ageNow"), "future clock skew is shown as just now");
        var footer = ui.Canvas.Texts.Where(t => t.X > ui.View.DiscussionBounds.X && t.Value == L.Get("discussion.permalink")).First();
        Check(ui.Canvas.Texts.Any(t => t.X > footer.X && Math.Abs(t.Y - footer.Y) < 3 && t.Size < 10 && t.Color == 0x829097), "permalink and muted right-aligned posting age share one footer row");
        foreach (string type in new[] { "hype", "mapper_note", "praise", "problem", "issue", "review", "suggestion" })
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "discussion", EditorView.DiscussionTypeIcon(type).Icon + ".png")), "every discussion type icon is distributed");
        Check(ui.View.DiscussionMarkerHits.Count > 0 && ui.View.SelectedObjectIds.Count == 0, "markers appear without selection");
        Check(ui.Canvas.Images.Any(i => i.Path.EndsWith("RulesetCatch.png") && i.Bounds.X > ui.View.DiscussionBounds.X), "BN uses the official Catch mode icon");
        Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("BN fruits")), "mode identifier is an icon rather than text");
        var firstCard = ui.View.DiscussionThreadBounds(3);
        var thumb = ui.Canvas.Images.First(i => i.Path.EndsWith("thumbs-up.png") && i.Bounds.X > ui.View.DiscussionBounds.X && i.Bounds.Y >= firstCard.Y && i.Bounds.Y < firstCard.Bottom);
        var firstRow = ui.Canvas.Texts.Where(t => t.Size == 12 && !t.Bold && t.X > ui.View.DiscussionBounds.X && Math.Abs(t.Y - thumb.Bounds.Y) < .1).ToArray();
        Check(firstRow.Length > 0 && firstRow.All(t => t.X + ((ICanvas)ui.Canvas).MeasureText(t.Value, t.Size) <= thumb.Bounds.X - 24), "first body line leaves 25px before action icons without adding a header row");
        Check(ui.Canvas.Texts.Any(t => t.Size == 12 && t.X > ui.View.DiscussionBounds.X && t.Y > thumb.Bounds.Y && t.Y < thumb.Bounds.Y + 23 && t.X + ((ICanvas)ui.Canvas).MeasureText(t.Value, t.Size) > thumb.Bounds.X), "the second body line uses the full text width");
        int builds = ui.View.DiscussionLayoutBuilds, quoteBuilds = ui.View.DiscussionQuoteLayoutBuilds;
        for (int i = 0; i < 8; i++) ui.Paint();
        Check(ui.View.DiscussionLayoutBuilds == builds && ui.View.DiscussionQuoteLayoutBuilds == quoteBuilds && ReferenceEquals(conversion, ui.View.Conversion), "warm paint reuses layout and conversion");
        var multi = ui.View.DiscussionHighlightRanges.First(r => r.Discussion == 3 && r.Start == 1000);
        Check(multi.End == 1500, "all referenced notes form the highlighted interval");
        Check(ui.View.DiscussionHighlightRanges.Any(r => r.Start == 4000 && r.End == 5000), "slider reference includes the complete converted duration");
        float diameter = (float)(2 * CatchSize.FruitRadius(ui.View.Document.CircleSize) * ui.View.PlayfieldBounds.Width / 512);
        var bands = ui.Canvas.Fills.Where(f => f.Color is 0xA6D9AE or 0xE6AA64 && Math.Abs(f.Bounds.Width - ui.View.PlayfieldBounds.Width) < .1).ToArray();
        Check(bands.Length > 0 && bands.All(f => Math.Abs(f.Bounds.X - ui.View.PlayfieldBounds.X) < .01), "highlight spans X 0 through 512");
        Check(bands.Any(f => Math.Abs(f.Bounds.Height - diameter) < .1), "single reference band is a full circle diameter");
        foreach (var quote in ui.View.DiscussionQuoteBounds)
            Check(quote.X >= ui.View.PlayfieldBounds.Right + 7 && quote.Right < ui.View.AssistButtonBounds[0].X, "quotes sit at 512 before tool icons");
        Check(ui.View.DiscussionQuoteBounds.Count > 0, "expanded canvas quotes contain discussion text");
        var card = ui.View.DiscussionThreadBounds(3);
        ui.Click(card.Right - 4, card.Y + 5);
        Check(ui.View.PlayheadMs == 1000 && ui.View.SelectedObjectIds.Count == 0, "whole card seeks its timestamp without selecting a note");
        Check(!ui.Canvas.Texts.Any(t => t.X > ui.View.DiscussionBounds.X && t.Value == "00:01:000"), "standalone card header timestamp is omitted");
        var times = new HashSet<double>();
        for (int i = 0; i < 28; i++)
        {
            foreach (var link in ui.View.DiscussionReferenceHits.ToArray())
            {
                times.Add(link.Time); ui.Click(link.Bounds.X + link.Bounds.Width / 2, link.Bounds.Y + link.Bounds.Height / 2);
                Check(Math.Abs(ui.View.PlayheadMs - link.Time) < .01, "post and reply timestamps seek: " + link.Time + " / " + ui.View.PlayheadMs);
            }
            var list = ui.View.DiscussionListBounds; ui.View.Wheel(list.X + 40, list.Y + 40, -120, false); ui.Paint();
        }
        Check(times.IsSupersetOf(new double[] { 1000, 1500, 2000, 3200, 4000 }), "all reply timestamp links remain accessible while scrolling");
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "discussion navigation and selection preserve content");
        float offset = ui.View.DiscussionScrollOffset;
        Select(ui, DiscussionCategory.GeneralAll); Check(ui.View.DiscussionSortByUpdate, "General defaults to last update");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("discussion.sortCreated")) && !ui.Canvas.Texts.Any(t => t.Value == L.Get("discussion.sortTimeline") && t.Y > ui.View.DiscussionTabsBounds.Bottom && t.X > ui.View.DiscussionBounds.X), "General offers creation time rather than timeline sorting");
        ui.ClickText(L.Get("discussion.sortCreated")); Check(!ui.View.DiscussionSortByUpdate, "General creation-time sort can be selected");
        Check(string.Concat(ui.Canvas.Texts.Select(t => t.Value)).Contains("metadata"), "shared general body displayed");
        Select(ui, DiscussionCategory.General); Check(string.Concat(ui.Canvas.Texts.Select(t => t.Value)).Contains("Combo Colours"), "current general body displayed");
        Select(ui, DiscussionCategory.Timeline); Check(ui.View.DiscussionScrollOffset == offset, "category scroll retained");
        Select(ui, DiscussionCategory.Reviews); Check(ui.View.DiscussionSortByUpdate, "Reviews defaults to last update independently");
        bool jumped = false;
        for (int i = 0; i < 30 && !jumped; i++)
        {
            var link = ui.View.DiscussionReferenceHits.FirstOrDefault(h => h.Discussion == 6);
            if (link.Bounds.Width > 0) { ui.Click(link.Bounds.X + link.Bounds.Width / 2, link.Bounds.Y + link.Bounds.Height / 2); jumped = true; }
            else { var r = ui.View.DiscussionListBounds; ui.View.Wheel(r.X + 40, r.Y + 40, -120, false); ui.Paint(); }
        }
        Check(jumped && ui.View.CurrentDifficultyName == "Other" && ui.View.PlayheadMs == 3000, "embedded review seeks its own difficulty");
        Check(loads == 1, "difficulty switches reuse mapset snapshot");
        foreach (float width in new[] { 180f, 360f, 520f })
        {
            ui.Resize(1600, 900); var handle = ui.View.DockResizeBounds(false);
            ui.View.PointerDown(handle.X + 1, handle.Y + 20, 0, false, false);
            ui.View.PointerMove(1600 - width, handle.Y + 20, false, false); ui.View.PointerUp(1600 - width, handle.Y + 20, 0); ui.Paint();
            foreach (var category in Enum.GetValues<DiscussionCategory>())
            {
                Select(ui, category);
                Check(ui.View.ActiveDiscussionCategory == category, "all four tabs reachable through overflow");
            }
        }
        ui.View.StopDiscussions();
    }

    private static void TypeCardLayout(OsuDiscussionSnapshot source)
    {
        var now = DateTimeOffset.UtcNow;
        var posts = new[] { new BeatmapDiscussion(200, null, null, 10, null, "hype", false, false, now.AddDays(-21),
            [new DiscussionPost(300, 10, "woah", false, now.AddDays(-21))]) { UpVotes = 1 } };
        var snapshot = new OsuDiscussionSnapshot(777, 20, posts, source.Users, source.Owners);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        ui.View.LoadProject(Project()); ui.View.ShowDiscussionPanel(); Wait(ui);
        var handle = ui.View.DockResizeBounds(false);
        ui.View.PointerDown(handle.X + 1, handle.Y + 20, 0, false, false);
        ui.View.PointerMove(840, handle.Y + 20, false, false); ui.View.PointerUp(840, handle.Y + 20, 0); ui.Paint();
        Select(ui, DiscussionCategory.GeneralAll);
        var card = ui.View.DiscussionThreadBounds(200);
        Check(ui.Canvas.Texts.Any(t => t.Value == "-Miya" && t.X > card.X && t.Y >= card.Y && t.Y < card.Bottom), "short Hype cards retain their author in a wide layout");
        Check(ui.Canvas.Fills.Any(f => f.Bounds.Width == 14 && f.Bounds.X < card.X && f.Bounds.X >= ui.View.DiscussionBounds.X), "single type icon uses a narrower background");
        ui.View.StopDiscussions();
    }

    private static void ResolutionProfileLinks(OsuDiscussionSnapshot source)
    {
        var now = DateTimeOffset.UtcNow;
        var thread = new BeatmapDiscussion(800, 101, null, 10, 0, "problem", true, true, now,
            [new(801, 10, "A short discussion.", false, now),
                new(802, 20, "{\"type\":\"resolved\",\"value\":false}", true, now),
                new(803, 20, "{\"type\":\"resolved\",\"value\":true}", true, now)]);
        const string name = "yf_bmp_[mapper]";
        var users = source.Users.ToDictionary(pair => pair.Key, pair => pair.Key == 20 ? pair.Value with { Name = name } : pair.Value);
        var snapshot = new OsuDiscussionSnapshot(777, 20, [thread], users, source.Owners);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        var opened = new List<string>(); ui.View.RequestVisitOsuWeb = opened.Add;
        ui.View.LoadProject(Project()); ui.View.ShowDiscussionPanel(); Wait(ui);
        float panelX = ui.View.DiscussionBounds.X;
        foreach (bool quote in new[] { false, true })
        {
            var links = ui.Canvas.Texts.Where(text => text.Value == name && !text.Bold && (quote ? text.X < panelX : text.X > panelX)).ToArray();
            Check(links.Length == 2 && links.Select(text => text.Color).ToHashSet().SetEquals(new uint[] { 0xA6D9AE, 0xE6AA64 }),
                "resolved/reopened usernames preserve literal names and state colours");
            foreach (var link in links)
            {
                ui.View.PointerMove(link.X + 3, link.Y + 5, false, false); ui.Paint();
                Check(ui.View.DiscussionHandCursor && ui.Canvas.Lines.Any(line => Math.Abs(line.X1 - link.X) < .1 && Math.Abs(line.Y1 - link.Y - 15) < .1),
                    "resolution usernames underline and use a hand cursor in panel and canvas");
                ui.Click(link.X + 3, link.Y + 5);
            }
        }
        Check(opened.Count == 4 && opened.All(url => url == "https://osu.ppy.sh/users/20") && !ui.View.IsDirty,
            "resolution records link to the actor's osu profile without editing content");
        ui.View.StopDiscussions();
    }

    private static void ObjectReferenceSelection(OsuDiscussionSnapshot source)
    {
        var thread = new BeatmapDiscussion(600, 101, null, 10, 1000, "suggestion", false, true, null,
            [new(601, 10, "00:01:000 (1,2,3)\n00:01:500\n00:01:000 (999)", false, null),
                new(602, 20, "00:02:000 (4,5)\n00:04:000 (11)", false, null)]);
        var review = source.Discussions.Single(d => d.Id == 6);
        var snapshot = new OsuDiscussionSnapshot(777, 20, [thread, source.Discussions.Single(d => d.Id == 5),
            review with { Posts = [review.Posts[0] with { Message = "00:03:000 (8,9,10)" }] }], source.Users, source.Owners);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        ui.View.LoadProject(Project()); ui.View.ShowDiscussionPanel(); Wait(ui);
        var before = ui.View.Document.DeepClone(); var conversion = ui.View.Conversion;
        ClickTime(1000);
        Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals(ui.View.Document.Fruits.Take(3).Select(f => f.Id)), "numbered timestamp selects its corresponding objects");
        var selected = ui.View.SelectedObjectIds.ToHashSet(); ClickTime(1500);
        Check(selected.SetEquals(ui.View.SelectedObjectIds), "time-only timestamp preserves selection");
        ClickTime(1000, last: true);
        Check(selected.SetEquals(ui.View.SelectedObjectIds), "unmatched object numbers preserve selection and still seek");
        ClickTime(2000);
        Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals(ui.View.Document.Fruits.Skip(3).Take(2).Select(f => f.Id)), "reply references replace selection with their own objects");
        ClickTime(4000);
        Check(ui.View.SelectedObjectIds.Single() == ui.View.Document.Tracks.Single().Id, "slider references select the editable parent object");
        var canvasLink = ui.Canvas.Texts.First(text => text.Value.StartsWith("00:01:000 (1,2,3)") && text.X < ui.View.DiscussionBounds.X);
        ui.Click(canvasLink.X + 4, canvasLink.Y + 5);
        Check(selected.SetEquals(ui.View.SelectedObjectIds) && ReferenceEquals(conversion, ui.View.Conversion)
            && before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "canvas reference selection reuses conversion and leaves content unchanged");
        Select(ui, DiscussionCategory.Reviews);
        var link = ui.View.DiscussionReferenceHits.First(hit => hit.Discussion == 6);
        ui.Click(link.Bounds.X + 2, link.Bounds.Y + 5);
        Check(ui.View.CurrentDifficultyName == "Other" && ui.View.PlayheadMs == 3000
            && ui.View.SelectedObjectIds.ToHashSet().SetEquals(ui.View.Document.Fruits.Skip(7).Take(3).Select(f => f.Id)), "cross-difficulty references select objects in the destination difficulty");
        ui.View.StopDiscussions();
        void ClickTime(double time, bool last = false)
        {
            var links = ui.View.DiscussionReferenceHits.Where(hit => hit.Time == time).ToArray();
            var link = last ? links[^1] : links[0]; ui.Click(link.Bounds.X + 3, link.Bounds.Y + link.Bounds.Height / 2);
            Check(ui.View.PlayheadMs == time, "numbered and plain references still seek");
        }
    }

    private static void TextSelection(OsuDiscussionSnapshot source)
    {
        const string body = "Copy **this** paragraph with [a link](https://osu.ppy.sh/). 中文🙂 text.\nSecond line.";
        const string plain = "Copy this paragraph with a link. 中文🙂 text.\nSecond line.";
        var thread = new BeatmapDiscussion(700, 101, null, 10, 1000, "mapper_note", false, false, null,
            [new(701, 10, body, false, null), new(702, 20, "Reply body.", false, null)]);
        var snapshot = new OsuDiscussionSnapshot(777, 20, [thread], source.Users, source.Owners);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        string? copied = null; var opened = new List<string>();
        ui.View.RequestCopyText = value => copied = value; ui.View.RequestVisitOsuWeb = opened.Add;
        ui.View.LoadProject(Project()); ui.View.ShowDiscussionPanel(); Wait(ui);
        var before = ui.View.Document.DeepClone(); double playhead = ui.View.PlayheadMs;
        foreach (bool canvas in new[] { false, true })
        {
            float panelX = ui.View.DiscussionBounds.X;
            var start = ui.Canvas.Texts.First(text => text.Value == "Copy" && (canvas ? text.X < panelX : text.X > panelX));
            ui.View.PointerMove(start.X + 2, start.Y + 5, false, false); ui.Paint();
            Check(ui.View.DiscussionTextCursor && !ui.View.DiscussionHandCursor, "body text uses the text-selection cursor in panel and canvas");
            var end = ui.Canvas.Texts.First(text => text.Value == "line." && (canvas ? text.X < panelX : text.X > panelX));
            Drag(start.X + 1, start.Y + 5, end.X + ((ICanvas)ui.Canvas).MeasureText(end.Value, 12) + 1, end.Y + 5);
            ui.Key('C', ctrl: true);
            Check(copied == plain && ui.View.SelectedDiscussionText == plain, "drag selection copies formatted body text with logical line breaks and Unicode intact");
            ui.Key(46); Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "read-only discussion selection cannot delete beatmap objects");
            ui.Key(27);
            var link = ui.Canvas.Texts.First(text => text.Value == "a link" && (canvas ? text.X < panelX : text.X > panelX));
            Drag(link.X + 1, link.Y + 5, link.X + ((ICanvas)ui.Canvas).MeasureText(link.Value, 12) + 1, link.Y + 5);
            ui.Key('C', ctrl: true);
            Check(copied == "a link" && opened.Count == 0 && ui.View.PlayheadMs == playhead, "dragging link text selects it without following the URL or seeking the card");
            ui.Key('A', ctrl: true); ui.Key('C', ctrl: true);
            Check(copied!.Contains(plain) && copied.Contains("Reply body."), "focused discussion select-all includes replies"); ui.Key(27);
        }
        ui.View.StopDiscussions();
        void Drag(float x, float y, float endX, float endY)
        {
            ui.View.PointerDown(x, y, 0, false, false); Check(ui.View.WantsCapture, "discussion text drag captures the pointer");
            ui.View.PointerMove(endX, endY, false, false); ui.View.PointerUp(endX, endY, 0); ui.Paint();
            Check(!ui.View.WantsCapture && ui.Canvas.Fills.Any(fill => fill.Color == 0x3B708B), "discussion text selection is highlighted and releases capture");
        }
    }

    private static void UrlNavigation(OsuDiscussionSnapshot source)
    {
        const string url = "https://osu.ppy.sh/beatmapsets/2562136/discussion/-/generalAll/total";
        var thread = new BeatmapDiscussion(201, 101, null, 10, 0, "problem", false, true, null,
            [new(301, 10, url, false, null), new(302, 20, url, false, null)]);
        var snapshot = new OsuDiscussionSnapshot(777, 20, [thread], source.Users, source.Owners);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        var opened = new List<string>(); ui.View.RequestVisitOsuWeb = opened.Add;
        ui.View.LoadProject(Project()); ui.View.ShowDiscussionPanel(); Wait(ui);
        var before = ui.View.Document.DeepClone(); double playhead = ui.View.PlayheadMs;
        var panel = ui.View.DiscussionBounds;
        var quote = ui.View.DiscussionQuoteBounds.Single();
        foreach (bool canvas in new[] { false, true })
        {
            var segments = ui.Canvas.Texts.Where(t => t.Size == 12 && t.Color == 0xA6C5D0
                && (canvas ? t.X >= quote.X && t.X < quote.Right && t.Y >= quote.Y && t.Y < quote.Bottom - 20 : t.X > panel.X)).ToArray();
            Check(segments.Length >= 4, "both main post and reply URLs wrap into clickable segments");
            var firstLink = segments.TakeWhile((segment, index) => index == 0 || !segment.Value.StartsWith("https://", StringComparison.Ordinal)).ToArray();
            Check(firstLink.Length > 1, "test URL spans multiple rendered lines");
            foreach (var hovered in firstLink)
            {
                ui.View.PointerMove(hovered.X + 2, hovered.Y + 5, false, false); ui.Paint();
                Check(ui.View.DiscussionHandCursor && !ui.View.DiscussionTextCursor, "URL segments use the hand cursor in panel and canvas");
                Check(firstLink.All(Underlined) && segments.Skip(firstLink.Length).All(segment => !Underlined(segment)),
                    "hovering any URL segment underlines every segment of that link without underlining a repeated URL in another post");
            }
            foreach (var segment in segments) ui.Click(segment.X + 2, segment.Y + 5);
            Check(opened.Count == segments.Length && opened.All(value => value == url), "every wrapped segment opens the complete URL in panel and canvas");
            opened.Clear();
            bool Underlined(RecordingCanvas.Label segment) => ui.Canvas.Lines.Any(line => Math.Abs(line.X1 - segment.X) < .1
                && Math.Abs(line.Y1 - segment.Y - 15) < .1 && line.Color == segment.Color);
        }
        Check(ui.View.PlayheadMs == playhead && ui.View.Document.ContentEquals(before) && !ui.View.IsDirty,
            "URL navigation takes priority over card seeking and preserves editing state");
        ui.View.StopDiscussions();
    }

    private static void LongAnnotationAtZero(OsuDiscussionSnapshot source)
    {
        var posts = new List<DiscussionPost>
        {
            new(1001, 10, "00:00:000 (1) - " + string.Join(" ", Enumerable.Repeat("A detailed opening discussion.", 40)), false, DateTimeOffset.UtcNow)
        };
        for (int i = 0; i < 8; i++) posts.Add(new(1002 + i, i % 2 == 0 ? 20 : 10,
            "Reply " + i + ": 00:01:000 - " + string.Join(" ", Enumerable.Repeat("Complete reply text.", 20)), false, DateTimeOffset.UtcNow.AddMinutes(i + 1)));
        posts.Add(new(1010, 20, "END-OF-THREAD", false, DateTimeOffset.UtcNow.AddHours(1)));
        posts.Add(new(1011, 20, "{\"type\":\"resolved\",\"value\":true}", true, DateTimeOffset.UtcNow.AddHours(2)));
        var thread = new BeatmapDiscussion(100, 101, null, 10, 0, "problem", true, true, null, posts);
        var snapshot = new OsuDiscussionSnapshot(777, 20, [thread], source.Users, source.Owners);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => Task.FromResult(snapshot);
        var project = Project(); project.Difficulties[0].Document.Fruits.Add(new() { TimeMs = 0, X = 256 });
        ui.View.LoadProject(project); ui.View.ShowDiscussionPanel(); Wait(ui);
        var before = ui.View.Document.DeepClone();
        Check(ui.View.DiscussionQuoteBounds.Count == 1, "one annotation includes the thread and all its replies");
        var quote = ui.View.DiscussionQuoteBounds[0];
        Check(quote.Y >= ui.Plot.Y && quote.Bottom <= ui.Plot.Bottom, "long zero-time annotation remains entirely within the canvas");
        Check(ui.View.DiscussionQuoteScrollState.Single().Maximum > 500, "complete long discussion has an internal scroll range");
        ui.View.PointerDown(quote.Right - 5, quote.Bottom - 12, 0, false, false);
        Check(ui.View.WantsCapture, "annotation scrollbar captures pointer dragging");
        ui.View.PointerMove(quote.Right - 5, quote.Y + 5, false, false);
        ui.View.PointerUp(quote.Right - 5, quote.Y + 5, 0); ui.Paint();
        Check(!ui.View.WantsCapture && ui.View.DiscussionQuoteScrollState.Single().Scroll == 0, "annotation scrollbar returns to the first post and releases capture");
        bool reply = false, end = false, resolved = false, canvasLink = false;
        for (int i = 0; i < 160; i++)
        {
            string text = string.Concat(ui.Canvas.Texts.Where(t => t.X >= quote.X && t.X < quote.Right && t.Y >= quote.Y && t.Y <= quote.Bottom).Select(t => t.Value));
            reply |= text.Contains("Reply"); end |= text.Contains("END-OF-THREAD"); resolved |= text.Contains(L.Get("discussion.resolvedBy", "Yumeno Himiko"));
            if (!canvasLink)
            {
                var label = ui.Canvas.Texts.FirstOrDefault(t => t.Value.StartsWith("00:01:000") && t.X >= quote.X && t.X < quote.Right && t.Y > quote.Y + 5 && t.Y < quote.Bottom - 22);
                if (label.Value is not null)
                {
                    ui.Click(label.X + 4, label.Y + 7);
                    Check(ui.View.PlayheadMs == 1000, "canvas reply timestamp takes priority over the annotation card");
                    var root = ui.View.DiscussionThreadBounds(100); ui.Click(root.Right - 4, root.Y + 5);
                    Check(ui.View.PlayheadMs == 0, "whole zero-time card still seeks after canvas navigation"); canvasLink = true;
                }
            }
            if (end && resolved) break;
            ui.View.Wheel(quote.X + 20, quote.Y + 30, -120, false); ui.Paint();
        }
        Check(reply && end && resolved && canvasLink, "canvas scrolling reveals every reply and final resolution without truncation");
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty && ui.View.PlayheadMs == 0, "annotation scrolling preserves playback and authoring state");
        var plot = ui.Plot; double start = ui.View.ViewStartMs;
        for (int i = 0; i < 15; i++) { ui.View.Wheel(plot.X + 12, plot.Y + 10, 120, false); ui.Paint(); }
        Check(ui.View.ViewStartMs < start && ui.View.ViewStartMs < 0 && ui.View.PlayheadMs == 0, "discussion canvas can browse before zero while playback remains nonnegative");
        Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "negative view scrolling does not edit note times");
        ui.Key('2'); ui.Click(plot.X + plot.Width / 2, plot.Bottom - 20);
        ui.Key('A', ctrl: true); ui.Key('J');
        Check(ui.View.Document.Fruits.All(f => f.TimeMs >= 0) && ui.View.Document.Tracks.SelectMany(t => t.Nodes).All(n => n.TimeMs >= 0), "placement in negative view remains clamped to valid authored time");
        var timeDisplay = ui.View.TimeDisplayBounds; ui.Click(timeDisplay.X + 10, timeDisplay.Y + 10);
        ui.Key('A', ctrl: true); ui.Type("-00:01:000"); ui.Key(13);
        Check(ui.View.TimeJumpVisible && ui.View.PlayheadMs == 0, "normal time entry rejects negative values"); ui.Key(27);
        ui.View.StopDiscussions();
    }

    private static void StaleRequest(OsuDiscussionSnapshot snapshot)
    {
        var old = new TaskCompletionSource<OsuDiscussionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Ui(false); ui.View.DiscussionLoader = (_, _) => old.Task;
        ui.View.LoadProject(Project()); ui.View.ShowDiscussionPanel(); ui.Paint();
        ui.View.LoadDocument(new MapDocument()); old.SetResult(snapshot);
        for (int i = 0; i < 5; i++) { Thread.Sleep(1); ui.Paint(); }
        Check(ui.View.DiscussionSnapshot is null && !ui.View.DiscussionNeedsRedraw, "retired request cannot publish into another mapset");
    }

    private static void Select(Ui ui, DiscussionCategory category)
    {
        for (int i = 0; i < 25; i++)
        {
            var tab = ui.View.DiscussionCategoryBounds(category);
            if (tab.Width > 6) { ui.Click(tab.X + tab.Width / 2, tab.Y + 10); return; }
            var strip = ui.View.DiscussionTabsBounds;
            ui.View.Wheel(strip.X + 35, strip.Y + 10, category < ui.View.ActiveDiscussionCategory ? 120 : -120, false); ui.Paint();
        }
        throw new Exception("Discussion tab unavailable: " + category);
    }

    private static void Wait(Ui ui)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (ui.View.DiscussionSnapshot is null)
        { ui.Paint(); if (watch.Elapsed.TotalSeconds > 5) throw new Exception("Discussion load timeout"); Thread.Sleep(1); }
        ui.Paint();
    }

    internal static BeatmapProject Project()
    {
        return new() { Name = "Discussions", Difficulties = [Difficulty("Double Radiance", 101), Difficulty("Other", 102)] };
        static ProjectDifficulty Difficulty(string name, int id)
        {
            var map = new MapDocument { Name = name, DurationMs = 10000, IsDemo = false };
            SongSetup.Set(map, "Metadata", "BeatmapSetID", "777"); SongSetup.Set(map, "Metadata", "BeatmapID", id.ToString());
            SongSetup.Set(map, "Metadata", "Version", name);
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
            foreach (double time in new double[] { 1000, 1250, 1500, 2000, 2250, 2500, 2750, 3000, 3200, 3500 })
                map.Fruits.Add(new() { TimeMs = time, X = 160 + time % 300 });
            var track = new CurveTrack { Name = "Discussion slider", Kind = CurveKind.Linear };
            track.Nodes.Add(new() { TimeMs = 4000, X = 240 }); track.Nodes.Add(new() { TimeMs = 5000, X = 320 });
            map.Tracks.Add(track);
            return new() { Name = name, Document = map };
        }
    }

    private static void Reject(Action action)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Partial or mismatched data accepted"); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class SnapshotHandler(string json) : HttpMessageHandler
    {
        internal bool AnonymousJson;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AnonymousJson = request.RequestUri!.AbsoluteUri == "https://osu.ppy.sh/beatmapsets/777/discussion"
                && request.Headers.Accept.Any(a => a.MediaType == "application/json")
                && request.Headers.Authorization is null && !request.Headers.Contains("Cookie");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
