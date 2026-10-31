using System.Text;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

internal sealed class StoryboardExpansionBudget(int maximumCharacters = ProjectSerializer.MaximumFileBytes)
{
    internal int Remaining { get; private set; } = maximumCharacters;

    internal void Consume(int characters)
    {
        if (characters > Remaining) throw new InvalidDataException(L.Get("core.storyboard.sceneExpansionLimit"));
        Remaining -= characters;
    }
}

internal sealed class StoryboardVariables(StoryboardExpansionBudget? budget = null)
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private Dictionary<char, string>? singleCharacterValues;
    private int maximumSingleValueLength;
    private bool lookupCurrent;
    private readonly StoryboardExpansionBudget expansionBudget = budget ?? new();

    internal void Set(string key, string value)
    {
        values[key] = value;
        lookupCurrent = false;
    }

    internal string Expand(string line, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (line.Length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.storyboard.expansionLimit"));
        if (values.Count == 0) return Consume(line);
        if (!lookupCurrent)
        {
            singleCharacterValues = new();
            maximumSingleValueLength = 0;
            foreach (var pair in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pair.Key.Length != 2 || pair.Key[0] != '$' || pair.Key[1] == '$' || pair.Value.Contains('$'))
                { singleCharacterValues = null; break; }
                singleCharacterValues.Add(pair.Key[1], pair.Value);
                maximumSingleValueLength = Math.Max(maximumSingleValueLength, pair.Value.Length);
            }
            lookupCurrent = true;
        }

        // Adjacent dollars can expose a new key after a replacement: $$a -> $b -> x.
        // Those lines still require insertion-ordered substitution, even with fixed-size keys.
        if (singleCharacterValues is not null && !line.Contains("$$", StringComparison.Ordinal))
        {
            int first = line.IndexOf('$');
            if (first < 0) return Consume(line);
            if (first + 1 < line.Length && singleCharacterValues.TryGetValue(line[first + 1], out string? single)
                && line.IndexOf('$', first + 1) < 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckLength((long)line.Length - 2 + single.Length, finalOutput: true);
                return Consume(string.Concat(line.AsSpan(0, first), single.AsSpan(), line.AsSpan(first + 2)));
            }
            if (MayExceedLimit(line.Length, 2, maximumSingleValueLength, finalOutput: true))
            {
                long length = line.Length;
                for (int i = first; i + 1 < line.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (line[i] != '$' || !singleCharacterValues.TryGetValue(line[i + 1], out string? value)) continue;
                    length += value.Length - 2;
                    i++;
                }
                CheckLength(length, finalOutput: true);
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
            return Consume(expanded is null ? line : expanded.Append(line, copied, line.Length - copied).ToString());
        }

        int replacement = 0;
        foreach (var pair in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool finalOutput = ++replacement == values.Count;
            if (MayExceedLimit(line.Length, pair.Key.Length, pair.Value.Length, finalOutput))
            {
                long length = line.Length;
                for (int offset = 0; offset < line.Length;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int match = line.IndexOf(pair.Key, offset, StringComparison.Ordinal);
                    if (match < 0) break;
                    length += pair.Value.Length - pair.Key.Length;
                    offset = match + pair.Key.Length;
                }
                CheckLength(length, finalOutput);
            }
            line = line.Length < 65536 ? line.Replace(pair.Key, pair.Value, StringComparison.Ordinal)
                : ReplaceLargeLine(line, pair.Key, pair.Value, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Consume(line);
    }

    private bool MayExceedLimit(int length, int keyLength, int valueLength, bool finalOutput)
        => (long)length + (long)(length / keyLength) * Math.Max(0, valueLength - keyLength)
            > (finalOutput ? Math.Min(OsuBeatmapReader.MaximumFileBytes, expansionBudget.Remaining) : OsuBeatmapReader.MaximumFileBytes);

    private void CheckLength(long length, bool finalOutput)
    {
        if (length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.storyboard.expansionLimit"));
        if (finalOutput && length > expansionBudget.Remaining) throw new InvalidDataException(L.Get("core.storyboard.sceneExpansionLimit"));
    }

    private string Consume(string line)
    {
        CheckLength(line.Length, finalOutput: true);
        expansionBudget.Consume(line.Length);
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
