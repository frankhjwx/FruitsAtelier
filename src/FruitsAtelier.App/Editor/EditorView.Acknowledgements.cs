using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool AcknowledgementsVisible { get; private set; }
    public bool StartupDialogVisible => AcknowledgementsVisible || FirstRunSetupVisible;
    internal bool StartupHeaderDraggable => AcknowledgementsVisible ? !acknowledgementsScrollDragging : FirstRunHeaderDraggable;
    public static (double Width, double Height) AcknowledgementsWindowSize(double workWidth, double workHeight, double scale)
        => (Math.Min(820, workWidth * .85 / scale), Math.Min(740, workHeight * .85 / scale));
    public Action? RequestAcknowledgementsAccepted { get; set; }
    private Action? acknowledgementsContinuation;
    private string? acknowledgementsSettingsPath;
    private bool acknowledgementsAgreed, acknowledgementsScrollDragging;
    private float acknowledgementsScroll, acknowledgementsContentHeight, acknowledgementsScrollOffset;
    private int acknowledgementsFocus;
    private string acknowledgementsError = "";
    private Rect acknowledgementsTextBounds, acknowledgementsScrollTrack, acknowledgementsScrollThumb;
    internal Rect AcknowledgementsCheckboxBounds { get; private set; }
    internal Rect AcknowledgementsCancelBounds { get; private set; }
    internal Rect AcknowledgementsConfirmBounds { get; private set; }
    internal bool AcknowledgementsAgreed => acknowledgementsAgreed;
    internal float AcknowledgementsScroll => acknowledgementsScroll;
    private (Type Canvas, float Width)? acknowledgementsLayoutKey;
    private readonly List<(string Text, float Y, bool Heading)> acknowledgementsLines = [];
    private static readonly string[] AcknowledgementsSections = ["development", "editing", "privacy", "ranked", "descriptions"];

    internal bool BeginAcknowledgements(Action continuation, string? settingsPath = null)
    {
        if (AcknowledgementsPreference.IsAccepted(settingsPath)) return false;
        CancelInteraction();
        acknowledgementsContinuation = continuation;
        acknowledgementsSettingsPath = settingsPath;
        acknowledgementsAgreed = acknowledgementsScrollDragging = false;
        acknowledgementsScroll = 0;
        acknowledgementsFocus = 0;
        acknowledgementsError = "";
        AcknowledgementsVisible = true;
        hits.Clear(); fields.Clear();
        return true;
    }

    private void ConfirmAcknowledgements()
    {
        if (!AcknowledgementsVisible || !acknowledgementsAgreed) return;
        try { AcknowledgementsPreference.Accept(acknowledgementsSettingsPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            acknowledgementsError = L.Get("acknowledgements.saveFailed");
            return;
        }
        var continuation = acknowledgementsContinuation;
        acknowledgementsContinuation = null;
        AcknowledgementsVisible = false;
        acknowledgementsScrollDragging = false;
        hits.Clear(); fields.Clear();
        continuation?.Invoke();
        RequestAcknowledgementsAccepted?.Invoke();
    }

    private void DrawAcknowledgements(ICanvas c)
    {
        float w = width, h = height;
        var r = new Rect(0, 0, w, h);
        c.Fill(r, Panel);
        c.Text(L.Get("acknowledgements.title"), r.X + 24, r.Y + 22, 24, Foreground, w - 48, true);
        acknowledgementsTextBounds = new(r.X + 24, r.Y + 66, w - 48, h - 242);
        var text = acknowledgementsTextBounds;
        c.Fill(text, Background, 4); c.Stroke(text, Grid, radius: 4);
        LayoutAcknowledgements(c, text.Width - 44);
        ClampAcknowledgementsScroll();
        var clip = new Rect(text.X + 14, text.Y + 12, text.Width - 44, text.Height - 24);
        c.Clip(clip);
        foreach (var line in acknowledgementsLines)
        {
            float y = clip.Y + line.Y - acknowledgementsScroll;
            if (y + 24 < clip.Y || y >= clip.Bottom) continue;
            c.Text(line.Text, clip.X, y, line.Heading ? 17 : 15, Foreground, clip.Width, line.Heading);
        }
        c.Unclip();
        acknowledgementsScrollTrack = new(text.Right - 14, text.Y + 8, 6, text.Height - 16);
        acknowledgementsScrollThumb = default;
        if (AcknowledgementsMaxScroll > 0)
        {
            var track = acknowledgementsScrollTrack;
            float thumbHeight = Math.Max(24, track.Height * clip.Height / acknowledgementsContentHeight);
            acknowledgementsScrollThumb = new(track.X, track.Y + acknowledgementsScroll / AcknowledgementsMaxScroll * (track.Height - thumbHeight), track.Width, thumbHeight);
            c.Fill(track, Surface, 3); c.Fill(acknowledgementsScrollThumb, Muted, 3);
        }

        AcknowledgementsCheckboxBounds = new(r.X + 24, r.Bottom - 134, w - 48, 48);
        var check = new Rect(AcknowledgementsCheckboxBounds.X, AcknowledgementsCheckboxBounds.Y + 5, 20, 20);
        c.Fill(check, acknowledgementsAgreed ? Accent : Background, 3); c.Stroke(check, acknowledgementsFocus == 1 ? Accent : Muted, radius: 3);
        if (acknowledgementsAgreed)
        {
            c.Line(check.X + 4, check.Y + 10, check.X + 8, check.Y + 14, Background, 2);
            c.Line(check.X + 8, check.Y + 14, check.X + 16, check.Y + 6, Background, 2);
        }
        var checkboxLines = WrapAcknowledgements(c, L.Get("acknowledgements.agree"), w - 82);
        for (int i = 0; i < checkboxLines.Length; i++)
            c.Text(checkboxLines[i], check.Right + 12, check.Y + i * 20, 15, Foreground, w - 82);
        if (acknowledgementsError.Length > 0)
            c.Text(acknowledgementsError, r.X + 24, r.Bottom - 168, 13, Error, w - 48);
        AcknowledgementsCancelBounds = new(r.Right - 312, r.Bottom - 64, 120, 40);
        AcknowledgementsConfirmBounds = new(r.Right - 176, r.Bottom - 64, 152, 40);
        c.Fill(AcknowledgementsCancelBounds, Surface, 4); c.Stroke(AcknowledgementsCancelBounds, Grid, radius: 4);
        c.Fill(AcknowledgementsConfirmBounds, acknowledgementsAgreed ? Surface : Background, 4);
        c.Stroke(AcknowledgementsConfirmBounds, Grid, radius: 4);
        Button(c, AcknowledgementsCancelBounds, L.Get("acknowledgements.cancel"), () => RequestClose?.Invoke(), fontSize: 15);
        Button(c, AcknowledgementsConfirmBounds, L.Get("acknowledgements.confirm"), ConfirmAcknowledgements,
            active: acknowledgementsAgreed, enabled: acknowledgementsAgreed, fontSize: 15);
        if (acknowledgementsFocus == 2) c.Stroke(AcknowledgementsCancelBounds, Accent, radius: 4);
        if (acknowledgementsFocus == 3 && acknowledgementsAgreed) c.Stroke(AcknowledgementsConfirmBounds, Accent, 2, 4);
    }

    private void LayoutAcknowledgements(ICanvas c, float textWidth)
    {
        if (acknowledgementsLayoutKey == (c.GetType(), textWidth)) return;
        acknowledgementsLines.Clear();
        float y = 0;
        Paragraph(L.Get("acknowledgements.intro"));
        foreach (string section in AcknowledgementsSections)
        {
            acknowledgementsLines.Add((L.Get("acknowledgements." + section + "Title"), y, true));
            y += 30;
            Paragraph(L.Get("acknowledgements." + section));
        }
        acknowledgementsContentHeight = y;
        acknowledgementsLayoutKey = (c.GetType(), textWidth);

        void Paragraph(string value)
        {
            foreach (string line in WrapAcknowledgements(c, value, textWidth))
            { acknowledgementsLines.Add((line, y, false)); y += 22; }
            y += 16;
        }
    }

    private static string[] WrapAcknowledgements(ICanvas c, string value, float textWidth)
    {
        var lines = new List<string>();
        string line = "";
        foreach (string word in value.Split(' '))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && c.MeasureText(next, 15) > textWidth)
            { lines.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return lines.ToArray();
    }

    private float AcknowledgementsMaxScroll => Math.Max(0, acknowledgementsContentHeight - acknowledgementsTextBounds.Height + 24);
    private void ClampAcknowledgementsScroll() => acknowledgementsScroll = Math.Clamp(acknowledgementsScroll, 0, AcknowledgementsMaxScroll);

    private void AcknowledgementsPointerDown(float x, float y, int button)
    {
        mouseX = x; mouseY = y;
        if (button != 0) return;
        if (AcknowledgementsCheckboxBounds.Contains(x, y))
        {
            acknowledgementsFocus = 1;
            acknowledgementsAgreed = !acknowledgementsAgreed;
            return;
        }
        if (AcknowledgementsMaxScroll > 0 && x >= acknowledgementsScrollTrack.X - 6 && x < acknowledgementsScrollTrack.Right + 6
            && y >= acknowledgementsScrollTrack.Y && y < acknowledgementsScrollTrack.Bottom)
        {
            acknowledgementsScrollDragging = true;
            acknowledgementsFocus = 0;
            acknowledgementsScrollOffset = acknowledgementsScrollThumb.Contains(x, y) ? y - acknowledgementsScrollThumb.Y : acknowledgementsScrollThumb.Height / 2;
            MoveAcknowledgementsScroll(y);
            return;
        }
        if (acknowledgementsTextBounds.Contains(x, y)) acknowledgementsFocus = 0;
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
    }

    private void MoveAcknowledgementsScroll(float y)
    {
        float travel = acknowledgementsScrollTrack.Height - acknowledgementsScrollThumb.Height;
        if (travel > 0) acknowledgementsScroll = (y - acknowledgementsScrollTrack.Y - acknowledgementsScrollOffset) / travel * AcknowledgementsMaxScroll;
        ClampAcknowledgementsScroll();
    }

    private void AcknowledgementsKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { RequestClose?.Invoke(); return; }
        if (ctrl || altHeld) return;
        if (key == 9)
        {
            int count = acknowledgementsAgreed ? 4 : 3;
            acknowledgementsFocus = (acknowledgementsFocus + (shift ? count - 1 : 1)) % count;
        }
        else if (key is 32 or 13 && acknowledgementsFocus == 1) acknowledgementsAgreed = !acknowledgementsAgreed;
        else if (key == 13 && acknowledgementsFocus == 2) RequestClose?.Invoke();
        else if (key == 13 && acknowledgementsFocus == 3) ConfirmAcknowledgements();
        else if (key is 38 or 40 or 33 or 34 or 36 or 35)
        {
            acknowledgementsScroll = key switch
            {
                38 => acknowledgementsScroll - 22, 40 => acknowledgementsScroll + 22,
                33 => acknowledgementsScroll - acknowledgementsTextBounds.Height + 24,
                34 => acknowledgementsScroll + acknowledgementsTextBounds.Height - 24,
                36 => 0, _ => AcknowledgementsMaxScroll
            };
            ClampAcknowledgementsScroll();
        }
    }
}
