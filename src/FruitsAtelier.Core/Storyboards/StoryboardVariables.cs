using System.Text;

namespace FruitsAtelier.Core;

internal sealed class StoryboardVariables
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private Dictionary<char, string>? singleCharacterValues;
    private bool lookupCurrent;

    internal void Set(string key, string value)
    {
        values[key] = value;
        lookupCurrent = false;
    }

    internal string Expand(string line, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (values.Count == 0) return line;
        if (!lookupCurrent)
        {
            singleCharacterValues = new();
            foreach (var pair in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pair.Key.Length != 2 || pair.Key[0] != '$' || pair.Key[1] == '$' || pair.Value.Contains('$'))
                { singleCharacterValues = null; break; }
                singleCharacterValues.Add(pair.Key[1], pair.Value);
            }
            lookupCurrent = true;
        }

        // Adjacent dollars can expose a new key after a replacement: $$a -> $b -> x.
        // Those lines still require insertion-ordered substitution, even with fixed-size keys.
        if (singleCharacterValues is not null && !line.Contains("$$", StringComparison.Ordinal))
        {
            int first = line.IndexOf('$');
            if (first < 0) return line;
            if (first + 1 < line.Length && singleCharacterValues.TryGetValue(line[first + 1], out string? single)
                && line.IndexOf('$', first + 1) < 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return string.Concat(line.AsSpan(0, first), single.AsSpan(), line.AsSpan(first + 2));
            }
            StringBuilder? expanded = null;
            int copied = 0;
            int nextCancellationCheck = first;
            for (int i = first; i + 1 < line.Length; i++)
            {
                if (i >= nextCancellationCheck)
                { cancellationToken.ThrowIfCancellationRequested(); nextCancellationCheck = i + 4096; }
                if (line[i] != '$' || !singleCharacterValues.TryGetValue(line[i + 1], out string? value)) continue;
                expanded ??= new(line.Length);
                expanded.Append(line, copied, i - copied).Append(value);
                copied = i + 2;
                i++;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return expanded is null ? line : expanded.Append(line, copied, line.Length - copied).ToString();
        }

        foreach (var pair in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            line = line.Length < 65536 ? line.Replace(pair.Key, pair.Value, StringComparison.Ordinal)
                : ReplaceLargeLine(line, pair.Key, pair.Value, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return line;
    }

    private static string ReplaceLargeLine(string line, string key, string value, CancellationToken cancellationToken)
    {
        StringBuilder? expanded = null;
        int copied = 0;
        while (copied < line.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int match = line.IndexOf(key, copied, StringComparison.Ordinal);
            if (match < 0) break;
            expanded ??= new(line.Length);
            expanded.Append(line, copied, match - copied).Append(value);
            copied = match + key.Length;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return expanded is null ? line : expanded.Append(line, copied, line.Length - copied).ToString();
    }
}
