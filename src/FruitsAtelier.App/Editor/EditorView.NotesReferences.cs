using System.Globalization;
using System.Text.RegularExpressions;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record NotesObjectReference(int Start, int End, int TimeMs, int[] Numbers);
    private static readonly Regex notesReferencePattern = new(@"(?<m>[0-9]{2,}):(?<s>[0-5][0-9]):(?<ms>[0-9]{3})[ \t]+\((?<numbers>[0-9]+(?:[ \t]*,[ \t]*[0-9]+)*)\)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private string? notesReferenceText;
    private NotesObjectReference[] notesReferences = [];

    private IReadOnlyList<NotesObjectReference> NotesReferences()
    {
        if (notesReferenceText == projectNotes) return notesReferences;
        notesReferenceText = projectNotes;
        var references = new List<NotesObjectReference>();
        foreach (Match match in notesReferencePattern.Matches(projectNotes))
        {
            if (match.Index > 0 && (char.IsDigit(projectNotes[match.Index - 1]) || projectNotes[match.Index - 1] == ':')) continue;
            if (!int.TryParse(match.Groups["m"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)) continue;
            long time = minutes * 60000L + int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture) * 1000
                + int.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture);
            if (time > int.MaxValue) continue;
            var numbers = new List<int>();
            foreach (string part in match.Groups["numbers"].Value.Split(','))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1)
                { numbers.Clear(); break; }
                numbers.Add(number);
            }
            if (numbers.Count > 0) references.Add(new(match.Index, match.Index + match.Length, (int)time, numbers.ToArray()));
        }
        return notesReferences = references.ToArray();
    }

    private void LocateNotesReference(NotesObjectReference reference)
    {
        // Resolve only on activation: notes repaint must not rebuild map conversion or scan all objects.
        var numbers = ComboNumbers();
        var parents = ClipboardParents(Document).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        int start = Array.FindIndex(parents, p => (int)p.TimeMs == reference.TimeMs
            && numbers.GetValueOrDefault(p.Id) == reference.Numbers[0]);
        var ids = new List<Guid>();
        if (start >= 0)
        {
            int next = 0;
            for (int i = start; i < parents.Length && next < reference.Numbers.Length; i++)
                if (numbers.GetValueOrDefault(parents[i].Id) == reference.Numbers[next])
                { ids.Add(parents[i].Id); next++; }
            if (next != reference.Numbers.Length) ids.Clear();
        }
        notesFocused = false; tool = Tool.Select;
        // Text references may outlive their objects; the timestamp remains a useful navigation target.
        SelectObjects(ids, ids.FirstOrDefault());
        SeekTo(reference.TimeMs);
    }
}
