using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private SoundTag[] storyboardDropTags = [];
    private double storyboardDropTime;
    private SoundTag[] hitsoundingMoveTags = [], hitsoundingStoryboardMoveTags = [];
    public bool CanDropFilesAt(string[] paths, float x, float y) => StoryboardDropTarget(x, y)
        ? paths.Length == 1 && IsHitsoundingAudio(paths[0]) && CanDropAudio && !HitsoundingLaneLocked(6)
        : paths.Any(CanDropFile);
    public void ClearFileDropPreview() => storyboardDropTags = [];
    public void PreviewFileDrop(string[] paths, float x, float y)
    {
        if (paths.Length != 1 || !IsHitsoundingAudio(paths[0]) || !CanDropAudio || HitsoundingLaneLocked(6) || !StoryboardDropTarget(x, y))
        { ClearFileDropPreview(); return; }
        if (storyboardDropTags.Length == 0 || storyboardDropTags[0].Sounds[0].FilePath != paths[0])
            storyboardDropTags = [new(0, 6, Path.GetFileNameWithoutExtension(paths[0]), 0, double.PositiveInfinity,
                [new(CatchObjectKind.Fruit, paths[0], 1, "storyboard")])];
        storyboardDropTime = Math.Max(0, SnapTiming().Snap(HitsoundingPointerTime(x), divisor));
    }
    internal Rect StoryboardDropBounds => new(hitsoundingGridBounds.X, hitsoundingGridBounds.Y + hitsoundingGridBounds.Height * 6 / 7,
        hitsoundingGridBounds.Width, hitsoundingGridBounds.Height / 7);
    private bool StoryboardDropTarget(float x, float y) => HitsoundingPageVisible && !LibraryVisible && StoryboardDropBounds.Contains(x, y);

    private void DropStoryboardAudio(string[] paths, float x, float y)
    {
        if (paths.Length != 1 || !IsHitsoundingAudio(paths[0]) || !CanDropAudio || HitsoundingLaneLocked(6)
            || !StoryboardDropTarget(x, y) || !CommitField()) return;
        bool needsRoot = Document.SourcePath is null && Document.AudioPath is null;
        string? root = needsRoot ? MediaResourceRoot(difficulties[activeDifficulty].Id) : StoryboardMediaDirectory(Document);
        if (root is null) return;
        long remaining = 128L * 1024 * 1024 - Document.StoryboardAudio.Values.Sum(a => (long)(a.Data?.Length ?? 0));
        var audio = FruitsAtelier.Core.StoryboardAudio.Capture(paths[0], Math.Max(0, remaining));
        string relative = audio.RelativePath;
        double time = Math.Max(0, SnapTiming().Snap(HitsoundingPointerTime(x), divisor));
        EditHitsoundingStoryboard([], [new(Path.Combine(root, relative), time, 100, StoryboardLayer.Foreground)],
            editNotes: () => Document.StoryboardAudio[relative] = audio, retainSelection: true, resourceRoot: root);
    }

    private Hitsound StoryboardSound(StoryboardSample sample)
    {
        string? root = StoryboardMediaDirectory(Document);
        string relative = root is null ? "" : Path.GetRelativePath(root, sample.Path).Replace('\\', '/');
        Document.StoryboardAudio.TryGetValue(relative, out var audio);
        return new(CatchObjectKind.Fruit, sample.Path, sample.Volume / 100f, "storyboard", Data: audio?.Data);
    }
}
