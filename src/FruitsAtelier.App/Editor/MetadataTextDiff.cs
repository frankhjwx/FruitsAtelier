using System.Globalization;

namespace FruitsAtelier.App.Editor;

internal readonly record struct MetadataTextSpan(int Start, int Length);
internal sealed record MetadataTextDiff(IReadOnlyList<MetadataTextSpan> Local, IReadOnlyList<MetadataTextSpan> External)
{
    public static MetadataTextDiff Compare(string local, string external)
    {
        local = local.Replace("\r", ""); external = external.Replace("\r", "");
        int[] a = StringInfo.ParseCombiningCharacters(local).Append(local.Length).ToArray();
        int[] b = StringInfo.ParseCombiningCharacters(external).Append(external.Length).ToArray();
        bool Equal(int i, int j) => local.AsSpan(a[i], a[i + 1] - a[i]).SequenceEqual(external.AsSpan(b[j], b[j + 1] - b[j]));
        int start = 0, endA = a.Length - 1, endB = b.Length - 1;
        while (start < endA && start < endB && Equal(start, start)) start++;
        while (endA > start && endB > start && Equal(endA - 1, endB - 1)) { endA--; endB--; }
        var ours = new List<MetadataTextSpan>(); var theirs = new List<MetadataTextSpan>();
        int n = endA - start, m = endB - start;
        if (n == 0 && m == 0) return new(ours, theirs);
        // Bound comparison memory for unusually large metadata; the common prefix
        // and suffix still retain their normal colour when the middle is replaced.
        if ((long)(n + 1) * (m + 1) > 1_000_000)
            return new([new(a[start], a[endA] - a[start])], [new(b[start], b[endB] - b[start])]);
        var lengths = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lengths[i, j] = Equal(start + i, start + j) ? 1 + lengths[i + 1, j + 1] : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        int x = 0, y = 0, fromX = -1, fromY = -1;
        void Flush()
        {
            if (fromX < 0) return;
            ours.Add(new(a[start + fromX], a[start + x] - a[start + fromX]));
            theirs.Add(new(b[start + fromY], b[start + y] - b[start + fromY]));
            fromX = fromY = -1;
        }
        while (x < n || y < m)
        {
            if (x < n && y < m && Equal(start + x, start + y)) { Flush(); x++; y++; }
            else
            {
                if (fromX < 0) { fromX = x; fromY = y; }
                if (x < n && (y == m || lengths[x + 1, y] >= lengths[x, y + 1])) x++; else y++;
            }
        }
        Flush(); return new(ours, theirs);
    }
}
