using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<BrowserCommand>? RequestBrowserCommand { get; set; }
    private BrowserState browserState = new();
    private (long Set, long Map, bool Discussion) browserContext;
    private string? browserPage;
    private Rect browserToolbar;
    private Rect browserRenderedViewport;
    private string? browserTooltip;
    private long? browserCopiedAt;
    private float browserCopiedPainted;
    internal float BrowserCopyOpacity => browserCopiedAt is { } started
        ? (float)Math.Clamp((2 - timeProvider.GetElapsedTime(started).TotalSeconds) / .4, 0, 1) : 0;
    public bool BrowserFeedbackNeedsRedraw => BrowserContentAvailable && browserCopiedPainted > 0;
    private bool BrowserContentAvailable => PanelVisible(DockPanel.OsuWeb) && DockContentInputAvailable
        && !librarySettingsOpen && !StartupDialogVisible && !CollaborationReportVisible && !RotationVisible
        && !MapTemplatesVisible && !addDifficultyConfirmation && !OsuLinkPickerVisible && !OsuLinkFallbackRequired
        && !PatternGalleryPreviewVisible && !panelMenuOpen && !volumePopoverOpen && !languageMenuOpen
        && !updatesPage && !resourcePage && syncPage != "resolve"
        && menu < 0 && contextItems.Count == 0 && !dockDragging;
    private bool BrowserToolbarContains(float x, float y) => BrowserContentAvailable && browserToolbar.Contains(x, y);
    private bool BrowserToolbarPointer(float x, float y, int button)
    {
        if (!BrowserToolbarContains(x, y)) return false;
        mouseX = x; mouseY = y;
        if (button == 0)
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
        return true;
    }
    internal Rect BrowserAddressBounds { get; private set; }
    public bool UpdateBrowserState(BrowserState state)
    {
        if (browserState == state) return false;
        browserState = state; UpdateBrowserPage(state.Url);
        return true;
    }
    public bool UpdateBrowserPage(string? url)
    {
        if (browserPage == url) return false;
        browserPage = url; browserContext = OsuBrowser.PageContext(url);
        return true;
    }

    private bool BrowserDiscussionMatches
    {
        get
        {
            var context = browserContext;
            return context.Discussion && context.Set == DiscussionOnlineId(Document, "BeatmapSetID")
                && context.Map > 0 && context.Map == DiscussionOnlineId(Document, "BeatmapID");
        }
    }
    private bool DiscussionAnnotationsVisible => PanelVisible(DockPanel.Discussion)
        || BrowserContentAvailable && BrowserDiscussionMatches;

    public bool HandleBrowserEditorLink(string link, string page)
    {
        var context = OsuBrowser.PageContext(page);
        if (context.Set <= 0 || context.Map <= 0 || !EditorTimestamp.IsEditorLink(link)
            || !EditorTimestamp.TryParse(link, out _)) return false;
        if (context.Set != DiscussionOnlineId(Document, "BeatmapSetID"))
        { SetNotice(FruitsAtelier.Localization.Strings.Get("discussion.openDifficulty")); return false; }
        if (context.Map != DiscussionOnlineId(Document, "BeatmapID"))
        {
            int index = difficulties.FindIndex(d => DiscussionOnlineId(d.History.Document, "BeatmapID") == context.Map);
            if (index < 0) { SetNotice(FruitsAtelier.Localization.Strings.Get("discussion.openDifficulty")); return false; }
            if (!SwitchDifficulty(index)) return false;
        }
        return HandleEditorLink(link);
    }

    public string? BrowserUrl
    {
        get
        {
            if (!HasEditorProject || PatternEditingActive) return null;
            long set = DiscussionOnlineId(Document, "BeatmapSetID"), map = DiscussionOnlineId(Document, "BeatmapID");
            return set > 0 ? $"https://osu.ppy.sh/beatmapsets/{set}/discussion" + (map > 0 ? $"/{map}/timeline" : "")
                : map > 0 ? $"https://osu.ppy.sh/b/{map}" : null;
        }
    }

    private void DrawBrowserPanel(ICanvas c, Rect bounds)
    {
        browserRenderedViewport = new(bounds.X + 8, bounds.Y + 104, Math.Max(0, bounds.Width - 16), Math.Max(0, bounds.Height - 110));
        browserToolbar = new(bounds.X + 8, bounds.Y + 44, Math.Max(0, bounds.Width - 16), 54);
        browserTooltip = null;
        float x = browserToolbar.X, y = browserToolbar.Y;
        void Command(BrowserCommand command, string key, float left, bool enabled)
        {
            var button = new Rect(left, y, 26, 28);
            Button(c, button, "", () => RequestBrowserCommand?.Invoke(command), enabled: enabled);
            DrawBrowserIcon(c, button, command, enabled ? Foreground : 0x5B6777);
            if (button.Contains(mouseX, mouseY)) browserTooltip = L.Get(key);
        }
        bool available = BrowserUrl is not null;
        Command(BrowserCommand.Back, "browser.back", x, browserState.Back);
        Command(BrowserCommand.Forward, "browser.forward", x + 30, browserState.Forward);
        Command(BrowserCommand.Refresh, "browser.refreshDiscussion", x + 60, available);
        Command(BrowserCommand.External, "browser.external", browserToolbar.Right - 26, OsuBrowser.IsWebLink(browserPage ?? BrowserUrl ?? ""));
        BrowserAddressBounds = new(x + 90, y, Math.Max(0, browserToolbar.Width - 120), 28);
        string address = browserPage ?? BrowserUrl ?? "";
        Button(c, BrowserAddressBounds, "", () => { if (address.Length > 0 && RequestCopyText is not null) { RequestCopyText(address); browserCopiedAt = timeProvider.GetTimestamp(); } }, enabled: address.Length > 0);
        c.Clip(BrowserAddressBounds);
        browserCopiedPainted = BrowserCopyOpacity;
        uint colour = 0;
        for (int shift = 0; shift <= 16; shift += 8)
            colour |= (uint)MathF.Round(((Foreground >> shift) & 255) * (1 - browserCopiedPainted) + ((Accent >> shift) & 255) * browserCopiedPainted) << shift;
        c.Text(address, BrowserAddressBounds.X + 6, y + 7, 11, colour, Math.Max(0, BrowserAddressBounds.Width - 12));
        c.Unclip();
        if (BrowserAddressBounds.Contains(mouseX, mouseY)) browserTooltip = L.Get("discussion.copyLinkHint");
        string status = !available ? L.Get("browser.noMap") : browserState.Failed ? L.Get("browser.failed") : browserState.Loading ? L.Get("browser.loading") : "";
        if (browserCopiedPainted > 0)
        {
            string copied = L.Get("discussion.linkCopied");
            float w = Math.Min(browserToolbar.Width, c.MeasureText(copied, 11) + 16);
            float left = Math.Min(BrowserAddressBounds.X, browserToolbar.Right - w);
            var popup = new Rect(left, y + 31, w, 22);
            c.Fill(popup, Surface, 4, browserCopiedPainted);
            c.StrokeOpacity(popup, Accent, 1, 4, browserCopiedPainted);
            c.TextOpacity(copied, left + 8, y + 35, 11, Accent, w - 16, false, browserCopiedPainted);
        }
        else c.Text(browserTooltip ?? status, x, y + 34, 11, browserState.Failed ? Accent : Muted, browserToolbar.Width);
    }

    private static void DrawBrowserIcon(ICanvas c, Rect button, BrowserCommand command, uint colour)
    {
        float x = button.X + button.Width / 2, y = button.Y + button.Height / 2;
        void Line(float x1, float y1, float x2, float y2) => c.Line(x + x1, y + y1, x + x2, y + y2, colour, 1.8f);
        if (command is BrowserCommand.Back or BrowserCommand.Forward)
        {
            float direction = command == BrowserCommand.Back ? -1 : 1;
            Line(-6, 0, 6, 0); Line(6 * direction, 0, direction, -5); Line(6 * direction, 0, direction, 5);
        }
        else if (command == BrowserCommand.Refresh)
        {
            for (int i = 0; i < 28; i++)
            {
                float a = (40 + i * 10) * MathF.PI / 180, b = a + MathF.PI / 18;
                Line(6 * MathF.Cos(a), 6 * MathF.Sin(a), 6 * MathF.Cos(b), 6 * MathF.Sin(b));
            }
            float endX = 6 * MathF.Cos(320 * MathF.PI / 180), endY = 6 * MathF.Sin(320 * MathF.PI / 180);
            Line(endX - 5, endY, endX, endY); Line(endX, endY - 5, endX, endY);
        }
        else
        {
            Line(-5, 5, 5, -5); Line(0, -5, 5, -5); Line(5, -5, 5, 0);
        }
    }

    // Native child views must yield to editor overlays instead of covering their controls.
    public Rect BrowserViewport
    {
        get
        {
            if (!BrowserContentAvailable || BrowserUrl is null) return default;
            return browserRenderedViewport;
        }
    }
}
