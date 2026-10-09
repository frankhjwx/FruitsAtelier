using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public enum UpdatePhase { Idle, Unsupported, Checking, Current, Available, Downloading, Ready, Failed }
public sealed record UpdateStatus(UpdatePhase Phase, string Version = "", int Progress = 0);

public sealed partial class EditorView
{
    private const string OsuProfileUrl = "https://osu.ppy.sh/users/1806962";
    private const string ProjectWebsiteUrl = "https://fruitsatelier.himiko.moe/";
    private const string GithubPageUrl = "https://github.com/frankhjwx/FruitsAtelier";
    public Action? RequestUpdateCheck { get; set; }
    public Action? RequestUpdateDownload { get; set; }
    public Action? RequestUpdateRestart { get; set; }
    public Action? RequestUpdateNotes { get; set; }
    public Action? RequestUpdatePreference { get; set; }
    public bool AutomaticUpdateChecks { get; set; } = true;
    public UpdateStatus UpdateStatus { get; set; } = new(UpdatePhase.Idle);
    private bool updatesPage;
    private float UpdatesCompactness => Math.Clamp((680 - SettingsBounds.Height) / 124, 0, 1);
    internal Rect UpdateRestartButtonBounds => new(SettingsContentX + 222, SettingsTop + 296 - 72 * UpdatesCompactness,
        SettingsContentWidth - 222, SettingsControlHeight);

    private void OpenUpdates()
    {
        if (!PrepareFileOperation()) return;
        updatesPage = true; libraryField = bindingCapture = -1; FinishVolumeDrag();
        if (UpdateStatus.Phase is UpdatePhase.Idle or UpdatePhase.Current or UpdatePhase.Failed)
            RequestUpdateCheck?.Invoke();
    }

    private void DrawUpdateNotice(ICanvas c)
    {
        if (RequestUpdateCheck is null || UpdateStatus.Phase is not (UpdatePhase.Available or UpdatePhase.Ready)) return;
        Button(c, new(Math.Max(16, width - 430), height - 34, Math.Min(414, width - 32), 28),
            L.Get("update.noticeButton", UpdateStatus.Version), OpenUpdates, active: true);
    }

    private void DrawUpdates(ICanvas c, float x = 32, bool embedded = false)
    {
        float top = embedded ? SettingsTop : 0;
        float right = embedded ? SettingsRight : width;
        float textSize = embedded ? SettingsTextSize : 15;
        float compact = embedded ? UpdatesCompactness : 0;
        void ActionButton(Rect bounds, string label, Action action, bool active = false, bool enabled = true)
        {
            if (embedded) SettingsButton(c, bounds with { Height = SettingsControlHeight }, label, action, active, enabled);
            else Button(c, bounds, label, action, active, enabled);
        }
        if (!embedded) c.Text(L.Get("update.title"), x, 96, 22, Foreground, right - x - 32, true);
        c.Text(L.Get("update.currentVersion", DisplayVersion), x, top + 144 - 28 * compact, textSize, Muted, right - x - 32);
        ActionButton(new(x, top + 186 - 44 * compact, embedded ? SettingsContentWidth : 340, 36), L.Get(AutomaticUpdateChecks ? "update.automaticOn" : "update.automaticOff"), () =>
        { AutomaticUpdateChecks = !AutomaticUpdateChecks; RequestUpdatePreference?.Invoke(); }, AutomaticUpdateChecks);
        string key = UpdateStatus.Phase switch
        {
            UpdatePhase.Unsupported => "update.unsupported", UpdatePhase.Checking => "update.checking",
            UpdatePhase.Current => "update.current", UpdatePhase.Available => "update.available",
            UpdatePhase.Downloading => "update.downloading", UpdatePhase.Ready => "update.ready",
            UpdatePhase.Failed => "update.failed", _ => "update.help"
        };
        c.Text(L.Get(key, UpdateStatus.Version, UpdateStatus.Progress), x, top + 246 - 52 * compact, textSize, Foreground, right - x - 32);
        var phase = UpdateStatus.Phase;
        ActionButton(new(x, top + 296 - 72 * compact, 210, 38), L.Get("update.check"), () => RequestUpdateCheck?.Invoke(), active: true,
            enabled: phase is not (UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Unsupported));
        if (phase is UpdatePhase.Available or UpdatePhase.Ready)
            ActionButton(embedded ? UpdateRestartButtonBounds : new(x + 222, top + 296, 320, 38), L.Get(phase == UpdatePhase.Ready ? "update.restart" : "update.download"), () =>
        {
            if (phase == UpdatePhase.Ready)
            {
                // Saving checks modal state, so dismiss Settings before handing off to the host.
                if (embedded) CloseSettings();
                RequestUpdateRestart?.Invoke();
            }
            else RequestUpdateDownload?.Invoke();
        }, active: true);
        ActionButton(new(x, top + 350 - 84 * compact, embedded ? SettingsContentWidth : 220, 38), L.Get("update.notes"), () => RequestUpdateNotes?.Invoke());
        if (embedded)
        {
            float dividerY = top + 400 - 96 * compact;
            c.Line(x, dividerY, x + SettingsContentWidth, dividerY, Grid);
            float introY = dividerY + 18;
            string intro = L.Get("update.projectIntro");
            const string name = "Yumeno Himiko";
            float nameWidth = c.MeasureText(name, SettingsHintSize);
            float introWidth = c.MeasureText(intro, SettingsHintSize);
            float spaceWidth = c.MeasureText(" ", SettingsHintSize);
            float periodWidth = c.MeasureText(".", SettingsHintSize);
            if (introWidth + spaceWidth + nameWidth + periodWidth > SettingsContentWidth)
            {
                introY = SettingsParagraph(c, intro, introY);
                introWidth = -spaceWidth;
            }
            else c.Text(intro, x, introY, SettingsHintSize, Muted, SettingsContentWidth);
            float nameX = x + introWidth + spaceWidth;
            c.Text(name, nameX, introY, SettingsHintSize, Accent, nameWidth + 1);
            c.Text(".", nameX + nameWidth, introY, SettingsHintSize, Muted, periodWidth + 1);
            c.Line(nameX, introY + SettingsHintSize + 2, nameX + nameWidth, introY + SettingsHintSize + 2, Accent);
            hits.Add(new(new Rect(nameX, introY - 2, nameWidth, SettingsHintSize + 8),
                () => RequestSetupLink?.Invoke(OsuProfileUrl), true));
            float joinY = SettingsParagraph(c, L.Get("update.discordHint"), introY + 28) + 8;
            string websiteLabel = L.Get("update.projectWebsite");
            string githubLabel = L.Get("update.githubPage");
            string discordLabel = L.Get("setup.discord");
            const float buttonGap = 8;
            float websiteWidth = c.MeasureText(websiteLabel, SettingsTextSize) + SettingsControlPadding * 2;
            float githubWidth = c.MeasureText(githubLabel, SettingsTextSize) + SettingsControlPadding * 2;
            float discordWidth = c.MeasureText(discordLabel, SettingsTextSize) + SettingsControlPadding * 2;
            float extraWidth = (SettingsContentWidth - buttonGap * 2 - websiteWidth - githubWidth - discordWidth) / 3;
            websiteWidth += extraWidth;
            githubWidth += extraWidth;
            discordWidth += extraWidth;
            ActionButton(new(x, joinY, websiteWidth, SettingsControlHeight), websiteLabel,
                () => RequestSetupLink?.Invoke(ProjectWebsiteUrl));
            ActionButton(new(x + websiteWidth + buttonGap, joinY, githubWidth, SettingsControlHeight), githubLabel,
                () => RequestSetupLink?.Invoke(GithubPageUrl));
            ActionButton(new(x + websiteWidth + githubWidth + buttonGap * 2, joinY, discordWidth, SettingsControlHeight), discordLabel,
                () => RequestSetupLink?.Invoke(DiscordInviteUrl));
        }
        if (!embedded) c.Text(L.Get("update.saveHelp"), x, top + 410, 14, Muted, right - x - 32);
        if (!embedded) Button(c, new(x, 466, 200, 36), L.Get(LibraryVisible ? "update.back" : "library.editor"), () => updatesPage = false);
    }
}
