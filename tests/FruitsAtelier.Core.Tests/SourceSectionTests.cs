using System.Collections;
using FruitsAtelier.Core;

internal static class SourceSectionTests
{
    public static void SnapshotsAndWrites()
    {
        var original = new MapDocument();
        var section = new OsuSection { Name = "Events" };
        section.Lines.AddRange(["// first", "// second", "// third"]);
        original.OriginalSections.Add(section);
        Action<OsuSectionLines>[] changes = [
            lines => lines[1] = "// replacement",
            lines => ((IList<string>)lines)[1] = "// generic replacement",
            lines => ((IList)lines)[1] = "// non-generic replacement",
            lines => lines.Add("// added"), lines => lines.Insert(1, "// inserted"),
            lines => lines.RemoveAt(1), lines => lines.Remove("// second"), lines => lines.Clear(),
            lines => lines.AddRange(["// fourth", "// fifth"]),
            lines => lines.RemoveAll(line => line == "// second"), lines => lines.RemoveRange(0, 2)
        ];
        foreach (var change in changes)
        {
            var copy = original.DeepClone();
            var sibling = copy.DeepClone();
            Check(copy.ContentEquals(original) && sibling.ContentEquals(copy), "Unchanged snapshots differ.");
            change(copy.OriginalSections[0].Lines);
            Check(!copy.ContentEquals(original) && !original.ContentEquals(copy), "Source write escaped equality invalidation.");
            Check(sibling.ContentEquals(original), "Editing a clone modified another snapshot.");
            var edited = copy.DeepClone();
            Check(edited.ContentEquals(copy), "Edited snapshot differs from its clone.");
            copy.OriginalSections[0].Lines.Clear();
            copy.OriginalSections[0].Lines.AddRange(original.OriginalSections[0].Lines);
            Check(copy.ContentEquals(original), "Equal content with different snapshot identities differs.");
        }
        var first = original.DeepClone(); var second = original.DeepClone();
        first.OriginalSections[0].Lines[0] = "// A"; second.OriginalSections[0].Lines[0] = "// B";
        Check(!first.ContentEquals(second), "Separate writes to sibling snapshots compare equal.");
        var saved = original.DeepClone();
        original.OriginalSections[0].Lines[0] = "// changed original";
        Check(!original.ContentEquals(saved), "Changing the original modified its snapshot identity.");
        var history = new EditorHistory(original);
        history.Begin("Source edit"); history.Document.OriginalSections[0].Lines[0] = "// history"; history.Commit();
        Check(history.IsDirty, "Source edit did not dirty the project.");
        history.Undo(); Check(!history.IsDirty, "Source undo did not restore the saved state.");
        history.Redo(); Check(history.IsDirty, "Source redo did not restore the edit.");
        var loaded = ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document));
        Check(loaded.ContentEquals(history.Document), "Section lines changed through project persistence.");
        loaded.OriginalSections[0].Lines[0] = "// loaded edit";
        Check(!loaded.ContentEquals(history.Document), "Loaded sections share mutable storage.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
