using System.Collections.ObjectModel;

namespace FruitsAtelier.Core;

public sealed class OsuSectionLines : Collection<string>
{
    private object? snapshotIdentity;
    public OsuSectionLines() : base(new List<string>()) { }
    private OsuSectionLines(List<string> lines, object identity) : base(lines) => snapshotIdentity = identity;

    // Unchanged source sections can contain entire storyboards. Clones share an equality
    // identity, not mutable storage; every write invalidates that identity before comparison.
    internal OsuSectionLines DeepClone()
        => new(new List<string>(Items), snapshotIdentity ??= new object());

    internal bool ContentEquals(OsuSectionLines other)
        => snapshotIdentity is not null && ReferenceEquals(snapshotIdentity, other.snapshotIdentity)
            || Items.SequenceEqual(other.Items);

    protected override void InsertItem(int index, string item) { snapshotIdentity = null; base.InsertItem(index, item); }
    protected override void SetItem(int index, string item) { snapshotIdentity = null; base.SetItem(index, item); }
    protected override void RemoveItem(int index) { snapshotIdentity = null; base.RemoveItem(index); }
    protected override void ClearItems() { snapshotIdentity = null; base.ClearItems(); }

    public void AddRange(IEnumerable<string> lines) { snapshotIdentity = null; ((List<string>)Items).AddRange(lines); }
    public int RemoveAll(Predicate<string> match) { snapshotIdentity = null; return ((List<string>)Items).RemoveAll(match); }
    public void RemoveRange(int index, int count) { snapshotIdentity = null; ((List<string>)Items).RemoveRange(index, count); }
}
