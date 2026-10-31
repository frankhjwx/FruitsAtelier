using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class DiscussionRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string folder = Path.GetFullPath("artifacts/tests/discussions/native"); Directory.CreateDirectory(folder);
        var snapshot = OsuDiscussionClient.Parse(File.ReadAllText("tests/FruitsAtelier.App.Tests/Fixtures/discussions.json"), 777);
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var editor = new EditorView(false) { DiscussionLoader = (_, _) => Task.FromResult(snapshot) };
                var map = new MapDocument { Name = "Double Radiance", DurationMs = 10000, IsDemo = false };
                SongSetup.Set(map, "Metadata", "BeatmapSetID", "777"); SongSetup.Set(map, "Metadata", "BeatmapID", "101");
                SongSetup.Set(map, "Metadata", "Version", map.Name);
                map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
                foreach (double time in new double[] { 1000, 1250, 1500, 2000, 2250, 2500, 2750, 3000, 3200, 3500, 4000 })
                    map.Fruits.Add(new() { TimeMs = time, X = 160 + time % 300 });
                editor.LoadDocument(map); editor.ShowDiscussionPanel();
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                do { Paint(); Thread.Sleep(1); } while (editor.DiscussionSnapshot is null && watch.Elapsed.TotalSeconds < 5);
                if (editor.DiscussionSnapshot is null) throw new InvalidOperationException("Native discussions did not load.");
                var plot = editor.CanvasPlotBounds;
                editor.Wheel(plot.X, plot.Bottom, (float)(120 * Math.Log(.09 / editor.PixelsPerMs) / Math.Log(1.16)), false, false, true);
                Paint("timeline");
                var toggle = editor.CanvasDiscussionToggleBounds;
                editor.PointerMove(toggle.X + 12, toggle.Y + 16, false, false); Paint();
                Thread.Sleep(170); Paint("canvas-controls");
                editor.PointerDown(toggle.X + 12, toggle.Y + 16, 0, false, false);
                editor.PointerUp(toggle.X + 12, toggle.Y + 16, 0); Paint("annotations-hidden");
                if (editor.DiscussionQuoteBounds.Count != 0 || editor.DiscussionMarkerHits.Any(h => h.Bounds.Y >= editor.CanvasPlotBounds.Y && h.Bounds.Y < editor.CanvasPlotBounds.Bottom))
                    throw new InvalidOperationException("Hidden canvas annotations retain hit targets.");
                editor.PointerDown(toggle.X + 12, toggle.Y + 16, 0, false, false);
                editor.PointerUp(toggle.X + 12, toggle.Y + 16, 0); Paint();
                var discussionTab = editor.DockTabBounds(EditorView.DockPanel.Discussion);
                editor.PointerDown(discussionTab.X + 10, discussionTab.Y + 10, 2, false, false);
                editor.PointerUp(discussionTab.X + 10, discussionTab.Y + 10, 2); Paint("mode-menu");
                editor.KeyDown(27, false, false); Paint();
                var list = editor.DiscussionListBounds;
                editor.Wheel(list.X + 40, list.Y + 40, -480, false); Paint("replies");
                if (size.Item1 == 1440)
                {
                    var handle = editor.DockResizeBounds(false);
                    editor.PointerDown(handle.X + 1, handle.Y + 20, 0, false, false);
                    editor.PointerMove(size.Item1 - 600, handle.Y + 20, false, false);
                    editor.PointerUp(size.Item1 - 600, handle.Y + 20, 0); Paint();
                    editor.Wheel(list.X + 40, list.Y + 40, 12000, false); Paint("wide");
                    if (dpi == 96)
                    {
                        var reference = editor.DiscussionReferenceHits.First(hit => hit.Time == 1000);
                        editor.PointerDown(reference.Bounds.X + 4, reference.Bounds.Y + 5, 0, false, false);
                        editor.PointerUp(reference.Bounds.X + 4, reference.Bounds.Y + 5, 0); Paint("object-reference-selection");
                        if (editor.SelectedObjectIds.Count != 3 || editor.IsDirty) throw new InvalidOperationException("Native discussion reference object selection failed.");
                    }
                }
                var zeroThread = snapshot.Discussions.First(d => d.Id == 3) with
                {
                    Timestamp = 0,
                    Posts = [new(901, 10, "00:00:000 (1) - " + string.Join(" ", Enumerable.Repeat("Detailed opening discussion.", 50)), false, DateTimeOffset.UtcNow),
                        new(902, 20, string.Join(" ", Enumerable.Repeat("Complete mapper reply.", 60)) + " END-OF-THREAD", false, DateTimeOffset.UtcNow),
                        new(903, 20, "{\"type\":\"resolved\",\"value\":true}", true, DateTimeOffset.UtcNow)]
                };
                var zero = new OsuDiscussionSnapshot(777, 20, [zeroThread], snapshot.Users, snapshot.Owners);
                editor.DiscussionLoader = (_, _) => Task.FromResult(zero);
                map.Fruits.Add(new() { TimeMs = 0, X = 256 }); editor.LoadDocument(map); editor.ShowDiscussionPanel();
                watch.Restart();
                do { Paint(); Thread.Sleep(1); } while (editor.DiscussionSnapshot is null && watch.Elapsed.TotalSeconds < 5);
                if (editor.DiscussionQuoteBounds.Count != 1) throw new InvalidOperationException("Zero-time full annotation missing.");
                Paint("zero");
                var quote = editor.DiscussionQuoteBounds[0];
                editor.Wheel(quote.X + 20, quote.Y + 30, -24000, false); Paint("zero-end");
                var quoteScroll = editor.DiscussionQuoteScrollState.Single();
                if (quoteScroll.Scroll != quoteScroll.Maximum || editor.PlayheadMs != 0) throw new InvalidOperationException("Long annotation did not reach its final reply.");
                plot = editor.CanvasPlotBounds;
                editor.Wheel(plot.X + 12, plot.Y + 10, 2400, false); Paint("preroll");
                if (editor.ViewStartMs >= 0 || editor.PlayheadMs != 0 || editor.Document.Fruits.Any(f => f.TimeMs < 0))
                    throw new InvalidOperationException("Native pre-zero viewing changed authored time.");
                var crowded = new OsuDiscussionSnapshot(777, 20,
                    Enumerable.Range(0, 3).Select(i => zeroThread with { Id = 910 + i }).ToArray(), snapshot.Users, snapshot.Owners);
                editor.DiscussionLoader = (_, _) => Task.FromResult(crowded);
                editor.LoadDocument(map); editor.ShowDiscussionPanel(); watch.Restart();
                do { Paint(); Thread.Sleep(1); } while (editor.DiscussionSnapshot is null && watch.Elapsed.TotalSeconds < 5);
                for (int i = 0; i < 12; i++)
                {
                    plot = editor.CanvasPlotBounds;
                    editor.Wheel(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, i % 2 == 0 ? -120 : 120, false);
                    Paint();
                }
                if (editor.DiscussionQuoteBounds.Count != 3 || editor.IsDirty)
                    throw new InvalidOperationException("Crowded annotation scrolling lost cards or changed content.");
                Paint("crowded-quotes");
                if (dpi == 96 && size.Item1 == 1440)
                {
                    string[] types = ["hype", "praise", "mapper_note", "problem", "suggestion", "review"];
                    var samples = types.Select((type, index) => new BeatmapDiscussion(200 + index, null, null, 10, null, type,
                        false, type is "problem" or "suggestion", DateTimeOffset.UtcNow.AddDays(-21 - index),
                        [new DiscussionPost(300 + index, 10, type == "praise" ? "Himiko never disappoints!" : "woah", false, DateTimeOffset.UtcNow.AddDays(-21 - index))])
                        { UpVotes = index + 1 }).ToArray();
                    var sample = new OsuDiscussionSnapshot(777, 20, samples, snapshot.Users, snapshot.Owners);
                    editor.DiscussionLoader = (_, _) => Task.FromResult(sample); editor.LoadDocument(map); editor.ShowDiscussionPanel();
                    watch.Restart(); do { Paint(); Thread.Sleep(1); } while (editor.DiscussionSnapshot is null && watch.Elapsed.TotalSeconds < 5);
                    Select(DiscussionCategory.GeneralAll); Paint("types");
                    var body = editor.DiscussionListBounds; editor.Wheel(body.X + 30, body.Y + 30, -2400, false); Paint("types-end");
                    Select(DiscussionCategory.Reviews); Paint("reviews");
                    if (!editor.DiscussionSortByUpdate) throw new InvalidOperationException("Reviews did not default to last update.");
                    editor.DiscussionCacheLoader = (_, _) => Task.FromResult<OsuDiscussionSnapshot?>(sample);
                    editor.DiscussionLoader = (_, _) => Task.FromException<OsuDiscussionSnapshot>(new HttpRequestException("offline"));
                    editor.LoadDocument(map); editor.ShowDiscussionPanel(); watch.Restart();
                    do { Paint(); Thread.Sleep(1); } while ((editor.DiscussionSnapshot is null || editor.DiscussionLoading) && watch.Elapsed.TotalSeconds < 5);
                    Paint("offline");
                    if (editor.DiscussionSnapshot is null || editor.DiscussionLoading || editor.IsDirty) throw new InvalidOperationException("Offline discussions blocked native editor.");
                    var urlThread = new BeatmapDiscussion(400, 101, null, 10, 1000, "suggestion", false, true, null,
                        [new(401, 10, "refer to https://p.eagate.573.jp/game/2dx/33/newsong/index.html", false, DateTimeOffset.UtcNow.AddDays(-21)),
                            new(402, 20, "https://osu.ppy.sh/beatmapsets/2562136/discussion/-/generalAll/total\nI've gone through the map metadata discussion.", false, DateTimeOffset.UtcNow.AddDays(-20))]);
                    var links = new OsuDiscussionSnapshot(777, 20, [urlThread], snapshot.Users, snapshot.Owners);
                    editor.DiscussionLoader = (_, _) => Task.FromResult(links); editor.LoadDocument(map); editor.ShowDiscussionPanel();
                    watch.Restart(); do { Paint(); Thread.Sleep(1); } while ((editor.DiscussionSnapshot != links || editor.DiscussionLoading) && watch.Elapsed.TotalSeconds < 5);
                    Select(DiscussionCategory.Timeline); Paint("urls");
                    var wrappedLink = editor.DiscussionRenderedLinkBounds("https://osu.ppy.sh/beatmapsets/2562136/discussion/-/generalAll/total");
                    editor.PointerMove(wrappedLink.X + 3, wrappedLink.Y + 5, false, false); Paint("url-hover");
                    if (!editor.DiscussionHandCursor || editor.DiscussionTextCursor) throw new InvalidOperationException("Native discussion link cursor failed.");
                    var canvasLink = editor.DiscussionRenderedLinkBounds("https://p.eagate.573.jp/game/2dx/33/newsong/index.html", quote: true);
                    editor.PointerMove(canvasLink.X + 3, canvasLink.Y + 5, false, false); Paint("url-hover-canvas");
                    if (!editor.DiscussionHandCursor) throw new InvalidOperationException("Native canvas discussion link cursor failed.");
                    string? fixture = Environment.GetCommandLineArgs().FirstOrDefault(argument => argument.StartsWith("--discussions-image-fixture=", StringComparison.Ordinal));
                    string imagePath = fixture is not null ? Path.GetFullPath(fixture["--discussions-image-fixture=".Length..])
                        : Path.Combine(AppContext.BaseDirectory, "assets", "icons", "discussion", "bullhorn.png");
                    var imageThread = urlThread with { Posts = [new(401, 10, "![](https://koiyuki.s-ul.eu/L1ONATEm)\n**Bold** and *italic*, ~~removed~~ and `literal code`\n[osu! link](https://osu.ppy.sh/)", false, DateTimeOffset.UtcNow.AddDays(-21)),
                        new(402, 20, "> Quoted reply\n```\n**literal** 00:01:000\n```\n![reply image](https://koiyuki.s-ul.eu/L1ONATEm)", false, DateTimeOffset.UtcNow.AddDays(-20))] };
                    var images = new OsuDiscussionSnapshot(777, 20, [imageThread], snapshot.Users, snapshot.Owners);
                    editor.DiscussionImageLoader = async (_, token) =>
                    {
                        if (fixture is not null) return await OsuDiscussionClient.ReadImageAsync(imagePath, token);
                        return new(imagePath, 128, 128);
                    };
                    editor.DiscussionLoader = (_, _) => Task.FromResult(images); editor.LoadDocument(map); editor.ShowDiscussionPanel();
                    watch.Restart(); do { Paint(); Thread.Sleep(1); } while ((editor.DiscussionSnapshot != images || editor.DiscussionImagesLoading) && watch.Elapsed.TotalSeconds < 5);
                    Select(DiscussionCategory.Timeline); Paint("markdown");
                    var caption = editor.DiscussionRenderedTextBounds("Bold");
                    editor.PointerMove(caption.X + 1, caption.Y + 5, false, false); Paint();
                    if (!editor.DiscussionTextCursor || editor.DiscussionHandCursor) throw new InvalidOperationException("Native discussion body text cursor failed.");
                    editor.PointerDown(caption.X + 1, caption.Y + 5, 0, false, false);
                    editor.PointerMove(caption.Right - 1, caption.Y + 5, false, false);
                    editor.PointerUp(caption.Right - 1, caption.Y + 5, 0); Paint("text-selection");
                    if (editor.SelectedDiscussionText.Length == 0) throw new InvalidOperationException("Native discussion text selection failed.");
                    editor.KeyDown(27, false, false); Paint();
                    var namedLink = editor.DiscussionRenderedTextBounds("osu! link");
                    float footerX = namedLink.X + 3, footerY = namedLink.Y + 30;
                    editor.PointerMove(footerX, footerY, false, false); Paint("permalink-hover");
                    if (!editor.DiscussionHandCursor) throw new InvalidOperationException("Native discussion permalink cursor failed.");
                    string? copied = null; editor.RequestCopyText = value => copied = value;
                    editor.PointerDown(footerX, footerY, 0, false, false); editor.PointerUp(footerX, footerY, 0); Paint("permalink-copied");
                    if (copied is null) throw new InvalidOperationException("Native discussion permalink copy failed.");
                    editor.PointerDown(caption.X + 5, caption.Y - 15, 0, false, false);
                    editor.PointerUp(caption.X + 5, caption.Y - 15, 0); Paint("image-preview");
                    if (!editor.DiscussionImageVisible) throw new InvalidOperationException("Native discussion image preview failed.");
                    if (editor.DiscussionImageZoom != 1) throw new InvalidOperationException("Native discussion image preview did not open at 100 percent.");
                    var imageViewport = editor.DiscussionImageViewport;
                    editor.Wheel(imageViewport.X + imageViewport.Width / 2, imageViewport.Y + imageViewport.Height / 2, 240, true); Paint("image-zoom");
                    if (editor.DiscussionImageZoom <= 1) throw new InvalidOperationException("Native discussion image zoom failed.");
                    editor.PointerDown(8, 8, 0, false, false); editor.PointerUp(8, 8, 0); Paint();
                    if (editor.DiscussionImageVisible) throw new InvalidOperationException("Native image preview did not dismiss on a backdrop click.");
                }
                editor.StopDiscussions();
                Console.WriteLine($"PASS discussions native {locale} {size.Item1}x{size.Item2} {dpi} DPI");

                void Select(DiscussionCategory category)
                {
                    for (int i = 0; i < 30; i++)
                    {
                        var tab = editor.DiscussionCategoryBounds(category);
                        if (tab.Width > 8)
                        { editor.PointerDown(tab.X + tab.Width / 2, tab.Y + 10, 0, false, false); editor.PointerUp(tab.X + tab.Width / 2, tab.Y + 10, 0); Paint(); return; }
                        var tabs = editor.DiscussionTabsBounds;
                        editor.Wheel(tabs.X + 30, tabs.Y + 10, category < editor.ActiveDiscussionCategory ? 120 : -120, false); Paint();
                    }
                    throw new InvalidOperationException("Native discussion tab unavailable.");
                }

                void Paint(string? state = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: state is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var output = new BinaryWriter(File.Create(Path.Combine(folder, $"{state}-{locale}-{size.Item1}-{dpi}.bmp")));
                        output.Write((ushort)0x4D42); output.Write(54 + frame.Pixels.Length); output.Write(0); output.Write(54);
                        output.Write(40); output.Write(frame.Width); output.Write(-frame.Height); output.Write((ushort)1); output.Write((ushort)32);
                        output.Write(0); output.Write(frame.Pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
