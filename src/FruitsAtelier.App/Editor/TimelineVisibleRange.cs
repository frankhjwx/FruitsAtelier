namespace FruitsAtelier.App.Editor;

internal sealed class TimelineVisibleRange
{
    private double[] starts = [], maximumEnds = [];
    private int leafCount;

    internal void Rebuild(double[] orderedStarts, double[] ends)
    {
        starts = orderedStarts;
        leafCount = 1;
        while (leafCount < starts.Length) leafCount *= 2;
        maximumEnds = new double[leafCount * 2];
        Array.Fill(maximumEnds, double.NegativeInfinity);
        ends.CopyTo(maximumEnds, leafCount);
        for (int i = leafCount - 1; i > 0; i--)
            maximumEnds[i] = Math.Max(maximumEnds[i * 2], maximumEnds[i * 2 + 1]);
    }

    internal void UpdateEnd(int index, double end)
    {
        int node = leafCount + index;
        maximumEnds[node] = end;
        while ((node /= 2) > 0)
            maximumEnds[node] = Math.Max(maximumEnds[node * 2], maximumEnds[node * 2 + 1]);
    }

    internal void Fill(double from, double through, List<int> result)
    {
        result.Clear();
        Visit(1, 0, leafCount);

        void Visit(int node, int first, int after)
        {
            if (first >= starts.Length || starts[first] > through || maximumEnds[node] < from) return;
            if (after - first == 1) { result.Add(first); return; }
            int middle = first + (after - first) / 2;
            // Reverse chronological traversal preserves the timeline's paint and hit order.
            Visit(node * 2 + 1, middle, after);
            Visit(node * 2, first, middle);
        }
    }
}
