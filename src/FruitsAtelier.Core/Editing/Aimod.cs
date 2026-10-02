namespace FruitsAtelier.Core;

public readonly record struct NoteOverlap(Guid FirstId, Guid SecondId, double FirstTimeMs, double SecondTimeMs);

public static class Aimod
{
    public static IReadOnlyList<NoteOverlap> FindOverlaps(MapDocument document)
    {
        var notes = document.Fruits.Select(f => (f.Id, Time: f.TimeMs))
            .Concat(document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Time: t.Nodes[0].TimeMs)))
            .Concat(document.ImportedSliders.Select(s => (s.Id, Time: s.TimeMs)))
            .Concat(document.BananaShowers.Select(s => (s.Id, Time: s.TimeMs)))
            .OrderBy(n => n.Time).ToArray();
        var errors = new List<NoteOverlap>();
        // Adjacent pairs identify every involved object without quadratic output for a stack.
        for (int i = 1; i < notes.Length; i++)
            if (notes[i].Time - notes[i - 1].Time < 10)
                errors.Add(new(notes[i - 1].Id, notes[i].Id, notes[i - 1].Time, notes[i].Time));
        return errors;
    }
}
