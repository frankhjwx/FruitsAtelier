using System.Globalization;
using System.Text.RegularExpressions;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool TimeJumpVisible { get; private set; }
    public int TimeJumpSession { get; private set; }
    public Rect TimeDisplayBounds { get; private set; }
    public Rect TimeJumpInputBounds { get; private set; }
    public Action? RequestPasteTime { get; set; }
    private string timeJumpText = "", timeJumpError = "";

    private void OpenTimeJump()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || drag != DragKind.None) return;
        menu = -1; languageMenuOpen = false; contextItems.Clear();
        TimeJumpVisible = true; TimeJumpSession++;
        timeJumpText = Time(playhead); timeJumpError = ""; SelectInput("time", timeJumpText);
        ResetTextCaret();
    }
    public void PasteTimeJumpText(string text, int session)
    {
        if (!TimeJumpVisible || session != TimeJumpSession || ErrorVisible || DiscardConfirmationVisible) return;
        string value = text.Trim();
        timeJumpText = InsertInput("time", timeJumpText, value, 128);
        timeJumpError = "";
    }
    private void TimeJumpKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { TimeJumpVisible = false; return; }
        if (ctrl)
        {
            InputKey("time", ref timeJumpText, key, true, shift, 128, RequestPasteTime);
            return;
        }
        if (key == 13) { SubmitTimeJump(); return; }
        if (InputKey("time", ref timeJumpText, key, false, shift, 128)) timeJumpError = "";
    }
    private void SubmitTimeJump()
    {
        var match = Regex.Match(timeJumpText.Trim(), @"^(?<time>\d+:\d{1,2}:\d{1,3}|\d+(?:\.\d+)?)\s*(?:\(\d+(?:\s*,\s*\d+)*\)\s*-?)?$");
        string value = match.Groups["time"].Value;
        double time = double.NaN;
        if (match.Success && value.Contains(':'))
        {
            var parts = value.Split(':');
            if (double.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out double minutes)
                && int.TryParse(parts[1], out int seconds) && seconds < 60
                && int.TryParse(parts[2], out int milliseconds) && milliseconds < 1000)
                time = minutes * 60000 + seconds * 1000 + milliseconds;
        }
        else if (match.Success) double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out time);
        if (!double.IsFinite(time) || time < 0 || time > int.MaxValue)
        { timeJumpError = L.Get("editor.error.timestampRequired"); return; }
        SeekTo(time); TimeJumpVisible = false;
    }
    private void DrawTimeJump(ICanvas c)
    {
        if (!TimeJumpVisible) return;
        hits.Clear();
        float w = Math.Min(480, width - 40), x = (width - w) / 2, y = (height - 174) / 2;
        c.Fill(new(x, y, w, 174), Panel, 8); c.Stroke(new(x, y, w, 174), Grid, radius: 8);
        c.Text(L.Get("timeJump.title") + " [mm:ss:ms]", x + 18, y + 17, 16, Foreground, w - 36, true);
        TimeJumpInputBounds = new(x + 18, y + 48, w - 36, 38);
        c.Fill(TimeJumpInputBounds, Surface, 4); c.Stroke(TimeJumpInputBounds, timeJumpError.Length > 0 ? Error : Accent, radius: 4);
        DrawInputText(c, new(x + 28, y + 58, w - 56, 20), timeJumpText, 14, true, "time");
        hits.Add(new(TimeJumpInputBounds, () => FocusInput("time", timeJumpText, mouseX), true));
        if (timeJumpError.Length > 0) c.Text(timeJumpError, x + 18, y + 93, 11, Error, w - 36);
        Button(c, new(x + 18, y + 124, 76, 32), L.Get("timeJump.copy"), () => RequestCopyText?.Invoke(timeJumpText));
        Button(c, new(x + 98, y + 124, 76, 32), L.Get("timeJump.paste"), () => RequestPasteTime?.Invoke());
        Button(c, new(x + w - 178, y + 124, 76, 32), L.Get("mac.cancel"), () => TimeJumpVisible = false);
        Button(c, new(x + w - 98, y + 124, 80, 32), L.Get("timeJump.go"), SubmitTimeJump, true);
    }
}
