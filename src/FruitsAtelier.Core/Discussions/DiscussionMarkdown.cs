using System.Text;
using System.Text.RegularExpressions;

namespace FruitsAtelier.Core;

[Flags]
public enum DiscussionTextStyle { None = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8 }
public sealed record DiscussionSpan(string Text, DiscussionTextStyle Style = DiscussionTextStyle.None, string? Url = null, bool Image = false);
public sealed record DiscussionParagraph(IReadOnlyList<DiscussionSpan> Spans, int QuoteDepth = 0, bool Code = false);

/// <summary>The common discussion formatting subset, rendered without HTML.</summary>
public static class DiscussionMarkdown
{
    private static readonly (string Marker, DiscussionTextStyle Style)[] delimiters =
        [("***", DiscussionTextStyle.Bold | DiscussionTextStyle.Italic), ("___", DiscussionTextStyle.Bold | DiscussionTextStyle.Italic),
            ("**", DiscussionTextStyle.Bold), ("__", DiscussionTextStyle.Bold), ("~~", DiscussionTextStyle.Strike), ("*", DiscussionTextStyle.Italic), ("_", DiscussionTextStyle.Italic)];
    public static DiscussionParagraph[] Parse(string message)
    {
        var paragraphs = new List<DiscussionParagraph>();
        string? fence = null;
        foreach (string original in message.Replace("\r", "").Split('\n'))
        {
            string text = original; string trimmed = text.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                string marker = new(trimmed[0], trimmed.TakeWhile(c => c == trimmed[0]).Count());
                if (fence is null) { fence = marker; continue; }
                if (marker[0] == fence[0] && marker.Length >= fence.Length && trimmed[marker.Length..].Trim().Length == 0) { fence = null; continue; }
            }
            if (fence is not null) { paragraphs.Add(new([new(text, DiscussionTextStyle.Code)], Code: true)); continue; }
            int quote = 0;
            while (quote < 8 && text.TrimStart().StartsWith('>'))
            { quote++; text = text.TrimStart()[1..]; if (text.StartsWith(' ')) text = text[1..]; }
            var heading = Regex.Match(text, @"^ {0,3}#{1,6} +(.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var list = Regex.Match(text, @"^( *)(?:[-+*]|(?<number>[0-9]{1,9})[.)]) +", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            DiscussionTextStyle style = DiscussionTextStyle.None;
            if (heading.Success) { text = heading.Groups[1].Value; style = DiscussionTextStyle.Bold; }
            else if (list.Success) text = list.Groups[1].Value + (list.Groups["number"].Success ? list.Groups["number"].Value + ". " : "• ") + text[list.Length..];
            paragraphs.Add(new(Inline(text, style, 0), quote));
        }
        return paragraphs.ToArray();
    }

    private static IReadOnlyList<DiscussionSpan> Inline(string text, DiscussionTextStyle style, int depth)
    {
        if (depth >= 12) return [new(text, style)];
        var spans = new List<DiscussionSpan>(); var plain = new StringBuilder();
        var images = DiscussionText.Images(text).ToDictionary(image => image.Start);
        var urls = DiscussionText.Urls(text).ToDictionary(url => url.Start);
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '\\' && i + 1 < text.Length && char.IsPunctuation(text[i + 1]))
            { plain.Append(text[i + 1]); i += 2; continue; }
            if (images.TryGetValue(i, out var image))
            { Flush(); spans.Add(new("", style, image.Url, true)); i += image.Length; continue; }
            if (urls.TryGetValue(i, out var bareUrl))
            { Flush(); spans.Add(new(bareUrl.Url, style, bareUrl.Url)); i += bareUrl.Length; continue; }
            if (text[i] == '`')
            {
                int count = text[i..].TakeWhile(c => c == '`').Count(); string marker = new('`', count);
                int end = text.IndexOf(marker, i + count, StringComparison.Ordinal);
                if (end >= 0) { Flush(); spans.Add(new(text[(i + count)..end], style | DiscussionTextStyle.Code)); i = end + count; continue; }
            }
            if (text[i] == '[')
            {
                int close = text.IndexOf("](", i + 1, StringComparison.Ordinal);
                if (close >= 0)
                {
                    int end = close + 2, balance = 1;
                    for (; end < text.Length && balance > 0; end++)
                    { if (text[end] == '(') balance++; else if (text[end] == ')') balance--; }
                    if (balance == 0 && SafeUrl(text[(close + 2)..(end - 1)]) is { } url)
                    { Flush(); spans.AddRange(Inline(text[(i + 1)..close], style, depth + 1).Select(span => span with { Url = url })); i = end; continue; }
                }
            }
            if (text[i] == '<' && text.IndexOf('>', i + 1) is int angle && angle > i && SafeUrl(text[(i + 1)..angle]) is { } autoUrl)
            { Flush(); spans.Add(new(autoUrl, style, autoUrl)); i = angle + 1; continue; }
            string? delimiter = null; DiscussionTextStyle added = DiscussionTextStyle.None;
            foreach (var (marker, flag) in delimiters)
                if (text.AsSpan(i).StartsWith(marker, StringComparison.Ordinal) && i + marker.Length < text.Length && !char.IsWhiteSpace(text[i + marker.Length])
                    && !(marker.Contains('_') && i > 0 && char.IsLetterOrDigit(text[i - 1])))
                { delimiter = marker; added = flag; break; }
            if (delimiter is not null)
            {
                int end = ClosingDelimiter(text, delimiter, i + delimiter.Length);
                if (end > i + delimiter.Length && !char.IsWhiteSpace(text[end - 1]))
                { Flush(); spans.AddRange(Inline(text[(i + delimiter.Length)..end], style | added, depth + 1)); i = end + delimiter.Length; continue; }
            }
            plain.Append(text[i++]);
        }
        Flush(); return spans;
        void Flush() { if (plain.Length > 0) { spans.Add(new(plain.ToString(), style)); plain.Clear(); } }
    }

    private static int ClosingDelimiter(string text, string marker, int start)
    {
        for (int found = text.IndexOf(marker, start, StringComparison.Ordinal); found >= 0;
            found = text.IndexOf(marker, found + marker.Length, StringComparison.Ordinal))
        {
            int escapes = 0;
            for (int i = found - 1; i >= 0 && text[i] == '\\'; i--) escapes++;
            if (escapes % 2 == 0) return found;
        }
        return -1;
    }

    private static string? SafeUrl(string value)
    {
        value = value.Trim();
        if (value.StartsWith('/')) value = "https://osu.ppy.sh" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) ? value : null;
    }
}
