using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<string>? RequestSelectOsuLinkFallback { get; set; }
    public Action? RequestClearOsuLinkBinding { get; set; }
    public Action? RequestBindOsuLinks { get; set; }
    public Func<IReadOnlyList<OsuLinkApplication>>? ReadOsuLinkApplications { get; set; }
    public Func<(string Target, bool IsDefault, bool TargetValid)>? ReadOsuLinkSettings { get; set; }
    private (string Target, bool IsDefault, bool TargetValid) osuLinkState;
    public bool IsOsuLinkDefault => osuLinkState.IsDefault;
    internal Rect OsuLinkTargetButtonBounds { get; private set; }
    internal Rect OsuLinkFallbackButtonBounds { get; private set; }
    internal Rect OsuLinkConfirmButtonBounds { get; private set; }
    internal Rect OsuLinkClearButtonBounds { get; private set; }
    public bool OsuLinkFallbackRequired { get; private set; }
    private IReadOnlyList<OsuLinkApplication> osuLinkApplications = [];
    private bool osuLinkPickerOpen;
    private string osuLinkPendingApplication = "";
    private int osuLinkFirstApplication;
    private int osuLinkFocusedApplication, osuLinkVisibleRows = 1;
    private Rect osuLinkApplicationList;
    private bool OsuLinkPickerVisible => OsuLinkFallbackRequired || osuLinkPickerOpen;
    private string? osuLinkNotice;
    private long osuLinkNoticeStarted;
    public bool OsuLinkNoticeNeedsRedraw => osuLinkNotice is not null;
    internal float OsuLinkNoticeOpacity => osuLinkNotice is null ? 0
        : (float)Math.Clamp((3.5 - timeProvider.GetElapsedTime(osuLinkNoticeStarted).TotalSeconds) / .5, 0, 1);
    public void ShowOsuLinkNotice(string key)
        => ShowEditorNotice(key);

    private void ShowEditorNotice(string key)
    {
        osuLinkNotice = key; osuLinkNoticeStarted = timeProvider.GetTimestamp();
        SetNotice(L.Get(key));
    }
    private void DrawOsuLinkNotice(ICanvas c)
    {
        if (osuLinkNotice is null) return;
        float opacity = OsuLinkNoticeOpacity;
        if (opacity <= 0) { osuLinkNotice = null; return; }
        float w = Math.Min(900, width - 32);
        var bounds = new Rect((width - w) / 2, 48, w, 48);
        c.Fill(bounds, Panel, 6, opacity); c.StrokeOpacity(bounds, Accent, 1, 6, opacity);
        c.TextOpacity(L.Get(osuLinkNotice), bounds.X + 16, bounds.Y + 16, 12, Foreground, bounds.Width - 32, false, opacity);
    }
    public void RefreshOsuLinkSettings(bool refreshApplications = false)
    {
        osuLinkState = ReadOsuLinkSettings?.Invoke() ?? ("", false, false);
        bool required = osuLinkState.IsDefault && !osuLinkState.TargetValid;
        if (required && !OsuLinkFallbackRequired)
        {
            CancelInteraction(preserveTestplay: true);
            LoadOsuLinkApplications();
        }
        else if (refreshApplications && OsuLinkPickerVisible) LoadOsuLinkApplications(resetSelection: false);
        OsuLinkFallbackRequired = required;
        if (!osuLinkState.IsDefault) osuLinkPickerOpen = false;
    }

    internal void LoadOsuLinkApplications(bool resetSelection = true)
    {
        osuLinkApplications = ReadOsuLinkApplications?.Invoke() ?? [];
        if (resetSelection) osuLinkPendingApplication = osuLinkState.Target;
        if (!osuLinkApplications.Any(a => a.Id == osuLinkPendingApplication)) osuLinkPendingApplication = "";
        osuLinkFirstApplication = 0;
        osuLinkFocusedApplication = Math.Max(0, osuLinkApplications.ToList().FindIndex(a => a.Id == osuLinkPendingApplication));
    }

    private void OpenOsuLinkPicker()
    {
        if (!osuLinkState.IsDefault) return;
        LoadOsuLinkApplications(); osuLinkPickerOpen = true;
    }

    private void ConfirmOsuLinkApplication()
    {
        if (!osuLinkApplications.Any(a => a.Id == osuLinkPendingApplication)) return;
        RequestSelectOsuLinkFallback?.Invoke(osuLinkPendingApplication);
        RefreshOsuLinkSettings();
        if (!OsuLinkFallbackRequired) osuLinkPickerOpen = false;
    }

    private void DrawOsuLinkFallback(ICanvas c)
    {
        if (!OsuLinkPickerVisible || ErrorVisible) return;
        hits.Clear(); fields.Clear();
        string title = L.Get("osuLinks.setup");
        float w = Math.Min(Math.Max(460, c.MeasureText(title, 16, true) + 40), width - 40);
        int rows = Math.Min(Math.Max(1, osuLinkApplications.Count), Math.Max(1, (int)((height - 236) / 56)));
        osuLinkVisibleRows = rows;
        osuLinkFirstApplication = Math.Clamp(osuLinkFirstApplication, 0, Math.Max(0, osuLinkApplications.Count - rows));
        float h = 132 + rows * 56, x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(0, 0, width, height), Background, opacity: .85f);
        c.Fill(new(x, y, w, h), Panel, 8);
        c.Text(title, x + 20, y + 24, 16, Foreground, w - 40, true);
        osuLinkApplicationList = new(x + 12, y + 64, w - 24, rows * 56);
        OsuLinkFallbackButtonBounds = new(x + 12, y + 64, w - 24, 52);
        if (osuLinkApplications.Count == 0)
            c.Text(L.Get("osuLinks.none"), x + 20, y + 80, 13, Muted, w - 40);
        for (int row = 0; row < rows && row + osuLinkFirstApplication < osuLinkApplications.Count; row++)
        {
            int index = row + osuLinkFirstApplication;
            var app = osuLinkApplications[index];
            var bounds = new Rect(x + 12, y + 64 + row * 56, w - 24, 52);
            bool selected = app.Id == osuLinkPendingApplication;
            if (row + osuLinkFirstApplication == osuLinkFocusedApplication || bounds.Contains(mouseX, mouseY)) c.Fill(bounds, Surface, 4);
            c.Image(app.IconPath, new(bounds.X + 10, bounds.Y + 10, 32, 32));
            DrawOsuLinkApplicationName(c, app, bounds.X + 54, bounds.Y + 18, bounds.Width - 88, 14);
            if (selected) c.Text("✓", bounds.Right - 28, bounds.Y + 17, 14, Accent, 20);
            hits.Add(new(bounds, () => { osuLinkPendingApplication = app.Id; osuLinkFocusedApplication = index; }, true));
        }
        if (osuLinkApplications.Count > rows)
        {
            float track = rows * 56, thumb = track * rows / osuLinkApplications.Count;
            c.Fill(new(x + w - 8, y + 64 + (track - thumb) * osuLinkFirstApplication / (osuLinkApplications.Count - rows), 3, thumb), Muted, 1);
        }
        OsuLinkConfirmButtonBounds = new(x + w - 120, y + h - 52, 100, 32);
        SettingsButton(c, OsuLinkConfirmButtonBounds, L.Get("osuLinks.confirm"), ConfirmOsuLinkApplication,
            enabled: osuLinkApplications.Any(a => a.Id == osuLinkPendingApplication));
    }

    private void OsuLinkPickerPointerDown(float x, float y, int button)
    {
        mouseX = x; mouseY = y;
        if (button == 0)
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return; }
        if (!OsuLinkFallbackRequired) osuLinkPickerOpen = false;
    }

    private void OsuLinkPickerKey(int key)
    {
        if (key == 27 && !OsuLinkFallbackRequired) osuLinkPickerOpen = false;
        if (key is 38 or 40 && osuLinkApplications.Count > 0)
        {
            osuLinkFocusedApplication = Math.Clamp(osuLinkFocusedApplication + (key == 38 ? -1 : 1), 0, osuLinkApplications.Count - 1);
            osuLinkPendingApplication = osuLinkApplications[osuLinkFocusedApplication].Id;
            osuLinkFirstApplication = Math.Clamp(osuLinkFirstApplication, Math.Max(0, osuLinkFocusedApplication - osuLinkVisibleRows + 1), osuLinkFocusedApplication);
        }
        if (key == 13) ConfirmOsuLinkApplication();
    }

    public bool HandleEditorLink(string link)
    {
        if (!HasEditorProject || LibraryVisible || librarySettingsOpen || resourcePage || updatesPage || IsTestplaying || StartupDialogVisible)
        { ShowOsuLinkNotice("osuLinks.editModeRequired"); return false; }
        if (!EditorTimestamp.IsEditorLink(link) || !EditorTimestamp.TryParse(link, out var timestamp))
        { ShowOsuLinkNotice("osuLinks.invalid"); return false; }
        if (OsuLinkPickerVisible || !CanDropAudio || !ClipboardInteractionReady || TimeJumpVisible || IsEditingText)
        { ShowOsuLinkNotice("osuLinks.busy"); return false; }
        ApplyEditorTimestamp(timestamp!);
        return true;
    }

    private void ApplyEditorTimestamp(EditorTimestamp timestamp)
    {
        osuLinkNotice = null;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        SelectObjects([]);
        double time = timestamp.TimeMs;
        if (timestamp.ComboNumbers is { } numbers)
        {
            EnsureConversion();
            var starts = ClipboardParents(Document).ToDictionary(p => (p.Id, EventIndex: 0), p => p.TimeMs);
            var streams = Document.Tracks.Where(t => t.StreamSnapDivisor is not null).Select(t => t.Id).ToHashSet();
            foreach (var item in conversion!.Objects.Where(o => streams.Contains(o.SourceId)))
                starts[(item.SourceId, item.EventIndex)] = item.TimeMs;
            foreach (var group in bananaSegments)
                for (int i = 0; i < group.Value.Count; i++) starts[(group.Key, i)] = group.Value[i].TimeMs;
            var comboNumbers = ComboEventNumbers();
            var entries = ComboEntries().Where(e => starts.TryGetValue((e.Id, e.EventIndex), out double start) && start >= time)
                .Select(e => (e.Id, Time: starts[(e.Id, e.EventIndex)], Number: comboNumbers[(e.Id, e.EventIndex)])).ToArray();
            if (entries.Length > 0) time = entries[0].Time;
            var selected = new List<Guid>();
            var candidates = entries.GroupBy(e => e.Number).ToDictionary(g => g.Key, g => new Queue<(Guid Id, double Time, int Number)>(g));
            double selectionTime = time;
            foreach (int number in numbers)
            {
                if (number < 1 || !candidates.TryGetValue(number, out var queue)) continue;
                while (queue.TryPeek(out var earlier) && earlier.Time < selectionTime) queue.Dequeue();
                if (!queue.TryDequeue(out var found)) continue;
                selected.Add(found.Id); selectionTime = found.Time;
            }
            TimingPageVisible = false;
            SelectObjects(selected);
        }
        SeekTo(time);
    }

    private void DrawOsuLinkSettings(ICanvas c)
    {
        float top = SettingsParagraph(c, L.Get("osuLinks.description"), SettingsTop + 128) + 12;
        c.Text(L.Get("osuLinks.fallback"), SettingsContentX, top, SettingsTextSize, Foreground, SettingsContentWidth);
        OsuLinkTargetButtonBounds = new(SettingsContentX, top + 28, SettingsContentWidth, SettingsControlHeight);
        var selected = osuLinkApplications.FirstOrDefault(a => a.Id == osuLinkState.Target);
        SettingsButton(c, OsuLinkTargetButtonBounds, "", OpenOsuLinkPicker, enabled: osuLinkState.IsDefault);
        if (selected is not null)
        {
            c.Image(selected.IconPath, new(OsuLinkTargetButtonBounds.X + 8, OsuLinkTargetButtonBounds.Y + 4, 24, 24));
            DrawOsuLinkApplicationName(c, selected, OsuLinkTargetButtonBounds.X + 40, OsuLinkTargetButtonBounds.Y + 9,
                OsuLinkTargetButtonBounds.Width - 76, 13);
        }
        else c.Text(L.Get("osuLinks.choose"), OsuLinkTargetButtonBounds.X + 12, OsuLinkTargetButtonBounds.Y + 9, 13,
            Muted, OsuLinkTargetButtonBounds.Width - 48);
        c.Text("▾", OsuLinkTargetButtonBounds.Right - 24, OsuLinkTargetButtonBounds.Y + 9, 13, Muted, 16);
        OsuLinkClearButtonBounds = new(SettingsContentX, top + 76, Math.Min(280, SettingsContentWidth), SettingsControlHeight);
        SettingsButton(c, OsuLinkClearButtonBounds,
            L.Get(osuLinkState.IsDefault ? "osuLinks.clearBinding" : "osuLinks.bind"),
            () => { if (osuLinkState.IsDefault) RequestClearOsuLinkBinding?.Invoke(); else RequestBindOsuLinks?.Invoke(); },
            enabled: osuLinkState.IsDefault ? RequestClearOsuLinkBinding is not null : RequestBindOsuLinks is not null);
    }

    private static void DrawOsuLinkApplicationName(ICanvas c, OsuLinkApplication app, float x, float y, float width, float size)
    {
        float nameWidth = Math.Min(c.MeasureText(app.Name, size), width * .45f);
        c.Text(app.Name, x, y, size, Foreground, nameWidth);
        if (app.Path.Length > 0) c.Text(app.Path, x + nameWidth + 12, y + 2, 11, Muted, Math.Max(1, width - nameWidth - 12));
    }
}
