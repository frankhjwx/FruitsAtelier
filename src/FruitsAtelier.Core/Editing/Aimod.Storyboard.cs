namespace FruitsAtelier.Core;

public static partial class Aimod
{
    private sealed class StoryboardRange(int depth, double offset = 0, double repeats = 1)
    {
        public int Depth = depth;
        public double Offset = offset, Repeats = repeats;
        public double Start = double.PositiveInfinity, End = double.NegativeInfinity;
        public void Include(double start, double end) { Start = Math.Min(Start, start); End = Math.Max(End, end); }
    }

    internal static int ShortStoryboardEvents(IEnumerable<string> lines)
    {
        int count = 0;
        var stack = new Stack<StoryboardRange>();
        void Pop()
        {
            var range = stack.Pop();
            if (!double.IsFinite(range.Start)) return;
            if (stack.TryPeek(out var parent))
                parent.Include(range.Offset + range.Start, range.Offset + range.End * range.Repeats);
            else if (range.End - range.Start < 100) count++;
        }
        foreach (string line in lines)
        {
            string text = line.TrimStart(' ', '_', '\t');
            if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal)) continue;
            int depth = line.Length - text.Length;
            var fields = WorkspaceProject.Csv(text);
            if (depth == 0)
            {
                while (stack.Count > 0) Pop();
                if (fields[0] is "Sprite" or "Animation" or "4" or "6") stack.Push(new(0));
                continue;
            }
            while (stack.Count > 1 && stack.Peek().Depth >= depth) Pop();
            if (stack.Count == 0) continue;
            if (fields[0] == "L" && fields.Length >= 3)
                stack.Push(new(depth, Number(fields[1]), Math.Max(1, Number(fields[2]))));
            else if (fields[0] == "T")
            {
                // Trigger activation spans the declared window; its child times are relative.
                double start = fields.Length > 2 ? Number(fields[2]) : 0;
                double end = fields.Length > 3 && fields[3].Length > 0 ? Number(fields[3]) : double.MaxValue;
                stack.Peek().Include(start, end);
                stack.Push(new(depth, end));
            }
            else if (fields.Length >= 4 && fields[0] is "F" or "M" or "MX" or "MY" or "S" or "V" or "R" or "C" or "P")
            {
                double start = Number(fields[2]), end = fields[3].Length == 0 ? start : Number(fields[3]);
                int dimensions = fields[0] is "M" or "V" ? 2 : fields[0] == "C" ? 3 : 1;
                int steps = Math.Max(1, (fields.Length - 4) / dimensions - 1);
                stack.Peek().Include(start, start + (end - start) * steps);
            }
        }
        while (stack.Count > 0) Pop();
        return count;
    }
}
