using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly List<(Rect Bounds, StoryboardSample Sample)> hitsoundingStoryboardHits = [];
    private readonly HashSet<StoryboardSample> hitsoundingStoryboardSelection = [];
    private StoryboardSample[] hitsoundingStoryboardClipboard = [];
    private StoryboardSample? hitsoundingStoryboardMove;
    private double hitsoundingStoryboardMoveTime;
    private (float X, float Y) hitsoundingStoryboardMoveStart;
    private bool hitsoundingStoryboardMoved;

    private bool BeginHitsoundingStoryboardPointer(float x, float y, int button, bool ctrl)
    {
        var hit = hitsoundingStoryboardHits.LastOrDefault(h => h.Bounds.Contains(x, y));
        if (hit.Sample.Path is null) return false;
        if (!CommitField()) return true;
        if (button == 0 && ctrl)
        {
            if (!hitsoundingStoryboardSelection.Add(hit.Sample)) hitsoundingStoryboardSelection.Remove(hit.Sample);
            SyncHitsoundingNoteSelection();
            return true;
        }
        hitsoundingMultiSelection.Clear(); hitsoundingStoryboardSelection.Clear();
        hitsoundingSelected = null; hitsoundingSelectedTiming = null; SelectObjects([]);
        hitsoundingStoryboardSelection.Add(hit.Sample);
        if (button == 2) DeleteHitsoundingStoryboardSelection();
        else if (button == 0)
        {
            hitsoundingStoryboardMove = hit.Sample;
            hitsoundingStoryboardMoveTime = hit.Sample.TimeMs;
            hitsoundingStoryboardMoveStart = (x, y); hitsoundingStoryboardMoved = false;
        }
        return true;
    }
    private bool MoveHitsoundingStoryboard(float x, float y)
    {
        if (hitsoundingStoryboardMove is not { } sample) return false;
        hitsoundingStoryboardMoved |= Math.Abs(x - hitsoundingStoryboardMoveStart.X) > ContentDragThreshold || Math.Abs(y - hitsoundingStoryboardMoveStart.Y) > ContentDragThreshold;
        if (hitsoundingStoryboardMoved)
            hitsoundingStoryboardMoveTime = Math.Max(0, SnapTiming().Snap(sample.TimeMs + (x - hitsoundingStoryboardMoveStart.X) / hitsoundingGridBounds.Width * hitsoundingSpan, divisor));
        return true;
    }
    private bool EndHitsoundingStoryboardMove()
    {
        if (hitsoundingStoryboardMove is not { } sample) return false;
        hitsoundingStoryboardMove = null;
        if (hitsoundingStoryboardMoved && hitsoundingStoryboardMoveTime != sample.TimeMs)
            EditHitsoundingStoryboard([sample], [sample with { TimeMs = hitsoundingStoryboardMoveTime }], move: true);
        hitsoundingStoryboardMoved = false;
        return true;
    }
    private void DeleteHitsoundingStoryboardSelection()
    {
        if (hitsoundingStoryboardSelection.Count > 0) EditHitsoundingStoryboard(hitsoundingStoryboardSelection.ToArray(), []);
    }
    private void EditHitsoundingStoryboard(StoryboardSample[] remove, StoryboardSample[] add, bool move = false, Action? editNotes = null)
    {
        if (!CommitField()) return;
        string? root = StoryboardMediaDirectory(Document);
        if (root is null) return;
        try
        {
            var mapLines = new List<(OsuSection Section, int Index)>();
            int line = 0;
            var wanted = remove.Where(s => !s.Shared).Select(s => s.SourceLine).ToHashSet();
            foreach (var section in Document.OriginalSections)
            {
                if (section.Name is not ("Events" or "Variables" or "General")) continue;
                line++;
                for (int i = 0; i < section.Lines.Count; i++)
                    if (wanted.Contains(++line))
                    {
                        if (remove.First(s => !s.Shared && s.SourceLine == line).SourceText != section.Lines[i]) throw new InvalidOperationException(L.Get("copier.fileChanged", Document.SourcePath ?? ""));
                        mapLines.Add((section, i));
                    }
            }
            if (mapLines.Count != wanted.Count) throw new InvalidOperationException(L.Get("copier.fileChanged", Document.SourcePath ?? ""));
            string Serialize(StoryboardSample sample) => $"Sample,{sample.TimeMs.ToString(CultureInfo.InvariantCulture)},{(int)sample.Layer},\"{Path.GetRelativePath(root, sample.Path).Replace('\\', '/')}\",{sample.Volume}";
            var mapAdd = add.Where(s => !move || !s.Shared).ToArray();
            string? sharedPath = null, before = null, after = null;
            var sharedLines = remove.Where(s => s.Shared).Select(s => s.SourceLine).ToHashSet();
            if (sharedLines.Count > 0)
            {
                sharedPath = Directory.EnumerateFiles(root).Where(p => string.Equals(Path.GetExtension(p), ".osb", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (sharedPath is null) return;
                before = File.ReadAllText(sharedPath);
                var originalLines = before.Split('\n');
                foreach (var sample in remove.Where(s => s.Shared))
                    if (sample.SourceLine < 1 || sample.SourceLine > originalLines.Length || originalLines[sample.SourceLine - 1].TrimEnd('\r') != sample.SourceText)
                        throw new InvalidOperationException(L.Get("copier.fileChanged", sharedPath));
                after = string.Join("\n", before.Split('\n').Select((text, i) => sharedLines.Contains(i + 1) ? (move ? Serialize(add.Single(s => s.Shared && s.SourceLine == i + 1)) : null) : text).Where(text => text is not null));
            }
            void Resources(bool forward)
            {
                if (sharedPath is null) return;
                string expected = forward ? before! : after!, next = forward ? after! : before!;
                if (File.ReadAllText(sharedPath) != expected) throw new InvalidOperationException(L.Get("copier.fileChanged", sharedPath));
                string temporary = sharedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temporary, next); File.Move(temporary, sharedPath, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                RetireStoryboard(preserveDisplay: true); RefreshStoryboard();
            }
            if (mapLines.Count == 0 && mapAdd.Length == 0 && editNotes is null)
            {
                if (sharedPath is not null) history.EditResources(L.Get("assist.soundChange"), Resources);
            }
            else
            {
                if (sharedPath is not null) Resources(true);
                bool applied = false;
                try { applied = Edit(L.Get("assist.soundChange"), () =>
                {
                    editNotes?.Invoke();
                    foreach (var group in mapLines.GroupBy(p => p.Section))
                        foreach (var entry in group.OrderByDescending(p => p.Index)) entry.Section.Lines.RemoveAt(entry.Index);
                    if (mapAdd.Length == 0) return;
                    var events = Document.OriginalSections.FirstOrDefault(s => s.Name == "Events");
                    if (events is null) { events = new OsuSection { Name = "Events" }; Document.OriginalSections.Add(events); }
                    foreach (var sample in mapAdd)
                    {
                        events.Lines.Add(Serialize(sample));
                    }
                }, (forward, _, _) => Resources(forward)); }
                finally { if (!applied && sharedPath is not null) Resources(false); }
            }
            hitsoundingStoryboardSelection.Clear();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { ShowError(error.Message); }
    }
}
