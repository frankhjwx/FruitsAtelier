using System.Globalization;
using System.Text.RegularExpressions;

namespace FruitsAtelier.Core;

public sealed record EditorTimestamp(double TimeMs, int[]? ComboNumbers)
{
    public static bool IsOsuLink(string text) => text.StartsWith("osu://", StringComparison.OrdinalIgnoreCase);
    public static bool IsEditorLink(string text) => text.StartsWith("osu://edit/", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string text, out EditorTimestamp? timestamp)
    {
        timestamp = null;
        if (text.Length > 32768) return false;
        text = text.Trim();
        if (IsOsuLink(text))
        {
            if (!IsEditorLink(text)) return false;
            text = text[11..];
            if (Regex.IsMatch(text, @"%(?![0-9a-fA-F]{2})")) return false;
            text = Uri.UnescapeDataString(text);
        }
        var match = Regex.Match(text, @"^(?<time>[0-9]+:[0-5]?[0-9][:.][0-9]{1,3}|[0-9]+(?:\.[0-9]+)?)\s*(?:\((?<selection>[0-9]+(?:\s*,\s*[0-9]+)*)\))?(?:\s*-\s*)?$");
        if (!match.Success) return false;
        string value = match.Groups["time"].Value;
        double time;
        if (value.Contains(':'))
        {
            var parts = value.Split([':', '.']);
            if (!double.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out double minutes)) return false;
            time = minutes * 60000 + int.Parse(parts[1], CultureInfo.InvariantCulture) * 1000 + int.Parse(parts[2], CultureInfo.InvariantCulture);
        }
        else if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out time)) return false;
        if (!double.IsFinite(time) || time < 0 || time > int.MaxValue) return false;
        int[]? numbers = null;
        if (match.Groups["selection"].Success)
        {
            var values = match.Groups["selection"].Value.Split(',');
            numbers = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
                if (!int.TryParse(values[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }
        timestamp = new(time, numbers);
        return true;
    }
}
